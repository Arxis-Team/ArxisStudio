using Avalonia;
using Avalonia.Controls;

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

    static DockView()
    {
        RootProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ItemsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyGroupProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Человек потянул границу.</summary>
    public event EventHandler<DockResize>? Resized;

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
