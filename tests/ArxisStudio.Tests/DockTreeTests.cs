using ArxisStudio.Docking;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правки дерева доков.
/// </summary>
/// <remarks>
/// Дерево нарочно сделано чистыми данными с чистыми функциями — чтобы разбирать
/// перекладку без окна, без контролов и без потока интерфейса. Эти тесты и есть
/// проверка того, что решение окупилось: ни один из них не поднимает Avalonia.
/// </remarks>
public class DockTreeTests
{
    /// <summary>Вкладка встаёт в группу и становится выбранной.</summary>
    [Fact]
    public void A_tab_joins_the_group_and_becomes_the_chosen_one()
    {
        var root = Group("left", "solution");

        var after = DockTree.Insert(root, "left", DockSide.Tab, "structure", "unused");
        var group = Assert.IsType<DockGroup>(after);

        Assert.Equal(["solution", "structure"], group.Items);
        Assert.Equal("structure", group.Selected);
    }

    /// <summary>Сторона делит группу, и порядок зависит от стороны.</summary>
    [Theory]
    [InlineData(DockSide.Left, DockOrientation.Horizontal, "fresh", "left")]
    [InlineData(DockSide.Right, DockOrientation.Horizontal, "left", "fresh")]
    [InlineData(DockSide.Top, DockOrientation.Vertical, "fresh", "left")]
    [InlineData(DockSide.Bottom, DockOrientation.Vertical, "left", "fresh")]
    public void A_side_divides_the_group(DockSide side, DockOrientation orientation, string first, string second)
    {
        var after = DockTree.Insert(Group("left", "solution"), "left", side, "console", "fresh");
        var split = Assert.IsType<DockSplit>(after);

        Assert.Equal(orientation, split.Orientation);
        Assert.Equal([first, second], split.Children.Cast<DockGroup>().Select(group => group.Id));
        Assert.Equal(1, split.Weights.Sum(), 6);
    }

    /// <summary>Незнакомая группа оставляет дерево прежним.</summary>
    [Fact]
    public void An_unknown_group_leaves_the_tree_alone()
    {
        var root = Group("left", "solution");

        Assert.Same(root, DockTree.Insert(root, "нет.такой", DockSide.Tab, "console", "fresh"));
    }

    /// <summary>Опустевшая группа уходит, а деление с одним ребёнком заменяется им.</summary>
    /// <remarks>
    /// Без этого дерево обрастало бы делениями-пустышками: каждое перетаскивание
    /// оставляло бы после себя узел, который ничего не делит, а место занимает.
    /// </remarks>
    [Fact]
    public void An_emptied_group_leaves_and_a_lonely_split_collapses()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("right", "properties")],
            Weights = [0.3, 0.7],
        };

        var group = Assert.IsType<DockGroup>(DockTree.Remove(root, "properties"));

        Assert.Equal("left", group.Id);
        Assert.Equal(["solution"], group.Items);
    }

    /// <summary>Единственная группа остаётся, даже опустев.</summary>
    /// <remarks>
    /// Дерево без единого узла показывать нечем, а пустое место, куда открываются
    /// документы, человек видит и узнаёт.
    /// </remarks>
    [Fact]
    public void The_last_group_stays_even_when_empty()
    {
        var group = Assert.IsType<DockGroup>(DockTree.Remove(Group("root", "solution"), "solution"));

        Assert.Empty(group.Items);
        Assert.Null(group.Selected);
    }

    /// <summary>Ушедшая выбранная вкладка уступает место оставшейся.</summary>
    [Fact]
    public void A_departed_choice_hands_over_to_a_survivor()
    {
        var root = new DockGroup { Id = "bottom", Items = ["console", "errors"], Selected = "errors" };
        var group = Assert.IsType<DockGroup>(DockTree.Remove(root, "errors"));

        Assert.Equal("console", group.Selected);
    }

    /// <summary>
    /// Уцелевший сохраняет свою долю, а не наследует чужую.
    /// </summary>
    /// <remarks>
    /// Долю надо брать по прежнему месту ребёнка, а не по порядку выживших: иначе
    /// при уходе среднего последний тихо получал бы размер соседа — панель без
    /// причины меняла бы ширину, и виновника искали бы в перетаскивании.
    /// </remarks>
    [Fact]
    public void A_survivor_keeps_its_own_share()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Vertical,
            Children = [Group("a", "one"), Group("b", "two"), Group("c", "three")],
            Weights = [0.2, 0.3, 0.5],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Remove(root, "two"));

        Assert.Equal(2, split.Children.Count);
        Assert.Equal(split.Children.Count, split.Weights.Count);
        Assert.Equal(1, split.Weights.Sum(), 6);

        // Уцелели первый (0.2) и третий (0.5) — их отношение и обязано сохраниться.
        Assert.Equal(0.2 / 0.7, split.Weights[0], 6);
        Assert.Equal(0.5 / 0.7, split.Weights[1], 6);
    }

    /// <summary>Доли из ниоткуда делятся поровну, а не роняют раскладку в ноль.</summary>
    [Fact]
    public void Missing_shares_are_split_evenly()
    {
        Assert.Equal([0.5, 0.5], DockTree.Normalize([0, 0]));
        Assert.Equal([0.25, 0.75], DockTree.Normalize([1, 3]));
        Assert.Empty(DockTree.Normalize([]));
    }

    /// <summary>
    /// Неизвестные панели отсеиваются при чтении, но выбранная не остаётся призраком.
    /// </summary>
    /// <remarks>
    /// Плагин могли удалить, пока студия не работала. Дерево обязано открыться без
    /// него — и не ссылаться выбранной вкладкой на то, чего в группе больше нет.
    /// </remarks>
    [Fact]
    public void Unknown_items_are_sifted_out_on_reading()
    {
        var root = new DockGroup { Id = "left", Items = ["solution", "ghost"], Selected = "ghost" };
        var group = Assert.IsType<DockGroup>(DockTree.Keep(root, new HashSet<string>(["solution"])));

        Assert.Equal(["solution"], group.Items);
        Assert.Equal("solution", group.Selected);
    }

    /// <summary>Панель находится по имени, и её группа — тоже.</summary>
    [Fact]
    public void A_panel_and_its_group_are_found_by_name()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("right", "properties")],
            Weights = [0.5, 0.5],
        };

        Assert.Equal("right", DockTree.Holder(root, "properties")?.Id);
        Assert.Null(DockTree.Holder(root, "нет.такой"));
        Assert.Equal("left", DockTree.Group(root, "left")?.Id);
        Assert.Null(DockTree.Group(root, null));
    }

    /// <summary>Выбор вкладки достаёт её в своей группе, не трогая соседей.</summary>
    [Fact]
    public void Choosing_a_tab_reaches_into_its_own_group()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "solution" },
                Group("right", "properties"),
            ],
            Weights = [0.5, 0.5],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Select(root, "structure"));

        Assert.Equal("structure", ((DockGroup)split.Children[0]).Selected);
        Assert.Equal("properties", ((DockGroup)split.Children[1]).Selected);
    }

    private static DockGroup Group(string id, string item) =>
        new() { Id = id, Items = [item], Selected = item };
}
