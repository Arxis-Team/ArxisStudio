using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
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
/// <b>Правят на холсте — так же, как на доске.</b> Документ формы держит сессия (<see cref="FormSession"/>),
/// а выбор, жесты, правки строения, меню и XAML — холст форм (<see cref="FormCanvas"/>), один у вкладки и
/// у доски. У вкладки он с одной формой; своё у неё — полоса, вид и баннеры.
/// </para>
/// <para>
/// <b>Сохраняет сама</b>, как IntelliJ: при уходе из окна студии, при закрытии вкладки и перезапуске и
/// после паузы в правках (<see cref="UiDesignerOptions.AutoSaveDelay"/>); выключается настройкой
/// <see cref="UiDesignerModule.AutoSaveKey"/>. Пишет служба файлов со сверкой: переписанный мимо файл не
/// затирается, а становится вопросом на баннере.
/// </para>
/// </remarks>
internal sealed partial class LiveFormDocument : DocumentView, IFormCanvasHost
{
    private readonly IStudioContext _context;
    private readonly IStudioXamlDesign? _design;
    private readonly CanonicalPath _path;
    private readonly LiveFormView _view;
    private readonly UiDesignerFormItem _form;
    private readonly SheetControls _controls;
    private readonly FormCanvas _canvas;
    private readonly FormSession _session;
    private readonly FormSlot _slot;

    private string? _dismissed;
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
        _design = context.XamlDesign();
        _path = path;
        _view = new LiveFormView();

        // Размеченный режим: что на форме редактируется, говорит документ — объявленное им помечается.
        // Загруженный режим предлагал бы к выбору и внутренности контролов проекта: их разметка построила
        // своё, и элемента в этом документе у него нет.
        _form = new UiDesignerFormItem
        {
            ContentMode = SurfaceContentMode.Annotated,
            Width = root.Width ?? SheetControls.LengthOf(_view, "AxFormFrameWidth"),
            Height = root.Height ?? SheetControls.LengthOf(_view, "AxFormFrameHeight"),
        };

        var sheet = _view.Sheet;

        sheet.ItemsSource = new[] { _form };
        _controls = new SheetControls(context, sheet, _view.Fit, _view.Actual, _view.GridToggle, Everything);
        _canvas = new FormCanvas(context, sheet, _view.Code, this, options);

        sheet.KeyCommands.Add(new SurfaceKeyCommand(FrameCommand, BoardMenu.FrameKey, _ =>
        {
            _controls.FrameAll();

            return true;
        }));

        _session = new FormSession(context, documents, path, options, _canvas);
        _slot = _canvas.Add(_session, _form);

        _canvas.RootTaken += OnRootTaken;
        _session.Changed += OnSessionChanged;
        sheet.Loaded += OnSheetLoaded;

        _view.Mode.SelectionChanged += OnModeChanged;
        _view.TakeTheirs.Click += OnTakeTheirs;
        _view.KeepMine.Click += OnKeepMine;
        _view.Rebuild.Click += OnRebuild;
        _view.ShowProblem.Click += OnShowProblem;
        _view.Notice.Closed += OnNoticeClosed;
        _view.Conflict.Closed += OnConflictClosed;
        _view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);

        if (_design is not null)
            _design.StateChanged += OnDesignStateChanged;

        ApplyMode(ModeOf(context.Settings.Get<string>(UiDesignerModule.ViewKey)));
        ShowState();

        // Показ у документа один: фоновый снимок этой формы его отпустит, а новых не будет, пока вкладка жива.
        _session.Claim();

        Opening = OpenAsync();
    }

    /// <summary>Идентификатор команды холста «показать всё».</summary>
    public const string FrameCommand = "ui-designer.frame";

    /// <inheritdoc/>
    public override Control Content => _view;

    /// <inheritdoc/>
    public override string Title => _path.FileName;

    /// <inheritdoc/>
    /// <remarks>Холст — с него работают; в виде одного XAML холста не видно, и каретку берёт текст.</remarks>
    public override Control? FocusTarget => _mode == FormViewMode.Xaml ? _view.Code : _view.Sheet;

    /// <summary>Открытие документа и его показ: тестам — дождаться их.</summary>
    internal Task Opening { get; }

    /// <summary>Разметка — тестам.</summary>
    internal LiveFormView View => _view;

    /// <summary>Карточка формы — тестам.</summary>
    internal UiDesignerFormItem Form => _form;

    /// <summary>Документ, когда он открыт, — тестам.</summary>
    internal IXamlDocumentHandle? Document => _session.Document;

    /// <summary>Показ, когда он есть, — тестам.</summary>
    internal IXamlDesignView? Shown => _session.Shown;

    /// <summary>Выбор вкладки — пути элементов, первый главный; тестам.</summary>
    internal IReadOnlyList<XamlElementPath> Selection => _canvas.Selection;

    /// <summary>Текст, который показывает просмотр XAML; тестам.</summary>
    internal XamlDocument? Code => _canvas.Code;

    /// <summary>Правки строения выбранного, когда документ открыт, — тестам.</summary>
    internal FormCommands? Commands => _canvas.Commands;

    /// <summary>Последняя запись снимка — тестам: дождаться, а не спать.</summary>
    internal Task Snapshotting => _slot.Snapshotting;

    /// <summary>Выбирает элементы формы; тестам.</summary>
    /// <param name="paths">Пути в нынешнем тексте, первый главный.</param>
    internal void Select(IReadOnlyList<XamlElementPath> paths) => _canvas.Select(_slot, paths);

    /// <summary>Пункты контекстного меню холста — тем же путём, каким их собирает меню; тестам.</summary>
    internal IReadOnlyList<Control> MenuItems() => _canvas.MenuItems();

    /// <inheritdoc/>
    /// <remarks>
    /// Файл, переписанный поверх несохранённого, ждёт ответа: запись разошлась бы с диском, и служба её
    /// не пропустит. Сохранение тогда отказывает и возвращает вопрос, если его закрыли крестиком.
    /// </remarks>
    public override async Task<bool> SaveAsync()
    {
        if (_session.Document is { IsModified: true, HasConflict: true })
        {
            ShowConflict();
            return false;
        }

        return await _session.SaveAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Сохраняет само, если сохранять самому разрешено: тогда о вкладке человека не спросят. Не вышло —
    /// отметка остаётся, и спросит студия.
    /// </remarks>
    public override async ValueTask<bool> CanCloseAsync(DocumentCloseReason reason)
    {
        if (_session.AutoSaves)
            await _session.AutoSaveAsync();

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Холст — раньше сессии: он отдаёт выбор, корень и приложение формы, а сессия потом отпускает показ и
    /// документ — последняя аренда его закрывает.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_design is not null)
            _design.StateChanged -= OnDesignStateChanged;

        _canvas.RootTaken -= OnRootTaken;
        _session.Changed -= OnSessionChanged;
        _view.Sheet.Loaded -= OnSheetLoaded;

        _view.Mode.SelectionChanged -= OnModeChanged;
        _view.TakeTheirs.Click -= OnTakeTheirs;
        _view.KeepMine.Click -= OnKeepMine;
        _view.Rebuild.Click -= OnRebuild;
        _view.ShowProblem.Click -= OnShowProblem;
        _view.Notice.Closed -= OnNoticeClosed;
        _view.Conflict.Closed -= OnConflictClosed;
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

        _controls.Dispose();
        _canvas.Dispose();

        await _session.DisposeAsync();
    }

    /// <inheritdoc/>
    void IFormCanvasHost.Step(bool back)
    {
        if (_session.Edits is { } edits)
            _ = edits.StepAsync(back);
    }

    /// <inheritdoc/>
    /// <remarks>Форма вкладки — сама вкладка: убирать её некуда, закрывают вкладку.</remarks>
    void IFormCanvasHost.Remove(IReadOnlyList<FormSlot> forms)
    {
    }

    /// <inheritdoc/>
    void IFormCanvasHost.FrameAll() => _controls.FrameAll();

    /// <inheritdoc/>
    void IFormCanvasHost.LeftWindow()
    {
        if (_session.AutoSaves)
            _ = _session.AutoSaveAsync();
    }

    /// <summary>Берёт документ у службы, потом показ.</summary>
    private async Task OpenAsync()
    {
        await _session.OpenAsync();
        await _session.ShowAsync();
    }

    /// <summary>Что сменилось у формы — отметка несохранённого, вопрос о чужой записи, баннер и чип.</summary>
    private void OnSessionChanged(object? sender, FormChanges changes)
    {
        if (_disposed)
            return;

        if ((changes & (FormChanges.Opened | FormChanges.Text | FormChanges.Saved)) != 0)
            SetModified(_session.Document?.IsModified ?? false);

        if ((changes & (FormChanges.Opened | FormChanges.Conflict)) != 0)
            ShowConflict();

        if ((changes & (FormChanges.Opened | FormChanges.Root | FormChanges.State | FormChanges.Deleted | FormChanges.Closed | FormChanges.Problem)) != 0)
            ShowState();
    }

    /// <summary>
    /// Первый корень форма вписывает в холст: до него карточка стояла объявленным размером, без заголовка
    /// окна, и кадр, снятый тогда, обрезал бы заголовок сверху.
    /// </summary>
    /// <remarks>
    /// Корень, сменившийся потом, — правка или замена типов, — кадр не трогает: масштаб к этому времени
    /// выбирал человек.
    /// </remarks>
    private void OnRootTaken(object? sender, FormSlot slot)
    {
        if (_rootFramed)
            return;

        _rootFramed = true;
        Dispatcher.UIThread.Post(_controls.FrameAll, DispatcherPriority.Loaded);
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

    private void OnDesignStateChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            ShowState();
    }

    /// <summary>Отмена и возврат — история документа, откуда бы на вкладке ни нажали.</summary>
    /// <remarks>
    /// Холст ловит их сам и раньше, когда клавиатура у него; сюда всплывает то, что не взяли ни он, ни
    /// просмотр XAML, ни полоса.
    /// </remarks>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _session.Edits is not { } edits || _view.GetPlatformSettings()?.HotkeyConfiguration is not { } keys)
            return;

        if (keys.Undo.Any(gesture => gesture.Matches(e)))
        {
            e.Handled = true;
            _ = edits.StepAsync(back: true);
        }
        else if (keys.Redo.Any(gesture => gesture.Matches(e)))
        {
            e.Handled = true;
            _ = edits.StepAsync(back: false);
        }
    }

    private void Say(string message) => _context.GetService<IStudioStatus>()?.Show(message);

    private string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], values);
}
