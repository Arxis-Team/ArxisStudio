using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Что сменилось у формы — у её документа или у показа.</summary>
[Flags]
internal enum FormChanges
{
    /// <summary>Ничего.</summary>
    None = 0,

    /// <summary>Текст документа: правка, отмена, текст с диска.</summary>
    Text = 1 << 0,

    /// <summary>Документ сохранён или сошёлся с диском.</summary>
    Saved = 1 << 1,

    /// <summary>Состояние документа: живой, отстал от текста, сломан.</summary>
    State = 1 << 2,

    /// <summary>Объекты показа перестроены без смены текста.</summary>
    Objects = 1 << 3,

    /// <summary>Файл удалён.</summary>
    Deleted = 1 << 4,

    /// <summary>Вопрос о чужой записи появился или снят.</summary>
    Conflict = 1 << 5,

    /// <summary>Документ закрыла служба: решение кончилось.</summary>
    Closed = 1 << 6,

    /// <summary>Документ взят у службы: текст, правки и история есть.</summary>
    Opened = 1 << 7,

    /// <summary>Корень показа встал, сменился или ушёл.</summary>
    Root = 1 << 8,

    /// <summary>Приложение формы сменилось.</summary>
    Application = 1 << 9,

    /// <summary>Форма не открылась или не показалась.</summary>
    Problem = 1 << 10,
}

/// <summary>
/// Форма, взятая у службы XAML: аренда документа, его показ, правки и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// <b>Где форма стоит, сессия не знает.</b> Её показывают вкладка и доска — холстом (<see cref="FormCanvas"/>),
/// — а сессия держит документ и даёт показ, когда его просят: вкладка — сразу, доска — пока форма на виду.
/// </para>
/// <para>
/// <b>Показ у документа один</b>, и делят его по старшинству (<see cref="FormShows"/>): вкладка на экране
/// забирает форму у доски и у фонового снимка, доска — у снимка и у вкладки, ушедшей с экрана. Сессия, которую
/// попросили уступить, отпускает показ сама (<see cref="Hide"/>), а документ и его историю держит дальше.
/// </para>
/// <para>
/// <b>Показы идут по одному.</b> Служба занимает показ документа сразу, ещё не дождавшись его, и второй,
/// начатый раньше, чем кончился первый, она не даст. Поэтому заявка, под которой показ ещё идёт, уходит вместе
/// с ним (<see cref="FormHold.Yielding"/>), а новая — своей сессии или чужой — ждёт уходящих.
/// </para>
/// <para>
/// <b>Форма встаёт одетой.</b> Приложение формы служба строит на каждый показ и отдаёт после корня. Если к
/// прежнему показу оно приходило, новый ждёт его (<see cref="UiDesignerOptions.ShowApplicationWait"/>) и отдаёт
/// корень вместе с ним: форма, перешедшая со вкладки на доску и назад, не стоит на миг без своих стилей.
/// </para>
/// <para>
/// <b>Сохраняет сама</b>, как IntelliJ: после паузы в правках (<see cref="UiDesignerOptions.AutoSaveDelay"/>),
/// а при уходе из окна студии, закрытии и перезапуске — по зову того, кто её показывает. Выключается
/// настройкой <see cref="UiDesignerModule.AutoSaveKey"/>. Пишет служба файлов со сверкой: переписанный мимо
/// файл не затирается, а становится вопросом о чужой записи.
/// </para>
/// </remarks>
internal sealed class FormSession : IAsyncDisposable
{
    private readonly IStudioContext _context;
    private readonly IStudioXamlDocuments _documents;
    private readonly UiDesignerOptions _options;
    private readonly IXamlRootLender _lender;
    private readonly CancellationTokenSource _lifetime = new();

    private FormShowRank _rank;
    private FormHold? _hold;
    private FormHold? _showingUnder;
    private IXamlDocumentHandle? _document;
    private IXamlDesignView? _shown;
    private FormEdits? _edits;
    private Task? _opening;
    private Task? _showing;
    private TaskCompletionSource? _dressing;
    private ITimer? _autoSave;
    private string? _saveFailure;
    private int _showTurn;
    private bool _disposed;

    /// <summary>Заводит сессию формы; документ берётся по <see cref="OpenAsync"/>.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="documents">Документы службы XAML.</param>
    /// <param name="path">Файл формы.</param>
    /// <param name="options">Часы и паузы.</param>
    /// <param name="lender">Кто одалживает корень показа на время записи сессии разметки.</param>
    /// <param name="rank">Кто показывает форму: вкладка или доска; вкладка меняет его потом (<see cref="Rank"/>).</param>
    public FormSession(
        IStudioContext context,
        IStudioXamlDocuments documents,
        CanonicalPath path,
        UiDesignerOptions options,
        IXamlRootLender lender,
        FormShowRank rank)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lender);

        _context = context;
        _documents = documents;
        _options = options;
        _lender = lender;
        _rank = rank;
        Path = path;
    }

    /// <summary>Что сменилось у формы; в потоке интерфейса.</summary>
    public event EventHandler<FormChanges>? Changed;

    /// <summary>Правка, сдвинувшая пути, показана: выбрать надо то, что она оставила.</summary>
    public event EventHandler<IReadOnlyList<XamlElementPath>>? SelectRequested;

    /// <summary>
    /// Правка формы изменила текст — шаг истории её документа сделан; отмена и возврат шага сюда не
    /// говорят.
    /// </summary>
    /// <remarks>Доске — положить шаг в свою историю: Ctrl+Z на доске отменяет последнее, где бы его ни сделали.</remarks>
    public event EventHandler? Edited;

    /// <summary>Файл формы.</summary>
    public CanonicalPath Path { get; }

    /// <summary>Документ, когда он взят.</summary>
    public IXamlDocumentHandle? Document => _document;

    /// <summary>Показ, когда он есть.</summary>
    public IXamlDesignView? Shown => _shown;

    /// <summary>Правки документа, когда он взят.</summary>
    public FormEdits? Edits => _edits;

    /// <summary>Почему форма не открылась или не показалась; null — всё встало.</summary>
    public string? Problem { get; private set; }

    /// <summary>Сохранять ли самому: настройка модуля.</summary>
    public bool AutoSaves => _context.Settings.Get<bool?>(UiDesignerModule.AutoSaveKey) ?? true;

    /// <summary>Держит ли форму тот, кто старше этой сессии: тогда сессия её не покажет.</summary>
    public bool IsHeldElsewhere => !_disposed && _hold is null && FormShows.Of(_context).IsHeldAbove(Path.Value, _rank);

    /// <summary>Кто показывает форму.</summary>
    /// <remarks>
    /// Вкладка меняет его, уходя с экрана и возвращаясь: заявка сессии встаёт на новое место
    /// (<see cref="FormShows.Rerank"/>), а показ остаётся, где был. Заявку, которая уже уходит, не трогают.
    /// </remarks>
    public FormShowRank Rank
    {
        get => _rank;
        set
        {
            if (_rank == value)
                return;

            _rank = value;

            if (!_disposed && _hold is { } hold)
                FormShows.Of(_context).Rerank(hold, value);
        }
    }

    /// <summary>
    /// Заявляет форму: младшие отпускают её показ и новых не начинают, пока заявка жива.
    /// </summary>
    /// <returns>Форма за сессией; false — её держит старший.</returns>
    /// <remarks>
    /// Показ заявляет форму и сам; заявить раньше — значит взять её сразу, а не когда документ откроется.
    /// Заявка живёт, пока сессия показывает или собирается показать; отпускает её <see cref="Hide"/>.
    /// </remarks>
    public bool Claim()
    {
        if (_disposed)
            return false;

        _hold ??= FormShows.Of(_context).TryHold(Path.Value, _rank, OnYield);

        return _hold is not null;
    }

    /// <summary>Берёт документ у службы; повторный вызов ждёт того же.</summary>
    public Task OpenAsync() => _opening ??= OpenCoreAsync();

    /// <summary>Показывает документ, взяв его, если ещё не взят; повторный вызов ждёт того же показа.</summary>
    /// <remarks>
    /// Показ, который кончился ничем, — форму держал старший или она не открылась, — повторный вызов пробует
    /// снова: доска просит форму опять, когда вкладка её отпустила.
    /// </remarks>
    public Task ShowAsync()
    {
        if (_showing is { IsCompleted: true } && _shown is null)
            _showing = null;

        return _showing ??= ShowCoreAsync(_showTurn);
    }

    /// <summary>
    /// Отпускает показ: корень и приложение формы уходят, документ и его история остаются.
    /// </summary>
    /// <remarks>
    /// Тот, кто ставил корень на холст, снимает его раньше: показ, отпущенный под стоящей формой, оставил бы
    /// на холсте объекты поколения, которое служба вправе выгрузить. Идущий показ, не успевший встать,
    /// отпускается, как только встанет, — и заявка, под которой он идёт, уходит вместе с ним.
    /// </remarks>
    public void Hide()
    {
        _showTurn++;
        _showing = null;
        _dressing?.TrySetResult();

        // Корень уходит с холста раньше, чем показ отпущен: держащая его карточка отдаёт его, пока он ещё
        // показ, — а тот, кто попросил форму, покажет её уже свободной.
        if (_shown is { } shown)
        {
            _shown = null;
            shown.RootChanged -= OnRootChanged;
            shown.ApplicationChanged -= OnApplicationChanged;
            Raise(FormChanges.Root | FormChanges.Application);
            shown.Dispose();
        }

        LetHoldGo();
    }

    /// <summary>Сохраняет документ, если есть что и можно.</summary>
    /// <returns>Сохранено или сохранять нечего; false — не записалось, или ждёт ответа о чужой записи.</returns>
    /// <remarks>
    /// Файл, переписанный поверх несохранённого, ждёт ответа: запись разошлась бы с диском, и служба её не
    /// пропустит.
    /// </remarks>
    public async Task<bool> SaveAsync()
    {
        if (_document is not { IsModified: true } document)
            return true;

        if (document.HasConflict)
            return false;

        try
        {
            await document.SaveAsync(_lifetime.Token);
            _saveFailure = null;

            return true;
        }
        catch (IOException e)
        {
            Say(Format("form.saveFailed", Path.FileName, e.Message));

            return false;
        }
        catch (ObjectDisposedException)
        {
            // Документ закрыла служба: решение кончилось, писать больше некуда.
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Сохраняет, если есть что и можно: вопрос о чужой записи и удалённый файл ждут человека.
    /// </summary>
    /// <remarks>
    /// Отказ говорится строкой состояния один раз на текст: пауза и уход из окна повторяли бы его после
    /// каждой правки.
    /// </remarks>
    public async Task AutoSaveAsync()
    {
        if (_disposed || _document is not { IsModified: true, HasConflict: false, IsDeleted: false, IsClosed: false } document)
            return;

        try
        {
            await document.SaveAsync(_lifetime.Token);
        }
        catch (IOException e) when (_saveFailure is null)
        {
            _saveFailure = e.Message;
            Say(Format("form.saveFailed", Path.FileName, e.Message));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Сказано раньше, документ закрыт или форма уходит.
        }
    }

    /// <summary>Отвечает на вопрос о чужой записи; оставленные правки сохраняются поверх файла как обычно.</summary>
    /// <param name="choice">Ответ.</param>
    /// <returns>Ответ принят; false — документа нет, он закрыт или форма уходит.</returns>
    public async Task<bool> ResolveAsync(XamlConflictChoice choice)
    {
        if (_document is not { } document)
            return false;

        try
        {
            await document.ResolveConflictAsync(choice, _lifetime.Token);
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }

        ScheduleAutoSave();

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Показ — раньше аренды: последняя аренда закрывает документ. Заявка — последней: показ отпущен, и
    /// фоновый снимок формы его уже не встретит.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _lifetime.CancelAsync();

        _autoSave?.Dispose();
        _autoSave = null;

        _showTurn++;
        _dressing?.TrySetResult();

        if (_shown is { } shown)
        {
            _shown = null;
            shown.RootChanged -= OnRootChanged;
            shown.ApplicationChanged -= OnApplicationChanged;
            shown.Dispose();
        }

        if (_document is { } document)
        {
            _document = null;
            document.Changed -= OnDocumentChanged;
            document.ExternalConflict -= OnExternalConflict;

            try
            {
                await document.DisposeAsync();
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
            {
                // Служба уже остановилась: её документы закрыла она сама.
            }
        }

        _hold?.Dispose();
        _hold = null;
        _lifetime.Dispose();
    }

    /// <summary>Берёт документ у службы и заводит его правки.</summary>
    private async Task OpenCoreAsync()
    {
        try
        {
            var document = await _documents.OpenAsync(Path, _lifetime.Token);

            if (_disposed)
            {
                await document.DisposeAsync();
                return;
            }

            _document = document;
            document.Changed += OnDocumentChanged;
            document.ExternalConflict += OnExternalConflict;

            _edits = new FormEdits(document, _context.Strings, OnSelect, Refused, OnEdited);
            Raise(FormChanges.Opened);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Форму закрыли, пока документ открывался.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or ObjectDisposedException or IOException)
        {
            Fail(e);
        }
    }

    /// <summary>Берёт документ, ждёт, пока младшие и уходящие отпустят показ, и показывает.</summary>
    /// <param name="turn">Очередь показа: <see cref="Hide"/>, пришедший раньше ответа, его отменяет.</param>
    /// <remarks>
    /// Форму держит старший — показа нет: сессия стоит без корня, пока её не попросят снова. Показ занимает
    /// документ под заявкой: отпущенная, пока он идёт, она уходит вместе с ним, и прежний показ этой же сессии
    /// новый ждёт так же, как чужой.
    /// </remarks>
    private async Task ShowCoreAsync(int turn)
    {
        FormHold? under = null;

        try
        {
            await OpenAsync();

            if (_document is not { } document || _disposed || turn != _showTurn || !Claim())
                return;

            under = _showingUnder = _hold!;

            await under.Released;

            if (_disposed || turn != _showTurn)
                return;

            var shown = await document.ShowAsync(_lender, _lifetime.Token);

            try
            {
                if (_options.FormShown is { } shownHook)
                    await shownHook(_rank, _lifetime.Token);

                if (!_disposed && turn == _showTurn)
                    await DressAsync(shown);
            }
            catch
            {
                // Показ, не дошедший до холста, документ не держит.
                shown.Dispose();
                throw;
            }

            if (_disposed || turn != _showTurn)
            {
                shown.Dispose();
                return;
            }

            _shown = shown;
            shown.RootChanged += OnRootChanged;
            shown.ApplicationChanged += OnApplicationChanged;

            if (shown.Application is not null)
                FormShows.Of(_context).Dress(Path.Value, true);

            Raise(FormChanges.Root | FormChanges.Application);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Форму закрыли, пока документ показывался.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or ObjectDisposedException or IOException)
        {
            Fail(e);
        }
        finally
        {
            if (ReferenceEquals(_showingUnder, under))
                _showingUnder = null;

            // Заявку отпустили, пока показ под ней шёл: она уходит теперь, когда показа уже нет.
            if (under is not null && !ReferenceEquals(under, _hold))
                under.Dispose();
        }
    }

    /// <summary>
    /// Ждёт приложения формы, если оно приходило к прежнему её показу: корень встаёт вместе с ним, а не на
    /// миг без его стилей.
    /// </summary>
    /// <remarks>
    /// Не пришло за паузу (<see cref="UiDesignerOptions.ShowApplicationWait"/>) — форма встаёт как есть, а
    /// память о приложении сбрасывается: следующий показ его не ждёт. Отпущенный показ ждать перестаёт.
    /// </remarks>
    private async Task DressAsync(IXamlDesignView shown)
    {
        var shows = FormShows.Of(_context);

        if (shown.Application is not null || !shows.IsDressed(Path.Value))
            return;

        var dressing = _dressing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDressed(object? sender, EventArgs e)
        {
            if (shown.Application is not null)
                dressing.TrySetResult();
        }

        shown.ApplicationChanged += OnDressed;

        try
        {
            if (shown.Application is null)
                await Task.WhenAny(dressing.Task, Task.Delay(_options.ShowApplicationWait, _options.TimeProvider, _lifetime.Token));
        }
        finally
        {
            shown.ApplicationChanged -= OnDressed;

            if (ReferenceEquals(_dressing, dressing))
                _dressing = null;
        }

        if (!_disposed && shown.Application is null && !dressing.Task.IsCompleted)
            shows.Dress(Path.Value, false);
    }

    /// <summary>Старший взял форму: показ уходит, документ и история остаются.</summary>
    private void OnYield()
    {
        if (!_disposed)
            Hide();
    }

    /// <summary>
    /// Отпускает заявку: сразу, если показа под ней не идёт, а иначе — когда он кончится; до тех пор новые
    /// заявки её ждут.
    /// </summary>
    private void LetHoldGo()
    {
        if (_hold is not { } hold)
            return;

        _hold = null;

        if (ReferenceEquals(hold, _showingUnder))
            hold.Retire();
        else
            hold.Dispose();
    }

    private void Fail(Exception e)
    {
        Problem = Format("form.openFailed", e.Message);
        Raise(FormChanges.Problem);
    }

    private void OnRootChanged(object? sender, EventArgs e)
    {
        if (!_disposed && ReferenceEquals(sender, _shown))
            Raise(FormChanges.Root);
    }

    private void OnApplicationChanged(object? sender, EventArgs e)
    {
        if (_disposed || sender is not IXamlDesignView shown || !ReferenceEquals(shown, _shown))
            return;

        if (shown.Application is not null)
            FormShows.Of(_context).Dress(Path.Value, true);

        Raise(FormChanges.Application);
    }

    private void OnDocumentChanged(object? sender, XamlDocumentChangesEventArgs e)
    {
        if (_disposed || _document is null)
            return;

        if ((e.Changes & XamlDocumentChanges.Text) != 0)
        {
            _saveFailure = null;
            ScheduleAutoSave();
        }

        Raise(Map(e.Changes));
    }

    private void OnExternalConflict(object? sender, XamlExternalConflictEventArgs e)
    {
        if (!_disposed)
            Raise(FormChanges.Conflict);
    }

    /// <summary>После правки — сохранение через паузу; новая правка паузу начинает заново.</summary>
    private void ScheduleAutoSave()
    {
        _autoSave?.Dispose();
        _autoSave = null;

        if (_disposed || !AutoSaves || _document is not { IsModified: true } || _options.AutoSaveDelay == Timeout.InfiniteTimeSpan)
            return;

        _autoSave = _options.TimeProvider.CreateTimer(
            _ => Dispatcher.UIThread.Post(() => _ = AutoSaveAsync()),
            null,
            _options.AutoSaveDelay,
            Timeout.InfiniteTimeSpan);
    }

    private void OnEdited()
    {
        if (!_disposed)
            Edited?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelect(IReadOnlyList<XamlElementPath> paths)
    {
        if (!_disposed)
            SelectRequested?.Invoke(this, paths);
    }

    /// <summary>Жест не записан: почему — строкой состояния.</summary>
    private void Refused(string reason) => Say(Format("form.refused", reason));

    private void Raise(FormChanges changes)
    {
        if (changes != FormChanges.None)
            Changed?.Invoke(this, changes);
    }

    private void Say(string message) => _context.GetService<IStudioStatus>()?.Show(message);

    private string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], values);

    /// <summary>Перемены документа словами формы.</summary>
    private static FormChanges Map(XamlDocumentChanges changes)
    {
        var mapped = FormChanges.None;

        if ((changes & XamlDocumentChanges.Text) != 0)
            mapped |= FormChanges.Text;

        if ((changes & XamlDocumentChanges.Saved) != 0)
            mapped |= FormChanges.Saved;

        if ((changes & XamlDocumentChanges.State) != 0)
            mapped |= FormChanges.State;

        if ((changes & XamlDocumentChanges.Objects) != 0)
            mapped |= FormChanges.Objects;

        if ((changes & XamlDocumentChanges.Deleted) != 0)
            mapped |= FormChanges.Deleted;

        if ((changes & XamlDocumentChanges.Conflict) != 0)
            mapped |= FormChanges.Conflict;

        if ((changes & XamlDocumentChanges.Closed) != 0)
            mapped |= FormChanges.Closed;

        return mapped;
    }
}
