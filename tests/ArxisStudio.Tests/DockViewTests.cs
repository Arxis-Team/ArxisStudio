using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
        Assert.Equal(2, Tabs(group).Items.Count);
        Assert.Equal(1, Tabs(group).SelectedIndex);
        Assert.Same(items.Find("structure")?.Content, Content(group));
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

        Assert.Equal("Проект", ((AxTabItem)Tabs(view.View("left")!).Items[0]!).Content);
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
        Tabs(group).SelectedIndex = 0;

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

        Assert.Single(Tabs(view.View("left")!).Items);
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

        view.Root = DockTree.Insert(DockTree.Remove(view.Root!, "solution"), "right", DockSide.Tab, "solution", "unused");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(right, view.View("right"));
        Assert.Null(view.View("left"));
        Assert.Same(panel, items.Find("solution")?.Content);
        Assert.Equal(2, Tabs(right!).Items.Count);
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

    /// <summary>Полоса вкладок показанной группы.</summary>
    private static AxTabStrip Tabs(DockGroupView group) =>
        group.GetVisualDescendants().OfType<AxTabStrip>().Single();

    /// <summary>Что показано в группе.</summary>
    private static object? Content(DockGroupView group) =>
        group.GetVisualDescendants()
            .OfType<ContentControl>()
            .First(content => content.Name == "PART_Content")
            .Content;
}
