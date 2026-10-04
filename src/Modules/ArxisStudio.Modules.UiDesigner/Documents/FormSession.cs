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
/// <b>Показ у документа один.</b> Фоновый снимок формы его отпускает, когда сессия заявляет форму
/// (<see cref="Claim"/>), и новых не начинает, пока заявка жива: снимать будет тот, кто показывает.
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

    private Snapshots.FormClaim? _claim;
    private IXamlDocumentHandle? _document;
    private IXamlDesignView? _shown;
    private FormEdits? _edits;
    private Task? _opening;
    private Task? _showing;
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
    public FormSession(
        IStudioContext context,
        IStudioXamlDocuments documents,
        CanonicalPath path,
        UiDesignerOptions options,
        IXamlRootLender lender)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lender);

        _context = context;
        _documents = documents;
        _options = options;
        _lender = lender;
        Path = path;
    }

    /// <summary>Что сменилось у формы; в потоке интерфейса.</summary>
    public event EventHandler<FormChanges>? Changed;

    /// <summary>Правка, сдвинувшая пути, показана: выбрать надо то, что она оставила.</summary>
    public event EventHandler<IReadOnlyList<XamlElementPath>>? SelectRequested;

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

    /// <summary>
    /// Заявляет форму: фоновый снимок отпускает её показ и новых не начинает, пока сессия жива.
    /// </summary>
    /// <remarks>Показ заявляет форму и сам; заявить раньше — значит не дать снимку начаться, пока документ берётся.</remarks>
    public void Claim()
    {
        if (!_disposed)
            _claim ??= Snapshots.FormCaptures.Of(_context)?.Claim(Path.Value);
    }

    /// <summary>Берёт документ у службы; повторный вызов ждёт того же.</summary>
    public Task OpenAsync() => _opening ??= OpenCoreAsync();

    /// <summary>Показывает документ, взяв его, если ещё не взят; повторный вызов ждёт того же показа.</summary>
    public Task ShowAsync() => _showing ??= ShowCoreAsync(_showTurn);

    /// <summary>
    /// Отпускает показ: корень и приложение формы уходят, документ и его история остаются.
    /// </summary>
    /// <remarks>
    /// Тот, кто ставил корень на холст, снимает его раньше: показ, отпущенный под стоящей формой, оставил бы
    /// на холсте объекты поколения, которое служба вправе выгрузить. Идущий показ, не успевший встать,
    /// отпускается, как только встанет.
    /// </remarks>
    public void Hide()
    {
        _showTurn++;
        _showing = null;

        if (_shown is { } shown)
        {
            _shown = null;
            shown.RootChanged -= OnRootChanged;
            shown.ApplicationChanged -= OnApplicationChanged;
            shown.Dispose();
            Raise(FormChanges.Root | FormChanges.Application);
        }

        _claim?.Dispose();
        _claim = null;
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

        _claim?.Dispose();
        _claim = null;
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

            _edits = new FormEdits(document, _context.Strings, OnSelect, Refused);
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

    /// <summary>Берёт документ, ждёт, пока фоновый снимок отпустит показ, и показывает.</summary>
    /// <param name="turn">Очередь показа: <see cref="Hide"/>, пришедший раньше ответа, его отменяет.</param>
    private async Task ShowCoreAsync(int turn)
    {
        try
        {
            await OpenAsync();

            if (_document is not { } document || _disposed || turn != _showTurn)
                return;

            Claim();

            if (_claim is { } claim)
                await claim.Released;

            if (_disposed || turn != _showTurn)
                return;

            var shown = await document.ShowAsync(_lender, _lifetime.Token);

            if (_disposed || turn != _showTurn)
            {
                shown.Dispose();
                return;
            }

            _shown = shown;
            shown.RootChanged += OnRootChanged;
            shown.ApplicationChanged += OnApplicationChanged;

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
        if (!_disposed && ReferenceEquals(sender, _shown))
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
