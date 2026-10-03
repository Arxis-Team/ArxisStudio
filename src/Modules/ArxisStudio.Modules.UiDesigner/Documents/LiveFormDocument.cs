using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Живая вкладка формы: корень, построенный службой XAML в поколении типов проекта, на холсте дизайнера,
/// и её XAML рядом.
/// </summary>
/// <remarks>
/// <para>
/// <b>Вкладка открывается сразу.</b> Документ берётся у службы в фоне: первый документ решения поднимает
/// профиль дизайна, сборку и загрузку типов, и это секунды. Пока они идут, на холсте стоит карточка
/// объявленного размера, а чип на полосе говорит, что идёт.
/// </para>
/// <para>
/// <b>Правят текст.</b> Жест холста — правка документа одним шагом его истории (<see cref="FormGestures"/>),
/// и живое дерево перестраивает уже служба. Холст ничего не пишет в контролы мимо текста: что видно, то и
/// в файле.
/// </para>
/// <para>
/// <b>Поколение не держит.</b> Вкладка — участник замены: на замену холст замирает стоп-кадром, отдаёт
/// корень и приложение формы и выбор; после — берёт новые и выбирает те же элементы по путям. Между
/// заменами вкладка помнит пути (<see cref="Markup.Xaml.XamlElementPath"/>), а не контролы.
/// </para>
/// <para>
/// <b>Сохраняет сама</b>, как IntelliJ: при уходе из окна студии, при закрытии вкладки и перезапуске и
/// после паузы в правках (<see cref="UiDesignerOptions.AutoSaveDelay"/>); выключается настройкой
/// <see cref="UiDesignerModule.AutoSaveKey"/>. Пишет служба файлов со сверкой: переписанный мимо файл не
/// затирается, а становится вопросом на баннере.
/// </para>
/// </remarks>
internal sealed partial class LiveFormDocument : DocumentView, IXamlDesignParticipant, IXamlRootLender
{
    private readonly IStudioContext _context;
    private readonly IStudioXamlDocuments _documents;
    private readonly IStudioXamlDesign? _design;
    private readonly DesignerWorkbench _bench;
    private readonly UiDesignerOptions _options;
    private readonly CanonicalPath _path;
    private readonly LiveFormView _view;
    private readonly UiDesignerFormItem _form;
    private readonly SheetControls _controls;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IDisposable? _participation;

    private IXamlDocumentHandle? _document;
    private IXamlDesignView? _shown;
    private FormEdits? _edits;
    private FormGestures? _gestures;
    private readonly FormDrops _drops;
    private IDisposable? _frozen;
    private IDisposable? _gesture;
    private ITimer? _autoSave;
    private Window? _window;
    private string? _problem;
    private string? _dismissed;
    private string? _saveFailure;
    private bool _framed;
    private bool _rootFramed;
    private bool _disposed;

    /// <summary>Строит вкладку и начинает открывать документ.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="documents">Документы службы XAML.</param>
    /// <param name="path">Файл формы.</param>
    /// <param name="root">Корень, уже прочитанный редактором: размер карточки, пока документ открывается.</param>
    /// <param name="options">Часы и паузы.</param>
    public LiveFormDocument(
        IStudioContext context,
        IStudioXamlDocuments documents,
        CanonicalPath path,
        FormRoot root,
        UiDesignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _documents = documents;
        _design = context.XamlDesign();
        _bench = DesignerWorkbench.Of(context);
        _options = options;
        _path = path;
        _view = new LiveFormView();

        // Размеченный режим: что на форме редактируется, говорит документ — объявленное им помечается
        // (Mark). Загруженный режим предлагал бы к выбору и внутренности контролов проекта: их разметка
        // построила своё, и элемента в этом документе у него нет.
        _form = new UiDesignerFormItem
        {
            ContentMode = SurfaceContentMode.Annotated,
            Width = root.Width ?? SheetControls.LengthOf(_view, "AxFormFrameWidth"),
            Height = root.Height ?? SheetControls.LengthOf(_view, "AxFormFrameHeight"),
        };
        _form.AddHandler(InputElement.GettingFocusEvent, OnFormGettingFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        var sheet = _view.Sheet;

        sheet.ItemsSource = new[] { _form };
        _controls = new SheetControls(context, sheet, _view.Fit, _view.Actual, _view.GridToggle, Everything);

        // Esc — к тому, в чём стоит выбранное, как в дизайнерах Visual Studio; на корне снимает выбор.
        var clear = IndexOf(sheet.KeyCommands, SurfaceKeyCommands.ClearSelection);

        sheet.KeyCommands.Remove(SurfaceKeyCommands.ClearSelection);
        sheet.KeyCommands.Insert(
            Math.Max(0, clear),
            new SurfaceKeyCommand(ParentCommand, new KeyGesture(Key.Escape), _ => SelectParent()));
        sheet.KeyCommands.Add(new SurfaceKeyCommand(FrameCommand, BoardMenu.FrameKey, _ =>
        {
            _controls.FrameAll();

            return true;
        }));

        sheet.SurfaceSelectionChanged += OnSheetSelectionChanged;
        sheet.PropertyChanged += OnSheetPropertyChanged;
        sheet.Loaded += OnSheetLoaded;

        _view.Code.CaretMoved += OnCaretMoved;
        _view.Mode.SelectionChanged += OnModeChanged;
        _view.TakeTheirs.Click += OnTakeTheirs;
        _view.KeepMine.Click += OnKeepMine;
        _view.Rebuild.Click += OnRebuild;
        _view.ShowProblem.Click += OnShowProblem;
        _view.Notice.Closed += OnNoticeClosed;
        _view.Conflict.Closed += OnConflictClosed;
        _view.AttachedToVisualTree += OnAttached;
        _view.DetachedFromVisualTree += OnDetached;
        _view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);

        if (_design is not null)
        {
            _design.StateChanged += OnDesignStateChanged;
            _participation = _design.Register(this);
        }

        _drops = new FormDrops(this, sheet, context.XamlTypes(), context.Strings, Say);

        ApplyMode(ModeOf(context.Settings.Get<string>(UiDesignerModule.ViewKey)));
        ShowState();

        Opening = OpenAsync();
    }

    /// <summary>Идентификатор команды холста «к родителю».</summary>
    public const string ParentCommand = "ui-designer.parent";

    /// <summary>Идентификатор команды холста «показать всё».</summary>
    public const string FrameCommand = "ui-designer.frame";

    /// <inheritdoc/>
    public override Control Content => _view;

    /// <inheritdoc/>
    public override string Title => _path.FileName;

    /// <inheritdoc/>
    /// <remarks>Холст — с него работают; в виде одного XAML холста не видно, и каретку берёт текст.</remarks>
    public override Control? FocusTarget => _mode == FormViewMode.Xaml ? _view.Code : _view.Sheet;

    /// <summary>Открытие документа: тестам — дождаться его.</summary>
    internal Task Opening { get; }

    /// <summary>Разметка — тестам.</summary>
    internal LiveFormView View => _view;

    /// <summary>Карточка формы — тестам.</summary>
    internal UiDesignerFormItem Form => _form;

    /// <summary>Документ, когда он открыт, — тестам.</summary>
    internal IXamlDocumentHandle? Document => _document;

    /// <summary>Показ, когда он есть: панели спрашивают у него члены и значения.</summary>
    internal IXamlDesignView? Shown => _shown;

    /// <summary>Правки формы по путям, когда документ открыт: ими правят и панели.</summary>
    internal FormEdits? Edits => _edits;

    /// <summary>Холст как цель перетаскивания — тестам.</summary>
    internal FormDrops Drops => _drops;

    /// <summary>Файл формы.</summary>
    internal CanonicalPath Path => _path;

    /// <inheritdoc/>
    /// <remarks>
    /// Файл, переписанный поверх несохранённого, ждёт ответа: запись разошлась бы с диском, и служба её
    /// не пропустит. Сохранение тогда отказывает и возвращает вопрос, если его закрыли крестиком.
    /// </remarks>
    public override async Task<bool> SaveAsync()
    {
        if (_document is not { IsModified: true } document)
            return true;

        if (document.HasConflict)
        {
            ShowConflict();
            return false;
        }

        try
        {
            await document.SaveAsync(_lifetime.Token);
            _saveFailure = null;

            return true;
        }
        catch (IOException e)
        {
            Say(Format("form.saveFailed", _path.FileName, e.Message));

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

    /// <inheritdoc/>
    /// <remarks>
    /// Сохраняет само, если сохранять самому разрешено: тогда о вкладке человека не спросят. Не вышло —
    /// отметка остаётся, и спросит студия.
    /// </remarks>
    public override async ValueTask<bool> CanCloseAsync(DocumentCloseReason reason)
    {
        if (AutoSaves)
            await AutoSaveAsync();

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Панели модуля смотрят на форму впереди: показанная — эта.</remarks>
    public override void OnActivated() => _bench.Activated(this);

    /// <inheritdoc/>
    public override void OnDeactivated() => _bench.Deactivated(this);

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _bench.Deactivated(this);
        await _lifetime.CancelAsync();

        _autoSave?.Dispose();
        _gesture?.Dispose();
        _participation?.Dispose();

        if (_design is not null)
            _design.StateChanged -= OnDesignStateChanged;

        var sheet = _view.Sheet;

        sheet.SurfaceSelectionChanged -= OnSheetSelectionChanged;
        sheet.PropertyChanged -= OnSheetPropertyChanged;
        sheet.Loaded -= OnSheetLoaded;

        _view.Code.CaretMoved -= OnCaretMoved;
        _view.Mode.SelectionChanged -= OnModeChanged;
        _view.TakeTheirs.Click -= OnTakeTheirs;
        _view.KeepMine.Click -= OnKeepMine;
        _view.Rebuild.Click -= OnRebuild;
        _view.ShowProblem.Click -= OnShowProblem;
        _view.Notice.Closed -= OnNoticeClosed;
        _view.Conflict.Closed -= OnConflictClosed;
        _view.AttachedToVisualTree -= OnAttached;
        _view.DetachedFromVisualTree -= OnDetached;
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _form.RemoveHandler(InputElement.GettingFocusEvent, OnFormGettingFocus);

        Watch(null);

        _gestures?.Dispose();
        _drops.Dispose();
        _controls.Dispose();

        // Сперва то, что держит объекты поколения: выбор, корень, приложение, показ. Потом аренда — последняя
        // закрывает документ.
        using (Syncing())
            sheet.SelectedItems?.Clear();

        _form.Root = null;
        _form.ApplicationRoot = null;
        _frozen?.Dispose();
        _frozen = null;

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

        _lifetime.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Замена поколения: холст замирает на кадре — человек видит форму, а не пустоту, — и отдаёт всё, что
    /// построено из уходящих типов: выбор, корень, приложение. Пути выбора остаются у вкладки.
    /// </remarks>
    public ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        _frozen ??= _view.Sheet.Freeze();

        using (Syncing())
            _view.Sheet.SelectedItems?.Clear();

        _form.Root = null;
        _form.ApplicationRoot = null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Показ уже взял новый корень (его <c>RootChanged</c> пришёл раньше): осталось ожить и выбрать.</remarks>
    public ValueTask RestoreAsync(CancellationToken cancellationToken)
    {
        TakeRoot();
        TakeApplication();

        _frozen?.Dispose();
        _frozen = null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Окно на время записи сессии отдаётся целиком: пока карточка держит его содержимое, окно пусто, и
    /// запись ушла бы в окно, которого никто не видит. Чужой корень — не наш: отдавать нечего.
    /// </remarks>
    public IDisposable Lend(object root) =>
        ReferenceEquals(_form.Root, root) ? _form.SuspendRoot() : Nothing.Instance;

    /// <summary>Берёт документ у службы, потом показ.</summary>
    private async Task OpenAsync()
    {
        try
        {
            var document = await _documents.OpenAsync(_path, _lifetime.Token);

            if (_disposed)
            {
                await document.DisposeAsync();
                return;
            }

            _document = document;
            document.Changed += OnDocumentChanged;
            document.ExternalConflict += OnExternalConflict;

            _edits = new FormEdits(document, _context.Strings, Select, Refused);
            _gestures = new FormGestures(_view.Sheet, _form, _edits, () => _shown, _context.Strings);

            SetModified(document.IsModified);
            ShowConflict();
            ShowState();
            _ = RefreshCodeAsync();

            var shown = await document.ShowAsync(this, _lifetime.Token);

            if (_disposed)
            {
                shown.Dispose();
                return;
            }

            _shown = shown;
            shown.RootChanged += OnRootChanged;
            shown.ApplicationChanged += OnApplicationChanged;

            TakeRoot();
            TakeApplication();
            ShowState();
            _ = _drops.ListAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Вкладку закрыли, пока документ открывался.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or ObjectDisposedException or IOException)
        {
            _problem = Format("form.openFailed", e.Message);
            ShowState();
        }
    }

    /// <summary>Ставит карточке корень показа и помечает объявленное документом.</summary>
    /// <remarks>
    /// Первый корень форма вписывает в холст: до него карточка стояла объявленным размером, без
    /// заголовка окна, и кадр, снятый тогда, обрезал бы заголовок сверху. Корень, сменившийся потом, —
    /// правка или замена типов, — кадр не трогает: масштаб к этому времени выбирал человек.
    /// </remarks>
    private void TakeRoot()
    {
        var root = _shown?.Root;

        if (!ReferenceEquals(_form.Root, root))
            _form.Root = root;

        if (root is not null && !_rootFramed)
        {
            _rootFramed = true;
            Dispatcher.UIThread.Post(_controls.FrameAll, DispatcherPriority.Loaded);
        }

        Mark();
        Reselect();
        _bench.Changed(this);
    }

    /// <summary>Ставит карточке приложение формы: его стили, ресурсы, шаблоны данных и тему.</summary>
    /// <remarks>
    /// Со сменой приложения карточка ставит содержимое формы в дерево заново — тему своего типа контрол
    /// ищет при входе, — и выбор холста на миг уходит с контролов: его возвращают пути. Вариант темы
    /// ставится раньше приложения, чтобы содержимое вошло в дерево уже под ним.
    /// </remarks>
    private void TakeApplication()
    {
        var application = _shown?.Application;

        _form.ApplicationThemeVariant = application is null ? ThemeVariant.Default : VariantOf(application);

        if (ReferenceEquals(_form.ApplicationRoot, application))
            return;

        using (Syncing())
            _form.ApplicationRoot = application;

        Reselect();
    }

    /// <summary>Тема, к которой пришло бы приложение формы при работе.</summary>
    /// <remarks>
    /// Объявленную сторону приложение называет само — <c>RequestedThemeVariant</c> в <c>App.axaml</c>, — а
    /// «по умолчанию» при работе решает платформа, а не студия: тёмная студия — сведение о студии, а не о
    /// проекте. Без приложения тему не знает никто, и карточка наследует студийную.
    /// </remarks>
    private static ThemeVariant VariantOf(Application application) =>
        application.RequestedThemeVariant is { } requested && requested != ThemeVariant.Default
            ? requested
            : Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

    /// <summary>
    /// Помечает контролы, которые документ объявил сам: только их холст предлагает к выбору.
    /// </summary>
    /// <remarks>
    /// Корень не помечается: за него стоит карточка, и её ручки и есть размер формы. Метка внутри кнопки
    /// построена её шаблоном и элемента не имеет — показ её не называет.
    /// </remarks>
    private void Mark()
    {
        if (_shown is not { } shown)
            return;

        foreach (var declared in shown.GetDeclaredObjects())
        {
            if (declared is Control control && !ArxisStudio.Surface.UiDesigner.Layout.GetIsTracked(control))
                ArxisStudio.Surface.UiDesigner.Layout.SetIsTracked(control, true);
        }
    }

    /// <summary>Всё на холсте: карточка, а у окна — и его заголовок над ней.</summary>
    private Rect? Everything()
    {
        var size = _form.Bounds.Size is { Width: > 0, Height: > 0 } measured
            ? measured
            : new Size(_form.Width, _form.Height);

        var chrome = _form.IsTopLevel ? SheetControls.LengthOf(_view, "UiDesigner.Form.TitleBar.Height") : 0;

        return new Rect(_form.Location.X, _form.Location.Y - chrome, size.Width, size.Height + chrome);
    }

    /// <summary>Первый показ: форма вписывается, когда холст измерен, — до раскладки его размер нулевой.</summary>
    private void OnSheetLoaded(object? sender, RoutedEventArgs e)
    {
        if (_framed)
            return;

        _framed = true;
        Dispatcher.UIThread.Post(_controls.FrameAll, DispatcherPriority.Loaded);
    }

    private void OnRootChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            TakeRoot();
    }

    private void OnApplicationChanged(object? sender, EventArgs e)
    {
        if (!_disposed && _frozen is null)
            TakeApplication();
    }

    private void OnDocumentChanged(object? sender, XamlDocumentChangesEventArgs e)
    {
        if (_disposed || _document is not { } document)
            return;

        var changes = e.Changes;

        if ((changes & (XamlDocumentChanges.Text | XamlDocumentChanges.Saved)) != 0)
            SetModified(document.IsModified);

        if ((changes & XamlDocumentChanges.Text) != 0)
        {
            _saveFailure = null;
            _ = RefreshCodeAsync();
            ScheduleAutoSave();
        }

        if ((changes & (XamlDocumentChanges.Text | XamlDocumentChanges.Objects)) != 0)
        {
            Mark();
            Reselect();
            _bench.Changed(this);
        }

        if ((changes & XamlDocumentChanges.Conflict) != 0)
            ShowConflict();

        if ((changes & (XamlDocumentChanges.State | XamlDocumentChanges.Deleted | XamlDocumentChanges.Closed)) != 0)
            ShowState();
    }

    private void OnExternalConflict(object? sender, XamlExternalConflictEventArgs e) => ShowConflict();

    private void OnDesignStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        ShowState();

        // Сборка и замена меняют, что из контролов проекта собрано: несобранный стал собранным.
        if (_design?.State == XamlDesignState.Live)
            _ = _drops.ListAsync();
    }

    /// <summary>
    /// Пока на холсте идёт жест, замена типов ждёт: перестроенный под рукой холст бросил бы жест на середине.
    /// </summary>
    private void OnSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != SurfaceView.IsInteractingProperty || _design is null)
            return;

        if (_view.Sheet.IsInteracting)
        {
            _gesture ??= _design.Defer(Format("form.defer.gesture", _path.FileName));
        }
        else
        {
            _gesture?.Dispose();
            _gesture = null;
        }
    }

    /// <summary>
    /// Фокус в форму не заходит: кнопка формы не нажимается, поле не берёт текст, а Delete и Ctrl+A
    /// остаются у холста.
    /// </summary>
    /// <remarks>
    /// Обход Tab запирает стиль вкладки на хосте формы; сюда доходит фокус, пришедший иначе, — из кода
    /// самого контрола формы. Неотменимую перемену уводят на холст: он в фокусе ничего не печатает.
    /// </remarks>
    private void OnFormGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (e.NewFocusedElement is not Visual target
            || ReferenceEquals(target, _form)
            || !_form.IsVisualAncestorOf(target))
        {
            return;
        }

        if (!e.TryCancel())
            e.TrySetNewFocusedElement(_view.Sheet);
    }

    /// <summary>Отмена и возврат — история документа, откуда бы на вкладке ни нажали.</summary>
    /// <remarks>
    /// Холст ловит их сам и раньше, когда клавиатура у него; сюда всплывает то, что не взяли ни он, ни
    /// просмотр XAML, ни полоса.
    /// </remarks>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _edits is null || _view.GetPlatformSettings()?.HotkeyConfiguration is not { } keys)
            return;

        if (keys.Undo.Any(gesture => gesture.Matches(e)))
        {
            e.Handled = true;
            _ = _edits.StepAsync(back: true);
        }
        else if (keys.Redo.Any(gesture => gesture.Matches(e)))
        {
            e.Handled = true;
            _ = _edits.StepAsync(back: false);
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Watch(TopLevel.GetTopLevel(_view) as Window);

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Watch(null);

    /// <summary>Слушает уход из окна, где стоит вкладка: оторванную вкладку несёт другое окно.</summary>
    private void Watch(Window? window)
    {
        if (ReferenceEquals(window, _window))
            return;

        if (_window is not null)
            _window.Deactivated -= OnWindowDeactivated;

        _window = window;

        if (_window is not null)
            _window.Deactivated += OnWindowDeactivated;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (AutoSaves)
            _ = AutoSaveAsync();
    }

    /// <summary>Сохранять ли самому: настройка модуля.</summary>
    private bool AutoSaves => _context.Settings.Get<bool?>(UiDesignerModule.AutoSaveKey) ?? true;

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

    /// <summary>
    /// Сохраняет, если есть что и можно: вопрос о чужой записи и удалённый файл ждут человека.
    /// </summary>
    /// <remarks>
    /// Отказ говорится строкой состояния один раз на текст: пауза и уход из окна повторяли бы его
    /// после каждой правки.
    /// </remarks>
    private async Task AutoSaveAsync()
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
            Say(Format("form.saveFailed", _path.FileName, e.Message));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Сказано раньше, документ закрыт или вкладка уходит.
        }
    }

    /// <summary>Жест не записан: почему — строкой состояния.</summary>
    private void Refused(string reason) => Say(Format("form.refused", reason));

    private void Say(string message) => _context.GetService<IStudioStatus>()?.Show(message);

    private string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], values);

    private static int IndexOf(SurfaceKeyCommands commands, string id)
    {
        var index = 0;

        foreach (var command in commands)
        {
            if (command.Id == id)
                return index;

            index++;
        }

        return -1;
    }

    /// <summary>Отдавать нечего: корень не наш.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
