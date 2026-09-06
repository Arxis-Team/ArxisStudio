using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Группа, убранная на рейку: с экрана ушла, место отдала, размер сохранила.
/// </summary>
/// <remarks>
/// Уборка живёт в трёх местах сразу, и каждое проверяется отдельно. Дерево
/// помнит сторону и переживает с ней перезапуск; вид перестаёт показывать
/// группу и отдаёт её место соседям; доля при этом остаётся в делении
/// нетронутой — иначе возврат отдавал бы панели среднее по палате.
/// <para>
/// Прежде это называлось сворачиванием и работало иначе: полоса вкладок
/// оставалась на месте, тело пряталось. Снизу выходила тонкая лента, а сбоку —
/// пустая колонка шириной в подпись: у горизонтальной полосы вкладок нет
/// осмысленной свёрнутой ширины.
/// </para>
/// </remarks>
public class DockRailTests
{
    /// <summary>Группа уходит на рейку и возвращается, не теряя ни вкладок, ни выбора.</summary>
    [Fact]
    public void A_group_goes_to_a_rail_and_comes_back()
    {
        var root = new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "structure" };

        var stowed = Assert.IsType<DockGroup>(DockTree.Stow(root, "left", DockSide.Left));

        Assert.Equal(DockSide.Left, stowed.Rail);
        Assert.Equal(["solution", "structure"], stowed.Items);
        Assert.Equal("structure", stowed.Selected);

        var back = Assert.IsType<DockGroup>(DockTree.Stow(stowed, "left", null));

        Assert.Null(back.Rail);
        Assert.Equal("structure", back.Selected);
    }

    /// <summary>
    /// Уборка, которой нечего менять, возвращает то же дерево той же ссылкой.
    /// </summary>
    /// <remarks>
    /// На этом держится вся студия: дерево — свойство вида, и присвоение нового
    /// перекладывает окно целиком, а с ним пропадает курсор в панели, где
    /// человек печатает.
    /// </remarks>
    [Fact]
    public void Stowing_what_is_already_stowed_changes_nothing()
    {
        var root = new DockGroup { Id = "left", Items = ["solution"], Selected = "solution", Rail = DockSide.Left };

        Assert.Same(root, DockTree.Stow(root, "left", DockSide.Left));
        Assert.Same(root, DockTree.Stow(root, "нет.такой", DockSide.Left));
        Assert.NotSame(root, DockTree.Stow(root, "left", null));

        // Смена стороны — тоже правка: рейка у панели одна, и она переезжает.
        Assert.NotSame(root, DockTree.Stow(root, "left", DockSide.Right));
    }

    /// <summary>
    /// Доля убранной группы в делении не трогается.
    /// </summary>
    /// <remarks>
    /// Место достаётся соседям на время, а прежний размер ждёт в дереве —
    /// иначе возврат отдавал бы панели не ту ширину, к которой человек её
    /// привёл, а среднее по палате.
    /// </remarks>
    [Fact]
    public void Stowing_does_not_spend_the_share()
    {
        var split = Split();

        var after = Assert.IsType<DockSplit>(DockTree.Stow(split, "bottom", DockSide.Bottom));

        Assert.Equal(split.Weights, after.Weights);
    }

    /// <summary>
    /// Правки дерева не теряют рейку.
    /// </summary>
    /// <remarks>
    /// Узлы неизменяемы, и каждая правка собирает группу заново — забыв
    /// перенести сторону, она вернула бы панель на экран человеку за спиной:
    /// пришла вкладка, выключили соседний плагин, выбрали другую вкладку.
    /// </remarks>
    [Fact]
    public void Every_edit_carries_the_rail_over()
    {
        var root = new DockGroup
        {
            Id = "left",
            Items = ["solution", "structure"],
            Selected = "solution",
            Rail = DockSide.Left,
        };

        Assert.Equal(DockSide.Left, Assert.IsType<DockGroup>(DockTree.Attach(root, "left", "problems")).Rail);
        Assert.Equal(DockSide.Left, Assert.IsType<DockGroup>(DockTree.Select(root, "structure")).Rail);
        Assert.Equal(DockSide.Left, Assert.IsType<DockGroup>(DockTree.Keep(root, new HashSet<string>(["solution"]))).Rail);
    }

    /// <summary>Рейка уезжает в файл раскладки и возвращается оттуда.</summary>
    [Fact]
    public void The_rail_survives_the_file()
    {
        var text = DockLayoutSerializer.Write(new DockLayout
        {
            Active = DockLayout.DefaultName,
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new DockWorkspace { Root = Split(rail: DockSide.Bottom) },
            },
        });

        // Стоящая в дереве группа не пишет о себе ничего: поле у неё пустое, и
        // файл не пухнет от "rail": null на каждой группе.
        Assert.DoesNotContain("\"rail\": null", text, StringComparison.Ordinal);

        var after = DockLayoutSerializer.Read(text, out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.NotNull(after);

        var split = Assert.IsType<DockSplit>(after!.Current!.Root);
        var bottom = split.Children.OfType<DockGroup>().Single(group => group.Id == "bottom");

        Assert.Equal(DockSide.Bottom, bottom.Rail);
        Assert.Null(split.Children.OfType<DockGroup>().Single(group => group.Id == "top").Rail);
    }

    /// <summary>
    /// Раскладка, написанная до появления реек, читается стоящей в дереве.
    /// </summary>
    /// <remarks>
    /// Версию формата ради нового поля не поднимали, и обе стороны деградируют
    /// мягко: незнакомое поле разбор молча пропускает, отсутствующее берёт
    /// умолчание. Здесь проверена вторая половина — старый файл с прежним
    /// признаком свёрнутости.
    /// </remarks>
    [Fact]
    public void An_older_layout_reads_as_docked()
    {
        const string Text = """
            {
              "version": 1,
              "active": "default",
              "layouts": {
                "default": {
                  "root": {
                    "kind": "group", "id": "left", "items": ["solution"],
                    "selected": "solution", "collapsed": true
                  }
                }
              }
            }
            """;

        var after = DockLayoutSerializer.Read(Text, out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.Null(Assert.IsType<DockGroup>(after!.Current!.Root).Rail);
    }

    /// <summary>
    /// Убранная группа уходит с экрана целиком, и место достаётся соседу.
    /// </summary>
    /// <remarks>
    /// В этом всё отличие от прежнего сворачивания: там от группы оставалась
    /// полоса вкладок, и сбоку она держала колонку шириной в подпись. Место
    /// соседу не доставалось вовсе.
    /// </remarks>
    [AvaloniaFact]
    public void A_stowed_group_leaves_the_screen_and_its_place_to_the_neighbour()
    {
        var (view, window) = Shown(Split());
        var tall = Group(view, "bottom").Bounds.Height;
        var above = Group(view, "top").Bounds.Height;

        view.Root = DockTree.Stow(view.Root!, "bottom", DockSide.Bottom);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(view.GetVisualDescendants().OfType<DockGroupView>(), group => group.Id == "bottom");
        Assert.True(Group(view, "top").Bounds.Height > above, "место не досталось соседу");

        // Возврат отдаёт ту же высоту: доля ждала в дереве.
        view.Root = DockTree.Stow(view.Root!, "bottom", null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(tall, Group(view, "bottom").Bounds.Height, 1);

        window.Close();
    }

    /// <summary>
    /// Границы соседей остаются живыми: замороженных больше нет.
    /// </summary>
    /// <remarks>
    /// Прежде граница рядом со свёрнутой группой не тянулась — у той был размер
    /// по шапке, и сплиттер выдал бы ей пиксели вместо доли. Убранной группы в
    /// сетке нет вовсе, и тянуть больше нечему мешать.
    /// </remarks>
    [AvaloniaFact]
    public void Every_border_left_on_the_screen_can_be_dragged()
    {
        var (view, window) = Shown(new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "middle", Items = ["console"], Selected = "console" },
                new DockGroup { Id = "right", Items = ["problems"], Selected = "problems" },
            ],
            Weights = [0.3, 0.4, 0.3],
        });

        Assert.Equal(2, Splitters(view).Count);
        Assert.All(Splitters(view), splitter => Assert.True(splitter.IsEnabled));

        view.Root = DockTree.Stow(view.Root!, "middle", DockSide.Bottom);
        Dispatcher.UIThread.RunJobs();

        // Соседей осталось двое, граница между ними одна — и она живая.
        // Прежде граница рядом со свёрнутой не тянулась вовсе.
        var left = Assert.Single(Splitters(view));

        Assert.True(left.IsEnabled, "границу рядом с убранной группой нельзя тянуть");

        window.Close();
    }

    /// <summary>
    /// Кнопка есть у той группы, чьё место кому-то достанется.
    /// </summary>
    /// <remarks>
    /// Одинокой группе убираться некуда — под ней осталась бы пустота, — а
    /// пол рабочей области не убирается вовсе: документы не прячут.
    /// <para>
    /// Спрашивается не только свойство, но и сама кнопка. Прежде показ ей
    /// задавала привязка в шаблоне — и не задавала ничего: содержимое шапки
    /// переезжает в шаблон <c>AxToolWindow</c>, хозяин шаблона у переехавшего
    /// теряется, и <c>TemplateBinding</c> перестаёт находить свойство молча.
    /// Кнопка стояла у всех групп подряд.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Only_a_group_with_a_neighbour_offers_the_button()
    {
        var (pair, window) = Shown(Split());

        Assert.True(Group(pair, "top").CanStow);
        Assert.True(Group(pair, "bottom").CanStow);
        Assert.True(Button(Group(pair, "top")).IsVisible);

        window.Close();

        var (alone, lonely) = Shown(new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" });

        Assert.False(Group(alone, "top").CanStow, "одинокая группа предлагает убраться в никуда");
        Assert.False(
            Button(Group(alone, "top")).IsVisible,
            "кнопка стоит в шапке одинокой группы, хотя убираться ей некуда");

        lonely.Close();

        var (floor, room) = Shown(Split(), documents: "bottom");

        Assert.False(Group(floor, "bottom").CanStow, "пол рабочей области предлагает убраться");
        Assert.False(Button(Group(floor, "bottom")).IsVisible, "кнопка стоит в шапке пола рабочей области");
        Assert.True(Group(floor, "top").CanStow);

        room.Close();
    }

    /// <summary>
    /// Дереву без реек кнопка уборки не достаётся вовсе.
    /// </summary>
    /// <remarks>
    /// Это оторванное окно. Рейки там нет — 420×320 с рейкой это почти одна
    /// рейка, — и кнопка обещала бы то, чего не будет: убранной панели неоткуда
    /// было бы вернуться. Так же решает Visual Studio: плавающую панель там
    /// сперва пристыковывают.
    /// </remarks>
    [AvaloniaFact]
    public void A_tree_without_rails_offers_no_button_at_all()
    {
        var (view, window) = Shown(Split());

        Assert.True(Group(view, "top").CanStow);

        view.Railed = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Group(view, "top").CanStow, "окно без реек предлагает убрать панель в никуда");
        Assert.False(Button(Group(view, "top")).IsVisible, "кнопка уборки осталась в окне без реек");
        Assert.False(Group(view, "bottom").CanStow);

        window.Close();
    }

    /// <summary>
    /// Кнопка просит убрать группу на ближайшую к ней рейку.
    /// </summary>
    /// <remarks>
    /// Сторону называет вид, а не дерево: поля стороны у узлов нет, а после
    /// перекладки форма дерева о сторонах ничего не говорит. Человек же видит
    /// экран — и ждёт, что нижняя панель уйдёт на нижнюю рейку, а левая на
    /// левую.
    /// </remarks>
    [AvaloniaFact]
    public void The_button_asks_for_the_nearest_rail()
    {
        var (down, tall) = Shown(Split());
        var asked = new List<DockStow>();

        down.Stowing += (_, stow) => asked.Add(stow);

        Press(Button(Group(down, "bottom")));

        Assert.Equal(new DockStow("bottom", DockSide.Bottom), Assert.Single(asked));

        asked.Clear();
        Press(Button(Group(down, "top")));

        Assert.Equal(new DockStow("top", DockSide.Top), Assert.Single(asked));

        tall.Close();

        var (across, wide) = Shown(new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "right", Items = ["problems"], Selected = "problems" },
            ],
            Weights = [0.5, 0.5],
        });

        asked.Clear();
        across.Stowing += (_, stow) => asked.Add(stow);

        Press(Button(Group(across, "left")));

        Assert.Equal(new DockStow("left", DockSide.Left), Assert.Single(asked));

        wide.Close();
    }

    /// <summary>
    /// Кнопка говорит, что она сделает, и показывает один контур.
    /// </summary>
    /// <remarks>
    /// Подпись у кнопки теперь одна: смысл у неё один — убрать группу. Она же
    /// подсказка и она же имя для средств доступности: на кнопке значок 12×12,
    /// и узнать о ней больше неоткуда.
    /// </remarks>
    [AvaloniaFact]
    public void The_button_says_what_it_will_do()
    {
        var (view, window) = Shown(Split());

        view.StowTitle = "Убрать панель";
        Dispatcher.UIThread.RunJobs();

        var button = Button(Group(view, "bottom"));

        Assert.Equal("Убрать панель", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.Equal("Убрать панель", ToolTip.GetTip(button));
        Assert.Same(AxIcons.WindowMinimize, Assert.Single(button.GetVisualDescendants().OfType<AxIcon>()).Data);

        window.Close();
    }

    /// <summary>
    /// Убранный сосед не теряет своей доли, когда тянут чужую границу.
    /// </summary>
    /// <remarks>
    /// Доли снимаются с сетки, а убранной группы в сетке нет — попади она в
    /// счёт, её прежний размер пропал бы, и возврат отдал бы панели чужую
    /// ширину. Это единственный путь, которым уборка может испортить раскладку
    /// молча.
    /// </remarks>
    [AvaloniaFact]
    public void A_stowed_neighbour_keeps_its_share_while_others_resize()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "middle", Items = ["console"], Selected = "console" },
                new DockGroup { Id = "right", Items = ["problems"], Selected = "problems", Rail = DockSide.Right },
            ],
            Weights = [0.3, 0.4, 0.3],
        };

        var (view, window) = Shown(root);

        view.Resized += (_, resize) => view.Root = DockTree.Resize(view.Root!, resize.Path, resize.Weights);

        var splitter = Assert.IsType<Grid>(view.Child).Children.OfType<GridSplitter>().Single();
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

    /// <summary>
    /// Пустая рейка не занимает места.
    /// </summary>
    /// <remarks>
    /// Полоса в два десятка пикселей вдоль трёх краёв — ощутимая часть окна, и
    /// держать её ради того, чего нет, незачем. Скрытый ребёнок DockPanel места
    /// не занимает, поэтому и проверяется именно видимость.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_rail_takes_no_room()
    {
        var (rail, window) = Rail(DockSide.Left);

        rail.Update([]);
        Dispatcher.UIThread.RunJobs();

        Assert.False(rail.IsVisible, "пустая рейка осталась на экране и съела полосу окна");

        rail.Update([Stowed("solution")]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(rail.IsVisible, "рейке дали кнопку, а она не показалась");
        Assert.True(rail.Bounds.Width > 0, "рейка с кнопкой не заняла ни пикселя");

        window.Close();
    }

    /// <summary>
    /// Кнопка называет свою панель — и подписью, и для средств доступности.
    /// </summary>
    /// <remarks>
    /// Рейка — единственное место, откуда убранную панель возвращают: на экране
    /// её нет. Безымянная кнопка делает возврат угадыванием, а для читателя
    /// экрана — невозможным.
    /// <para>
    /// Подпись приходит привязкой, а не строкой: снятая однажды, она осталась
    /// бы на языке той минуты, когда панель убрали.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_rail_button_says_which_panel_it_brings_back()
    {
        var (rail, window) = Rail(DockSide.Left);
        var item = new DockItem("solution", new Border()) { Title = "Обозреватель" };

        rail.Update([new DockRailItem("left", item)]);
        Dispatcher.UIThread.RunJobs();

        var button = Assert.Single(rail.GetVisualDescendants().OfType<AxButton>());

        Assert.Equal("Обозреватель", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.Equal("Обозреватель", ToolTip.GetTip(button));
        Assert.Equal("Обозреватель", Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);

        // Язык сменился — сменилась и подпись на рейке.
        item.Title = "Solution";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Solution", Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
        Assert.Equal("Solution", Avalonia.Automation.AutomationProperties.GetName(button));

        window.Close();
    }

    /// <summary>
    /// Боковая рейка пишет поперёк и остаётся узкой.
    /// </summary>
    /// <remarks>
    /// Иначе кнопка была бы шириной в подпись, и рейка съела бы ту самую полосу
    /// окна, ради которой панель убирали. Поворот обязан быть в раскладке, а не
    /// в отрисовке: RenderTransform места не меняет, и рейка мерялась бы как
    /// горизонтальная.
    /// <para>
    /// Слева читается снизу вверх, справа сверху вниз — как в Visual Studio и
    /// Rider; снизу подпись обычная.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_side_rail_writes_across_and_stays_narrow()
    {
        var (left, tall) = Rail(DockSide.Left);

        left.Update([Stowed("Довольно длинная подпись панели")]);
        Dispatcher.UIThread.RunJobs();

        var text = Assert.Single(left.GetVisualDescendants().OfType<TextBlock>());

        Assert.True(
            left.Bounds.Width < text.Bounds.Width,
            $"рейка шириной {left.Bounds.Width:0} при подписи {text.Bounds.Width:0} — подпись не повернулась");

        Assert.Equal(-90, Turn(left));

        tall.Close();

        var (right, wide) = Rail(DockSide.Right);

        right.Update([Stowed("solution")]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(90, Turn(right));

        wide.Close();

        var (bottom, low) = Rail(DockSide.Bottom);

        bottom.Update([Stowed("solution")]);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(bottom.GetVisualDescendants().OfType<LayoutTransformControl>());

        low.Close();
    }

    /// <summary>
    /// Тот же список кнопок не перестраивает.
    /// </summary>
    /// <remarks>
    /// Рейку спрашивают на каждой правке дерева, а правок при тяге границы —
    /// десятки в секунду. Пересобирай она кнопки каждый раз, человек тянул бы
    /// границу под мигающей рейкой.
    /// </remarks>
    [AvaloniaFact]
    public void The_same_list_rebuilds_nothing()
    {
        var (rail, window) = Rail(DockSide.Left);
        var stowed = Stowed("Обозреватель");

        rail.Update([stowed]);
        Dispatcher.UIThread.RunJobs();

        var button = Assert.Single(rail.GetVisualDescendants().OfType<AxButton>());

        rail.Update([stowed]);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(button, Assert.Single(rail.GetVisualDescendants().OfType<AxButton>()));

        // А другой список — перестраивает: одинаковость проверяется по составу,
        // а не по длине.
        rail.Update([Stowed("Консоль")]);
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(button, Assert.Single(rail.GetVisualDescendants().OfType<AxButton>()));

        window.Close();
    }

    /// <summary>Кнопка на рейке просит вернуть свою панель.</summary>
    [AvaloniaFact]
    public void A_rail_button_asks_for_its_panel_back()
    {
        var (rail, window) = Rail(DockSide.Left);
        var chosen = new List<DockRailItem>();
        var stowed = Stowed("solution");

        rail.Chosen += (_, item) => chosen.Add(item);
        rail.Update([stowed]);
        Dispatcher.UIThread.RunJobs();

        Press(Assert.Single(rail.GetVisualDescendants().OfType<AxButton>()));

        Assert.Same(stowed, Assert.Single(chosen));

        window.Close();
    }

    /// <summary>
    /// Рейка отпускает панели, когда её опустошают.
    /// </summary>
    /// <remarks>
    /// Кнопка держит <see cref="DockItem"/>, а тот — контрол расширения.
    /// Оставленная привязка пережила бы выгрузку плагина и не дала бы уйти его
    /// контексту загрузки — ровно та беда, от которой заведено снятие панелей
    /// по хозяину.
    /// </remarks>
    [AvaloniaFact]
    public void An_emptied_rail_lets_go_of_its_panels()
    {
        var (rail, window) = Rail(DockSide.Left);
        var item = new DockItem("solution", new Border()) { Title = "Обозреватель" };

        rail.Update([new DockRailItem("left", item)]);
        Dispatcher.UIThread.RunJobs();

        var text = Assert.Single(rail.GetVisualDescendants().OfType<TextBlock>());

        rail.Update([]);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(rail.GetVisualDescendants().OfType<AxButton>());
        Assert.Null(text.Text);

        item.Title = "Solution";
        Dispatcher.UIThread.RunJobs();

        Assert.True(text.Text is null, "снятая кнопка всё ещё слушает панель — привязку не отпустили");

        window.Close();
    }

    /// <summary>Деление сверху вниз: две группы с двумя вкладками в нижней.</summary>
    private static DockSplit Split(DockSide? rail = null) => new()
    {
        Orientation = DockOrientation.Vertical,
        Children =
        [
            new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" },
            new DockGroup { Id = "bottom", Items = ["console", "problems"], Selected = "console", Rail = rail },
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

    private static AxButton Button(DockGroupView group) =>
        group.GetVisualDescendants().OfType<AxButton>().Single(button => button.Name == "PART_Stow");

    private static IReadOnlyList<GridSplitter> Splitters(DockView view) =>
        [.. view.GetVisualDescendants().OfType<GridSplitter>()];

    /// <summary>Нажимает кнопку так, как это делает человек.</summary>
    private static void Press(AxButton button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    /// <summary>Рейка у своего края окна — так, как её ставит оболочка.</summary>
    private static (DockRail Rail, Window Window) Rail(DockSide side)
    {
        var rail = new DockRail { Side = side };

        rail[DockPanel.DockProperty] = side switch
        {
            DockSide.Left => Avalonia.Controls.Dock.Left,
            DockSide.Right => Avalonia.Controls.Dock.Right,
            _ => Avalonia.Controls.Dock.Bottom,
        };

        // Рядом с рейкой обязан стоять кто-то, забирающий остаток: у DockPanel
        // последний ребёнок и есть остаток, и без него рейка растянулась бы на
        // всё окно, а её ширина ничего не значила бы.
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = new DockPanel { Children = { rail, new Border() } },
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (rail, window);
    }

    /// <summary>Убранная панель с такой подписью.</summary>
    private static DockRailItem Stowed(string title) =>
        new("left", new DockItem("solution", new Border()) { Title = title });

    /// <summary>На сколько повёрнута подпись единственной кнопки рейки.</summary>
    private static double Turn(DockRail rail) =>
        Assert.IsType<RotateTransform>(
            Assert.Single(rail.GetVisualDescendants().OfType<LayoutTransformControl>()).LayoutTransform).Angle;
}
