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

        var after = DockTree.Attach(root, "left", "structure");
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

    /// <summary>
    /// Вкладка встаёт на указанное место, а не всегда в конец.
    /// </summary>
    /// <remarks>
    /// Место человек выбирает тем же движением, каким несёт вкладку, — и полоса
    /// вкладок обязана его слушать. Пока места не было, всякая вкладка
    /// оказывалась последней, где бы её ни отпустили.
    /// </remarks>
    [Theory]
    [InlineData(0, new[] { "fresh", "one", "two" })]
    [InlineData(1, new[] { "one", "fresh", "two" })]
    [InlineData(2, new[] { "one", "two", "fresh" })]
    [InlineData(-1, new[] { "one", "two", "fresh" })]
    [InlineData(99, new[] { "one", "two", "fresh" })]
    public void A_tab_takes_the_place_it_was_given(int at, string[] order)
    {
        var root = new DockGroup { Id = "left", Items = ["one", "two"], Selected = "one" };
        var group = Assert.IsType<DockGroup>(DockTree.Attach(root, "left", "fresh", at));

        Assert.Equal(order, group.Items);
        Assert.Equal("fresh", group.Selected);
    }

    /// <summary>
    /// Переставленная вкладка не удваивается, а место считается среди остальных.
    /// </summary>
    /// <remarks>
    /// Перестановка внутри полосы — та же вставка: вкладка сперва уходит со
    /// своего места и только потом встаёт на новое. Не убери её — в группе
    /// оказалось бы два одинаковых имени, и панель стала бы дважды своей.
    /// </remarks>
    [Fact]
    public void A_moved_tab_is_counted_among_the_others()
    {
        var root = new DockGroup { Id = "left", Items = ["one", "two", "three"], Selected = "one" };
        var group = Assert.IsType<DockGroup>(DockTree.Attach(root, "left", "one", 2));

        Assert.Equal(["two", "three", "one"], group.Items);
    }

    /// <summary>
    /// Полоса ложится поперёк всего дерева, а не внутрь чьей-то колонки.
    /// </summary>
    /// <remarks>
    /// Этим корневая стыковка и отличается от деления области: консоль во всю
    /// ширину окна иначе собрать нечем — любое деление оказывается внутри
    /// соседа.
    /// </remarks>
    [Fact]
    public void A_strip_lies_across_the_whole_tree()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "one"), Group("right", "two")],
            Weights = [0.3, 0.7],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Frame(root, DockSide.Bottom, "console", "strip"));

        Assert.Equal(DockOrientation.Vertical, split.Orientation);
        Assert.Same(root, split.Children[0]);
        Assert.Equal("strip", ((DockGroup)split.Children[1]).Id);
        Assert.Equal([0.75, 0.25], split.Weights);
    }

    /// <summary>
    /// Полоса вдоль того же направления встаёт крайним ребёнком, а не вложенным делением.
    /// </summary>
    /// <remarks>
    /// Три полосы в ряд — один узел с тремя детьми: так тянется любая граница,
    /// а не только соседняя. Место при этом отдают все понемногу — полоса
    /// ложится поперёк всего окна, и брать его у кого-то одного не за что.
    /// </remarks>
    [Fact]
    public void A_strip_along_the_same_direction_joins_the_row()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "one"), Group("right", "two")],
            Weights = [0.3, 0.7],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Frame(root, DockSide.Left, "tools", "strip"));

        Assert.Equal(DockOrientation.Horizontal, split.Orientation);
        Assert.Equal(["strip", "left", "right"], split.Children.Cast<DockGroup>().Select(group => group.Id));

        // Соседи ужались в прежней пропорции: 3 к 7 так и осталось.
        Assert.Equal(0.25, split.Weights[0], 6);
        Assert.Equal(0.3 * 0.75, split.Weights[1], 6);
        Assert.Equal(0.7 * 0.75, split.Weights[2], 6);
    }

    /// <summary>
    /// Каждое намерение проходит через одну дверь.
    /// </summary>
    /// <remarks>
    /// Через неё же строится предпросмотр, и поэтому показанное человеку и
    /// полученное им — буквально один и тот же результат. Пока дверей было две,
    /// подсветка рисовала половину области, а новичок получал половину доли
    /// соседа.
    /// </remarks>
    [Fact]
    public void Every_aim_goes_through_one_door()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "one"), Group("right", "two")],
            Weights = [0.5, 0.5],
        };

        var joined = DockTree.Apply(root, new DockAim.Tab("left", 0), "fresh", "unused");
        Assert.Equal(["fresh", "one"], DockTree.Group(joined, "left")!.Items);

        var divided = DockTree.Apply(root, new DockAim.Split("right", DockSide.Bottom), "fresh", "born");
        Assert.Equal("born", DockTree.Holder(divided, "fresh")?.Id);

        var framed = Assert.IsType<DockSplit>(
            DockTree.Apply(root, new DockAim.Frame(DockSide.Top), "fresh", "strip"));
        Assert.Equal(DockOrientation.Vertical, framed.Orientation);
        Assert.Equal("strip", ((DockGroup)framed.Children[0]).Id);

        // Отдельное окно заводит тот, у кого окна есть: дерево тут ни при чём.
        Assert.Same(root, DockTree.Apply(root, new DockAim.Float(), "fresh", "unused"));
    }

    /// <summary>Незнакомая группа оставляет дерево прежним.</summary>
    [Fact]
    public void An_unknown_group_leaves_the_tree_alone()
    {
        var root = Group("left", "solution");

        Assert.Same(root, DockTree.Attach(root, "нет.такой", "console"));
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

    /// <summary>
    /// Область получает долю, о которой просили, а соседи делят остаток как делили.
    /// </summary>
    /// <remarks>
    /// Пропорция соседей сохраняется не из аккуратности: раздвинув одну область,
    /// человек не просил перекроить все остальные, и уж точно не просил
    /// уравнять их между собой.
    /// </remarks>
    [Fact]
    public void An_area_takes_the_share_it_asked_for()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("a", "one"), Group("b", "two"), Group("c", "three")],
            Weights = [0.2, 0.6, 0.2],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Widen(root, "a", 0.5));

        Assert.Equal(0.5, split.Weights[0], 6);

        // Соседи делили остаток как 3 к 1 — так и делят.
        Assert.Equal(0.375, split.Weights[1], 6);
        Assert.Equal(0.125, split.Weights[2], 6);
        Assert.Equal(1, split.Weights.Sum(), 6);
    }

    /// <summary>Доля вне промежутка и незнакомая область оставляют доли прежними.</summary>
    [Fact]
    public void A_meaningless_share_leaves_the_shares_alone()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("a", "one"), Group("b", "two")],
            Weights = [0.3, 0.7],
        };

        Assert.Same(root, DockTree.Widen(root, "a", 0));
        Assert.Same(root, DockTree.Widen(root, "a", 1));
        Assert.Equal([0.3, 0.7], Assert.IsType<DockSplit>(DockTree.Widen(root, "нет.такой", 0.5)).Weights);
    }

    /// <summary>Доля достаётся области и в глубине дерева.</summary>
    [Fact]
    public void A_share_reaches_an_area_deep_in_the_tree()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                Group("left", "solution"),
                new DockSplit
                {
                    Orientation = DockOrientation.Vertical,
                    Children = [Group("documents", "form"), Group("bottom", "console")],
                    Weights = [0.8, 0.2],
                },
            ],
            Weights = [0.3, 0.7],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Widen(root, "bottom", 0.5));
        var inner = Assert.IsType<DockSplit>(split.Children[1]);

        Assert.Equal([0.3, 0.7], split.Weights);
        Assert.Equal(0.5, inner.Weights[1], 6);
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

    /// <summary>
    /// Соседу по ряду места не находят заново — его отдаёт тот, кого делили.
    /// </summary>
    /// <remarks>
    /// Три области в ряд — это один узел с тремя детьми, а не пара внутри
    /// пары: так тянется любая граница, а не только соседняя. И доли соседей
    /// при этом не трогают: человек делил ту область, на которую смотрел, и
    /// переставлять границы на другом конце окна ему никто не обещал.
    /// </remarks>
    [Theory]
    [InlineData(DockSide.Left, 1, "fresh")]
    [InlineData(DockSide.Right, 2, "fresh")]
    public void A_neighbour_takes_room_from_the_one_it_divided(DockSide side, int at, string expected)
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("centre", "form"), Group("right", "properties")],
            Weights = [0.2, 0.6, 0.2],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Insert(root, "centre", side, "console", "fresh"));

        Assert.Equal(4, split.Children.Count);
        Assert.Equal(expected, ((DockGroup)split.Children[at]).Id);

        // Делили середину — её 0.6 и разошлись пополам, а края остались.
        Assert.Equal(0.2, split.Weights[0], 6);
        Assert.Equal(0.2, split.Weights[3], 6);
        Assert.Equal(0.3, split.Weights[at], 6);
    }

    /// <summary>Деление поперёк заворачивает группу, а не встаёт рядом.</summary>
    [Fact]
    public void A_crosswise_split_wraps_the_group_instead()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("centre", "form")],
            Weights = [0.3, 0.7],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Insert(root, "centre", DockSide.Bottom, "console", "fresh"));

        Assert.Equal(2, split.Children.Count);

        var inner = Assert.IsType<DockSplit>(split.Children[1]);

        Assert.Equal(DockOrientation.Vertical, inner.Orientation);
        Assert.Equal(["centre", "fresh"], inner.Children.Cast<DockGroup>().Select(group => group.Id));
    }

    /// <summary>
    /// Названная группа остаётся, даже опустев.
    /// </summary>
    /// <remarks>
    /// Так живёт место, куда открываются документы: исчезни оно вместе с
    /// последней закрытой вкладкой — следующий документ появился бы неизвестно
    /// где, а человек искал бы глазами область, которая только что была.
    /// </remarks>
    [Fact]
    public void A_named_group_stays_even_when_empty()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("documents", "form")],
            Weights = [0.3, 0.7],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Remove(root, "form", new HashSet<string>(["documents"])));

        Assert.Equal(2, split.Children.Count);
        Assert.Empty(((DockGroup)split.Children[1]).Items);

        // Без охранной грамоты та же правка группу убирает.
        Assert.IsType<DockGroup>(DockTree.Remove(root, "form"));
    }

    /// <summary>Потянутая граница переставляет доли по пути от корня.</summary>
    [Fact]
    public void A_dragged_border_moves_the_shares_along_the_path()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                Group("left", "solution"),
                new DockSplit
                {
                    Orientation = DockOrientation.Vertical,
                    Children = [Group("centre", "form"), Group("bottom", "console")],
                    Weights = [0.5, 0.5],
                },
            ],
            Weights = [0.5, 0.5],
        };

        var split = Assert.IsType<DockSplit>(DockTree.Resize(root, [1], [0.8, 0.2]));
        var inner = Assert.IsType<DockSplit>(split.Children[1]);

        Assert.Equal([0.8, 0.2], inner.Weights);

        // Соседнее деление стоит нетронутым, и это видно по нему самому.
        Assert.Equal([0.5, 0.5], split.Weights);
        Assert.Same(root.Children[0], split.Children[0]);
    }

    /// <summary>
    /// Доли не по числу детей отвергаются целиком.
    /// </summary>
    /// <remarks>
    /// Такой список означает, что мерили уже не то дерево. Принять его значило
    /// бы переставить границы, которых человек не трогал; отказ всего лишь не
    /// двинет ту, что он потянул.
    /// </remarks>
    [Theory]
    [InlineData(new double[] { 1 })]
    [InlineData(new double[] { 0.3, 0.3, 0.4 })]
    public void Shares_that_do_not_fit_the_children_are_refused(double[] weights)
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("right", "properties")],
            Weights = [0.5, 0.5],
        };

        Assert.Same(root, DockTree.Resize(root, [], weights));
    }

    /// <summary>Путь в никуда оставляет дерево прежним.</summary>
    [Fact]
    public void A_path_to_nowhere_leaves_the_tree_alone()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("left", "solution"), Group("right", "properties")],
            Weights = [0.5, 0.5],
        };

        Assert.Same(root, DockTree.Resize(root, [7], [0.5, 0.5]));
        Assert.Same(root, DockTree.Resize(root, [0], [0.5, 0.5]));

        // Группа ничего не делит, и тянуть в ней нечего.
        var lonely = Group("left", "solution");

        Assert.Same(lonely, DockTree.Resize(lonely, [], [1]));
    }

    /// <summary>Ребёнку без доли достаётся ровная часть, а не ноль.</summary>
    [Fact]
    public void A_child_without_a_share_gets_an_even_one()
    {
        var split = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children = [Group("a", "one"), Group("b", "two"), Group("c", "three")],
            Weights = [0.5],
        };

        var shares = DockTree.Shares(split);

        Assert.Equal(3, shares.Count);
        Assert.Equal(1, shares.Sum(), 6);
        Assert.All(shares, share => Assert.True(share > 0));
    }

    private static DockGroup Group(string id, string item) =>
        new() { Id = id, Items = [item], Selected = item };
}
