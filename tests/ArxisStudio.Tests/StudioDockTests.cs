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
using Avalonia.Interactivity;
using Avalonia.Media;
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
    /// Граница между областями принадлежит соседке — её краевой зоне, — и
    /// значит «раздели». Но делить область её же единственной вкладкой нечем:
    /// вкладка сперва уходит, группа пустеет и прибирается, ставить некуда, и
    /// правка отменяется целиком. Своего окна человек за неточность мыши не
    /// получает.
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
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Across(torn, DockMouse.Tab(torn.View.View(group)!, 0, torn), window));

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
    /// Пока вкладку несут, будущую раскладку показывает то окно, над которым курсор.
    /// </summary>
    /// <remarks>
    /// Показывать её окну, начавшему тягу, было бы неправдой: курсор к этому
    /// мигу может быть уже далеко. Настоящее дерево при этом не трогают — пока
    /// кнопка нажата, человек волен увести вкладку куда угодно.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_under_the_cursor_shows_where_the_tab_lands()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        var torn = Assert.Single(dock.Floating);
        var group = torn.View.Root!.Groups().Single().Id;
        var was = torn.View.Root;
        var untouched = Shown(view);
        var from = DockMouse.Tab(view.View("right")!, 0, window);
        var to = DockMouse.Across(
            torn, DockMouse.Inside(torn.View.View(group)!, 0.1, 0.5, torn), window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        // Подсказка — в том окне, над которым курсор, и только там. Раскладка
        // при этом не тронута ни в одном из них.
        Assert.NotNull(Hint(torn.View));
        Assert.Null(Hint(view));
        Assert.Same(was, torn.View.Root);
        Assert.Equal(untouched, Shown(view));

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // А после броска панель и правда переехала.
        Assert.NotNull(DockTree.Holder(torn.View.Root!, "friend:tips"));
        Assert.Null(DockTree.Holder(view.Root!, "friend:tips"));
    }

    /// <summary>Подсказка места; null — её нет.</summary>
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

    /// <summary>
    /// Убранная панель остаётся убранной и после перезапуска.
    /// </summary>
    /// <remarks>
    /// Кнопка в шапке только просит: сторону записывает в дерево студия, и она
    /// же уносит её в файл вместе с остальной раскладкой. Разорвись эта дорога
    /// — панель возвращалась бы на экран при каждом запуске, а человек убирал
    /// бы её заново.
    /// </remarks>
    [AvaloniaFact]
    public void A_stowed_panel_comes_back_stowed()
    {
        var store = new DockLayoutStore(File);
        var (first, view, _) = Two(store);
        var group = view.View("left")!;

        Assert.True(group.CanStow, "у панели с соседом нет кнопки уборки");

        var button = group.GetVisualDescendants().OfType<AxButton>().Single(item => item.Name == "PART_Stow");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            DockSide.Left,
            view.Root!.Groups().Single(candidate => candidate.Id == "left").Rail);

        first.Flush();

        var (second, next) = Dock(new DockLayoutStore(File));

        second.Restore();
        second.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        second.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            DockSide.Left,
            next.Root!.Groups().Single(candidate => candidate.Id == "left").Rail);

        // Убранной группы на экране нет вовсе: её место у соседа, а размер
        // ждёт в дереве.
        Assert.Null(next.View("left"));
    }

    /// <summary>
    /// Убранная панель встаёт кнопкой на рейку своей стороны и возвращается щелчком.
    /// </summary>
    /// <remarks>
    /// Рейка — единственная дорога назад: убранной группы на экране нет, и в
    /// дереве от неё остаётся одно имя. Разорвись эта дорога — кнопка «убрать»
    /// стала бы кнопкой «потерять».
    /// </remarks>
    [AvaloniaFact]
    public void A_stowed_panel_waits_on_the_rail_of_its_side()
    {
        var (dock, view, _) = Two();

        Assert.Empty(dock.Stowed(DockSide.Left));

        view.Root = DockTree.Stow(view.Root!, "left", DockSide.Left);
        Settle();

        var button = Assert.Single(dock.Stowed(DockSide.Left));

        Assert.Equal("left", button.Group);
        Assert.Equal("hello:tree", button.Item.Id);
        Assert.Empty(dock.Stowed(DockSide.Right));

        dock.Unstow(button);
        Settle();

        Assert.Empty(dock.Stowed(DockSide.Left));
        Assert.NotNull(view.View("left"));
    }

    /// <summary>
    /// Панели выключенного плагина кнопки на рейке не достаётся.
    /// </summary>
    /// <remarks>
    /// За её именем нет контрола, и по щелчку человек получил бы пустое место
    /// вместо панели. Место в дереве при этом за ней числится — плагин
    /// включат, и она вернётся на рейку сама.
    /// </remarks>
    [AvaloniaFact]
    public void A_dead_panel_gets_no_button_on_the_rail()
    {
        var (dock, view, _) = Two();

        view.Root = DockTree.Stow(view.Root!, "left", DockSide.Left);
        Settle();

        Assert.Single(dock.Stowed(DockSide.Left));

        dock.RemoveOwnedBy("hello");
        Settle();

        Assert.Empty(dock.Stowed(DockSide.Left));

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Settle();

        Assert.Single(dock.Stowed(DockSide.Left));
    }

    /// <summary>
    /// Оболочку зовут пересобрать рейки на каждой правке раскладки.
    /// </summary>
    /// <remarks>
    /// Рейки живут снаружи дерева, и другого способа узнать о правке у них нет.
    /// Молчание здесь выглядит как пропавшая панель: с экрана она ушла, а
    /// кнопки не появилось.
    /// </remarks>
    [AvaloniaFact]
    public void The_shell_hears_about_every_shift()
    {
        var (dock, view, _) = Two();
        var heard = 0;

        dock.Shifted += (_, _) => heard++;

        view.Root = DockTree.Stow(view.Root!, "left", DockSide.Left);
        Settle();

        Assert.Equal(0, heard);

        dock.Unstow(Assert.Single(dock.Stowed(DockSide.Left)));
        Settle();

        Assert.True(heard > 0, "правку дерева оболочка не услышала");

        heard = 0;
        dock.RemoveOwnedBy("hello");
        Settle();

        Assert.True(heard > 0, "выключение плагина оболочка не услышала");
    }

    /// <summary>
    /// Панель, закрытую крестиком, возвращает меню — и на объявленное место.
    /// </summary>
    /// <remarks>
    /// Крестик у панели появился вместе с этой дорогой назад. Без неё закрытие
    /// было бы дверью в одну сторону: имя ушло бы из дерева, а позвать
    /// <see cref="StudioDock.Add"/> второй раз некому — плагин зовёт его
    /// однажды, при подъёме.
    /// </remarks>
    [AvaloniaFact]
    public void A_closed_panel_comes_back_from_the_menu()
    {
        var (dock, view, _) = Two();

        Assert.Equal(["hello:tree", "friend:tips"], dock.Panels.Select(panel => panel.Id));
        Assert.All(dock.Panels, panel => Assert.True(panel.Standing));

        dock.Hide("hello:tree");
        Settle();

        Assert.Null(view.View("left"));
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
        Assert.False(dock.Panels.Single(panel => panel.Id == "hello:tree").Standing);

        // Контрол при этом никуда не делся: панель закрыта, а не выброшена.
        Assert.NotNull(dock.Items.Find("hello:tree"));

        dock.Reopen("hello:tree");
        Settle();

        // На объявленное место, а не куда придётся: панель просилась влево.
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
        Assert.NotNull(view.View("left"));
        Assert.True(dock.Panels.Single(panel => panel.Id == "hello:tree").Standing);
    }

    /// <summary>
    /// Закрытая панель остаётся закрытой и после перезапуска.
    /// </summary>
    /// <remarks>
    /// В дереве её имени нет — но нет его и у панели, которая просто ещё не
    /// вставала, и такую студия ставит на объявленное место. Не запиши она
    /// закрытых списком, панель возвращалась бы с каждым запуском, и человек
    /// закрывал бы её заново каждое утро.
    /// </remarks>
    [AvaloniaFact]
    public void A_closed_panel_stays_closed_after_a_restart()
    {
        var store = new DockLayoutStore(File);
        var (first, _, _) = Two(store);

        first.Hide("hello:tree");
        first.Flush();

        var (second, next) = Dock(new DockLayoutStore(File));

        second.Restore();
        second.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        second.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Settle();

        Assert.Null(DockTree.Holder(next.Root!, "hello:tree"));
        Assert.NotNull(DockTree.Holder(next.Root!, "friend:tips"));
        Assert.False(second.Panels.Single(panel => panel.Id == "hello:tree").Standing);

        // И возвращается она из меню — там же, где закрытую и ищут.
        second.Reopen("hello:tree");
        Settle();

        Assert.NotNull(DockTree.Holder(next.Root!, "hello:tree"));
    }

    /// <summary>
    /// Сброс раскладки возвращает и закрытые, и убранные панели — и говорит об этом.
    /// </summary>
    /// <remarks>
    /// «Как при первом запуске» — значит все панели на местах: при первом
    /// запуске ни закрытых, ни убранных нет. Иначе сброс, к которому человек
    /// прибегает, когда потерял панель, как раз её бы и не вернул.
    /// <para>
    /// Дерево при сбросе встаёт целиком и мимо <c>Edit</c>, и об этом надо
    /// сказать отдельно: промолчи раскладка — на рейке остались бы кнопки
    /// панелей, которые сброс только что вернул на места.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_reset_brings_every_panel_back_and_says_so()
    {
        var (dock, view, _) = Two();

        dock.Hide("hello:tree");
        view.Root = DockTree.Stow(view.Root!, "right", DockSide.Right);
        Settle();

        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
        Assert.Single(dock.Stowed(DockSide.Right));

        var heard = 0;

        dock.Shifted += (_, _) => heard++;
        dock.Reset();
        Settle();

        Assert.NotNull(DockTree.Holder(view.Root!, "hello:tree"));
        Assert.All(dock.Panels, panel => Assert.True(panel.Standing));
        Assert.Empty(dock.Stowed(DockSide.Right));
        Assert.True(heard > 0, "о сбросе оболочке не сказали — рейка осталась со вчерашними кнопками");
    }

    /// <summary>
    /// Показанным считается документ, а не панель с крестиком.
    /// </summary>
    /// <remarks>
    /// Прежде документ узнавали по крестику — единственному, что его отличало.
    /// С крестиком у панели признак перестал различать, и оторванная панель
    /// выдала бы себя за показанный документ: оболочка написала бы её имя в
    /// строке состояния и разбудила бы не тот редактор.
    /// </remarks>
    [AvaloniaFact]
    public void A_torn_panel_does_not_pass_for_a_shown_document()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        Assert.Single(dock.Floating);
        Assert.Null(dock.Showing);

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Assert.Equal("doc:a.axaml", dock.Showing);
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
            DockMouse.Tab(view.View("right")!, 0, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);
        Assert.Equal("hello:tree", DockTree.Group(view.Root!, "right")?.Selected);

        // Из левой группы панель ушла — а с нею и сама группа: человек унёс
        // последнее, что в ней стояло, и держать пустое место незачем.
        Assert.Null(DockTree.Group(view.Root!, "left"));

        // Переезд — не потеря: обе панели живы, просто стоят вместе.
        Assert.Equal(2, dock.Items.Count);
    }

    /// <summary>
    /// Брошенная в середину области вкладка уходит в своё окно.
    /// </summary>
    /// <remarks>
    /// Так человеку не нужен свободный рабочий стол, чтобы оторвать панель:
    /// довольно бросить её в середину любой области. Панель при этом обязана
    /// уцелеть — снять её из дерева и никуда не поставить значит потерять.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_in_the_middle_gets_a_window_of_its_own()
    {
        var (dock, view, window) = Two();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            DockMouse.Inside(view.View("right")!, 0.5, 0.5, window));

        var torn = Assert.Single(dock.Floating);

        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
        Assert.Equal(2, dock.Items.Count);
    }

    /// <summary>
    /// Брошенная на полосу над деревом вкладка ложится во всю ширину окна.
    /// </summary>
    /// <remarks>
    /// Полосу поперёк всего окна иначе собрать нечем: любое деление области
    /// оказывается внутри чьей-то колонки. Целятся туда, где дерева нет, — у
    /// студии это тулбар и строка состояния.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_above_the_tree_lies_across_the_whole_window()
    {
        var (_, view, window) = Strips();

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("left")!, 0, window),
            new Point(window.ClientSize.Width / 2, 12));

        var split = Assert.IsType<DockSplit>(view.Root);

        Assert.Equal(DockOrientation.Vertical, split.Orientation);
        Assert.Equal("hello:tree", ((DockGroup)split.Children[0]).Items.Single());
        Assert.Equal(0.25, split.Weights[0], 6);

        // Полоса — сосед всего прежнего дерева, а не чьей-то колонки.
        Assert.IsType<DockSplit>(split.Children[1]);
    }

    /// <summary>
    /// Вкладка, брошенная на рейку, встаёт полосой с её стороны.
    /// </summary>
    /// <remarks>
    /// Ради этого рейка и стоит снаружи дерева, а не внутри него: точку внутри
    /// окна, но вне дерева, вид уже считает корневой стыковкой, и бросок на
    /// рейку работает сам собой. Поставь рейку внутрь вида — и этот бросок
    /// пришлось бы разбирать отдельно, правя самый рискованный код движка.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_dropped_on_the_rail_docks_to_that_side()
    {
        var rail = new DockRail { Side = DockSide.Left };
        var view = new DockView();
        var dock = new StudioDock(view);

        rail[DockPanel.DockProperty] = Avalonia.Controls.Dock.Left;

        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = new DockPanel { Children = { rail, view } },
        };

        window.Show();
        dock.Shown();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Settle();

        // Пустая рейка места не занимает, и целиться было бы некуда: сперва
        // убираем на неё панель — так рейка и появляется у человека.
        view.Root = DockTree.Stow(view.Root!, "left", DockSide.Left);
        rail.Update(dock.Stowed(DockSide.Left));
        Settle();

        Assert.True(rail.Bounds.Width > 0, "рейки на экране нет — бросать не на что");

        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            new Point(rail.Bounds.Width / 2, 400));

        var split = Assert.IsType<DockSplit>(view.Root);

        Assert.Equal(DockOrientation.Horizontal, split.Orientation);
        Assert.Equal("friend:tips", Assert.IsType<DockGroup>(split.Children[0]).Items.Single());

        window.Close();
    }

    /// <summary>
    /// Пока вкладку несут, она остаётся на своём месте.
    /// </summary>
    /// <remarks>
    /// Человек ещё держит кнопку и волен передумать. Вырывать панель из-под
    /// курсора на полпути значит перекладывать всё окно на каждое движение —
    /// раскладка прыгает, и целиться становится не во что. Поэтому во время
    /// тяги панель видна дважды: телом там, где стоит, и пустым призраком там,
    /// куда собирается. Так же показывает Unity.
    /// </remarks>
    [AvaloniaFact]
    public void A_carried_tab_stays_where_it_was_until_it_is_dropped()
    {
        var (_, view, window) = Two();

        var before = Shown(view);
        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(view.View("right")!, 0.1, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();

        // Не «почти не двигается», а не двигается вовсе: подсказка рисуется
        // поверх, и ни одна настоящая область не поехала.
        Assert.Equal(before, Shown(view));
        Assert.NotNull(Hint(view));

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // А после броска панель и правда ушла: своё место она освобождает
        // только тогда.
        Assert.DoesNotContain("left", Shown(view));
    }

    /// <summary>
    /// Пока вкладку несут в середину, ей обещают своё окно.
    /// </summary>
    /// <remarks>
    /// Середина области значит «оторви», и человеку надо это увидеть: пустая
    /// рамка с подписью под курсором объясняет жест лучше любого слова.
    /// </remarks>
    [AvaloniaFact]
    public void While_a_tab_is_carried_to_the_middle_a_window_is_promised()
    {
        var (dock, view, window) = Two();

        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(view.View("right")!, 0.5, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Settle();

        var promised = dock.Promised;

        Assert.NotNull(promised);

        // Место обещано точно: угол призрака — тот самый угол будущего окна.
        var where = promised.Position;

        // А размер — нарочно меньше: рамка в полный рост окна закрыла бы собой
        // ту самую раскладку, по которой человек и выбирает место.
        Assert.True(
            promised.Width < DockFloat.DefaultWidth && promised.Height < DockFloat.DefaultHeight,
            "призрак ростом с окно");

        window.MouseUp(to, MouseButton.Left);
        Settle();

        Assert.Null(dock.Promised);

        var torn = Assert.Single(dock.Floating);

        Assert.Equal(where, torn.Position);
        Assert.Equal(DockFloat.DefaultWidth, torn.Width);
        Assert.Equal(DockFloat.DefaultHeight, torn.Height);
        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));
    }

    /// <summary>
    /// Вкладка, унесённая мимо всех окон, тоже обещает окно.
    /// </summary>
    /// <remarks>
    /// Раньше там не показывалось ничего: под курсором чужое приложение или
    /// рабочий стол, и рисовать внутри себя студии негде. Человек вёл вкладку в
    /// пустоту и до самого броска не знал, случится ли что-нибудь.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_carried_past_every_window_promises_one_too()
    {
        var (dock, view, window) = Two();

        var from = DockMouse.Tab(view.View("left")!, 0, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(-80, 60));
        Settle();

        Assert.NotNull(dock.Promised);

        window.MouseUp(new Point(-80, 60), MouseButton.Left);
        Settle();

        Assert.Null(dock.Promised);
        Assert.Single(dock.Floating);
    }

    /// <summary>
    /// Оборванная тяга уносит обещание с собой.
    /// </summary>
    /// <remarks>
    /// Призрак — отдельное окно, и само оно не исчезнет: вид, потерявший захват,
    /// убирает своё, а про чужое сказать может только событием. Не скажи он —
    /// пустая рамка осталась бы висеть поверх всего до конца дня.
    /// </remarks>
    [AvaloniaFact]
    public void A_broken_drag_takes_the_promise_with_it()
    {
        var (dock, view, window) = Two();

        IPointer? pointer = null;

        view.PointerMoved += (_, moved) => pointer = moved.Pointer;

        var from = DockMouse.Tab(view.View("left")!, 0, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(-80, 60));
        Settle();

        Assert.NotNull(dock.Promised);
        Assert.NotNull(pointer);

        pointer.Capture(window);
        Settle();

        Assert.Null(dock.Promised);
        Assert.Empty(dock.Floating);
    }

    /// <summary>
    /// Кончившаяся тяга не оставляет призрака открытым.
    /// </summary>
    /// <remarks>
    /// Призрака прятали, а не закрывали, и для Avalonia он оставался открытым
    /// окном. Студия закрывается по последнему окну — и после одной тяги
    /// переставала закрываться вовсе: главное окно пропадало с экрана, а
    /// процесс жил дальше, держа свои файлы занятыми. Снаружи это выглядело
    /// как «студия не пересобирается».
    /// <para>
    /// Спрашивается закрытие, а не <see cref="StudioDock.Promised"/>: тот
    /// отвечает, виден ли призрак, — и спрятанный отвечал «нет» одинаково, и
    /// когда его не стало, и когда он остался жить.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_finished_drag_leaves_no_window_behind()
    {
        var (dock, view, window) = Two();

        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(view.View("right")!, 0.5, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Settle();

        var promised = dock.Promised;

        Assert.NotNull(promised);

        var closed = false;

        promised.Closed += (_, _) => closed = true;

        window.MouseUp(to, MouseButton.Left);
        Settle();

        Assert.True(closed, "призрак пережил тягу и держит студию открытой");
        Assert.Null(dock.Promised);
    }

    /// <summary>
    /// Оторванные окна ждут окна студии и появляются вместе с ним.
    /// </summary>
    /// <remarks>
    /// Оторванная панель — окно при студии: всегда над ней, сворачивается и
    /// закрывается вместе с ней, своей кнопки в панели задач не заводит. Всё
    /// это держится на владельце, а владельцем может быть только показанное
    /// окно.
    /// <para>
    /// Прежде окна восстановленной раскладки показывались сразу — под
    /// заставкой, когда окна студии ещё нет, — и человек видел панель над
    /// экраном Welcome, где рабочего места не бывает. Хозяином им доставалось
    /// окно, которого никто не видел, и за студию они потом уходили вместо
    /// того, чтобы держаться над ней.
    /// </para>
    /// <para>
    /// Спрашивать окно бесполезно: у <c>Window</c>, который ещё ни разу не
    /// показывали, <c>IsVisible</c> уже <c>true</c> — умолчание <c>Visual</c>.
    /// Ровно на этот вопрос раскладка и получала «да» задолго до появления
    /// окна.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Torn_off_windows_wait_for_the_studio_window()
    {
        var view = new DockView();
        var dock = new StudioDock(view);
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Окно студии показано, но раскладке об этом ещё не сказали: так
        // выглядит подъём под заставкой.
        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(view.View("right")!, 0.5, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.False(torn.IsVisible, "оторванное окно вышло на экран раньше студии");

        dock.Shown();
        Dispatcher.UIThread.RunJobs();

        Assert.True(torn.IsVisible, "оторванное окно не появилось вместе со студией");
        Assert.False(torn.ShowInTaskbar, "окно при студии завело себе кнопку в панели задач");
    }

    /// <summary>
    /// Окно, записанное на исчезнувшем мониторе, возвращается на видное место.
    /// </summary>
    /// <remarks>
    /// Место записано при той раскладке экранов, какая была тогда: отключили
    /// второй монитор, сменили разрешение, принесли ноутбук домой — и окно
    /// приходит туда, где смотреть его некому. Найти его нечем: кнопки в панели
    /// задач у окна при студии нет.
    /// <para>
    /// Достаточно пересечения, а не полного вхождения: окно, наполовину
    /// свешенное за край, человек так и оставил.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_window_saved_on_a_monitor_that_is_gone_comes_back_into_view()
    {
        var primary = new PixelRect(0, 0, 1920, 1040);
        var screens = new[] { new PixelRect(0, 0, 1920, 1080) };

        // Записано на втором мониторе справа, которого больше нет.
        Assert.Equal(
            new PixelPoint(750, 360),
            DockFloat.Landed(new PixelRect(2200, 300, 420, 320), screens, primary));

        // На месте — не трогаем.
        Assert.Equal(
            new PixelPoint(100, 200),
            DockFloat.Landed(new PixelRect(100, 200, 420, 320), screens, primary));

        // Свешено за край, но задевает монитор — человек так и оставил.
        Assert.Equal(
            new PixelPoint(1800, 900),
            DockFloat.Landed(new PixelRect(1800, 900, 420, 320), screens, primary));

        // Мониторов не знаем — возвращать некуда, оставляем как записано.
        Assert.Equal(
            new PixelPoint(2200, 300),
            DockFloat.Landed(new PixelRect(2200, 300, 420, 320), screens, fallback: null));
    }

    /// <summary>
    /// Прощаясь, студия закрывает и оторванные окна.
    /// </summary>
    /// <remarks>
    /// Хозяином им главное окно достаётся не всегда: раскладка поднимается под
    /// заставкой, когда окно студии построено, но ещё не показано, — и окно,
    /// восстановленное из файла, встаёт без хозяина. Пережив главное, оно
    /// держало студию открытой: та закрывается по последнему окну, и человек
    /// получал процесс без единого видимого окна.
    /// </remarks>
    [AvaloniaFact]
    public void Saying_goodbye_closes_the_torn_off_windows()
    {
        var (dock, view, window) = Two();

        var from = DockMouse.Tab(view.View("left")!, 0, window);
        var to = DockMouse.Inside(view.View("right")!, 0.5, 0.5, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle();

        var torn = Assert.Single(dock.Floating);
        var closed = false;

        torn.Closed += (_, _) => closed = true;

        dock.Farewell();
        Settle();

        Assert.True(closed, "оторванное окно пережило студию и держит её открытой");
        Assert.Empty(dock.Floating);
    }

    /// <summary>
    /// Тяга через ничью землю не теряет прицел.
    /// </summary>
    /// <remarks>
    /// Путь курсора идёт и через границы между областями, где не отвечает никто:
    /// показанное там снимают. Забыть заодно разметку прицела нельзя — снятие
    /// перекладывает области заново, и следующее движение мерило бы уже по ним,
    /// причём до того, как их успели разместить. Вкладка улетала бы в своё окно
    /// вместо того места, куда её вели.
    /// </remarks>
    [AvaloniaFact]
    public void A_drag_across_a_border_keeps_its_aim()
    {
        var (dock, view, window) = Two();

        var splitter = view.GetVisualDescendants().OfType<GridSplitter>().First();
        var border = splitter.TranslatePoint(
            new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window);

        Assert.NotNull(border);

        var from = DockMouse.Tab(view.View("left")!, 0, window);

        // По дороге вкладку заносит к левому краю правой области: предпросмотр
        // делит её пополам, и области разъезжаются по-настоящему.
        var aside = DockMouse.Inside(view.View("right")!, 0.1, 0.5, window);
        var to = DockMouse.Inside(view.View("right")!, 0.5, 0.9, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(aside);
        window.MouseMove(border.Value);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var holder = DockTree.Holder(view.Root!, "hello:tree");

        Assert.NotNull(holder);
        Assert.NotEqual("left", holder.Id);
        Assert.Empty(dock.Floating);
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
            DockMouse.Tab(view.View("right")!, 0, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "friend:tips")?.Id);
        Assert.NotNull(view.View("right"));
    }

    /// <summary>
    /// Крестик у документа спрашивает хозяина, у панели закрывает сам.
    /// </summary>
    /// <remarks>
    /// Крестик теперь есть у обоих: закрытую панель возвращает меню «Панели»,
    /// и дорога назад у неё появилась. Дальше пути расходятся. За документом
    /// стоит файл и, возможно, несохранённое — закрывает его тот, кто открыл, а
    /// раскладка только спрашивает. За панелью не стоит ничего: спроси о ней
    /// раскладка, отвечать было бы некому, и крестик у панели не делал бы
    /// ничего.
    /// </remarks>
    [AvaloniaFact]
    public void A_cross_asks_about_a_document_and_closes_a_panel_itself()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<AxTabItem>(DockMouse.Tabs(view.View("left")!).Items[0]);

        Assert.True(panel.IsClosable, "У панели нет крестика — закрыть её человеку нечем.");

        var asked = new List<string>();
        dock.Closing += (_, id) => asked.Add(id);

        DockMouse.Click(window, DockMouse.Cross(view.View("left")!, 0, window));
        Settle();

        Assert.Null(view.View("left"));
        Assert.DoesNotContain("hello:tree", asked);

        DockMouse.Click(window, DockMouse.Cross(view.View(StudioDock.Documents)!, 0, window));

        Assert.Equal(["doc:a.axaml"], asked);
    }

    /// <summary>
    /// Набор помнит и свои оторванные окна.
    /// </summary>
    /// <remarks>
    /// Уходя из набора, студия его запоминает — но запоминала только главное
    /// дерево. Оторванные окна при этом разбирались и в набор не возвращались:
    /// человек уходил посмотреть соседний набор и терял расставленные окна.
    /// </remarks>
    [AvaloniaFact]
    public void A_set_remembers_its_torn_windows()
    {
        var (dock, view, window) = Two();

        dock.SaveAs("отладка");
        Tear(view, window, "left");
        Settle();

        Assert.Single(dock.Floating);

        dock.Switch("default");
        Settle();

        Assert.Empty(dock.Floating);

        dock.Switch("отладка");
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
    }

    /// <summary>
    /// Дважды открытый документ не удваивается.
    /// </summary>
    /// <remarks>
    /// Оболочка сегодня и не зовёт открытие дважды — но это её вежливость, а не
    /// свойство раскладки. Панель, оказавшаяся разом в двух деревьях, кончается
    /// исключением, и полагаться тут на вежливость зовущего нельзя.
    /// </remarks>
    [AvaloniaFact]
    public void Opening_the_same_document_twice_does_not_double_it()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Tear(view, window, StudioDock.Documents);
        Settle();

        var torn = Assert.Single(dock.Floating);

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Assert.NotNull(DockTree.Holder(torn.View.Root!, "doc:a.axaml"));
        Assert.Null(DockTree.Holder(view.Root!, "doc:a.axaml"));
    }

    /// <summary>
    /// Показать оторванную панель — значит достать её в её же окне.
    /// </summary>
    /// <remarks>
    /// Выбор искали только в главном дереве, и просьба показать документ,
    /// уехавший в своё окно, не делала ничего: человек открывал тот же файл
    /// снова и не понимал, куда он делся.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_a_torn_panel_reaches_it_in_its_own_window()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Tear(view, window, StudioDock.Documents);
        Settle();

        var torn = Assert.Single(dock.Floating);
        var group = torn.View.Root!.Groups().Single().Id;

        // Пусть в окне будет две вкладки, и выбрана не наша: соседку приносим
        // туда же из главного окна. Берём её справа, а не слева: у окон в
        // безголовом Avalonia общее начало координат, и левая вкладка стоит
        // ровно там же, где вкладка оторванного окна, — тяги на нулевое
        // расстояние не бывает.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Across(torn, DockMouse.Tab(torn.View.View(group)!, 0, torn), window));

        Settle();

        Assert.Equal("friend:tips", DockTree.Group(torn.View.Root!, group)?.Selected);

        dock.Show("doc:a.axaml");
        Settle();

        Assert.Equal("doc:a.axaml", DockTree.Group(torn.View.Root!, group)?.Selected);
    }

    /// <summary>
    /// Свёрнутое окно разворачивается, когда просят его панель.
    /// </summary>
    /// <remarks>
    /// Свёрнутое окно Avalonia считает видимым, и один подъём оставляет его
    /// свёрнутым. Кнопки на панели задач у оторванного окна нет — не развернув
    /// его, студия отвечает на просьбу ничем, и панель становится недостижимой.
    /// </remarks>
    [AvaloniaFact]
    public void A_minimized_window_comes_back_when_its_panel_is_asked_for()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);

        torn.WindowState = WindowState.Minimized;
        Settle();

        dock.Show("hello:tree");
        Settle();

        Assert.Equal(WindowState.Normal, torn.WindowState);
    }

    /// <summary>
    /// Показать уже показанное — не перекладка.
    /// </summary>
    /// <remarks>
    /// Перекладка сносит и ставит заново всё дерево окна, а вместе с ним
    /// пропадает место, где человек печатал. Просьба показать вкладку, которая и
    /// так на виду, обязана не стоить ничего.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_what_is_already_shown_leaves_the_window_alone()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);
        var before = torn.View.Root;

        dock.Show("hello:tree");
        Settle();

        Assert.Same(before, torn.View.Root);
    }

    /// <summary>
    /// Уборка одного имени не трогает деревья, в которых его нет.
    /// </summary>
    /// <remarks>
    /// Закрытая вкладка главного окна — не повод перекладывать чужое: там человек
    /// мог печатать, и перекладка унесла бы его место в тексте.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_a_tab_does_not_stir_the_other_windows()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);
        var before = torn.View.Root;

        dock.Remove("doc:a.axaml");
        Settle();

        Assert.Same(before, torn.View.Root);
    }

    /// <summary>
    /// Окно, оставшееся без живых вкладок, уходит с экрана.
    /// </summary>
    /// <remarks>
    /// Само оно не закроется: имя выключенного плагина в группе осталось, и для
    /// дерева она не пуста. Человеку же остаётся пустая рамка с именем
    /// документа, который он только что закрыл.
    /// </remarks>
    [AvaloniaFact]
    public void A_window_without_a_live_tab_leaves_the_screen()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Tear(view, window, StudioDock.Documents);
        Settle();

        var torn = Assert.Single(dock.Floating);
        var group = torn.View.Root!.Groups().Single().Id;

        // В окно приносим панель другого плагина: выключенный, он оставит в
        // группе своё имя, и для дерева она перестанет быть пустой.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Across(torn, DockMouse.Tab(torn.View.View(group)!, 0, torn), window));

        Settle();

        dock.RemoveOwnedBy("friend");
        Settle();

        Assert.True(torn.IsVisible);

        dock.Remove("doc:a.axaml");
        Settle();

        Assert.False(torn.IsVisible);
    }

    /// <summary>
    /// Показанный документ ищется и в оторванных окнах.
    /// </summary>
    /// <remarks>
    /// Оболочка спрашивает об этом, закрыв соседа: кого будить взамен. Ответь
    /// раскладка одним главным деревом — редактор, единственный оставшийся на
    /// экране, так и не проснулся бы.
    /// </remarks>
    [AvaloniaFact]
    public void The_shown_document_is_found_in_its_own_window_too()
    {
        var (dock, view, window) = Two();

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Assert.Equal("doc:a.axaml", dock.Showing);

        Tear(view, window, StudioDock.Documents);
        Settle();

        Assert.Single(dock.Floating);
        Assert.Equal("doc:a.axaml", dock.Showing);
    }

    /// <summary>
    /// За мёртвым именем показывать нечего, и студия об этом молчит.
    /// </summary>
    /// <remarks>
    /// Плагин выключили, а место за ним в дереве осталось. Скажи раскладка
    /// «показал» — оболочка пометила бы документ показанным и написала его путь в
    /// строке состояния, хотя на экране не появилось ничего.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_a_name_with_nothing_behind_it_says_nothing()
    {
        var (dock, _, _) = Two();
        var heard = new List<string>();

        dock.Chosen += (_, id) => heard.Add(id);
        dock.RemoveOwnedBy("hello");
        Settle();

        dock.Show("hello:tree");
        Settle();

        Assert.Empty(heard);
    }

    /// <summary>
    /// Открытый документ объявляется показанным — оба раза.
    /// </summary>
    /// <remarks>
    /// Разница в том, помнила ли раскладка это имя, зовущего не касается:
    /// открыть документ и не показать его нельзя. Молчи один из путей — тот, кто
    /// слушает выбор вкладки, считал бы одни открытия и пропускал другие.
    /// </remarks>
    [AvaloniaFact]
    public void Opening_a_document_says_which_one_is_shown()
    {
        var (dock, _, _) = Two();
        var heard = new List<string>();

        dock.Chosen += (_, id) => heard.Add(id);

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Assert.Equal(["doc:a.axaml"], heard);

        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        Assert.Equal(["doc:a.axaml", "doc:a.axaml"], heard);
    }

    /// <summary>
    /// Сброс разбирает и оторванные окна.
    /// </summary>
    /// <remarks>
    /// При первом запуске оторванных окон нет, а сброс возвращает раскладку
    /// именно к нему. Оставь мы окно — панель оказалась бы разом и в нём, и в
    /// главном дереве, а родитель у контрола Avalonia ровно один: студия падала
    /// бы, и падала не сразу, а на следующей перекладке — там, где причину уже
    /// не найти.
    /// </remarks>
    [AvaloniaFact]
    public void Resetting_takes_the_torn_windows_apart_too()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");

        Assert.Single(dock.Floating);

        // Окно должно успеть разложиться по-настоящему: иначе панель в нём так
        // и не обзаведётся родителем, и проверка пройдёт мимо сути.
        Settle();

        dock.Reset();
        Settle();

        Assert.Empty(dock.Floating);
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);
        Assert.Equal(["left", StudioDock.Documents, "right"], Shown(view));
    }

    /// <summary>
    /// Имя, оказавшееся в двух деревьях, при чтении достаётся одному.
    /// </summary>
    /// <remarks>
    /// Испорченный файл — не выдумка: именно так его и записала студия, пока
    /// сброс не разбирал окон. Читатель обязан такой файл починить, а не
    /// повторить: панель, попавшая разом в окно и в главное дерево, кончается
    /// исключением.
    /// </remarks>
    [AvaloniaFact]
    public void A_name_found_in_two_trees_goes_to_one_of_them()
    {
        var broken = new DockLayout
        {
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new()
                {
                    Root = new DockGroup { Id = "left", Items = ["hello:tree"], Selected = "hello:tree" },
                    DocumentHome = StudioDock.Documents,
                    Floating =
                    [
                        new DockWindow
                        {
                            Root = new DockGroup
                            {
                                Id = "float",
                                Items = ["hello:tree"],
                                Selected = "hello:tree",
                            },
                        },
                    ],
                },
            },
        };

        System.IO.File.WriteAllText(File, DockLayoutSerializer.Write(broken));

        var (dock, view) = Dock(new DockLayoutStore(File));

        dock.Restore();
        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        foreach (var torn in dock.Floating)
            Assert.Null(DockTree.Holder(torn.View.Root!, "hello:tree"));
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
            DockMouse.Tab(view.View("right")!, 0, window));

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
    /// Окно, у которого не осталось имён, на экран не выходит и в наборы не идёт.
    /// </summary>
    /// <remarks>
    /// Испорченный файл мог записать имя разом в главное дерево и в окно; одно
    /// из них вычёркивается, и окно остаётся ни с чем. Спрячь его студия —
    /// спрятанное, оно так и лежало бы в списке окон, писалось в файл и
    /// копировалось в каждый новый набор: пустая рамка, которую человеку нечем
    /// ни открыть, ни закрыть.
    /// </remarks>
    [AvaloniaFact]
    public void A_window_left_without_names_does_not_reach_the_screen()
    {
        var broken = new DockLayout
        {
            Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
            {
                [DockLayout.DefaultName] = new()
                {
                    Root = new DockGroup { Id = "left", Items = ["hello:tree"], Selected = "hello:tree" },
                    DocumentHome = StudioDock.Documents,
                    Floating =
                    [
                        new DockWindow
                        {
                            Root = new DockGroup
                            {
                                Id = "float",
                                Items = ["hello:tree"],
                                Selected = "hello:tree",
                            },
                        },
                    ],
                },
            },
        };

        System.IO.File.WriteAllText(File, DockLayoutSerializer.Write(broken));

        var (dock, view) = Dock(new DockLayoutStore(File));

        dock.Restore();
        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        Settle();

        Assert.Empty(dock.Floating);
        Assert.Equal("left", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        // И в новый набор пустой рамке тоже не за чем идти.
        dock.SaveAs("отладка");
        Settle();

        Assert.Empty(dock.Floating);
    }

    /// <summary>
    /// Размер оторванного окна доходит до файла.
    /// </summary>
    /// <remarks>
    /// Место и размер — часть раскладки, и спрашивать о них приходится порознь:
    /// потянутый нижний угол окна не двигает, и одним переездом новая высота до
    /// файла не доходит. Человек подгоняет окно под свою работу, а наутро
    /// получает обратно заводские четыреста на триста.
    /// </remarks>
    [AvaloniaFact]
    public void The_size_of_a_torn_window_reaches_the_file()
    {
        var store = new DockLayoutStore(File);
        var (dock, view, window) = Two(store);

        Tear(view, window, "left");
        Settle();

        // Записываем до правки: отрыв уже пометил раскладку изменившейся, и без
        // этого запись дошла бы до файла сама, о размере не спросив.
        dock.Flush();

        dock.Floating[0].Height = 555;
        Settle();

        dock.Flush();

        var saved = store.Load(out _);

        Assert.Equal(555, Assert.Single(saved!.Current!.Floating).Height);
    }

    /// <summary>
    /// Документ из закрытого окна возвращается в область документов.
    /// </summary>
    /// <remarks>
    /// Имя этой области задаёт файл раскладки, и слово «documents» может не
    /// значить в ней ничего. Пойми студия просьбу буквально — документ завёл бы
    /// себе одноимённую группу у правого края и остался бы в ней навсегда, а
    /// следующий открытый файл ушёл бы в настоящую область: документы разъехались
    /// бы по двум местам.
    /// </remarks>
    [AvaloniaFact]
    public void A_document_coming_home_finds_the_place_for_documents()
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
        dock.Open("hello", "doc:a.axaml", "a.axaml", new Border());
        Settle();

        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view));

        Tear(view, window, "centre");
        Settle();

        Assert.Single(dock.Floating).Close();
        Settle();

        Assert.Equal(["centre"], Shown(view));
        Assert.Equal("centre", DockTree.Holder(view.Root!, "doc:a.axaml")?.Id);
        Assert.Equal("doc:a.axaml", dock.Showing);
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
            DockMouse.Tab(view.View("right")!, 0, window));

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
            DockMouse.Tab(view.View("right")!, 0, window));

        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);

        dock.Forget();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["default"], dock.Layouts);
        Assert.Equal("right", DockTree.Holder(view.Root!, "hello:tree")?.Id);
    }

    /// <summary>
    /// Уходя из набора, окна остаются ему, а возвращаясь — находятся на месте.
    /// </summary>
    /// <remarks>
    /// Окно отрывается <b>до</b> сохранения: иначе стандартному набору достаётся
    /// пустой список окон, и проверка не отличит запоминание от прежнего кода,
    /// который окон не помнил вовсе.
    /// </remarks>
    [AvaloniaFact]
    public void The_set_left_behind_keeps_the_windows_it_had()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        dock.SaveAs("отладка");
        Settle();

        // В «отладке» панель возвращаем домой — наборы расходятся, и каждому
        // теперь есть что о себе помнить.
        dock.Floating[0].Close();
        Settle();

        Assert.Empty(dock.Floating);

        dock.Switch("default");
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));
        Assert.Null(DockTree.Holder(view.Root!, "hello:tree"));
    }

    /// <summary>
    /// Забытый набор уступает место стандартному вместе с его окнами.
    /// </summary>
    /// <remarks>
    /// Стандартный набор помнит свои окна не хуже прочих: забыть показанный —
    /// значит вернуться к тому, что было у стандартного, а не к голому дереву.
    /// </remarks>
    [AvaloniaFact]
    public void A_forgotten_set_returns_to_the_windows_of_the_standard_one()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        dock.SaveAs("отладка");
        Settle();

        dock.Floating[0].Close();
        Settle();

        Assert.Empty(dock.Floating);

        dock.Forget();
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.Equal(["default"], dock.Layouts);
        Assert.NotNull(DockTree.Holder(torn.View.Root!, "hello:tree"));
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

    /// <summary>Даёт раскладке и отрисовке действительно случиться.</summary>
    private static void Settle()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Пожелание «встань с этой стороны» — как его пишет манифест.</summary>
    private static PluginPlacement At(string side) => new() { Side = side };

    private static PluginStrings Strings => PluginStrings.Studio;

    /// <summary>Имена групп, которые сейчас на экране, слева направо.</summary>
    private static IReadOnlyList<string> Shown(DockView view) =>
        [.. view.GetVisualDescendants().OfType<DockGroupView>().Select(group => group.Id)];

    /// <summary>
    /// Оторванное окно берёт подпись кнопки уборки у главного дерева.
    /// </summary>
    /// <remarks>
    /// Без них кнопка остаётся безымянной: программа чтения с экрана скажет о
    /// ней «кнопка» и ничего больше. В главном окне подпись задаёт разметка, а
    /// дерево оторванного окна студия заводит кодом — и подписи ему прежде не
    /// доставалось ни одной. Нашлось это не глазами: смотреть там не на что,
    /// имя для средств доступности не рисуется.
    /// <para>
    /// Привязкой, а не копией: подпись меняется вместе с языком студии, и
    /// снятая однажды осталась бы на языке той минуты, когда окно оторвали.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_torn_off_window_takes_its_button_titles_from_the_studio()
    {
        var (dock, view, window) = Two();

        view.StowTitle = "Убрать на рейку";

        var from = DockMouse.Tab(view.View("left")!, 0, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(-80, 60));
        Settle();
        window.MouseUp(new Point(-80, 60), MouseButton.Left);
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.Equal("Убрать на рейку", torn.View.StowTitle);

        // Язык сменился — сменилась и подпись в уже оторванном окне.
        view.StowTitle = "Hide to the rail";
        Settle();

        Assert.Equal("Hide to the rail", torn.View.StowTitle);
    }

    /// <summary>Две группы рядом: слева панель одного плагина, справа другого.</summary>
    private static (StudioDock Dock, DockView View, Window Window) Two(DockLayoutStore? store = null)
    {
        var (dock, view) = Dock(store);

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        return (dock, view, Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view)));
    }

    /// <summary>
    /// Раскладка в окне с полосами сверху и снизу — как тулбар и строка состояния.
    /// </summary>
    /// <remarks>
    /// Обычное тестовое окно отдаёт дереву всё место, и целиться «внутрь окна,
    /// но вне дерева» там некуда — а это и есть корневая стыковка.
    /// </remarks>
    private static (StudioDock Dock, DockView View, Window Window) Strips()
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = new DockPanel
            {
                Children =
                {
                    new Border { Height = 40, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Top },
                    new Border { Height = 24, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom },
                    view,
                },
            },
        };

        window.Show();
        dock.Shown();

        dock.Add("hello", "hello:tree", At("left"), "Проект", Strings, new Border());
        dock.Add("friend", "friend:tips", At("right"), "Советы", Strings, new Border());
        Dispatcher.UIThread.RunJobs();

        return (dock, view, window);
    }

    private static (StudioDock Dock, DockView View) Dock(DockLayoutStore? store = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view, store);

        new Window { Content = view, Width = 1200, Height = 800 }.Show();

        // То же, что делает окно студии, открывшись: без этого оторванные окна
        // остаются скрытыми — им нужен показанный хозяин.
        dock.Shown();
        Dispatcher.UIThread.RunJobs();

        return (dock, view);
    }

    /// <summary>
    /// У оторванного окна нет своей полосы заголовка — её работу делает полоса
    /// вкладок.
    /// </summary>
    /// <remarks>
    /// Пустая полоса поверх полосы вкладок съедала четверть невысокого окна ради
    /// трёх кнопок: 76 пикселей хрома на окне высотой 320.
    /// </remarks>
    [AvaloniaFact]
    public void A_torn_window_wears_its_tab_strip_as_a_title_bar()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);

        Assert.Empty(torn.GetVisualDescendants().OfType<AxTitleBar>());

        // Кнопки окна стоят в шапке группы, а не в отдельной полосе над ней.
        var buttons = Assert.Single(torn.GetVisualDescendants().OfType<AxWindowControls>());
        var group = Assert.Single(torn.View.GetVisualDescendants().OfType<DockGroupView>());

        Assert.Contains(group, buttons.GetVisualAncestors());
        Assert.True(buttons.TranslatePoint(default, group)?.Y < group.HeaderHeight);
    }

    /// <summary>
    /// В шапке оторванного окна нет ни уборки на рейку, ни сворачивания.
    /// </summary>
    /// <remarks>
    /// Обе кнопки — дороги в один конец. Рейки у оторванного окна нет, и
    /// убранной панели неоткуда было бы вернуться; кнопки в панели задач у него
    /// нет тоже, и свёрнутое окно исчезает, не оставив человеку способа его
    /// найти. Так же решает Visual Studio: плавающую панель там сперва
    /// пристыковывают.
    /// <para>
    /// Развернуть окно можно: это обратимо и повторяет двойной щелчок по
    /// шапке. Закрыть — тем более.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_torn_window_offers_neither_stowing_nor_minimizing()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);
        var group = Assert.Single(torn.View.GetVisualDescendants().OfType<DockGroupView>());

        Assert.False(group.CanStow, "оторванное окно предлагает убрать панель на рейку, которой у него нет");
        Assert.DoesNotContain(
            torn.GetVisualDescendants().OfType<AxButton>(),
            button => button.Name == "PART_Stow" && button.IsVisible);

        var buttons = Assert.Single(torn.GetVisualDescendants().OfType<AxWindowControls>());

        Assert.False(buttons.ShowMinimize, "оторванное окно предлагает свернуть себя в никуда");
        Assert.DoesNotContain(
            buttons.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "PART_Minimize" && button.IsVisible);

        // Развернуть и закрыть остаются: оба обратимы, и оба человек ищет там же.
        Assert.Contains(buttons.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_Maximize");
        Assert.Contains(buttons.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_Close");
    }

    /// <summary>
    /// Кнопки окна стоят в шапке угловой группы, а не в каждой.
    /// </summary>
    /// <remarks>
    /// Разделив окно надвое, человек ищет их там же, где и до этого: в правом
    /// верхнем углу. Вторая пара в соседней шапке была бы и лишней, и опасной —
    /// родитель у контрола Avalonia ровно один.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_buttons_stand_in_the_corner_group_alone()
    {
        var (dock, view, window) = Two();

        Tear(view, window, "left");
        Settle();

        var torn = Assert.Single(dock.Floating);
        var group = torn.View.Root!.Groups().Single().Id;

        // Приносим вторую панель к левому краю окна: угол остаётся за первой.
        DockMouse.Drag(
            window,
            DockMouse.Tab(view.View("right")!, 0, window),
            DockMouse.Across(torn, DockMouse.Inside(torn.View.View(group)!, 0.1, 0.5, torn), window));

        Settle();

        Assert.Equal(2, torn.View.GetVisualDescendants().OfType<DockGroupView>().Count());

        var buttons = Assert.Single(torn.GetVisualDescendants().OfType<AxWindowControls>());

        Assert.Equal(group, Assert.Single(buttons.GetVisualAncestors().OfType<DockGroupView>()).Id);
    }

    /// <summary>
    /// За пустое место шапки берутся, чтобы двигать окно; за вкладку — чтобы
    /// нести панель.
    /// </summary>
    /// <remarks>
    /// Другой ручки у оторванного окна нет: своей полосы заголовка не осталось,
    /// и не различай вид эти два нажатия — окно нельзя было бы ни подвинуть, ни
    /// оторвать от него вкладку.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_header_is_the_handle_of_the_window()
    {
        var (_, view, window) = Two();
        var grabbed = 0;

        view.Grabbed += (_, _) => grabbed++;

        var group = view.View("left")!;

        DockMouse.Click(window, DockMouse.Tab(group, 0, window));

        Assert.Equal(0, grabbed);

        // Пустое место той же шапки — правее последней вкладки, но левее
        // кнопки уборки: правый край шапки принадлежит ей, и нажатие на
        // кнопку окна не двигает — это нажатие на кнопку.
        var empty = DockMouse.Inside(group, 0.6, 0, window)
            + new Vector(0, group.HeaderHeight / 2);

        DockMouse.Click(window, empty);

        Assert.Equal(1, grabbed);
    }

    /// <summary>
    /// Пол рабочей области темнее панелей, которые на нём лежат.
    /// </summary>
    /// <remarks>
    /// Панели видно только потому, что они лежат на чём-то темнее себя. Покрась
    /// пол в их цвет — и окно станет одним ровным пятном от заголовка до строки
    /// состояния, где границы держит одна пиксельная линия; именно так и было,
    /// пока область документов рисовалась такой же панелью, как боковые.
    /// </remarks>
    [AvaloniaFact]
    public void The_floor_of_the_workspace_is_darker_than_the_panels_on_it()
    {
        var (_, view, _) = Two();

        var floor = view.View(StudioDock.Documents)!;
        var panel = view.View("left")!;

        Assert.True(floor.Standing, "пол не назвался полом");
        Assert.False(panel.Standing, "панель назвалась полом");

        // Цвета берём из палитры, а не цифрами: тема их и задаёт.
        Assert.Equal(Tone("AxBg1Brush"), Paint(floor));
        Assert.Equal(Tone("AxBg2Brush"), Paint(panel));
        Assert.NotEqual(Paint(floor), Paint(panel));
    }

    /// <summary>Чем закрашена группа на экране.</summary>
    private static Color Paint(DockGroupView group) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(
            group.GetVisualDescendants().OfType<AxToolWindow>().First().Background).Color;

    /// <summary>Цвет из палитры темы по имени кисти.</summary>
    private static Color Tone(string key)
    {
        // Спрашиваем с вариантом темы: палитра объявлена внутри тёмного и
        // светлого словарей, и без варианта ключ не находится.
        var app = Application.Current!;

        Assert.True(app.TryFindResource(key, app.ActualThemeVariant, out var found));

        return Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
    }
}
