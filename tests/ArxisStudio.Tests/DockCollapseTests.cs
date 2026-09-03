using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Свёрнутая группа: одна полоса вкладок вместо панели.
/// </summary>
/// <remarks>
/// Сворачивание живёт в трёх местах сразу, и каждое проверяется отдельно.
/// Дерево помнит решение и переживает с ним перезапуск; вид перестаёт
/// показывать тело и отдаёт место соседям; граница рядом со свёрнутой группой
/// перестаёт тянуться, иначе её доля превратилась бы в пиксели.
/// </remarks>
public class DockCollapseTests
{
    /// <summary>Группа сворачивается и разворачивается, не теряя ни вкладок, ни выбора.</summary>
    [Fact]
    public void A_group_folds_and_unfolds_keeping_its_tabs()
    {
        var root = new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "structure" };

        var folded = Assert.IsType<DockGroup>(DockTree.Collapse(root, "left", true));

        Assert.True(folded.Collapsed);
        Assert.Equal(["solution", "structure"], folded.Items);
        Assert.Equal("structure", folded.Selected);

        var back = Assert.IsType<DockGroup>(DockTree.Collapse(folded, "left", false));

        Assert.False(back.Collapsed);
        Assert.Equal("structure", back.Selected);
    }

    /// <summary>
    /// Свёртка, которой нечего менять, возвращает то же дерево той же ссылкой.
    /// </summary>
    /// <remarks>
    /// На этом держится вся студия: дерево — свойство вида, и присвоение нового
    /// перекладывает окно целиком, а с ним пропадает курсор в панели, где
    /// человек печатает.
    /// </remarks>
    [Fact]
    public void Folding_what_is_already_folded_changes_nothing()
    {
        var root = new DockGroup { Id = "left", Items = ["solution"], Selected = "solution", Collapsed = true };

        Assert.Same(root, DockTree.Collapse(root, "left", true));
        Assert.Same(root, DockTree.Collapse(root, "нет.такой", true));
        Assert.NotSame(root, DockTree.Collapse(root, "left", false));
    }

    /// <summary>
    /// Доля свёрнутой группы в делении не трогается.
    /// </summary>
    /// <remarks>
    /// Свёрнутая занимает место по своей шапке, а её прежний размер ждёт в
    /// дереве — иначе разворот возвращал бы панель шириной в полосу вкладок.
    /// </remarks>
    [Fact]
    public void Folding_does_not_spend_the_share()
    {
        var split = Split();

        var after = Assert.IsType<DockSplit>(DockTree.Collapse(split, "bottom", true));

        Assert.Equal(split.Weights, after.Weights);
    }

    /// <summary>
    /// Правки дерева не теряют свёрнутость.
    /// </summary>
    /// <remarks>
    /// Узлы неизменяемы, и каждая правка собирает группу заново — забыв перенести
    /// признак, она развернула бы панель человеку за спиной: пришла вкладка,
    /// выключили соседний плагин, выбрали другую вкладку.
    /// </remarks>
    [Fact]
    public void Every_edit_carries_the_fold_over()
    {
        var root = new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "solution", Collapsed = true };

        Assert.True(Assert.IsType<DockGroup>(DockTree.Attach(root, "left", "problems")).Collapsed);
        Assert.True(Assert.IsType<DockGroup>(DockTree.Select(root, "structure")).Collapsed);
        Assert.True(Assert.IsType<DockGroup>(DockTree.Keep(root, new HashSet<string>(["solution"]))).Collapsed);
    }

    /// <summary>Свёрнутость уезжает в файл раскладки и возвращается оттуда.</summary>
    [Fact]
    public void The_fold_survives_the_file()
    {
        var text = DockLayoutSerializer.Write(new DockLayout
        {
            Active = DockLayout.DefaultName,
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new DockWorkspace { Root = Split(collapsed: true) },
            },
        });

        var after = DockLayoutSerializer.Read(text, out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.NotNull(after);

        var split = Assert.IsType<DockSplit>(after!.Current!.Root);
        var bottom = split.Children.OfType<DockGroup>().Single(group => group.Id == "bottom");

        Assert.True(bottom.Collapsed);
        Assert.False(split.Children.OfType<DockGroup>().Single(group => group.Id == "top").Collapsed);
    }

    /// <summary>Раскладка, записанная до появления свёртки, читается развёрнутой.</summary>
    [Fact]
    public void An_older_layout_reads_as_unfolded()
    {
        const string Text = """
            {
              "version": 1,
              "active": "default",
              "layouts": {
                "default": {
                  "root": { "kind": "group", "id": "left", "items": ["solution"], "selected": "solution" }
                }
              }
            }
            """;

        var after = DockLayoutSerializer.Read(Text, out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.False(Assert.IsType<DockGroup>(after!.Current!.Root).Collapsed);
    }

    /// <summary>
    /// Свёрнутая группа показывает шапку и прячет тело, а место отдаёт соседу.
    /// </summary>
    [AvaloniaFact]
    public void A_folded_group_shows_only_its_header()
    {
        var (view, window) = Shown(Split());
        var bottom = Group(view, "bottom");
        var tall = bottom.Bounds.Height;

        Assert.True(Body(bottom).IsVisible, "тело развёрнутой группы спрятано");

        view.Root = DockTree.Collapse(view.Root!, "bottom", true);
        Dispatcher.UIThread.RunJobs();

        bottom = Group(view, "bottom");

        Assert.False(Body(bottom).IsVisible, "тело свёрнутой группы осталось на виду");
        Assert.True(bottom.Bounds.Height < tall, "свёрнутая группа занимает столько же места");
        Assert.InRange(bottom.Bounds.Height, 1, 40);
        Assert.True(Group(view, "top").Bounds.Height > tall, "место не досталось соседу");

        window.Close();
    }

    /// <summary>
    /// Граница рядом со свёрнутой группой не тянется.
    /// </summary>
    /// <remarks>
    /// Тянуть нечего: у свёрнутой размер по шапке, и сплиттер, дотянувшись до
    /// неё, выдал бы ей пиксели вместо доли — а из пикселей доля обратно уже не
    /// считается, и прежний размер панели пропал бы.
    /// </remarks>
    [AvaloniaFact]
    public void The_border_next_to_a_folded_group_is_frozen()
    {
        var (view, window) = Shown(Split());

        Assert.All(Splitters(view), splitter => Assert.True(splitter.IsEnabled));

        view.Root = DockTree.Collapse(view.Root!, "bottom", true);
        Dispatcher.UIThread.RunJobs();

        Assert.All(Splitters(view), splitter => Assert.False(splitter.IsEnabled, "границу свёрнутой группы можно тянуть"));

        window.Close();
    }

    /// <summary>
    /// Кнопка есть у той группы, чьё место кому-то достанется.
    /// </summary>
    /// <remarks>
    /// Одинокой группе сворачиваться некуда — под ней осталась бы пустота, — а
    /// пол рабочей области не сворачивается вовсе: документы не прячут.
    /// </remarks>
    [AvaloniaFact]
    public void Only_a_group_with_a_neighbour_offers_the_button()
    {
        var (pair, window) = Shown(Split());

        Assert.True(Group(pair, "top").CanCollapse);
        Assert.True(Group(pair, "bottom").CanCollapse);

        window.Close();

        var (alone, lonely) = Shown(new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" });

        Assert.False(Group(alone, "top").CanCollapse, "одинокая группа предлагает свернуться в никуда");

        lonely.Close();

        var (floor, room) = Shown(Split(), documents: "bottom");

        Assert.False(Group(floor, "bottom").CanCollapse, "пол рабочей области предлагает свернуться");
        Assert.True(Group(floor, "top").CanCollapse);

        room.Close();
    }

    /// <summary>Кнопка просит свернуть, а вкладка свёрнутой группы — развернуть.</summary>
    [AvaloniaFact]
    public void The_button_asks_to_fold_and_a_tab_asks_to_unfold()
    {
        var (view, window) = Shown(Split());
        var asked = new List<DockCollapse>();

        view.Collapsing += (_, fold) => asked.Add(fold);

        Press(Button(Group(view, "bottom")));

        Assert.Equal(new DockCollapse("bottom", true), Assert.Single(asked));

        view.Root = DockTree.Collapse(view.Root!, "bottom", true);
        Dispatcher.UIThread.RunJobs();
        asked.Clear();

        // Щелчок по второй вкладке свёрнутой группы: человек ткнул в панель,
        // чтобы её увидеть, а не чтобы выбрать её вслепую.
        var tabs = Group(view, "bottom").GetVisualDescendants().OfType<AxTabStrip>().First();

        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new DockCollapse("bottom", false), Assert.Single(asked));

        window.Close();
    }

    /// <summary>
    /// Контур кнопки говорит о действии: черта — свернуть, возврат — развернуть.
    /// </summary>
    /// <remarks>
    /// Контур один и меняется вместе со смыслом. Два наложенных друг на друга
    /// значка с переключаемой видимостью здесь уже были — и оба остались на
    /// виду: содержимое кнопки переезжает в её шаблон, теряя хозяина, к
    /// которому привязывались.
    /// </remarks>
    [AvaloniaFact]
    public void The_button_shows_one_glyph_at_a_time()
    {
        var (view, window) = Shown(Split());

        var glyph = Assert.Single(Button(Group(view, "bottom")).GetVisualDescendants().OfType<AxIcon>());

        Assert.Same(AxIcons.WindowMinimize, glyph.Data);

        view.Root = DockTree.Collapse(view.Root!, "bottom", true);
        Dispatcher.UIThread.RunJobs();

        glyph = Assert.Single(Button(Group(view, "bottom")).GetVisualDescendants().OfType<AxIcon>());

        Assert.Same(AxIcons.WindowRestore, glyph.Data);

        window.Close();
    }

    /// <summary>Подпись кнопки говорит, что она сделает, и меняется вместе со смыслом.</summary>
    [AvaloniaFact]
    public void The_button_says_what_it_will_do()
    {
        var (view, window) = Shown(Split());

        view.CollapseTitle = "Свернуть панель";
        view.ExpandTitle = "Развернуть панель";
        Dispatcher.UIThread.RunJobs();

        var button = Button(Group(view, "bottom"));

        Assert.Equal("Свернуть панель", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.Equal("Свернуть панель", ToolTip.GetTip(button));

        view.Root = DockTree.Collapse(view.Root!, "bottom", true);
        Dispatcher.UIThread.RunJobs();

        button = Button(Group(view, "bottom"));

        Assert.Equal("Развернуть панель", Avalonia.Automation.AutomationProperties.GetName(button));

        window.Close();
    }

    /// <summary>
    /// Свёрнутый сосед не теряет своей доли, когда тянут чужую границу.
    /// </summary>
    /// <remarks>
    /// Доли снимаются с сетки, а свёрнутая группа сидит на полосе размером по
    /// шапке — попади она в счёт, её прежний размер стал бы этой шапкой, и
    /// разворот вернул бы панель высотой в полосу вкладок. Это единственный
    /// путь, которым свёрнутость может испортить раскладку молча.
    /// </remarks>
    [AvaloniaFact]
    public void A_folded_neighbour_keeps_its_share_while_others_resize()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "middle", Items = ["console"], Selected = "console", Collapsed = true },
                new DockGroup { Id = "right", Items = ["problems"], Selected = "problems" },
            ],
            Weights = [0.3, 0.4, 0.3],
        };

        var (view, window) = Shown(root);

        view.Resized += (_, resize) => view.Root = DockTree.Resize(view.Root!, resize.Path, resize.Weights);

        // Границ две, и обе рядом со свёрнутой серединой — тянуть их нельзя;
        // разворачиваем правую группу к левой, чтобы граница между ними
        // осталась живой.
        view.Root = DockTree.Collapse(view.Root!, "middle", false);
        Dispatcher.UIThread.RunJobs();
        view.Root = DockTree.Collapse(view.Root!, "right", true);
        Dispatcher.UIThread.RunJobs();

        var splitter = Assert.IsType<Grid>(view.Child).Children.OfType<GridSplitter>().First(line => line.IsEnabled);
        var grip = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window);

        Assert.NotNull(grip);

        window.MouseMove(grip.Value);
        window.MouseDown(grip.Value, MouseButton.Left);

        for (var step = 1; step <= 4; step++)
            window.MouseMove(grip.Value.WithX(grip.Value.X + (step * 40)));

        window.MouseUp(grip.Value.WithX(grip.Value.X + 160), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var after = Assert.IsType<DockSplit>(view.Root);

        Assert.Equal(3, after.Weights.Count);
        Assert.Equal(0.3, after.Weights[2], 6);
        Assert.True(after.Weights[0] > 0.3, $"левая доля осталась {after.Weights[0]:0.000}");

        window.Close();
    }

    /// <summary>Деление сверху вниз: две группы с двумя вкладками в нижней.</summary>
    private static DockSplit Split(bool collapsed = false) => new()
    {
        Orientation = DockOrientation.Vertical,
        Children =
        [
            new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" },
            new DockGroup { Id = "bottom", Items = ["console", "problems"], Selected = "console", Collapsed = collapsed },
        ],
        Weights = [0.7, 0.3],
    };

    /// <summary>Показывает дерево в окне с живыми панелями.</summary>
    private static (DockView View, Window Window) Shown(DockNode root, string? documents = null)
    {
        var items = new DockItems();

        foreach (var id in new[] { "solution", "console", "problems" })
            items.Add("hello", new DockItem(id, new Border()) { Title = id });

        var view = new DockView { Items = items, Root = root, EmptyGroup = documents };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (view, window);
    }

    private static DockGroupView Group(DockView view, string id) =>
        view.GetVisualDescendants().OfType<DockGroupView>().Single(group => group.Id == id);

    private static Control Body(DockGroupView group) =>
        group.GetVisualDescendants().OfType<ContentControl>().Single(control => control.Name == "PART_Content");

    private static AxButton Button(DockGroupView group) =>
        group.GetVisualDescendants().OfType<AxButton>().Single(button => button.Name == "PART_Collapse");

    private static IReadOnlyList<GridSplitter> Splitters(DockView view) =>
        [.. view.GetVisualDescendants().OfType<GridSplitter>()];

    /// <summary>Нажимает кнопку так, как это делает человек.</summary>
    private static void Press(AxButton button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
}
