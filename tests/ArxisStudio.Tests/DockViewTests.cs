using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
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
/// Дерево раскладки на экране.
/// </summary>
/// <remarks>
/// Очередь общая с остальными: проверка «панель ушла из памяти» опирается на
/// сборщик мусора, а он в процессе один — пока рядом поднимают и выгружают
/// плагины другие классы, ответ у неё плавает.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class DockViewTests
{
    /// <summary>Группа показывает вкладки и содержимое выбранной.</summary>
    [AvaloniaFact]
    public void A_group_shows_its_tabs_and_the_chosen_panel()
    {
        var (view, items) = Shown(
            new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "structure" },
            "solution", "structure");

        var group = view.View("left");

        Assert.NotNull(group);
        Assert.Equal(2, DockMouse.Tabs(group).Items.Count);
        Assert.Equal(1, DockMouse.Tabs(group).SelectedIndex);
        Assert.Same(items.Find("structure")?.Content, Content(group));
        Assert.True(group.HasTabs);
        Assert.True(Chrome(group).ShowHeader);
    }

    /// <summary>
    /// Пустая группа показывает заставку и убирает шапку.
    /// </summary>
    /// <remarks>
    /// Пустой остаётся область документов — её не сносят, пока в ней ничего не
    /// открыто. Полоса шапки в 38 пикселей, в которой нечего показать, выглядит
    /// над заставкой недоделкой, а не местом, куда что-то откроется.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_group_shows_the_placeholder_and_hides_its_header()
    {
        var hint = new TextBlock { Text = "здесь открываются документы" };
        var view = new DockView
        {
            Items = new DockItems(),
            Empty = hint,
            EmptyGroup = "documents",
            Root = new DockGroup { Id = "documents" },
        };

        new Window { Content = view, Width = 600, Height = 400 }.Show();
        Dispatcher.UIThread.RunJobs();

        var group = view.View("documents");

        Assert.NotNull(group);
        Assert.False(group.HasTabs);
        Assert.False(Chrome(group).ShowHeader);
        Assert.Same(hint, Content(group));
    }

    /// <summary>
    /// Подпись вкладки идёт за заголовком панели.
    /// </summary>
    /// <remarks>
    /// Заголовок переводится на ходу: студия привязывает его к словарю
    /// владельца, и вкладка обязана менять подпись при смене языка, ничего не
    /// пересобирая.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_follows_the_title_of_its_panel()
    {
        var (view, items) = Shown(
            new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
            "solution");

        items.Find("solution")!.Title = "Проект";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Проект", ((AxTabItem)DockMouse.Tabs(view.View("left")!).Items[0]!).Content);
    }

    /// <summary>Выбор вкладки показывает её панель и сообщает хозяину дерева.</summary>
    [AvaloniaFact]
    public void Choosing_a_tab_shows_its_panel_and_tells_the_owner()
    {
        var (view, items) = Shown(
            new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "structure" },
            "solution", "structure");

        string? told = null;
        view.Chosen += (_, id) => told = id;

        var group = view.View("left")!;
        DockMouse.Tabs(group).SelectedIndex = 0;

        Assert.Equal("solution", told);
        Assert.Same(items.Find("solution")?.Content, Content(group));
    }

    /// <summary>
    /// Панель выключенного плагина места не занимает, но имя её остаётся в дереве.
    /// </summary>
    /// <remarks>
    /// Выключенный плагин обязан вернуться на своё место, когда его включат
    /// обратно, — а место помнит именно дерево.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_that_is_not_here_takes_no_room()
    {
        var root = new DockGroup { Id = "left", Items = ["solution", "ghost"], Selected = "solution" };
        var (view, _) = Shown(root, "solution");

        Assert.Single(DockMouse.Tabs(view.View("left")!).Items);
        Assert.Equal(["solution", "ghost"], ((DockGroup)view.Root!).Items);
    }

    /// <summary>Деление ставит между соседями границу и раздаёт им доли.</summary>
    [AvaloniaFact]
    public void A_split_puts_a_border_between_neighbours()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "right", Items = ["properties"], Selected = "properties" },
            ],
            Weights = [0.3, 0.7],
        };

        var (view, _) = Shown(root, "solution", "properties");
        var grid = Assert.IsType<Grid>(view.Child);

        Assert.Equal(3, grid.ColumnDefinitions.Count);
        Assert.Single(grid.Children.OfType<GridSplitter>());
        Assert.Equal(0.3, grid.ColumnDefinitions[0].Width.Value, 6);
        Assert.Equal(1, grid.ColumnDefinitions[1].Width.Value);
        Assert.Equal(0.7, grid.ColumnDefinitions[2].Width.Value, 6);
    }

    /// <summary>
    /// Отпущенная граница остаётся там, где её оставили.
    /// </summary>
    /// <remarks>
    /// Тянут мышью, а не подставляют доли: между потянутой границей и новым
    /// деревом лежит вся дорога — сплиттер, событие, правка дерева, перекладка,
    /// — и обрыв на любом её шаге выглядит одинаково: граница возвращается на
    /// место, едва её отпустили.
    /// </remarks>
    [AvaloniaFact]
    public void A_released_border_stays_where_it_was_left()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "right", Items = ["properties"], Selected = "properties" },
            ],
            Weights = [0.5, 0.5],
        };

        var (view, _) = Shown(root, "solution", "properties");

        // Вид о потянутой границе только сообщает: записывает её в дерево тот,
        // кто деревом владеет. В студии это делает StudioDock.
        view.Resized += (_, resize) => view.Root = DockTree.Resize(view.Root!, resize.Path, resize.Weights);

        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view));
        var splitter = Assert.IsType<Grid>(view.Child).Children.OfType<GridSplitter>().Single();

        var grip = splitter.TranslatePoint(
            new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window);

        Assert.NotNull(grip);

        var grid = Assert.IsType<Grid>(view.Child);

        window.MouseMove(grip.Value);
        window.MouseDown(grip.Value, MouseButton.Left);

        // Несколько движений, а не одно: тяга — это цепочка, и сплиттер меряет
        // от места нажатия, а не от последнего шага.
        for (var step = 1; step <= 4; step++)
            window.MouseMove(grip.Value.WithX(grip.Value.X + (step * 50)));

        window.MouseUp(grip.Value.WithX(grip.Value.X + 200), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(grid.ColumnDefinitions[0].Width.Value > grid.ColumnDefinitions[2].Width.Value,
            $"сетка не поехала: {grid.ColumnDefinitions[0].Width} против {grid.ColumnDefinitions[2].Width}");

        var after = Assert.IsType<DockSplit>(view.Root);

        Assert.Equal(1, after.Weights.Sum(), 6);
        Assert.True(after.Weights[0] > 0.6, $"левая доля осталась {after.Weights[0]:0.000}");
    }

    /// <summary>
    /// Брошенная в середину вкладка просится в своё окно.
    /// </summary>
    /// <remarks>
    /// Середина области значит «оторви», край — «раздели», полоса вкладок —
    /// «встань рядом». Так же в Unity, и так у каждого жеста ровно один смысл;
    /// заодно человеку не нужен свободный рабочий стол, чтобы оторвать панель.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_in_the_middle_asks_for_a_window_of_its_own()
    {
        var (view, window) = Pair();

        DockDrag? dropped = null;
        view.Dropped += (_, drop) => dropped = drop;

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.NotNull(dropped);
        Assert.Equal("solution", dropped.Item);
        Assert.IsType<DockAim.Float>(view.Aim(dropped.At, dropped.Item));
    }

    /// <summary>
    /// Брошенная в полосу вкладок просится соседней вкладкой, а не разделить сверху.
    /// </summary>
    /// <remarks>
    /// Полоса вкладок лежит у верхнего края, и по краям область делится — но
    /// целиться в чужую полосу человек будет именно затем, чтобы встать в неё
    /// вкладкой. Полоса поэтому проверяется первой.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_onto_the_tab_strip_asks_to_join()
    {
        var (view, window) = Pair();

        DockDrag? dropped = null;
        view.Dropped += (_, drop) => dropped = drop;

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Tab(view.View("right")!, 0, window));

        Assert.NotNull(dropped);
        Assert.Equal(new DockAim.Tab("right", 1), view.Aim(dropped.At, dropped.Item));
    }

    /// <summary>
    /// Брошенная у края вкладка просится разделить область.
    /// </summary>
    /// <remarks>
    /// Зона края — треть стороны, и верхняя из них наконец достижима: прежняя
    /// четверть почти целиком пряталась под полосой вкладок, и попасть в
    /// «раздели сверху» было нечем.
    /// </remarks>
    [InlineData(0.1, 0.5, DockSide.Left)]
    [InlineData(0.9, 0.5, DockSide.Right)]
    [InlineData(0.5, 0.2, DockSide.Top)]
    [InlineData(0.5, 0.9, DockSide.Bottom)]

    // Треть, а не четверть: на этом расстоянии от края прежнее правило уже
    // считало бы точку серединой.
    [InlineData(0.3, 0.5, DockSide.Left)]
    [AvaloniaTheory]
    public void A_tab_dropped_at_the_edge_asks_to_divide(double x, double y, DockSide side)
    {
        var (view, window) = Pair();

        DockDrag? dropped = null;
        view.Dropped += (_, drop) => dropped = drop;

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, x, y, window));

        Assert.NotNull(dropped);
        Assert.Equal(new DockAim.Split("right", side), view.Aim(dropped.At, dropped.Item));
    }

    /// <summary>
    /// В углу побеждает тот край, который ближе в долях своей стороны.
    /// </summary>
    /// <remarks>
    /// Прежнее правило спрашивало края по порядку — левый, правый, верхний,
    /// нижний, — и угол широкой низкой области всегда доставался боку, даже
    /// когда верх был много ближе. Доли уравнивают стороны в правах: у широкой
    /// области верхняя зона шире боковой ровно во столько, во сколько она сама
    /// шире своей высоты.
    /// </remarks>
    [AvaloniaFact]
    public void A_corner_goes_to_the_edge_that_is_nearer_in_shares()
    {
        var (view, window) = Pair();
        var group = view.View("right")!;

        // Область высокая и узкая: доля 0.08 по ширине — это меньше пикселей,
        // чем доля 0.08 по высоте, и всё же побеждает не она, а верх.
        Assert.True(group.Bounds.Height > group.Bounds.Width, "область оказалась не той формы");

        DockDrag? dropped = null;
        view.Dropped += (_, drop) => dropped = drop;

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(group, 0.2, 0.1, window));

        Assert.Equal(new DockAim.Split("right", DockSide.Top), view.Aim(dropped!.At, dropped.Item));
    }

    /// <summary>
    /// Место в полосе вкладок считается по серединам соседок.
    /// </summary>
    /// <remarks>
    /// Несомая вкладка при этом не считается вовсе: место человек выбирает
    /// среди остальных, и <c>Attach</c> убирает её из группы ровно так же.
    /// Считай мы её — перестановка внутри полосы промахивалась бы на единицу.
    /// </remarks>
    [AvaloniaFact]
    public void A_place_in_the_strip_is_counted_by_the_middles_of_the_others()
    {
        var (view, window) = Pair();
        var left = view.View("left")!;

        // «solution» и «structure» стоят в левой группе; несут первую из них,
        // значит соседка одна и мест ровно два — до неё и после. Середина
        // вкладки — это уже «после неё»: курсор её прошёл.
        Assert.Equal(
            new DockAim.Tab("left", 0),
            view.Aim(Screen(DockMouse.Tab(left, 0, window), window), "solution"));

        Assert.Equal(
            new DockAim.Tab("left", 1),
            view.Aim(Screen(DockMouse.Tab(left, 1, window), window), "solution"));

        // А несомая из чужой группы не убирает никого: соседок две, и та же
        // точка значит уже «после второй».
        Assert.Equal(
            new DockAim.Tab("left", 2),
            view.Aim(Screen(DockMouse.Tab(left, 1, window), window), "properties"));
    }

    /// <summary>
    /// Отнятый захват заканчивает тягу.
    /// </summary>
    /// <remarks>
    /// Захват отнимают чужое окно, Alt+Tab, всплывший модальный диалог. Без
    /// этого тяга осталась бы взведённой: подсветка висела бы на экране, а
    /// следующее движение мыши таскало бы вкладку с отпущенной кнопкой.
    /// </remarks>
    [AvaloniaFact]
    public void A_taken_capture_ends_the_drag()
    {
        var (view, window) = Pair();

        DockDrag? dropped = null;
        IPointer? pointer = null;

        view.Dropped += (_, drop) => dropped = drop;
        view.Dragging += (_, drag) => view.Show(drag.At, drag.Item);
        view.PointerMoved += (_, moved) => pointer = moved.Pointer;

        var from = DockMouse.Tab(view.View("left")!, 0, window);

        // К левому краю соседней области: там подсказку рисует сам вид. Середина
        // области обещает отдельное окно, и рисует его не вид, а хозяин деревьев.
        var to = DockMouse.Inside(view.View("right")!, 0.1, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(Hint(view));
        Assert.NotNull(pointer);

        pointer.Capture(window);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Hint(view));

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(dropped);
    }

    /// <summary>
    /// Щелчок по вкладке остаётся щелчком.
    /// </summary>
    /// <remarks>
    /// Нажать и отпустить, не сдвинув мышь, значит выбрать вкладку. Рука при
    /// этом почти всегда дрожит на пиксель-другой, и без порога раскладка
    /// разъезжалась бы от обычного щелчка.
    /// </remarks>
    [AvaloniaFact]
    public void A_click_on_a_tab_stays_a_click()
    {
        var (view, window) = Pair();

        DockDrag? dropped = null;
        view.Dropped += (_, drop) => dropped = drop;

        var at = DockMouse.Tab(view.View("left")!, 1, window);

        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at.WithX(at.X + 2));
        window.MouseUp(at.WithX(at.X + 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(dropped);
        Assert.Equal(1, DockMouse.Tabs(view.View("left")!).SelectedIndex);
    }

    /// <summary>
    /// Пока вкладку тащат, видно ровно то место, которое она займёт.
    /// </summary>
    /// <remarks>
    /// Доли, по которым место считается, живут в дереве и берутся оттуда, а не
    /// повторяются подсказкой своей цифрой: обещание поэтому не расходится с
    /// тем, что человек получит. Настоящих областей при этом не двигают —
    /// перекладка на каждой границе зон заставляла бы раскладку щёлкать под
    /// курсором.
    /// </remarks>
    [AvaloniaFact]
    public void While_a_tab_is_dragged_its_future_place_is_shown()
    {
        var (view, window) = Pair();
        var real = view.Root!;
        var right = view.View("right")!;

        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(right, 0.1, 0.5, window);

        view.Dragging += (_, drag) => view.Show(drag.At, drag.Item);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        var hint = Hint(view);

        Assert.NotNull(hint);
        Assert.False(hint.IsHitTestVisible);

        // Ровно половина области — столько новая и получит, разделив её.
        Assert.Equal(right.Bounds.Width * DockTree.SplitShare, hint.Width, 3);
        Assert.Equal(right.Bounds.Height, hint.Height, 3);

        // И ни одна настоящая область при этом не сдвинулась.
        Assert.Same(real, view.Root);
        Assert.NotNull(view.View("left"));

        view.Clear();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Hint(view));
    }

    /// <summary>
    /// Над полосой вкладок подсказка ставит черту — у неё вкладка и встанет.
    /// </summary>
    [AvaloniaFact]
    public void Over_the_strip_the_hint_marks_the_place_in_it()
    {
        var (view, window) = Pair();
        var left = view.View("left")!;

        view.Dragging += (_, drag) => view.Show(drag.At, drag.Item);

        var from = DockMouse.Tab(left, 0, window);
        var to = DockMouse.Tab(view.View("right")!, 0, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        var caret = Caret(view);

        Assert.NotNull(caret);
        Assert.Equal(2, caret.Width);
        Assert.True(caret.Height > 0, "черта нулевой высоты");

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Спрятанный сосед сохраняет свою долю, когда границу тянут без него.
    /// </summary>
    /// <remarks>
    /// Панель выключенного плагина места не занимает, но место за ней
    /// числится. Отдай мы в дерево доли одних лишь видимых — спрятанный
    /// лишился бы своей, и панель, вернувшись, встала бы шириной в ноль.
    /// </remarks>
    [AvaloniaFact]
    public void A_hidden_neighbour_keeps_its_share()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "middle", Items = ["gone"], Selected = "gone" },
                new DockGroup { Id = "right", Items = ["properties"], Selected = "properties" },
            ],
            Weights = [0.3, 0.4, 0.3],
        };

        // Панели «gone» среди живых нет: её плагин выключен.
        var (view, _) = Shown(root, "solution", "properties");

        view.Resized += (_, resize) => view.Root = DockTree.Resize(view.Root!, resize.Path, resize.Weights);

        Assert.NotNull(view.View("left"));
        Assert.Null(view.View("middle"));

        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view));
        var splitter = Assert.IsType<Grid>(view.Child).Children.OfType<GridSplitter>().Single();
        var grip = splitter.TranslatePoint(
            new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window);

        Assert.NotNull(grip);

        window.MouseMove(grip.Value);
        window.MouseDown(grip.Value, MouseButton.Left);

        for (var step = 1; step <= 4; step++)
            window.MouseMove(grip.Value.WithX(grip.Value.X + (step * 50)));

        window.MouseUp(grip.Value.WithX(grip.Value.X + 200), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var after = Assert.IsType<DockSplit>(view.Root);

        Assert.Equal(3, after.Weights.Count);
        Assert.Equal(0.4, after.Weights[1], 6);
        Assert.True(after.Weights[0] > 0.3, $"левая доля осталась {after.Weights[0]:0.000}");
    }

    /// <summary>
    /// Переезд панели переносит её саму, а не строит заново.
    /// </summary>
    /// <remarks>
    /// Построенная заново панель «излечилась» бы при каждом перетаскивании:
    /// упавшая забыла бы, что упала, и кнопка перезапуска в её заглушке
    /// исчезла бы вместе с самой заглушкой. Уцелевшая группа тоже переносится —
    /// иначе панель внутри теряла бы прокрутку и выделение.
    /// </remarks>
    [AvaloniaFact]
    public void A_moved_panel_is_carried_over_rather_than_built_anew()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
                new DockGroup { Id = "right", Items = ["properties"], Selected = "properties" },
            ],
            Weights = [0.5, 0.5],
        };

        var (view, items) = Shown(root, "solution", "properties");

        var right = view.View("right");
        var panel = items.Find("solution")?.Content;

        view.Root = DockTree.Attach(DockTree.Remove(view.Root!, "solution"), "right", "solution");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(right, view.View("right"));
        Assert.Null(view.View("left"));
        Assert.Same(panel, items.Find("solution")?.Content);
        Assert.Equal(2, DockMouse.Tabs(right!).Items.Count);
        Assert.Same(panel, Content(right!));
    }

    /// <summary>
    /// Панель уходит из памяти вместе со своим хозяином.
    /// </summary>
    /// <remarks>
    /// Это главное правило движка, а не мелочь про память. Контрол плагина
    /// принадлежит его контексту загрузки, и пока жив контрол — контекст не
    /// выгружается: перезагрузка плагина копила бы в памяти по контексту за раз
    /// и честно жаловалась бы, что прежняя копия осталась.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_leaves_no_trace_when_its_owner_goes()
    {
        var items = new DockItems();
        var view = new DockView { Items = items };

        new Window { Content = view, Width = 400, Height = 300 }.Show();

        var gone = Put(view, items);

        foreach (var id in items.RemoveOwnedBy("hello"))
            view.Root = DockTree.Remove(view.Root!, id);

        Settle();

        Assert.Equal(0, items.Count);
        Assert.False(gone.IsAlive, "контрол панели остался в памяти после ухода хозяина");
    }

    /// <summary>
    /// Доигрывает отложенную работу окна и собирает мусор.
    /// </summary>
    /// <remarks>
    /// Такт отрисовки здесь обязателен, и это не заклинание. Снятый контрол
    /// уходит из дерева сразу, но остаётся в отложенной работе окна — в
    /// очереди на перезамер, — и держит его до такта она, а не мы. Без такта
    /// проверка «панель ушла из памяти» падала бы через раз: ровно так она и
    /// падала, когда рядом успевал отработать сосед по набору, оставлявший
    /// после себя перекладку.
    /// </remarks>
    private static void Settle()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Кладёт панель в дерево и возвращает слабую ссылку на её контрол.
    /// </summary>
    /// <remarks>
    /// Отдельным методом, и он не встраивается: оставшись переменной в кадре
    /// теста, контрол был бы жив по вине самой проверки — и она доказывала бы
    /// только то, что у неё есть локальная переменная.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Put(DockView view, DockItems items)
    {
        var content = new Border();

        items.Add("hello", new DockItem("solution", content) { Title = "Проект" });
        view.Root = new DockGroup { Id = "root", Items = ["solution"], Selected = "solution" };

        Dispatcher.UIThread.RunJobs();

        return new WeakReference(content);
    }

    /// <summary>Показывает дерево в окне, заведя живые панели с такими именами.</summary>
    private static (DockView View, DockItems Items) Shown(DockNode root, params string[] ids)
    {
        var items = new DockItems();

        foreach (var id in ids)
            items.Add("hello", new DockItem(id, new Border()) { Title = id });

        var view = new DockView { Items = items, Root = root };

        new Window { Content = view, Width = 900, Height = 600 }.Show();
        Dispatcher.UIThread.RunJobs();

        return (view, items);
    }

    /// <summary>Окно инструментов, в которое одета показанная группа.</summary>
    private static AxToolWindow Chrome(DockGroupView group) =>
        group.GetVisualDescendants().OfType<AxToolWindow>().Single();

    /// <summary>Две группы рядом: слева две вкладки, справа одна.</summary>
    private static (DockView View, Window Window) Pair()
    {
        var root = new DockSplit
        {
            Orientation = DockOrientation.Horizontal,
            Children =
            [
                new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "solution" },
                new DockGroup { Id = "right", Items = ["properties"], Selected = "properties" },
            ],
            Weights = [0.5, 0.5],
        };

        var (view, _) = Shown(root, "solution", "structure", "properties");

        return (view, Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view)));
    }

    /// <summary>
    /// Точка внутри окна, но вне дерева, просит полосу во всё окно.
    /// </summary>
    /// <remarks>
    /// Это единственный способ положить панель поперёк всего окна: любое
    /// деление области оказывается внутри чьей-то колонки. У главного окна
    /// такие полосы — тулбар и строка состояния, у оторванного — заголовок.
    /// </remarks>
    [AvaloniaFact]
    public void A_point_inside_the_window_but_outside_the_tree_asks_for_a_strip()
    {
        var (view, window) = Strips();

        Assert.Equal(
            new DockAim.Frame(DockSide.Top),
            view.Aim(window.PointToScreen(new Point(400, 12)), "solution"));

        Assert.Equal(
            new DockAim.Frame(DockSide.Bottom),
            view.Aim(window.PointToScreen(new Point(400, window.ClientSize.Height - 12)), "solution"));

        // А внутри дерева та же высота — это обычная зона области.
        Assert.IsType<DockAim.Tab>(
            view.Aim(window.PointToScreen(new Point(400, 52)), "solution"));
    }

    /// <summary>
    /// Полоса во всё окно обещает ровно ту долю, которую и займёт.
    /// </summary>
    /// <remarks>
    /// Долю подсказка берёт из дерева, а не повторяет своей цифрой: иначе
    /// обещание разошлось бы с тем, что человек получит, — а разойтись ему
    /// достаточно один раз, чтобы ему перестали верить.
    /// </remarks>
    [AvaloniaFact]
    public void A_strip_promises_the_share_it_will_take()
    {
        var (view, window) = Strips();

        view.Show(window.PointToScreen(new Point(400, 12)), "solution");

        var hint = Hint(view);

        Assert.NotNull(hint);
        Assert.Equal(view.Bounds.Height * DockTree.FrameShare, hint.Height, 3);
        Assert.Equal(view.Bounds.Width, hint.Width, 3);

        view.Clear();

        Assert.Null(Hint(view));
    }

    /// <summary>Точка вне окна не занята никем: это отрыв, а не стыковка.</summary>
    [AvaloniaFact]
    public void A_point_outside_the_window_belongs_to_nobody()
    {
        var (view, window) = Strips();

        Assert.Null(view.Aim(window.PointToScreen(new Point(-40, 300)), "solution"));
        Assert.Null(view.Aim(window.PointToScreen(new Point(400, -40)), "solution"));
    }

    /// <summary>Вид в окне с полосами сверху и снизу — как тулбар и строка состояния.</summary>
    private static (DockView View, Window Window) Strips()
    {
        var items = new DockItems();

        items.Add("hello", new DockItem("solution", new Border()) { Title = "solution" });

        var view = new DockView
        {
            Items = items,
            Root = new DockGroup { Id = "left", Items = ["solution"], Selected = "solution" },
        };

        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = new DockPanel
            {
                Children =
                {
                    new Border { Height = 40, [DockPanel.DockProperty] = Dock.Top },
                    new Border { Height = 24, [DockPanel.DockProperty] = Dock.Bottom },
                    view,
                },
            },
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (view, window);
    }

    /// <summary>Точка окна в пикселях экрана.</summary>
    private static PixelPoint Screen(Point at, Window window) => window.PointToScreen(at);

    /// <summary>Подсказка места; null — её нет.</summary>
    private static Border? Hint(DockView view) =>
        OverlayLayer.GetOverlayLayer(view)?.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("dock-hint"));

    /// <summary>Черта вставки в полосе вкладок; null — её нет.</summary>
    private static Border? Caret(DockView view) =>
        OverlayLayer.GetOverlayLayer(view)?.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("dock-caret"));

    /// <summary>Что показано в группе.</summary>
    private static object? Content(DockGroupView group) =>
        group.GetVisualDescendants()
            .OfType<ContentControl>()
            .First(content => content.Name == "PART_Content")
            .Content;

    /// <summary>
    /// Клиентская область окна заходит в системную рамку.
    /// </summary>
    /// <remarks>
    /// Иначе Windows оставляет сверху восемь пикселей рамки изменения размера —
    /// при одном по бокам, — и они ложатся полосой над заголовком: рамку студия
    /// красит в цвет темы, так что пустота эта хорошо заметна. Промерено на
    /// живых окнах: <c>DwmGetWindowAttribute</c> против <c>ClientToScreen</c>
    /// давал 8 сверху и 1 слева, стал 1 и 1. Заодно ушёл выход развёрнутого окна
    /// на те же восемь пикселей за каждый край экрана.
    /// <para>
    /// Проверяется сама настройка, а не её последствия: рамку рисует система, и
    /// в headless её нет. Настройку легко потерять при правке окна — она о том,
    /// чего на экране <b>нет</b>, и пропажу заметил бы только глаз.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_studio_window_reaches_into_its_own_frame()
    {
        var window = new AxWindow();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
    }

    /// <summary>
    /// Полоса вкладок занимает шапку целиком, не считая её отступов.
    /// </summary>
    /// <remarks>
    /// Отступ до вкладок принадлежит заголовку панели: он их от него отделяет. У
    /// группы доков заголовка нет никогда — подписаны сами вкладки, — и лишние
    /// двенадцать пикселей отделяли вкладки от пустоты, съедая место, на которое
    /// их и помещается на одну больше.
    /// </remarks>
    [AvaloniaFact]
    public void The_tab_strip_fills_the_header_it_lives_in()
    {
        var (view, _) = Pair();

        var group = view.View("left")!;
        var header = group.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Name == "PART_Header");

        var strip = group.GetVisualDescendants().OfType<AxTabStrip>().First();
        var corner = strip.TranslatePoint(default, header);

        Assert.NotNull(corner);

        // Ровно отступ шапки — и ни пикселем больше.
        Assert.Equal(header.Padding.Left, corner.Value.X);
        Assert.Equal(
            header.Bounds.Width - header.Padding.Left - header.Padding.Right,
            strip.Bounds.Width);
    }

    /// <summary>
    /// У панели с заголовком вкладки от него отступают.
    /// </summary>
    /// <remarks>
    /// В Int UI вкладки не заменяют заголовок, а встают рядом — «Проект»
    /// остаётся подписан, когда внутри переключаются «Консоль» и «Проблемы».
    /// Отступ и есть то, что их разделяет: он потому и висит на заголовке, а не
    /// на вкладках — у панели без заголовка разделять нечего.
    /// </remarks>
    [AvaloniaFact]
    public void Tabs_keep_their_distance_from_a_title()
    {
        var tabs = new AxTabStrip();
        var tool = new AxToolWindow { Title = "Проект", Tabs = tabs };

        new Window { Content = tool, Width = 400, Height = 200 }.Show();
        Dispatcher.UIThread.RunJobs();

        var title = tool.GetVisualDescendants()
            .OfType<TextBlock>()
            .First(text => text.Name == "PART_Title");

        var corner = tabs.TranslatePoint(default, title);

        Assert.NotNull(corner);
        Assert.Equal(title.Bounds.Width + 12, corner.Value.X);
    }
}
