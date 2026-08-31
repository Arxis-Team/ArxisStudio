using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ArxisStudio.Docking;

/// <summary>
/// Потянутая граница: чьё это деление и какими стали доли.
/// </summary>
/// <param name="Path">Путь к делению от корня — номера детей сверху вниз.</param>
/// <param name="Weights">Новые доли по числу детей.</param>
/// <remarks>
/// Деление адресуется путём, а не именем: имя есть только у группы. Заводить
/// имя и делению значило бы придумывать его на каждое перетаскивание и хранить
/// в файле то, на что никто не ссылается.
/// </remarks>
public sealed record DockResize(IReadOnlyList<int> Path, IReadOnlyList<double> Weights);

/// <summary>
/// Вкладка в пути или отпущенная: что несут и где сейчас курсор.
/// </summary>
/// <param name="Item">Имя панели.</param>
/// <param name="At">Точка на экране.</param>
/// <remarks>
/// Точка в пикселях экрана, а не окна, и в этом всё перетаскивание между
/// окнами: пока кнопка нажата, движения приходят окну, начавшему тягу, даже
/// когда курсор давно над чужим. Своё дерево оно разберёт само, а какое из
/// окон под курсором — знает лишь тот, у кого этих окон несколько.
/// </remarks>
public sealed record DockDrag(string Item, PixelPoint At);

/// <summary>
/// Дерево раскладки на экране.
/// </summary>
/// <remarks>
/// Вид ничего не решает: он показывает дерево, которое ему дали, и сообщает о
/// том, что сделал человек. Правку дерева делает владелец — так одно и то же
/// дерево одинаково ведёт себя в главном окне, в оторванном окне и в тесте, где
/// окна нет вовсе.
/// </remarks>
public class DockView : Decorator
{
    /// <summary>
    /// Сколько пикселей надо пройти, чтобы это считалось тягой, а не щелчком.
    /// </summary>
    private const double Threshold = 6;

    /// <summary>
    /// Насколько глубоко от края тянется зона «раздели».
    /// </summary>
    /// <remarks>
    /// Треть — как в Unity; там это промерено проходом курсора, и цифра сошлась
    /// с расчётом до пикселя. Глубина считается в долях своей стороны, поэтому
    /// у широкой низкой области верхняя зона выходит шире боковой — иначе угол
    /// доставался бы не тому краю.
    /// </remarks>
    private const double Third = 1.0 / 3;

    /// <summary>Ширина призрака отдельного окна.</summary>
    /// <remarks>
    /// Размер условный и нарочно небольшой: призрак говорит «будет своё окно»,
    /// а не «будет вот такого размера» — настоящий размер окно возьмёт себе
    /// само, у панели, которую несут.
    /// </remarks>
    private const double GhostWidth = 280;

    /// <inheritdoc cref="GhostWidth"/>
    private const double GhostHeight = 160;

    /// <summary>Дерево, которое показываем.</summary>
    public static readonly StyledProperty<DockNode?> RootProperty =
        AvaloniaProperty.Register<DockView, DockNode?>(nameof(Root));

    /// <summary>Где брать живые панели.</summary>
    public static readonly StyledProperty<DockItems?> ItemsProperty =
        AvaloniaProperty.Register<DockView, DockItems?>(nameof(Items));

    /// <summary>
    /// Что показать там, где показывать нечего.
    /// </summary>
    /// <remarks>
    /// Достаётся одной названной группе, а не всем пустым: родитель у контрола
    /// ровно один, и одна и та же заставка в двух местах кончилась бы
    /// исключением.
    /// </remarks>
    public static readonly StyledProperty<object?> EmptyProperty =
        AvaloniaProperty.Register<DockView, object?>(nameof(Empty));

    /// <summary>
    /// Имя группы, которая показывается даже пустой.
    /// </summary>
    /// <remarks>
    /// Остальные пустые группы не показываются вовсе — но и не пропадают из
    /// дерева. Разница видна на выключенном плагине: имена его панелей остаются
    /// на своих местах, места на экране не занимают, и стоит плагин включить,
    /// как панель возвращается туда же, где стояла, той же ширины.
    /// </remarks>
    public static readonly StyledProperty<string?> EmptyGroupProperty =
        AvaloniaProperty.Register<DockView, string?>(nameof(EmptyGroup));

    /// <summary>
    /// Виды групп по именам — их переносим, а не создаём заново.
    /// </summary>
    /// <remarks>
    /// Пересоздание вида группы стоило бы дороже, чем кажется: панель внутри
    /// потеряла бы прокрутку, выделение и всё, что контрол помнит о себе сам.
    /// </remarks>
    private readonly Dictionary<string, DockGroupView> _groups = new(StringComparer.Ordinal);

    /// <summary>Вкладка, на которой нажали, и где нажали.</summary>
    private (string Item, Point At)? _pressed;

    /// <summary>Что тащат прямо сейчас.</summary>
    private string? _dragged;

    /// <summary>
    /// Дерево предпросмотра — каким оно станет, если бросить сейчас.
    /// </summary>
    /// <remarks>
    /// Настоящее дерево при этом не трогают: человек ещё держит кнопку и волен
    /// увести вкладку куда угодно. Показывают предпросмотр, помнят настоящее.
    /// </remarks>
    private DockNode? _preview;

    /// <summary>Имя несомой панели: в предпросмотре у неё рисуется вкладка, но не тело.</summary>
    private string? _ghost;

    /// <summary>Призрак отдельного окна под курсором.</summary>
    private Border? _carry;

    /// <summary>
    /// Разметка, по которой целятся, — снятая до первого предпросмотра.
    /// </summary>
    /// <remarks>
    /// Целиться по тому, что на экране, нельзя: предпросмотр перекладывает
    /// области по-настоящему, и следующее движение мерило бы уже по будущей
    /// раскладке. Получилась бы петля — показанное меняло бы цель, а цель
    /// показанное. Поэтому разметка снимается один раз, в начале тяги, и
    /// держится до её конца; так же поступает Unity, где предпросмотр рисуется
    /// поверх неизменного дерева.
    /// </remarks>
    private IReadOnlyList<Frozen>? _aiming;

    static DockView()
    {
        RootProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ItemsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyGroupProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
    }

    /// <summary>Заводит вид и подписывается на мышь.</summary>
    /// <remarks>
    /// Обработчики перехватывающие: вкладка забирает нажатие себе — ей надо
    /// стать выбранной, — и до всплытия дело не дойдёт. Само нажатие мы при
    /// этом не помечаем разобранным: щелчок обязан работать как щелчок, пока
    /// человек не потянул.
    /// <para>
    /// Только перехватывающие, не оба маршрута разом: вид лежит и на пути
    /// вниз, и на пути вверх, и подписка на оба поднимала бы каждое движение
    /// дважды. Пока за движением ничего тяжёлого не стояло, это было незаметно;
    /// с предпросмотром — уже нет.
    /// </para>
    /// </remarks>
    public DockView()
    {
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Человек потянул границу.</summary>
    public event EventHandler<DockResize>? Resized;

    /// <summary>Вкладку несут; в поле — она и точка экрана под курсором.</summary>
    public event EventHandler<DockDrag>? Dragging;

    /// <summary>Вкладку отпустили; в поле — она и точка экрана.</summary>
    /// <remarks>
    /// Куда она попадёт, вид не решает: он видит одно своё дерево, а окон у
    /// студии несколько, и брошенная мимо всех — это отрыв в новое окно.
    /// </remarks>
    public event EventHandler<DockDrag>? Dropped;

    /// <summary>Человек попросил закрыть панель; в поле — её имя.</summary>
    public event EventHandler<string>? Closing;

    /// <inheritdoc cref="RootProperty"/>
    public DockNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    /// <inheritdoc cref="ItemsProperty"/>
    public DockItems? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <inheritdoc cref="EmptyProperty"/>
    public object? Empty
    {
        get => GetValue(EmptyProperty);
        set => SetValue(EmptyProperty, value);
    }

    /// <inheritdoc cref="EmptyGroupProperty"/>
    public string? EmptyGroup
    {
        get => GetValue(EmptyGroupProperty);
        set => SetValue(EmptyGroupProperty, value);
    }

    /// <summary>Показанный вид группы; null — такой на экране нет.</summary>
    /// <param name="groupId">Имя группы.</param>
    public DockGroupView? View(string groupId) =>
        _groups.TryGetValue(groupId, out var view) ? view : null;

    /// <summary>
    /// Строит экран заново по тому же дереву.
    /// </summary>
    /// <remarks>
    /// Нужно, когда изменилось не дерево, а то, что стоит за именами: панель
    /// выключенного плагина вернулась на своё место, и дерево этого не заметило
    /// — оно и не менялось.
    /// </remarks>
    public void Refresh() => Rebuild();

    /// <summary>
    /// Отнятый захват заканчивает тягу.
    /// </summary>
    /// <remarks>
    /// Захват отнимают чужое окно, Alt+Tab, всплывшее модальное окно. Без этого
    /// тяга осталась бы взведённой: подсветка висела бы на экране, а следующее
    /// движение мыши таскало бы вкладку с отпущенной кнопкой.
    /// <para>
    /// Проверять, кто именно потерял захват, не нужно: событие направленное и
    /// приходит только тому, у кого захват и был. Отделять чужую потерю от
    /// своей было бы охраной от того, чего не бывает.
    /// </para>
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _pressed = null;

        Stop();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Подсветка живёт не в нас, а в слое поверх окна: уходя, забираем её с
        // собой, иначе она осталась бы висеть над пустым местом.
        Stop();
    }

    /// <summary>Запоминает вкладку, на которой нажали.</summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if ((e.Source as Visual)?.FindAncestorOfType<AxTabItem>() is not { } tab)
            return;

        if (tab.FindAncestorOfType<DockGroupView>()?.Item(tab) is { } item)
            _pressed = (item, e.GetPosition(this));
    }

    /// <summary>Начинает тягу, когда её уже не спутать со щелчком, и ведёт её.</summary>
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);

        if (_dragged is null && _pressed is { } pressed)
        {
            // Порог нужен, чтобы дрогнувшая рука не растаскивала раскладку:
            // щелчок по вкладке почти всегда сдвигает мышь на пиксель-другой.
            if (Math.Abs(point.X - pressed.At.X) < Threshold
                && Math.Abs(point.Y - pressed.At.Y) < Threshold)
            {
                return;
            }

            _dragged = pressed.Item;
            _pressed = null;

            e.Pointer.Capture(this);
        }

        if (_dragged is { } carried && Screen(point) is { } at)
            Dragging?.Invoke(this, new DockDrag(carried, at));
    }

    /// <summary>Сообщает, что вкладку отпустили и где.</summary>
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragged;
        var at = Screen(e.GetPosition(this));

        _pressed = null;
        _dragged = null;

        if (dragged is null)
            return;

        // Сперва о броске, потом отпустить захват. Отпускание синхронно
        // поднимает «захват потерян», а тот заканчивает тягу и забывает
        // разметку прицела — сделай мы это раньше, спрашивать «куда бросили»
        // было бы уже не по чему, и вкладка улетала бы в своё окно.
        if (at is { } where)
            Dropped?.Invoke(this, new DockDrag(dragged, where));

        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Точка в пикселях экрана; null — вид не на экране.
    /// </summary>
    /// <remarks>
    /// Окно должно быть не просто найдено, а показано: закрытое окно у вида
    /// ещё числится, но экранных пикселей у него уже нет, и перевод в них
    /// кончается исключением. Закрываются же окна прямо посреди тяги —
    /// опустевшее оторванное закрывает себя само.
    /// </remarks>
    private PixelPoint? Screen(Point point) =>
        TopLevel.GetTopLevel(this) is { IsVisible: true } top && this.TranslatePoint(point, top) is { } local
            ? top.PointToScreen(local)
            : null;

    /// <summary>Точка экрана в координатах этого вида; null — вид не на экране.</summary>
    private Point? Local(PixelPoint at) =>
        TopLevel.GetTopLevel(this) is { IsVisible: true } top
            ? top.TranslatePoint(top.PointToClient(at), this)
            : null;

    /// <summary>
    /// Куда попадёт вкладка, брошенная в эту точку экрана; null — мимо этого дерева.
    /// </summary>
    /// <param name="at">Точка на экране.</param>
    /// <param name="item">Какую панель несут.</param>
    /// <remarks>
    /// Спрашивают об этом каждое дерево по очереди — и то, в котором тягу
    /// начали, и деревья остальных окон. Курсор в каждый миг над одним из них,
    /// поэтому ответ есть не больше чем у одного.
    /// <para>
    /// Ответ зависит от того, что именно несут: место в полосе вкладок
    /// считается среди <b>остальных</b> вкладок, иначе перестановка внутри
    /// полосы промахивалась бы на единицу.
    /// </para>
    /// </remarks>
    public DockAim? Aim(PixelPoint at, string item) =>
        Local(at) is { } point ? Target(point, item) : null;

    /// <summary>
    /// Показывает, каким станет дерево, если бросить вкладку сейчас.
    /// </summary>
    /// <param name="tree">Будущее дерево.</param>
    /// <param name="ghost">Имя несомой панели.</param>
    /// <remarks>
    /// Предпросмотр — это и есть правка: дерево приходит сюда из той же
    /// функции, что применится при броске, и соврать поэтому не может. Пока
    /// вместо него рисовали плашку, она обещала половину области, а новичок
    /// получал половину доли соседа.
    /// <para>
    /// Тело призрака остаётся пустым не для красоты: панель в этот миг ещё
    /// живёт в дереве-источнике, а родитель у контрола Avalonia ровно один —
    /// возьми мы её сюда, она пропала бы из своего окна на полпути.
    /// </para>
    /// </remarks>
    public void Preview(DockNode tree, string ghost)
    {
        ArgumentNullException.ThrowIfNull(tree);

        Vanish();

        _preview = tree;
        _ghost = ghost;

        Rebuild();
    }

    /// <summary>
    /// Показывает призрак отдельного окна под курсором.
    /// </summary>
    /// <param name="at">Точка на экране.</param>
    /// <param name="title">Подпись несомой панели.</param>
    /// <remarks>
    /// Середина области значит «оторви», и человеку надо это увидеть: пустая
    /// рамка с подписью под курсором объясняет жест лучше любого слова.
    /// </remarks>
    public void Carry(PixelPoint at, string title)
    {
        Restore();

        if (OverlayLayer.GetOverlayLayer(this) is not { } layer
            || Local(at) is not { } point
            || this.TranslatePoint(point, layer) is not { } corner)
        {
            Vanish();
            return;
        }

        _carry ??= new Border
        {
            Classes = { "dock-ghost" },
            IsHitTestVisible = false,
            Width = GhostWidth,
            Height = GhostHeight,
            Child = new TextBlock(),
        };

        if (_carry.Child is TextBlock label)
            label.Text = title;

        if (!layer.Children.Contains(_carry))
            layer.Children.Add(_carry);

        Canvas.SetLeft(_carry, corner.X - (GhostWidth / 2));
        Canvas.SetTop(_carry, corner.Y - (GhostHeight / 2));
    }

    /// <summary>Снимает предпросмотр и призрак, возвращая настоящее дерево.</summary>
    /// <remarks>
    /// Тяги не касается: пока курсор идёт над чужим окном, показывать своему
    /// нечего, а вкладку несёт по-прежнему оно.
    /// </remarks>
    public void Clear()
    {
        _aiming = null;

        Vanish();
        Restore();
    }

    /// <summary>Область на прицеле: где была, докуда шапка и где середины вкладок.</summary>
    private sealed record Frozen(
        string Id, Rect Area, double Header, IReadOnlyList<(string Item, double Middle)> Slots);

    /// <summary>Куда попадёт брошенная вкладка; null — мимо всего.</summary>
    private DockAim? Target(Point point, string item)
    {
        _aiming ??= Freeze();

        if (_aiming.FirstOrDefault(group => group.Area.Contains(point)) is not { } aimed)
            return null;

        var size = aimed.Area.Size;

        if (size.Width <= 0 || size.Height <= 0)
            return null;

        var local = point - aimed.Area.Position;

        // Полоса вкладок сильнее всего: она и есть «встань рядом», и место в
        // ней человек выбирает тем же движением.
        if (local.Y < aimed.Header)
            return new DockAim.Tab(aimed.Id, Slot(aimed, local.X, item));

        var across = local.X / size.Width;
        var down = local.Y / size.Height;

        (double Share, DockSide Side)[] edges =
        [
            (across, DockSide.Left),
            (1 - across, DockSide.Right),
            (down, DockSide.Top),
            (1 - down, DockSide.Bottom),
        ];

        var near = edges.MinBy(edge => edge.Share);

        // Дальше трети от каждого края — это середина, а середина значит
        // «оторви в своё окно»: так человеку не нужен свободный рабочий стол.
        return near.Share < Third
            ? new DockAim.Split(aimed.Id, near.Side)
            : new DockAim.Float();
    }

    /// <summary>Снимает разметку показанных областей — по ней и целятся всю тягу.</summary>
    private IReadOnlyList<Frozen> Freeze() =>
    [
        .. _groups.Values
            .Where(group => group.IsVisible && group.TranslatePoint(default, this) is not null)
            .Select(group => new Frozen(
                group.Id,
                new Rect(group.TranslatePoint(default, this)!.Value, group.Bounds.Size),
                group.HeaderHeight,
                group.Slots())),
    ];

    /// <summary>
    /// Место в полосе, куда встанет вкладка, брошенная на этом расстоянии слева.
    /// </summary>
    /// <remarks>
    /// Номер — в счёте дерева, а не показанных вкладок: панель выключенного
    /// плагина остаётся в группе именем, вкладки у неё нет, и место, посчитанное
    /// по экрану, уехало бы мимо.
    /// <para>
    /// Несомая вкладка не считается: место человек выбирает среди остальных, и
    /// <see cref="DockTree.Attach"/> убирает её из группы ровно так же. Считай мы
    /// её — перестановка внутри полосы промахивалась бы на единицу.
    /// </para>
    /// </remarks>
    private int Slot(Frozen aimed, double x, string item)
    {
        if (Root is not { } root || DockTree.Group(root, aimed.Id) is not { } group)
            return 0;

        var rest = group.Items
            .Where(id => !string.Equals(id, item, StringComparison.Ordinal))
            .ToList();

        for (var at = 0; at < rest.Count; at++)
        {
            var slot = aimed.Slots.FirstOrDefault(shown =>
                string.Equals(shown.Item, rest[at], StringComparison.Ordinal));

            // У панели выключенного плагина вкладки нет — мерить нечего, и место
            // она делит с ближайшей видимой соседкой слева.
            if (slot.Item is not null && x < slot.Middle)
                return at;
        }

        return rest.Count;
    }

    /// <summary>Группа под указателем; null — там её нет.</summary>
    private DockGroupView? Group(Point point) =>
        new Rect(Bounds.Size).Contains(point)
            ? _groups.Values.FirstOrDefault(group => Inside(group, point))
            : null;

    /// <summary>Попадает ли точка в эту группу.</summary>
    private bool Inside(DockGroupView group, Point point) =>
        group.TranslatePoint(default, this) is { } origin
        && new Rect(origin, group.Bounds.Size).Contains(point);

    /// <summary>Убирает призрак отдельного окна со слоя.</summary>
    private void Vanish()
    {
        if (_carry is { Parent: Panel layer })
            layer.Children.Remove(_carry);

        _carry = null;
    }

    /// <summary>Возвращает вид к настоящему дереву, если показывался предпросмотр.</summary>
    private void Restore()
    {
        if (_preview is null)
            return;

        _preview = null;
        _ghost = null;

        Rebuild();
    }

    /// <summary>
    /// Заканчивает тягу и убирает всё, что она показывала.
    /// </summary>
    /// <remarks>
    /// Снять показанное и забыть, что вкладку несут, — разные дела, и путать их
    /// нельзя: пока курсор идёт над чужим окном, своему показывать нечего, а
    /// тяга всё ещё его. Смешай их — и тяга обрывалась бы на выходе из окна.
    /// </remarks>
    private void Stop()
    {
        _dragged = null;

        Clear();
    }

    /// <summary>Строит экран заново по нынешнему дереву.</summary>
    private void Rebuild()
    {
        // Сперва отпускаем всё и только потом строим. Родитель у контрола
        // Avalonia ровно один, и панель, переехавшая в соседнюю группу,
        // встала бы на новое место исключением, не уйдя со старого.
        foreach (var group in _groups.Values)
        {
            (group.Parent as Panel)?.Children.Remove(group);
            group.Release();
        }

        Child = null;

        var alive = new HashSet<string>(StringComparer.Ordinal);

        if ((_preview ?? Root) is { } root && Items is { } items)
            Child = Build(root, [], items, alive);

        foreach (var id in _groups.Keys.Where(id => !alive.Contains(id)).ToList())
            _groups.Remove(id);
    }

    /// <summary>
    /// Строит узел: группу — видом, деление — сеткой со сплиттерами.
    /// </summary>
    /// <returns>Контрол либо null — показывать тут нечего.</returns>
    private Control? Build(DockNode node, IReadOnlyList<int> path, DockItems items, HashSet<string> alive)
    {
        if (node is DockGroup group)
        {
            var named = string.Equals(group.Id, EmptyGroup, StringComparison.Ordinal);

            // Группа без единой живой панели места не занимает — но из дерева
            // не уходит: там остаются имена, и по ним панель вернётся сюда же.
            if (!named && !group.Items.Any(id => items.Find(id) is not null))
                return null;

            alive.Add(group.Id);

            if (!_groups.TryGetValue(group.Id, out var view))
            {
                view = new DockGroupView();
                view.Chosen += (_, id) => Chosen?.Invoke(this, id);
                view.Closing += (_, id) => Closing?.Invoke(this, id);
                _groups[group.Id] = view;
            }

            view.Update(group, items, named ? Empty : null, _ghost);

            return view;
        }

        var split = (DockSplit)node;
        var down = split.Orientation == DockOrientation.Vertical;
        var shares = DockTree.Shares(split);
        var shown = new List<(Control Control, int At)>();

        for (var at = 0; at < split.Children.Count; at++)
        {
            if (Build(split.Children[at], [.. path, at], items, alive) is { } control)
                shown.Add((control, at));
        }

        if (shown.Count == 0)
            return null;

        if (shown.Count == 1)
            return shown[0].Control;

        var grid = new Grid();
        var visible = shown.Select(child => child.At).ToList();

        for (var number = 0; number < shown.Count; number++)
        {
            if (number > 0)
                Line(grid, down, path, shares, visible);

            Put(grid, down, shown[number].Control,
                Row(grid, down, new GridLength(shares[shown[number].At], GridUnitType.Star)));
        }

        return grid;
    }

    /// <summary>Ставит между соседями границу, за которую можно взяться.</summary>
    private void Line(
        Grid grid,
        bool down,
        IReadOnlyList<int> path,
        IReadOnlyList<double> shares,
        IReadOnlyList<int> visible)
    {
        var splitter = new GridSplitter
        {
            Classes = { down ? "dock-h" : "dock-v" },
            ResizeDirection = down ? GridResizeDirection.Rows : GridResizeDirection.Columns,
        };

        splitter.DragCompleted += (_, _) =>
            Resized?.Invoke(this, new DockResize(path, Spread(shares, visible, Shares(grid, down))));

        Put(grid, down, splitter, Row(grid, down, new GridLength(1)));
    }

    /// <summary>
    /// Раскладывает померенные доли по местам, не трогая спрятанных.
    /// </summary>
    /// <remarks>
    /// На экране могли стоять не все дети: у соседа выключили плагин, и его
    /// группа ничего не показывает. Отдать в дерево доли одних лишь видимых
    /// значило бы отобрать место у спрятанного — и панель, вернувшись, встала
    /// бы шириной в ноль. Поэтому видимые делят между собой ровно то место,
    /// которое им и принадлежало.
    /// </remarks>
    private static IReadOnlyList<double> Spread(
        IReadOnlyList<double> all,
        IReadOnlyList<int> visible,
        IReadOnlyList<double> measured)
    {
        if (measured.Count != visible.Count)
            return all;

        var room = visible.Sum(at => all[at]);
        var next = all.ToList();

        for (var number = 0; number < visible.Count; number++)
            next[visible[number]] = measured[number] * room;

        return DockTree.Normalize(next);
    }

    /// <summary>Заводит очередную полосу сетки и возвращает её номер.</summary>
    private static int Row(Grid grid, bool down, GridLength size)
    {
        if (down)
            grid.RowDefinitions.Add(new RowDefinition(size));
        else
            grid.ColumnDefinitions.Add(new ColumnDefinition(size));

        return (down ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count) - 1;
    }

    /// <summary>Ставит контрол в полосу с указанным номером.</summary>
    private static void Put(Grid grid, bool down, Control control, int at)
    {
        if (down)
            Grid.SetRow(control, at);
        else
            Grid.SetColumn(control, at);

        grid.Children.Add(control);
    }

    /// <summary>Снимает доли с сетки.</summary>
    /// <remarks>
    /// Берём объявленные длины, а не занятое место: сплиттер переписывает
    /// длины сразу, а место обновится только на следующем проходе раскладки, и
    /// доли вышли бы вчерашними — граница возвращалась бы на место, едва её
    /// отпустили.
    /// <para>
    /// Содержимое стоит по чётным полосам, между ними линии; единица измерения
    /// для отбора не годится — сплиттер вправе переписать доли в пиксели, и
    /// тогда «звёздочка» перестала бы отличать одно от другого.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<double> Shares(Grid grid, bool down)
    {
        var count = down ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
        var sizes = new List<double>();

        for (var at = 0; at < count; at += 2)
            sizes.Add(down ? grid.RowDefinitions[at].Height.Value : grid.ColumnDefinitions[at].Width.Value);

        return DockTree.Normalize(sizes);
    }
}
