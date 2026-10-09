using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Что холст форм спрашивает у того, кто его показывает: вкладки или доски.</summary>
internal interface IFormCanvasHost
{
    /// <summary>
    /// Вид хозяина: полоса, холст и XAML — там, где с холстом работают. Фокус, пришедший внутрь него, и нажатие в
    /// нём ставят холст впереди (<see cref="FormFront"/>), а пока его не видно, холст впереди не стоит.
    /// </summary>
    Control HostView { get; }

    /// <summary>Отмена или возврат: у вкладки — история её документа, у доски — её общая история.</summary>
    /// <param name="back">Отменить, а не вернуть.</param>
    void Step(bool back);

    /// <summary>
    /// Delete: элементы форм холст уже убрал из их текста, а корень из документа не удалить — что делать с
    /// формами, выбранными целиком, решает хозяин.
    /// </summary>
    /// <param name="request">Просьба ядра: выбранное целиком — контейнеры и выбранные без контейнера.</param>
    void Remove(SurfaceDeleteRequestedEventArgs request);

    /// <summary>Показать всё, что на холсте.</summary>
    void FrameAll();

    /// <summary>Окно студии, где стоит холст, потеряло фокус: пора сохранить.</summary>
    void LeftWindow();

    /// <summary>Пункты контекстного меню холста: у вкладки — правки формы, у доски — ещё и свои.</summary>
    /// <param name="form">Правки строения выбранного в форме, с которой работают; пусто — формы нет.</param>
    /// <param name="request">О чём меню попросило ядро; null — попросили клавишей, у выбранного.</param>
    /// <returns>Пункты и черты между группами.</returns>
    IReadOnlyList<Control> MenuItems(IReadOnlyList<Control> form, SurfaceContextRequest? request);
}

/// <summary>
/// Холст форм: формы на одном холсте дизайнера и правка их текста — выбор, жесты, правки строения, меню и
/// XAML выбранного.
/// </summary>
/// <remarks>
/// <para>
/// <b>Одна дорога у вкладки и у доски.</b> Вкладка ставит на холст одну форму, доска — все, что на виду, а
/// правят их одинаково: жест холста — правка текста той формы, где он сделан (<see cref="FormGestures"/>),
/// правки строения — у формы выбранного (<see cref="FormCommands"/>).
/// </para>
/// <para>
/// <b>Выбор — пути, а не контролы.</b> Холст помнит, какие элементы какой формы выбраны, и после правки,
/// замены поколения и отмены выбирает их заново (<c>Reselect</c>): контролы меняются, пути — нет.
/// </para>
/// <para>
/// <b>Поколение не держит.</b> Холст — участник замены: на замену он замирает стоп-кадром, отдаёт корни и
/// приложения форм и выбор, после — берёт новые; и тот, кто одалживает корень на время записи сессии
/// разметки (<see cref="IXamlRootLender"/>).
/// </para>
/// </remarks>
internal sealed partial class FormCanvas : IXamlDesignParticipant, IXamlRootLender, IDisposable
{
    private readonly IFormCanvasHost _host;
    private readonly Control _hostView;
    private readonly FormFront _front;
    private readonly UiDesignerOptions _options;
    private readonly IStudioXamlDesign? _design;
    private readonly FormSnapshots? _snapshots;
    private readonly FormGestures _gestures;
    private readonly IDisposable? _participation;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<FormSlot> _slots = [];

    private IDisposable? _frozen;
    private IDisposable? _gesture;
    private Window? _window;
    private bool _disposed;

    /// <summary>Заводит холст форм.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="sheet">Холст дизайнера.</param>
    /// <param name="code">Просмотр XAML выбранной формы; null — его нет.</param>
    /// <param name="host">Хозяин холста.</param>
    /// <param name="options">Часы и паузы модуля.</param>
    public FormCanvas(IStudioContext context, UiDesignerView sheet, AxCodeView? code, IFormCanvasHost host, UiDesignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);

        Context = context;
        Sheet = sheet;
        _code = code;
        _host = host;
        _options = options;
        _design = context.XamlDesign();
        _snapshots = FormSnapshots.For(options);

        // Esc — к тому, в чём стоит выбранное, как в дизайнерах Visual Studio; на корне снимает выбор.
        var clear = IndexOf(sheet.KeyCommands, SurfaceKeyCommands.ClearSelection);

        sheet.KeyCommands.Remove(SurfaceKeyCommands.ClearSelection);
        sheet.KeyCommands.Insert(
            Math.Max(0, clear),
            new SurfaceKeyCommand(ParentCommand, new KeyGesture(Key.Escape), _ => SelectParent()));

        WireStructure(sheet);

        _gestures = new FormGestures(sheet, this, context.Strings);

        sheet.SurfaceSelectionChanged += OnSheetSelectionChanged;
        sheet.PropertyChanged += OnSheetPropertyChanged;
        sheet.AttachedToVisualTree += OnAttached;
        sheet.DetachedFromVisualTree += OnDetached;

        if (code is not null)
            code.CaretMoved += OnCaretMoved;

        if (_design is not null)
            _participation = _design.Register(this);

        Watch(TopLevel.GetTopLevel(sheet) as Window);

        // Панели модуля идут за холстом впереди: тем, с которым работают, пока его видно.
        _hostView = host.HostView;
        _front = FormFront.Of(context);
        _hostView.AddHandler(InputElement.GotFocusEvent, OnHostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _hostView.AddHandler(InputElement.PointerPressedEvent, OnHostPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _hostView.AttachedToVisualTree += OnHostTreeChanged;
        _hostView.DetachedFromVisualTree += OnHostTreeChanged;
        _front.Add(this);
    }

    /// <summary>Идентификатор команды холста «к родителю».</summary>
    public const string ParentCommand = "ui-designer.parent";

    /// <summary>Контекст модуля.</summary>
    public IStudioContext Context { get; }

    /// <summary>Холст.</summary>
    public UiDesignerView Sheet { get; }

    /// <summary>Хозяин холста.</summary>
    public IFormCanvasHost Host => _host;

    /// <summary>Формы на холсте.</summary>
    public IReadOnlyList<FormSlot> Slots => _slots;

    /// <summary>Холст замер на замену поколения.</summary>
    public bool IsFrozen => _frozen is not null;

    /// <summary>Холст убран.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Видно ли холст: вид его хозяина стоит в дереве окна.</summary>
    public bool IsOnScreen => !_disposed && _hostView.IsAttachedToVisualTree();

    /// <summary>Жизнь холста: записи, которые он начал, кончаются с ним.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>Ставит форму на холст; её карточка — <paramref name="item"/> или та, что даст <see cref="Place"/>.</summary>
    /// <param name="session">Сессия формы.</param>
    /// <param name="item">Карточка формы; null — форма пока не на виду.</param>
    /// <returns>Форма на холсте.</returns>
    public FormSlot Add(FormSession session, UiDesignerFormItem? item)
    {
        ArgumentNullException.ThrowIfNull(session);

        var slot = new FormSlot(this, session, _snapshots);

        _slots.Add(slot);
        session.Changed += OnSessionChanged;
        session.SelectRequested += OnSelectRequested;

        Place(slot, item);
        Reconsider();

        return slot;
    }

    /// <summary>
    /// Даёт форме карточку или забирает её: доска держит карточки только у видимых форм.
    /// </summary>
    /// <param name="slot">Форма.</param>
    /// <param name="item">Карточка; null — форма уходит с виду.</param>
    public void Place(FormSlot slot, UiDesignerFormItem? item)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (ReferenceEquals(slot.Item, item))
            return;

        using (Syncing())
            slot.Bind(item);

        Adopt(slot);

        // Приложение — раньше корня: корень входит в дерево уже под ним и не входит второй раз.
        if (item is not null && _frozen is null)
        {
            if (slot.TakeApplication())
                slot.QueueSnapshot();

            if (slot.TakeRoot())
                RootTaken?.Invoke(this, slot);

            Reselect();
            slot.QueueSnapshot();
        }

        // Форма встала на холст или ушла с него: форма, с которой работают, могла смениться.
        Reconsider();
    }

    /// <summary>Снимает форму с холста: выбор её уходит, карточка отдаёт корень.</summary>
    /// <param name="slot">Форма.</param>
    public void Remove(FormSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (!_slots.Remove(slot))
            return;

        slot.Session.Changed -= OnSessionChanged;
        slot.Session.SelectRequested -= OnSelectRequested;

        Forget(slot);

        using (Syncing())
            slot.Bind(null);
    }

    /// <summary>Форма, которой принадлежит то, что стоит на холсте: её карточка или контрол в ней.</summary>
    /// <param name="target">Карточка или контрол формы.</param>
    public FormSlot? SlotOf(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var slot in _slots)
        {
            if (slot.Item is { } item && (ReferenceEquals(item, target) || item.IsVisualAncestorOf(target)))
                return slot;
        }

        return null;
    }

    /// <summary>Форма показала первый корень или новый: хозяин может вписать её в кадр.</summary>
    public event EventHandler<FormSlot>? RootTaken;

    /// <inheritdoc/>
    /// <remarks>
    /// Замена поколения: холст замирает на кадре — человек видит формы, а не пустоту, — и отдаёт всё, что
    /// построено из уходящих типов: выбор, корни, приложения. Пути выбора остаются у холста.
    /// </remarks>
    public ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        _frozen ??= Sheet.Freeze();

        using (Syncing())
        {
            Sheet.SelectedItems?.Clear();

            foreach (var slot in _slots)
                slot.Release();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Показы уже взяли новые корни (их <c>RootChanged</c> пришёл раньше): осталось ожить и выбрать.</remarks>
    public ValueTask RestoreAsync(CancellationToken cancellationToken)
    {
        foreach (var slot in _slots)
        {
            slot.TakeApplication();
            slot.TakeRoot();
            slot.QueueSnapshot();
        }

        _frozen?.Dispose();
        _frozen = null;

        Reselect();

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Окно на время записи сессии отдаётся целиком: пока карточка держит его содержимое, окно пусто, и
    /// запись ушла бы в окно, которого никто не видит. Чужой корень — не наш: отдавать нечего.
    /// </remarks>
    public IDisposable Lend(object root)
    {
        foreach (var slot in _slots)
        {
            if (slot.Item is { } item && ReferenceEquals(item.Root, root))
                return item.SuspendRoot();
        }

        return Nothing.Instance;
    }

    /// <summary>Снимает все формы, которые снимок ещё не застал: холст снова виден.</summary>
    public void QueueSnapshots()
    {
        foreach (var slot in _slots)
            slot.QueueSnapshot();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Сперва то, что держит объекты поколения: выбор, корни, приложения. Сессии форм холст не отпускает — их
    /// отпускает тот, кто их завёл, после холста.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();

        _gesture?.Dispose();
        _gesture = null;
        _participation?.Dispose();
        _gestures.Dispose();

        Sheet.SurfaceSelectionChanged -= OnSheetSelectionChanged;
        Sheet.PropertyChanged -= OnSheetPropertyChanged;
        Sheet.AttachedToVisualTree -= OnAttached;
        Sheet.DetachedFromVisualTree -= OnDetached;
        UnwireStructure(Sheet);

        if (_code is not null)
            _code.CaretMoved -= OnCaretMoved;

        Watch(null);

        _hostView.RemoveHandler(InputElement.GotFocusEvent, OnHostFocus);
        _hostView.RemoveHandler(InputElement.PointerPressedEvent, OnHostPressed);
        _hostView.AttachedToVisualTree -= OnHostTreeChanged;
        _hostView.DetachedFromVisualTree -= OnHostTreeChanged;
        _front.Remove(this);

        using (Syncing())
        {
            Sheet.SelectedItems?.Clear();

            foreach (var slot in _slots)
            {
                slot.Session.Changed -= OnSessionChanged;
                slot.Session.SelectRequested -= OnSelectRequested;
                slot.Bind(null);
            }
        }

        _slots.Clear();
        _frozen?.Dispose();
        _frozen = null;
        _lifetime.Dispose();
    }

    /// <summary>Форма, которой принадлежит сессия.</summary>
    private FormSlot? SlotOf(FormSession session) => _slots.Find(slot => ReferenceEquals(slot.Session, session));

    private void OnSessionChanged(object? sender, FormChanges changes)
    {
        if (_disposed || sender is not FormSession session || SlotOf(session) is not { } slot)
            return;

        if ((changes & FormChanges.Opened) != 0 && ReferenceEquals(slot, Active))
            _ = RefreshCodeAsync();

        // Новый показ приносит корень и приложение вместе: приложение встаёт раньше — корень входит в дерево уже
        // одетым и не входит второй раз. Уходящий уносит корень раньше приложения: иначе форма перед уходом зря
        // входила бы в дерево заново без своих стилей.
        var dressFirst = session.Shown?.Root is not null;

        if (dressFirst)
            FollowApplication(slot, changes);

        FollowRoot(slot, changes);

        if (!dressFirst)
            FollowApplication(slot, changes);

        if ((changes & FormChanges.Text) != 0 && ReferenceEquals(slot, Active))
            _ = RefreshCodeAsync();

        if ((changes & (FormChanges.Opened | FormChanges.Text | FormChanges.Closed | FormChanges.Deleted)) != 0
            && ReferenceEquals(slot, Active))
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        if ((changes & (FormChanges.Text | FormChanges.Objects)) != 0)
        {
            slot.Mark();
            Reselect();
        }

        // Снимок отвечает файлу: снимается документ, сошедшийся с диском, — сохранённый или принятый снаружи,
        // — а не каждая правка.
        if ((changes & FormChanges.Saved) != 0 && session.Document is { IsModified: false })
            slot.QueueSnapshot();
    }

    /// <summary>Корень показа сменился: карточка берёт новый.</summary>
    /// <remarks>Корень, сменившийся под стоп-кадром, — новый корень замены: показ ставит его раньше, чем холст оживёт.</remarks>
    private void FollowRoot(FormSlot slot, FormChanges changes)
    {
        if ((changes & FormChanges.Root) == 0)
            return;

        if (slot.TakeRoot())
            RootTaken?.Invoke(this, slot);

        Reselect();
        slot.QueueSnapshot();
    }

    /// <summary>Приложение формы сменилось: карточка берёт новое.</summary>
    /// <remarks>Приложение, сменившееся под стоп-кадром, холст возьмёт, когда оживёт.</remarks>
    private void FollowApplication(FormSlot slot, FormChanges changes)
    {
        if ((changes & FormChanges.Application) == 0 || _frozen is not null || !slot.TakeApplication())
            return;

        Reselect();
        slot.QueueSnapshot();
    }

    private void OnSelectRequested(object? sender, IReadOnlyList<XamlElementPath> paths)
    {
        if (!_disposed && sender is FormSession session && SlotOf(session) is { } slot)
            Select(slot, paths);
    }

    /// <summary>
    /// Пока на холсте идёт жест, замена типов ждёт: перестроенный под рукой холст бросил бы жест на середине.
    /// </summary>
    private void OnSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != SurfaceView.IsInteractingProperty || _design is null)
            return;

        if (Sheet.IsInteracting)
        {
            var name = (Active ?? _slots.FirstOrDefault())?.Session.Path.FileName ?? string.Empty;

            _gesture ??= _design.Defer(string.Format(CultureInfo.CurrentCulture, Context.Strings["form.defer.gesture"], name));
        }
        else
        {
            _gesture?.Dispose();
            _gesture = null;
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Watch(TopLevel.GetTopLevel(Sheet) as Window);

        // Вне окна форму не разложить и не снять: снимок, пропущенный тогда, снимается по возвращении.
        QueueSnapshots();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Watch(null);

    /// <summary>Фокус пришёл внутрь вида хозяина: с холстом работают.</summary>
    private void OnHostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!_disposed)
            _front.Worked(this);
    }

    /// <summary>
    /// В виде хозяина нажали указатель: с холстом работают, даже если фокус в нём и был — пока он там стоял, рядом
    /// могли открыть вкладку, и второй раз холст его не получит.
    /// </summary>
    private void OnHostPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_disposed)
            _front.Worked(this);
    }

    /// <summary>Вид хозяина встал на экран или ушёл с него.</summary>
    private void OnHostTreeChanged(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_disposed)
            _front.Review();
    }

    /// <summary>Слушает уход из окна, где стоит холст: оторванную вкладку несёт другое окно.</summary>
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
        if (!_disposed)
            _host.LeftWindow();
    }

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
