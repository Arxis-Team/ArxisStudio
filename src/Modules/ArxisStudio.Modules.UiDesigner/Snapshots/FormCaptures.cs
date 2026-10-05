using System.Runtime.CompilerServices;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Фоновые снимки форм: форму, у которой снимка нет или он старше файла, дизайнер снимает невидимо, когда
/// её превью спросили, — по одной и когда студия свободна.
/// </summary>
/// <remarks>
/// <para>
/// <b>Та же дорога, что у вкладки.</b> Документ берётся у службы XAML, показ ставится на карточку формы —
/// только карточка стоит не на холсте вкладки, а на сцене (<see cref="FormStage"/>), — и снимается та же
/// область тем же кодом (<see cref="FormPicture"/>). Форма на плитке поэтому такая же, как во вкладке: со
/// стилями и темой её приложения и с контролами проекта.
/// </para>
/// <para>
/// <b>Только когда тихо.</b> Просьбы приходят от видимых плиток; снимок ждёт паузы в них
/// (<see cref="UiDesignerOptions.SnapshotQuiet"/>) и простоя диспетчера, и снимается одна форма за раз,
/// последняя спрошенная — первой: прокрутка не дёргается, а то, на что смотрят, встаёт раньше.
/// </para>
/// <para>
/// <b>Первая форма поднимает службу.</b> Документ решения открывает сессию дизайна — профиль, сборку,
/// загрузку типов, — и это секунды; дальше форма встаёт за десятки миллисекунд. Сессия кончится сама,
/// через простой после последнего отпущенного документа.
/// </para>
/// <para>
/// <b>Вкладке и доске не мешает.</b> Показ у документа один, и снимок в учёте показов
/// (<see cref="FormShows"/>) младше всех: вкладка или доска, взявшие форму, просят его уступить — идущий
/// снимок этой формы отпускает показ, а новых не будет, пока форму держат: снимет тот, кто показывает. На
/// замену поколения снимок — участник: отдаёт корень, бросает съёмку и вернёт форму в очередь.
/// </para>
/// <para>
/// Не снимаются не формы (приложение, словари), файлы вне решения, документ с несохранённым — снимок
/// отвечает диску — и форма, которая не встала с тем же текстом: её не пробуют снова, пока текст не
/// сменится. Всё здесь — в потоке интерфейса, кроме чтения файлов.
/// </para>
/// </remarks>
internal sealed class FormCaptures : IXamlRootLender, IXamlDesignParticipant, IDisposable
{
    /// <summary>Сколько просьб очередь помнит: дальние плитки давно ушли из вида.</summary>
    private const int QueueLimit = 64;

    private static readonly ConditionalWeakTable<IStudioContext, FormCaptures> Owners = new();

    private readonly IStudioContext _context;
    private readonly FormSnapshots _snapshots;
    private readonly UiDesignerOptions _options;
    private readonly LinkedList<string> _queue = [];
    private readonly FormShows _shows;
    private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();

    private FormStage? _stage;
    private Capture? _current;
    private Task _pump = Task.CompletedTask;
    private long _lastRequest;
    private bool _disposed;

    /// <summary>Заводит съёмку дизайнера и записывает её на его контекст.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="snapshots">Хранилище снимков.</param>
    /// <param name="options">Шов модуля.</param>
    public FormCaptures(IStudioContext context, FormSnapshots snapshots, UiDesignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _snapshots = snapshots;
        _options = options;
        _shows = FormShows.Of(context);

        Owners.AddOrUpdate(context, this);
    }

    /// <summary>Идущая работа — тестам: дождаться очереди, а не спать.</summary>
    internal Task Pumping => _pump;

    /// <summary>Сколько форм снято в фоне — тестам.</summary>
    internal int Taken { get; private set; }

    /// <summary>Сколько форм не встало — тестам.</summary>
    internal int Failed { get; private set; }

    /// <summary>Идёт ли снимок сейчас — тестам.</summary>
    internal bool Capturing => _current is not null;

    /// <summary>Снимать ли в фоне: настройка дизайнера, по умолчанию да.</summary>
    private bool Enabled => !_disposed && _context.Settings.Get<bool?>(UiDesignerModule.PreviewsKey) != false;

    /// <summary>Съёмка дизайнера этого контекста; null — снимков у него нет.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static FormCaptures? Of(IStudioContext context) =>
        Owners.TryGetValue(context, out var captures) && !captures._disposed ? captures : null;

    /// <summary>
    /// Просит снять форму: у неё нет снимка или он старше файла.
    /// </summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <remarks>Зовёт поставщик превью; снимок встанет потом, и показывающие узнают о нём из <c>Changed</c>.</remarks>
    public void Request(string formPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(formPath);

        if (!Enabled || IsClaimed(formPath))
            return;

        Forget(formPath);
        _queue.AddFirst(formPath);

        while (_queue.Count > QueueLimit)
            _queue.RemoveLast();

        _lastRequest = _options.TimeProvider.GetTimestamp();

        if (_pump.IsCompleted)
            _pump = PumpAsync();
    }

    /// <inheritdoc/>
    public IDisposable Lend(object root) => _stage?.Lend(root) ?? Nothing.Instance;

    /// <inheritdoc/>
    /// <remarks>
    /// Хост зовёт участников, держа свою очередь, поэтому здесь ничего не ждут: корень снимается с карточки
    /// сразу, а съёмка, брошенная отменой, отпустит показ и документ уже после замены.
    /// </remarks>
    public ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        _stage?.Clear();

        if (_current is { } current)
        {
            current.Retry = true;
            current.Cancel();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RestoreAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.Clear();
        _current?.Cancel();
        _stage?.Dispose();
        _stage = null;

        // Отмена без освобождения: очередь и съёмка, брошенные ею, ещё спросят признак отмены.
        _lifetime.Cancel();
        Owners.Remove(_context);
    }

    /// <summary>Снимает формы очереди одну за другой, когда тихо.</summary>
    private async Task PumpAsync()
    {
        try
        {
            while (Enabled && _queue.First is not null)
            {
                // Тишина после последней просьбы: пока плитки листают, не снимают.
                for (var left = Quiet(); left > TimeSpan.Zero; left = Quiet())
                    await Task.Delay(left, _options.TimeProvider, _lifetime.Token);

                // Простой диспетчера: ввод, раскладка и кадр — раньше снимка.
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);

                if (!Enabled || _queue.First is not { } first)
                    break;

                _queue.RemoveFirst();
                await CaptureAsync(first.Value);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Дизайнер ушёл.
        }
    }

    /// <summary>Снимает одну форму, если её ещё надо снимать.</summary>
    private async Task CaptureAsync(string formPath)
    {
        if (IsClaimed(formPath) || _context.XamlDocuments() is not { } documents)
            return;

        // Поколение, которое не встанет без человека, — чужая Avalonia, решение не прочлось, нужен
        // перезапуск, — корня не даст: ждать его у каждой формы незачем.
        if (_context.XamlDesign() is { State: XamlDesignState.Unsupported or XamlDesignState.Failed or XamlDesignState.RestartRequired })
            return;

        var path = CanonicalPath.Create(formPath);

        if (!InSolution(path))
            return;

        // Файл читается вне потока интерфейса: форма ли это, какого размера и тот ли текст, на котором она
        // уже не встала.
        var (declared, text) = await Task.Run(() => Inspect(formPath), _lifetime.Token);

        if (declared is not { Kind: not FormKind.Unreadable } || text is null)
            return;

        if (_failed.TryGetValue(formPath, out var failed) && failed == text)
            return;

        // Пока очередь ждала, форму могли снять — вкладка или прежняя просьба.
        if (await _snapshots.ReadAsync(formPath, _lifetime.Token) is { IsStale: false } || IsClaimed(formPath))
            return;

        using var capture = new Capture(formPath, _lifetime.Token);

        // Форму взяли вкладка или доска — снимать будут они: их просьба уступить бросает съёмку, а взятое
        // освобождается последним, когда показ и документ уже отпущены.
        if (_shows.TryHold(formPath, FormShowRank.Capture, capture.Cancel) is not { } hold)
            return;

        _current = capture;

        var participation = _context.XamlDesign()?.Register(this);
        IXamlDocumentHandle? document = null;
        IXamlDesignView? shown = null;
        FormSnapshot? snapshot = null;
        byte[]? picture = null;

        try
        {
            document = await documents.OpenAsync(path, capture.Token);

            // Несохранённое показывает вкладка, а снимок отвечает диску.
            if (document.IsModified)
                return;

            shown = await document.ShowAsync(this, capture.Token);

            if (_options.SnapshotShown is { } shownHook)
                await shownHook(capture.Token);

            // Не построился текст — не показался или называет тип, которого нет, — снимать нечего: прежний
            // показ файлу не ответил бы.
            if (document.State is XamlDocumentState.Broken or XamlDocumentState.Behind)
            {
                Fail(formPath, text, document.Diagnostics.FirstOrDefault()?.Message ?? document.State.ToString());
                return;
            }

            if (await ArrivedAsync(shown, static view => view.Root is not null, _options.SnapshotRootWait, capture.Token) is false)
            {
                Fail(formPath, text, "корень формы не встал");
                return;
            }

            // Приложение формы показ берёт следом за корнем, а приложения может не быть вовсе — и тогда о нём
            // не скажут ничего: ждут его недолго. Форма без него вышла бы без стилей и фона своей темы.
            await ArrivedAsync(shown, static view => view.Application is not null, _options.SnapshotApplicationWait, capture.Token);

            var stage = _stage ??= new FormStage();

            if (stage.Take(shown.Root!, shown.Application, declared, FormSnapshots.Pixels) is not { } taken)
            {
                Fail(formPath, text, "форма не разложилась");
                return;
            }

            picture = taken.Picture;
            snapshot = FormSnapshots.Describe(formPath, document, shown, stage.Item.ApplicationThemeVariant, taken.Size);
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            // Форму взяли вкладка или доска, или пришла замена поколения. Замена вернёт форму в очередь, а
            // взявшие — нет: они снимут сами.
            if (capture.Retry && !IsClaimed(formPath))
                _queue.AddLast(formPath);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // Перехват широкий намеренно: форму строит код проекта человека, и чем он ответит, не знает
            // никто. Снимок — сведение для плитки, и упавшая форма остаётся значком, а не роняет очередь.
            Fail(formPath, text, e.Message);
        }
        finally
        {
            // Объекты поколения — первыми: карточка, показ, аренда документа.
            _stage?.Clear();
            shown?.Dispose();

            if (document is not null)
                await Release(document);

            participation?.Dispose();
            _current = null;
            capture.Complete();
            hold.Dispose();
        }

        if (snapshot is null || picture is null)
            return;

        try
        {
            await _snapshots.WriteAsync(snapshot, picture, _lifetime.Token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Log.Write(StudioLogLevel.Warning, BoardModel.LogSource, $"{path.FileName}: снимок не записался — {e.Message}");
            return;
        }

        Taken++;
        _context.GetService<IStudioFilePreviews>()?.Invalidate(formPath);
    }

    /// <summary>Ждёт, пока показ дойдёт до нужного; false — не дошёл за отведённое время.</summary>
    private async Task<bool> ArrivedAsync(IXamlDesignView shown, Func<IXamlDesignView, bool> arrived, TimeSpan wait, CancellationToken token)
    {
        if (arrived(shown))
            return true;

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, EventArgs e)
        {
            if (arrived(shown))
                signal.TrySetResult();
        }

        shown.RootChanged += OnChanged;
        shown.ApplicationChanged += OnChanged;

        try
        {
            await signal.Task.WaitAsync(wait, _options.TimeProvider, token);

            return true;
        }
        catch (TimeoutException)
        {
            return arrived(shown);
        }
        finally
        {
            shown.RootChanged -= OnChanged;
            shown.ApplicationChanged -= OnChanged;
        }
    }

    /// <summary>Запоминает, что форма с этим текстом не встала, и говорит об этом в журнал.</summary>
    private void Fail(string formPath, string text, string reason)
    {
        _failed[formPath] = text;
        Failed++;
        _context.Log.Write(StudioLogLevel.Info, BoardModel.LogSource, $"{Path.GetFileName(formPath)}: снимок для превью не снят — {reason}");
    }

    /// <summary>Отпускает аренду документа; служба, уже закрывшая его сама, отвечает тем же.</summary>
    private static async Task Release(IXamlDocumentHandle document)
    {
        try
        {
            await document.DisposeAsync();
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            // Служба остановилась раньше: её документы закрыла она сама.
        }
    }

    /// <summary>Сколько ещё ждать тишины после последней просьбы.</summary>
    private TimeSpan Quiet() => _options.SnapshotQuiet - _options.TimeProvider.GetElapsedTime(_lastRequest);

    /// <summary>Убирает форму из очереди.</summary>
    private void Forget(string formPath)
    {
        for (var node = _queue.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value, formPath, StringComparison.OrdinalIgnoreCase))
            {
                _queue.Remove(node);
                return;
            }
        }
    }

    /// <summary>Держат ли форму вкладка или доска: снимать будут они.</summary>
    private bool IsClaimed(string formPath) => _shows.IsHeldAbove(formPath, FormShowRank.Capture);

    /// <summary>Файл — часть открытого решения: служба XAML строит только его формы.</summary>
    private bool InSolution(CanonicalPath path) =>
        _context.Projects()?.Status.Snapshot is { } snapshot && snapshot.TryGetProjectForFile(path, out _);

    /// <summary>Корень формы и отпечаток её текста; текста нет — файл не читается.</summary>
    private static (FormRoot? Declared, string? Text) Inspect(string formPath)
    {
        try
        {
            return (FormRoot.Read(formPath), FormSnapshots.Hash(File.ReadAllText(formPath)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>Идущий снимок: какой формы, его отмена и конец.</summary>
    private sealed class Capture(string path, CancellationToken lifetime) : IDisposable
    {
        private readonly CancellationTokenSource _cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Path { get; } = path;

        public CancellationToken Token => _cancel.Token;

        /// <summary>Кончается, когда снимок отпустил показ и документ; не падает никогда.</summary>
        public Task Finished => _finished.Task;

        /// <summary>Брошен заменой поколения: форму снять потом.</summary>
        public bool Retry { get; set; }

        public void Cancel() => _cancel.Cancel();

        public void Complete() => _finished.TrySetResult();

        public void Dispose() => _cancel.Dispose();
    }

    /// <summary>Аренда, которой одалживать нечего.</summary>
    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
