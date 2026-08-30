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

    /// <summary>Показанный вид группы; null — такой на экране нет.</summary>
    /// <param name="groupId">Имя группы.</param>
    public DockGroupView? View(string groupId) =>
        _groups.TryGetValue(groupId, out var view) ? view : null;

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

    /// <summary>Строит узел: группу — видом, деление — сеткой со сплиттерами.</summary>
    private Control Build(DockNode node, IReadOnlyList<int> path, DockItems items, HashSet<string> alive)
    {
        if (node is DockGroup group)
        {
            alive.Add(group.Id);

            if (!_groups.TryGetValue(group.Id, out var view))
            {
                view = new DockGroupView();
                view.Chosen += (_, id) => Chosen?.Invoke(this, id);
                _groups[group.Id] = view;
            }

            view.Update(group, items);

            return view;
        }

        var split = (DockSplit)node;
        var down = split.Orientation == DockOrientation.Vertical;
        var shares = DockTree.Shares(split);
        var grid = new Grid();

        for (var at = 0; at < split.Children.Count; at++)
        {
            if (at > 0)
                Line(grid, down, path);

            Put(grid, down, Build(split.Children[at], [.. path, at], items, alive),
                Row(grid, down, new GridLength(shares[at], GridUnitType.Star)));
        }

        return grid;
    }

    /// <summary>Ставит между соседями границу, за которую можно взяться.</summary>
    private void Line(Grid grid, bool down, IReadOnlyList<int> path)
    {
        var splitter = new GridSplitter
        {
            Classes = { down ? "dock-h" : "dock-v" },
            ResizeDirection = down ? GridResizeDirection.Rows : GridResizeDirection.Columns,
        };

        splitter.DragCompleted += (_, _) => Resized?.Invoke(this, new DockResize(path, Shares(grid, down)));

        Put(grid, down, splitter, Row(grid, down, new GridLength(1)));
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

    /// <summary>Снимает доли с готовой сетки.</summary>
    /// <remarks>
    /// Считаем по занятому месту и по чётным полосам, а не по «звёздочке»:
    /// сплиттер вправе переписать доли в пиксели, и тогда единица измерения
    /// перестала бы отличать содержимое от разделяющей линии.
    /// </remarks>
    private static IReadOnlyList<double> Shares(Grid grid, bool down)
    {
        var count = down ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
        var sizes = new List<double>();

        for (var at = 0; at < count; at += 2)
            sizes.Add(down ? grid.RowDefinitions[at].ActualHeight : grid.ColumnDefinitions[at].ActualWidth);

        return DockTree.Normalize(sizes);
    }
}
