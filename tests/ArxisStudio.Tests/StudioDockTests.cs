using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
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
/// Раскладка студии: как панели плагинов попадают в дерево доков.
/// </summary>
/// <remarks>
/// Очередь общая с остальными: заголовки панелей привязываются к словарям, а
/// <c>Localizer</c> один на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioDockTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"arxis-dock-{Guid.NewGuid():N}");

    public StudioDockTests() => Directory.CreateDirectory(_directory);

    private string File => Path.Combine(_directory, "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Панель встаёт в объявленную сторону, вторая — вкладкой рядом.</summary>
    [AvaloniaFact]
    public void A_panel_takes_the_side_it_asked_for()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("hello", "hello:outline", At("left"), "Структура", Strings, new Border());

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree", "hello:outline"], left.Items);
        Assert.Equal("hello:outline", left.Selected);
    }

    /// <summary>
    /// Пустая сторона места не занимает, но из дерева не уходит.
    /// </summary>
    /// <remarks>
    /// Стороны заведены заранее и с готовыми размерами. Показывать их пустыми
    /// незачем — студия без единого плагина показывает одну область
    /// документов, — но и сносить нельзя: пришедшая панель тогда делила бы
    /// пополам то, что подвернулось, вместо того чтобы встать на своё место.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_side_takes_no_room_but_stays_in_the_tree()
    {
        var (dock, view) = Dock();

        Assert.Equal([StudioDock.Documents], Shown(view));

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["left", StudioDock.Documents], Shown(view));

        // Правая сторона и низ на экране не появились, а в дереве стоят.
        Assert.NotNull(DockTree.Group(view.Root!, "right"));
        Assert.NotNull(DockTree.Group(view.Root!, "bottom"));
    }

    /// <summary>Уход хозяина убирает его панели с экрана.</summary>
    [AvaloniaFact]
    public void The_owner_leaving_takes_its_panels_off_the_screen()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        dock.RemoveOwnedBy("hello");
        Dispatcher.UIThread.RunJobs();

        Assert.Null(dock.Items.Find("hello:tree"));
        Assert.Equal(1, dock.Items.Count);
        Assert.Equal([StudioDock.Documents, "right"], Shown(view));
    }

    /// <summary>
    /// Панель возвращается ровно туда, где стояла.
    /// </summary>
    /// <remarks>
    /// Это и есть смысл того, что имена остаются в дереве. Выключил плагин и
    /// включил обратно — панель на своём месте, а не там, куда её отправил бы
    /// манифест; манифест спрашивают только про незнакомое имя.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_comes_back_exactly_where_it_stood()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("hello", "hello:outline", At("left"), "Структура", Strings, new Border());
        dock.RemoveOwnedBy("hello");

        // Плагин подняли заново — и он снова просится влево, но его уже не спрашивают.
        dock.Add("hello", "hello:outline", At("right"), "Структура", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree", "hello:outline"], left.Items);
        Assert.Equal(["left", StudioDock.Documents], Shown(view));
    }

    /// <summary>Документ открывается в области документов и становится выбранным.</summary>
    [AvaloniaFact]
    public void A_document_opens_where_documents_open()
    {
        var (dock, view) = Dock();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        dock.Open("hello", "doc:b.axaml", "b.axaml", new Border());

        var documents = DockTree.Group(view.Root!, StudioDock.Documents);

        Assert.NotNull(documents);
        Assert.Equal(["doc:a.axaml", "doc:b.axaml"], documents.Items);
        Assert.Equal("doc:b.axaml", dock.Showing);

        dock.Show("doc:a.axaml");

        Assert.Equal("doc:a.axaml", dock.Showing);
    }

    /// <summary>
    /// Закрытый документ уходит совсем, а место для документов остаётся.
    /// </summary>
    /// <remarks>
    /// Закрытая вкладка — не выключенный плагин: возвращать её некуда и незачем,
    /// поэтому имя уходит из дерева. Область документов при этом не исчезает —
    /// иначе следующий документ появился бы неизвестно где.
    /// </remarks>
    [AvaloniaFact]
    public void A_closed_document_leaves_for_good_but_its_place_remains()
    {
        var (dock, view) = Dock();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        dock.Remove("doc:a.axaml");
        Dispatcher.UIThread.RunJobs();

        var documents = DockTree.Group(view.Root!, StudioDock.Documents);

        Assert.NotNull(documents);
        Assert.Empty(documents.Items);
        Assert.Null(dock.Showing);
        Assert.Equal([StudioDock.Documents], Shown(view));
    }

    /// <summary>
    /// Незнакомая сторона всё равно даёт панели место — справа от документов.
    /// </summary>
    /// <remarks>
    /// Манифест пишет автор плагина, и слово в нём может быть любым. Отказать
    /// значило бы потерять панель молча; студия ставит её рядом с документами и
    /// оставляет человеку решать, где ей быть.
    /// </remarks>
    [AvaloniaFact]
    public void An_unknown_side_still_gets_a_place()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:odd", At("нигде"), "Странная", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("нигде", DockTree.Holder(view.Root!, "hello:odd")?.Id);
        Assert.Equal([StudioDock.Documents, "нигде"], Shown(view));
    }

    /// <summary>
    /// Ключ в заголовке переводится, обычный текст — нет.
    /// </summary>
    /// <remarks>
    /// Заголовок панели — единственный её текст, который показывает не автор, а
    /// студия: значит и переводить его при смене языка ей. Ключ узнаётся по
    /// процентам вокруг, всё остальное показывается как есть.
    /// </remarks>
    [AvaloniaFact]
    public void A_key_in_the_title_is_translated_and_plain_text_is_not()
    {
        var (dock, _) = Dock();

        dock.Add("hello", "hello:plain", At("left"), "Проект", Strings, new Border());
        dock.Add("hello", "hello:key", At("left"), "%panel.main%", Strings, new Border());

        Assert.Equal("Проект", dock.Items.Find("hello:plain")?.Title);

        var translated = dock.Items.Find("hello:key")?.Title;

        Assert.NotNull(translated);
        Assert.DoesNotContain("%", translated, StringComparison.Ordinal);
    }

    /// <summary>
    /// Вынесенная за пределы дерева вкладка получает своё окно.
    /// </summary>
    /// <remarks>
    /// За пределами дерева ничего нет, и отпустить там вкладку человек может
    /// только нарочно. Панель при этом не строится заново — она переезжает
    /// вместе с именем и сохраняет всё, что помнит о себе сама.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_carried_out_of_the_tree_gets_its_own_window()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);

        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));

        // Имя лежит ровно в одном дереве: у контрола Avalonia один родитель.
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
        Assert.Equal("Проект", torn.Title);
    }

    /// <summary>
    /// Брошенная на границу вкладка остаётся где была.
    /// </summary>
    /// <remarks>
    /// Граница между областями — это промах, а не вынос: человек целился в
    /// соседнюю область и не попал. Заводить ему на этом месте окно значило бы
    /// наказывать за неточность мыши.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_on_a_border_stays_where_it_was()
    {
        var (dock, view, window) = Two();

        var left = view.View("left")!;
        var splitter = view.GetVisualDescendants().OfType<GridSplitter>().First();
        var edge = splitter.TranslatePoint(
            new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window);

        Assert.NotNull(edge);

        DockMouse.Drag(window, DockMouse.Tab(left, 0, window), edge.Value);

        Assert.Empty(dock.Floating);
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Закрытое окно возвращает панель домой.
    /// </summary>
    /// <remarks>
    /// Закрыть окно — не значит выбросить панель: другого пути назад у человека
    /// пока нет, и панель, пропавшая вместе с окном, выглядела бы потерей.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_a_torn_window_brings_the_panel_home()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Assert.Single(dock.Floating);

        dock.Floating[0].Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(dock.Floating);
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Вкладка переезжает в оторванное окно, если её отпустили над ним.
    /// </summary>
    /// <remarks>
    /// Пока кнопка нажата, движения приходят окну, начавшему тягу, даже когда
    /// курсор давно над чужим. Оно и сообщает точку экрана — а какое окно под
    /// ней, знает раскладка: окон у неё несколько, у вида оно одно.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_released_over_a_torn_window_moves_into_it()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);
        var group = torn.View.Root!.Groups().Single().Id;

        // Оторванное окно легло поверх главного, и точка в нём — точка и в том
        // и в другом. Спросить обязаны сперва то, что сверху.
        DockMouse.Drag(window, DockMouse.Tab(view.View("right")!, 0, window), new Point(200, 200));

        Assert.Equal(group, DockTree.Holder(torn.View.Root!, "friend:tips")?.Id);
        Assert.Null(DockTree.Holder(view.Root!, "friend:tips"));
        Assert.Equal(2, dock.Items.Count);
    }

    /// <summary>Вкладка возвращается из оторванного окна в главное тем же путём.</summary>
    [AvaloniaFact]
    public void A_tab_released_over_the_main_window_moves_back()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);

        // Ведём из оторванного окна вниз, туда, где под ним только главное.
        DockMouse.Drag(
            torn,
            DockMouse.Tab(torn.View.View(torn.View.Root!.Groups().Single().Id)!, 0, torn),
            new Point(700, 600));

        Assert.NotNull(DockTree.Holder(view.Root!, "hello:tree"));

        // Опустевшее окно закрылось само.
        Assert.Empty(dock.Floating);
    }

    /// <summary>
    /// Пока вкладку несут, место показывает то окно, над которым курсор.
    /// </summary>
    /// <remarks>
    /// Подсказка обязана быть там же, где курсор: окно, начавшее тягу, к этому
    /// мигу может быть уже далеко, и подсветка в нём говорила бы неправду.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_under_the_cursor_shows_where_the_tab_lands()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);
        var from = DockMouse.Tab(view.View("right")!, 0, window);
        var to = new Point(200, 200);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(Hint(torn.View));
        Assert.Null(Hint(view));

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Hint(torn.View));
        Assert.Null(Hint(view));
    }

    /// <summary>Подсветка места, куда встанет вкладка; null — её нет.</summary>
    private static Border? Hint(DockView view) =>
        OverlayLayer.GetOverlayLayer(view)?.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("dock-hint"));

    /// <summary>
    /// Вынесенная из оторванного окна вкладка переносит его, а не пропадает.
    /// </summary>
    /// <remarks>
    /// Отпущенная мимо всех деревьев вкладка всегда получает окно — откуда её
    /// несли, неважно. Прежнее окно при этом опустело и закрылось: пустая рамка
    /// не нужна никому.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_carried_out_of_a_torn_window_moves_it()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var first = Assert.Single(dock.Floating);

        Tear(first.View, first, first.View.Root!.Groups().First().Id);
        Dispatcher.UIThread.RunJobs();

        var second = Assert.Single(dock.Floating);

        Assert.NotSame(first, second);
        Assert.Equal("hello:tree", second.View.Root!.Groups().Single().Items.Single());
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
    }

    /// <summary>
    /// Оторванное окно прячется, пока его плагин выключен, и возвращается с ним.
    /// </summary>
    /// <remarks>
    /// Имя панели в дереве окна остаётся: выключенный плагин обязан вернуться
    /// туда, где стоял. Пустая рамка на экране при этом человеку не нужна.
    /// </remarks>
    [AvaloniaFact]
    public void A_torn_window_waits_out_its_plugin_hidden()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);

        Assert.True(torn.IsVisible);

        dock.RemoveOwnedBy("hello");
        Dispatcher.UIThread.RunJobs();

        Assert.True(dock.Floating.Count == 1, "имя панели ушло вместе с окном");
        Assert.False(torn.IsVisible);

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.True(torn.IsVisible);
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
    }

    /// <summary>
    /// Закрытие студии не разбирает оторванные окна.
    /// </summary>
    /// <remarks>
    /// Оторванные окна закрываются вместе с главным, и их закрытие — не то, о
    /// котором просил человек: разбери их студия, в файл уехала бы раскладка
    /// без них, и наутро окон не стало бы.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_the_studio_does_not_take_the_torn_windows_apart()
    {
        var store = new DockLayoutStore(File);
        var (dock, view, window) = Two(store);

        Tear(view, window, "left");
        Assert.Single(dock.Floating);

        // Студия прощается до закрытия окон — там и записывается раскладка.
        window.Closing += (_, _) => dock.Farewell();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Панели вернулись домой, но в файл эта правка уже не попадёт.
        dock.Flush();

        var saved = store.Load(out _);

        Assert.Single(saved!.Current!.Floating);
        Assert.Equal(
            "hello:tree",
            saved.Current.Floating[0].Root.Groups().Single().Items.Single());
    }

    /// <summary>Оторванные окна переживают перезапуск студии вместе с местом на экране.</summary>
    [AvaloniaFact]
    public void A_torn_window_survives_a_restart()
    {
        var store = new DockLayoutStore(File);
        var (first, view, window) = Two(store);

        Tear(view, window, "left");
        first.Floating[0].Position = new PixelPoint(300, 200);
        first.Flush();

        var (second, next) = Dock(new DockLayoutStore(File));
        second.Restore();
        second.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        var torn = Assert.Single(second.Floating);

        Assert.Equal("hello:tree", torn.View.Root!.Groups().Single().Items.Single());
        Assert.Equal(new PixelPoint(300, 200), torn.Position);
        Assert.Null(DockTree.Holder(next.Root!, "hello:tree"));
    }

    /// <summary>Уносит первую вкладку названной области за пределы дерева.</summary>
    private static void Tear(DockView view, Window window, string group)
    {
        DockMouse.Drag(window, DockMouse.Tab(view.View(group)!, 0, window), new Point(-80, 60));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Панель встаёт рядом с той, которую назвала.
    /// </summary>
    /// <remarks>
    /// Соседство — пожелание точное, и оно сильнее стороны: раз плагин знает,
    /// с кем ему стоять, спрашивать его про сторону незачем.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_stands_next_to_the_one_it_named()
    {
        var (dock, view) = Dock();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add(
            "friend",
            "friend:tips",
            new PluginPlacement { Side = "right", Near = "hello:tree" },
            "Советы",
            Strings,
            new Border());

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("left", DockTree.Holder(view.Root!, "friend:tips")?.Id);
    }

    /// <summary>
    /// Названного соседа может не оказаться — тогда работает сторона.
    /// </summary>
    /// <remarks>
    /// Плагин с соседом могли не поставить или выключить. Пожелание от этого не
    /// становится ошибкой: панель просто встаёт туда, куда просилась иначе.
    /// </remarks>
    [AvaloniaFact]
    public void A_neighbour_who_is_not_there_gives_way_to_the_side()
    {
        var (dock, view) = Dock();

        dock.Add(
            "friend",
            "friend:tips",
            new PluginPlacement { Side = "right", Near = "hello:tree" },
            "Советы",
            Strings,
            new Border());

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("right", DockTree.Holder(view.Root!, "friend:tips")?.Id);
    }

    /// <summary>
    /// Ширину стороне задаёт первая панель на ней, а не каждая следующая.
    /// </summary>
    /// <remarks>
    /// У занятой стороны размер уже есть — его дал сосед или мышь человека, — и
    /// отбирать его новичок не вправе: иначе последний включённый плагин
    /// каждый раз перекраивал бы окно под себя.
    /// </remarks>
    [AvaloniaFact]
    public void The_first_panel_on_an_empty_side_sets_its_width()
    {
        var (dock, view) = Dock();

        dock.Add(
            "hello",
            "hello:tree",
            new PluginPlacement { Side = "left", Size = 0.4 },
            "Проект",
            Strings,
            new Border());

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.4, Assert.IsType<DockSplit>(view.Root).Weights[0], 6);

        dock.Add(
            "friend",
            "friend:tips",
            new PluginPlacement { Side = "left", Size = 0.9 },
            "Советы",
            Strings,
            new Border());

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.4, Assert.IsType<DockSplit>(view.Root).Weights[0], 6);
        Assert.Equal("left", DockTree.Holder(view.Root!, "friend:tips")?.Id);
    }

    /// <summary>
    /// Брошенная в середину вкладка переезжает в чужую группу.
    /// </summary>
    /// <remarks>
    /// Перетаскивание проверяется настоящей мышью: между нажатием на вкладку и
    /// новым деревом лежит вся дорога — порог, захват указателя, поиск цели,
    /// снятие, вставка, — и обрыв на любом её шаге выглядит одинаково.
    /// </remarks>
    [AvaloniaFact]
    public void A_dragged_tab_moves_into_the_group_it_was_dropped_on()
    {
        var (dock, view, window) = Two();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);
        Assert.Equal("hello:tree", DockTree.Group(view.Root!, "right")?.Selected);

        // Из левой группы панель ушла — а с нею и сама группа: человек унёс
        // последнее, что в ней стояло, и держать пустое место незачем.
        Assert.Null(DockTree.Group(view.Root!, "left"));

        // Переезд — не потеря: обе панели живы, просто стоят вместе.
        Assert.Equal(2, dock.Items.Count);
    }

    /// <summary>
    /// Брошенная у края вкладка заводит новую область.
    /// </summary>
    /// <remarks>
    /// Имя новой группе даёт студия, и оно попадёт в файл раскладки, поэтому
    /// берётся первое свободное, а не «следующее по счётчику»: иначе имена
    /// росли бы без конца, когда области заводят и сносят по кругу.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_at_the_edge_makes_a_new_area()
    {
        var (_, view, window) = Two();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.9, window));

        var holder = DockTree.Holder(view.Root!, "hello:tree");

        Assert.NotNull(holder);
        Assert.NotEqual("left", holder.Id);
        Assert.NotEqual("right", holder.Id);
        Assert.Equal(["hello:tree"], holder.Items);

        // Она встала под правой, а не рядом с ней.
        var split = Assert.IsType<DockSplit>(view.Root);
        var inner = Assert.IsType<DockSplit>(split.Children[^1]);

        Assert.Equal(DockOrientation.Vertical, inner.Orientation);
        Assert.Equal(["right", holder.Id], inner.Children.Cast<DockGroup>().Select(group => group.Id));

        // Второй области нужно своё имя, а не то же самое.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Inside(view.View(holder.Id)!, 0.1, 0.5, window));

        var second = DockTree.Holder(view.Root!, "friend:tips");

        Assert.NotNull(second);
        Assert.NotEqual(holder.Id, second.Id);
    }

    /// <summary>
    /// Последняя вкладка, брошенная в свою же группу, не пропадает.
    /// </summary>
    /// <remarks>
    /// Снять и поставить — две правки, и между ними группа исчезает: она
    /// опустела. Ставить некуда, и правка отменяется целиком — иначе панель
    /// просто пропала бы с экрана, а человек всего лишь промахнулся мимо
    /// соседа.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_back_onto_its_own_group_changes_nothing()
    {
        var (_, view, window) = Two();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "friend:tips")?.Id);
        Assert.NotNull(view.View("right"));
    }

    /// <summary>
    /// Документ закрывается крестиком, а панель плагина — нет.
    /// </summary>
    /// <remarks>
    /// У панели крестика нет не по забывчивости: закрытая панель уходит из
    /// дерева вместе со своим местом, а вернуть её человеку пока нечем, кроме
    /// сброса всей раскладки. Документ же открывают заново тем же файлом.
    /// </remarks>
    [AvaloniaFact]
    public void A_document_closes_by_its_cross_and_a_panel_has_none()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<AxTabItem>(DockMouse.Tabs(view.View("left")!).Items[0]);

        Assert.False(panel.IsClosable);

        string? asked = null;
        dock.Closing += (_, id) => asked = id;

        DockMouse.Click(window, DockMouse.Cross(view.View(StudioDock.Documents)!, 0, window));

        Assert.Equal("doc:a.axaml", asked);
    }

    /// <summary>
    /// Сброс возвращает раскладку к той, что бывает при первом запуске.
    /// </summary>
    /// <remarks>
    /// Без него перетаскивание — дверь в одну сторону: перекроить можно, а
    /// вернуть как было нечем. Панели раскладываются по объявленным местам и в
    /// том же порядке, в каком вставали при подъёме.
    /// </remarks>
    [AvaloniaFact]
    public void Resetting_puts_the_layout_back_the_way_it_starts()
    {
        var (dock, view, window) = Two();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        dock.Reset();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
        Assert.Equal("right", DockTree.Holder(view.Root!, "friend:tips")?.Id);
        Assert.Equal(["left", StudioDock.Documents, "right"], Shown(view));
    }

    /// <summary>Сброшенная раскладка сразу ложится в файл.</summary>
    /// <remarks>
    /// Иначе человек сбросил бы раскладку, закрыл студию раньше паузы записи и
    /// увидел бы наутро ту же кашу, от которой избавлялся.
    /// </remarks>
    [AvaloniaFact]
    public void A_reset_layout_reaches_the_file_at_once()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.Reset();

        Assert.True(System.IO.File.Exists(File));
    }

    /// <summary>
    /// Раскладка переживает перезапуск студии.
    /// </summary>
    /// <remarks>
    /// И место панели помнится раньше, чем сама панель появится: плагин ещё не
    /// поднят, экран пуст, но группа за ним числится — и поднятый плагин встаёт
    /// туда, а не туда, куда просится его манифест.
    /// </remarks>
    [AvaloniaFact]
    public void The_layout_survives_a_restart()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Flush();

        // Студию закрыли и открыли заново: тот же файл, новое окно.
        var (again, view) = Dock(new DockLayoutStore(File));

        again.Restore();

        var left = DockTree.Group(view.Root!, "left");

        Assert.NotNull(left);
        Assert.Equal(["hello:tree"], left.Items);
        Assert.Equal([StudioDock.Documents], Shown(view));

        // Плагин просится вправо — его не спрашивают.
        again.Add("hello", "hello:tree", At("right"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["left", StudioDock.Documents], Shown(view));
    }

    /// <summary>Нетронутая раскладка в файл не едет.</summary>
    /// <remarks>
    /// Иначе первый же запуск студии записывал бы скелет, ничего человеку не
    /// обещающий, — и потом объяснял бы, почему стандартная раскладка «уже
    /// сохранена».
    /// </remarks>
    [AvaloniaFact]
    public void An_untouched_layout_is_not_written()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.Flush();

        Assert.False(System.IO.File.Exists(File));
    }

    /// <summary>
    /// Место для документов берётся из файла, а не из имени по умолчанию.
    /// </summary>
    /// <remarks>
    /// Документы не выделены типом — выделен указатель, и хранится он вместе с
    /// раскладкой. Человек мог увести документы в другую группу, и следующий
    /// открытый файл обязан появиться там же.
    /// </remarks>
    [AvaloniaFact]
    public void The_place_for_documents_comes_from_the_file()
    {
        new DockLayoutStore(File).Save(new DockLayout
        {
            Active = DockLayout.DefaultName,
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new()
                {
                    DocumentHome = "centre",
                    Root = new DockGroup { Id = "centre" },
                },
            },
        });

        var (dock, view) = Dock(new DockLayoutStore(File));

        dock.Restore();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["centre"], Shown(view));

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());

        Assert.Equal(["doc:a.axaml"], ((DockGroup)view.Root!).Items);
        Assert.Equal("doc:a.axaml", dock.Showing);
    }

    /// <summary>
    /// Сохранение под именем заводит набор и переводит в него.
    /// </summary>
    /// <remarks>
    /// Прежний набор при этом ничего не теряет: показанная раскладка и была
    /// им — студия пишет её туда после каждой правки.
    /// </remarks>
    [AvaloniaFact]
    public void Saving_under_a_name_starts_a_set_and_moves_into_it()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.SaveAs("  отладка  ");

        Assert.Equal("отладка", dock.Layout);
        Assert.Equal(["default", "отладка"], dock.Layouts);
    }

    /// <summary>Безымянный набор не заводится.</summary>
    [AvaloniaFact]
    public void A_set_without_a_name_is_not_started()
    {
        var (dock, _) = Dock(new DockLayoutStore(File));

        dock.SaveAs("   ");

        Assert.Equal("default", dock.Layout);
        Assert.Equal(["default"], dock.Layouts);
    }

    /// <summary>
    /// Переключение возвращает ту раскладку, что была в наборе.
    /// </summary>
    /// <remarks>
    /// И ту, что человек оставил в покинутом: он её не сохранял, но и не
    /// отменял — он всего лишь ушёл посмотреть другую.
    /// </remarks>
    [AvaloniaFact]
    public void Switching_brings_back_the_layout_of_each_set()
    {
        var (dock, view, window) = Two();

        dock.SaveAs("отладка");

        // В «отладке» панель переезжает к соседке, в «default» она осталась слева.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        dock.Switch("default");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("default", dock.Layout);
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        dock.Switch("отладка");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Набор, сохранённый до плагина, всё равно показывает его панель.
    /// </summary>
    /// <remarks>
    /// Иначе панель просто пропала бы с экрана при переключении, и человек
    /// решил бы, что плагин сломался, — хотя дело в возрасте набора.
    /// </remarks>
    [AvaloniaFact]
    public void A_set_older_than_a_plugin_still_shows_its_panel()
    {
        var (dock, view) = Dock(new DockLayoutStore(File));

        dock.SaveAs("отладка");
        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        dock.Switch("default");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Забытый набор уступает место стандартному, а сам стандартный не забывается.
    /// </summary>
    /// <remarks>
    /// Стандартный — то, куда возвращаются: студия без него осталась бы без
    /// единого имени, и удалять его значило бы удалять саму раскладку.
    /// </remarks>
    [AvaloniaFact]
    public void A_forgotten_set_gives_way_to_the_standard_one()
    {
        var (dock, view, window) = Two();

        dock.SaveAs("отладка");
        dock.Forget();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("default", dock.Layout);
        Assert.Equal(["default"], dock.Layouts);

        // Раскладка стандартного набора, к которой человек вернулся, — его
        // собственная, и второе «забыть» её не трогает: забывать нечего.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        dock.Forget();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["default"], dock.Layouts);
        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Наборы переживают перезапуск студии — все, а не только показанный.
    /// </summary>
    /// <remarks>
    /// Набор, в который человек не заходил, обязан пережить и правки соседей:
    /// студия пишет файл целиком после каждой из них.
    /// </remarks>
    [AvaloniaFact]
    public void Every_set_survives_a_restart()
    {
        var (first, view) = Dock(new DockLayoutStore(File));

        first.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        first.SaveAs("отладка");
        first.Flush();

        var (second, next) = Dock(new DockLayoutStore(File));
        second.Restore();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("отладка", second.Layout);
        Assert.Equal(["default", "отладка"], second.Layouts);
        Assert.Equal("left", DockTree.Holder(next.Root!, "hello:tree")?.Id);

        // Показанный набор в прежней студии остался на месте.
        Assert.NotNull(view.Root);
    }

    /// <summary>Пожелание «встань с этой стороны» — как его пишет манифест.</summary>
    private static PluginPlacement At(string side) => new() { Side = side };

    private static PluginStrings Strings => PluginStrings.Studio;

    /// <summary>Имена групп, которые сейчас на экране, слева направо.</summary>
    private static IReadOnlyList<string> Shown(DockView view) =>
        [.. view.GetVisualDescendants().OfType<DockGroupView>().Select(group => group.Id)];

    /// <summary>Две группы рядом: слева панель одного плагина, справа другого.</summary>
    private static (StudioDock Dock, DockView View, Window Window) Two(DockLayoutStore? store = null)
    {
        var (dock, view) = Dock(store);

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        return (dock, view, Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view)));
    }

    private static (StudioDock Dock, DockView View) Dock(DockLayoutStore? store = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view, store);

        new Window { Content = view, Width = 1200, Height = 800 }.Show();
        Dispatcher.UIThread.RunJobs();

        return (dock, view);
    }
}
