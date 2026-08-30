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
/// Брошенная вкладка: что тащили, куда и с какой стороны.
/// </summary>
/// <param name="Item">Имя панели, которую тащили.</param>
/// <param name="Group">Имя группы, на которую бросили.</param>
/// <param name="Side">
/// <see cref="DockSide.Tab"/> — соседней вкладкой в ту же группу; иначе группа
/// делится, и панель встаёт с этой стороны.
/// </param>
public sealed record DockDrop(string Item, string Group, DockSide Side);

/// <summary>
/// Вынесенная за пределы дерева вкладка: что вынесли и куда на экране.
/// </summary>
/// <param name="Item">Имя панели.</param>
/// <param name="At">Точка на экране, где её отпустили.</param>
/// <remarks>
/// Точка в пикселях экрана, а не окна: оторванное окно откроется где-то там же,
/// а окон к тому времени может быть уже несколько.
/// </remarks>
public sealed record DockTear(string Item, PixelPoint At);

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
    /// Какая часть области с краю значит «раздели», а не «встань вкладкой».
    /// </summary>
    /// <remarks>
    /// Четверть — как в Unity: край достаточно широк, чтобы попасть в него не
    /// целясь, и достаточно узок, чтобы середина осталась серединой.
    /// </remarks>
    private const double Edge = 0.25;

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

    /// <summary>Подсветка места, куда бросят.</summary>
    private Border? _hint;

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
    /// </remarks>
    public DockView()
    {
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Человек потянул границу.</summary>
    public event EventHandler<DockResize>? Resized;

    /// <summary>Человек перетащил вкладку на новое место.</summary>
    public event EventHandler<DockDrop>? Dropped;

    /// <summary>Человек попросил закрыть панель; в поле — её имя.</summary>
    public event EventHandler<string>? Closing;

    /// <summary>
    /// Человек вынес вкладку за пределы дерева.
    /// </summary>
    /// <remarks>
    /// Это не бросок мимо: за пределами дерева ничего нет, и отпустить там
    /// вкладку человек может только нарочно. Что с ней делать — заводить окно
    /// или вернуть на место — решает владелец дерева.
    /// </remarks>
    public event EventHandler<DockTear>? Torn;

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

        if (_dragged is not null)
            Highlight(Target(point));
    }

    /// <summary>Бросает вкладку туда, где отпустили.</summary>
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragged;
        var point = e.GetPosition(this);
        var target = dragged is null ? null : Target(point);

        _pressed = null;
        Stop();

        if (dragged is null)
            return;

        e.Pointer.Capture(null);

        if (target is { } place)
        {
            Dropped?.Invoke(this, new DockDrop(dragged, place.Group, place.Side));
            return;
        }

        // Вне дерева отпускают нарочно: там ничего нет, и промахнуться туда
        // мимо цели нельзя. Внутри дерева, но мимо области — попадание в
        // границу между ними, и это как раз промах: вкладка остаётся где была.
        if (!new Rect(Bounds.Size).Contains(point) && Screen(point) is { } at)
            Torn?.Invoke(this, new DockTear(dragged, at));
    }

    /// <summary>Точка в пикселях экрана; null — окна под видом нет.</summary>
    private PixelPoint? Screen(Point point) =>
        TopLevel.GetTopLevel(this) is { } top && this.TranslatePoint(point, top) is { } local
            ? top.PointToScreen(local)
            : null;

    /// <summary>Куда попадёт брошенная вкладка; null — мимо всего.</summary>
    private (string Group, DockSide Side)? Target(Point point)
    {
        if (Group(point) is not { } group || group.TranslatePoint(default, this) is not { } origin)
            return null;

        var size = group.Bounds.Size;
        var local = point - origin;

        // Полоса вкладок — это «встань рядом», а не «раздели сверху».
        if (local.Y < group.HeaderHeight)
            return (group.Id, DockSide.Tab);

        var side = local.X < size.Width * Edge ? DockSide.Left
            : local.X > size.Width * (1 - Edge) ? DockSide.Right
            : local.Y < size.Height * Edge ? DockSide.Top
            : local.Y > size.Height * (1 - Edge) ? DockSide.Bottom
            : DockSide.Tab;

        return (group.Id, side);
    }

    /// <summary>Группа под указателем; null — там её нет.</summary>
    private DockGroupView? Group(Point point) =>
        Bounds.Contains(point + (Vector)Bounds.Position)
            ? _groups.Values.FirstOrDefault(group => Inside(group, point))
            : null;

    /// <summary>Попадает ли точка в эту группу.</summary>
    private bool Inside(DockGroupView group, Point point) =>
        group.TranslatePoint(default, this) is { } origin
        && new Rect(origin, group.Bounds.Size).Contains(point);

    /// <summary>Показывает, куда встанет вкладка; null — прячет подсказку.</summary>
    private void Highlight((string Group, DockSide Side)? target)
    {
        if (target is not { } place
            || View(place.Group) is not { } group
            || group.TranslatePoint(default, this) is not { } origin
            || OverlayLayer.GetOverlayLayer(this) is not { } layer
            || this.TranslatePoint(origin, layer) is not { } corner)
        {
            Hide();
            return;
        }

        var size = group.Bounds.Size;
        var area = place.Side switch
        {
            DockSide.Left => new Rect(corner.X, corner.Y, size.Width / 2, size.Height),
            DockSide.Right => new Rect(corner.X + (size.Width / 2), corner.Y, size.Width / 2, size.Height),
            DockSide.Top => new Rect(corner.X, corner.Y, size.Width, size.Height / 2),
            DockSide.Bottom => new Rect(corner.X, corner.Y + (size.Height / 2), size.Width, size.Height / 2),
            _ => new Rect(corner, size),
        };

        // Подсветка не ловит мышь: поймай она её — под указателем всегда была бы
        // она сама, и цель перестала бы меняться.
        _hint ??= new Border { Classes = { "dock-hint" }, IsHitTestVisible = false };

        if (!layer.Children.Contains(_hint))
            layer.Children.Add(_hint);

        Canvas.SetLeft(_hint, area.X);
        Canvas.SetTop(_hint, area.Y);
        _hint.Width = area.Width;
        _hint.Height = area.Height;
        _hint.IsVisible = true;
    }

    /// <summary>Убирает подсветку.</summary>
    private void Hide()
    {
        if (_hint is not null)
            _hint.IsVisible = false;
    }

    /// <summary>Заканчивает тягу и снимает подсветку со слоя.</summary>
    private void Stop()
    {
        _dragged = null;

        if (_hint is { Parent: Panel layer })
            layer.Children.Remove(_hint);

        _hint = null;
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

        if (Root is { } root && Items is { } items)
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

            view.Update(group, items, named ? Empty : null);

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
