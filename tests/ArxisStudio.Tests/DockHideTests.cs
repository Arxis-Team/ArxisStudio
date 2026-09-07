using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Кнопка «скрыть» в шапке группы: у кого стоит, о чём просит, что говорит о
/// себе.
/// </summary>
/// <remarks>
/// Кнопка одна, и решений за ней два. Кому её показывать — всякой группе, кроме
/// пола рабочей области и той, в которой скрывать нечего: документы не
/// скрывают. О чём она просит — скрыть свою группу целиком, а не выбранную
/// вкладку: человек убирает то, на что смотрит.
/// <para>
/// Спрашивается не только свойство, но и сама кнопка. Прежде показ ей задавала
/// привязка в шаблоне — и не задавала ничего: содержимое шапки переезжает в
/// шаблон <c>AxToolWindow</c>, хозяин шаблона у переехавшего теряется, и
/// <c>TemplateBinding</c> перестаёт находить свойство молча.
/// </para>
/// </remarks>
public class DockHideTests
{
    /// <summary>Кнопка есть у всякой группы, кроме пола рабочей области.</summary>
    /// <remarks>
    /// Одинокая группа тоже прячется, и это не оплошность: имя её остаётся в
    /// дереве вместе с местом и долей, место на экране достаётся полу рабочей
    /// области, а вернуть панель есть откуда — из меню «Панели». Прежде кнопке
    /// требовался сосед, но требовалось это уборке на рейку: место убранной
    /// одинокой группы не досталось бы никому.
    /// </remarks>
    [AvaloniaFact]
    public void Every_group_but_the_documents_floor_offers_the_button()
    {
        var (pair, window) = Shown(Split());

        Assert.True(Group(pair, "top").CanHide);
        Assert.True(Group(pair, "bottom").CanHide);
        Assert.True(Button(Group(pair, "top")).IsVisible);

        window.Close();

        var (alone, lonely) = Shown(new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" });

        Assert.True(
            Group(alone, "top").CanHide,
            "одинокая группа не предлагает скрыться, хотя прятать её теперь есть куда");
        Assert.True(
            Button(Group(alone, "top")).IsVisible,
            "кнопки нет в шапке одинокой группы");

        lonely.Close();

        var (floor, room) = Shown(Split(), documents: "bottom");

        Assert.False(Group(floor, "bottom").CanHide, "пол рабочей области предлагает себя скрыть");
        Assert.False(Button(Group(floor, "bottom")).IsVisible, "кнопка стоит в шапке пола рабочей области");
        Assert.True(Group(floor, "top").CanHide);

        room.Close();
    }

    /// <summary>
    /// Группе, в которой скрывать нечего, кнопка не достаётся.
    /// </summary>
    /// <remarks>
    /// Так выходит с группой из одних документов: их не скрывают — за ними стоят
    /// файлы, — и кнопка стояла бы в шапке, не делая ничего. Кнопка, которая не
    /// работает, хуже отсутствующей: человек нажимает её и не понимает, сломана
    /// она или он.
    /// </remarks>
    [AvaloniaFact]
    public void A_group_with_nothing_to_hide_offers_no_button()
    {
        var (view, window) = Shown(Split());

        Assert.True(Group(view, "bottom").CanHide);

        // «console» и «problems» — всё, что есть в нижней группе; запрещаем оба.
        view.Fixed = new HashSet<string>(["console", "problems"], StringComparer.Ordinal);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Group(view, "bottom").CanHide, "группа из одних документов предлагает себя скрыть");
        Assert.False(Button(Group(view, "bottom")).IsVisible, "кнопка стоит там, где скрывать нечего");

        // У соседа скрывать есть что — у него кнопка на месте.
        Assert.True(Group(view, "top").CanHide);

        window.Close();
    }

    /// <summary>
    /// Дереву, которое прячет себя само, кнопка не достаётся вовсе.
    /// </summary>
    /// <remarks>
    /// Это оторванное окно: «скрыть» стоит у него в шапке самого окна и убирает
    /// всё, что в нём лежит. Две кнопки рядом, делающие одно, — не выбор для
    /// человека, а недосмотр.
    /// </remarks>
    [AvaloniaFact]
    public void A_tree_that_hides_by_itself_offers_no_button()
    {
        var (view, window) = Shown(Split());

        Assert.True(Group(view, "top").CanHide);

        view.Hideable = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Group(view, "top").CanHide, "окно со своей кнопкой скрытия предлагает вторую");
        Assert.False(Button(Group(view, "top")).IsVisible, "кнопка группы осталась в окне, прячущем себя само");
        Assert.False(Group(view, "bottom").CanHide);

        window.Close();
    }

    /// <summary>Кнопка просит скрыть свою группу и называет её по имени.</summary>
    /// <remarks>
    /// Имя берётся у самого вида, а не из замыкания: та же группа переживает
    /// перекладку, а её узел в дереве — нет.
    /// </remarks>
    [AvaloniaFact]
    public void The_button_asks_to_hide_its_own_group()
    {
        var (view, window) = Shown(Split());
        var asked = new List<string>();

        view.Hiding += (_, group) => asked.Add(group);

        Press(Button(Group(view, "bottom")));

        Assert.Equal("bottom", Assert.Single(asked));

        asked.Clear();
        Press(Button(Group(view, "top")));

        Assert.Equal("top", Assert.Single(asked));

        window.Close();
    }

    /// <summary>
    /// Кнопка говорит, что она сделает, и показывает один контур.
    /// </summary>
    /// <remarks>
    /// Подпись у кнопки одна, и смысл один — скрыть группу. Она же подсказка и
    /// она же имя для средств доступности: на кнопке значок 12×12, и узнать о
    /// ней больше неоткуда.
    /// <para>
    /// Контур — тот же, что у кнопки «скрыть» в шапке оторванного окна. Дело у
    /// них одно, и звать его двумя именами значило бы оставить следующему
    /// читателю совпадение на проверку.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_button_says_what_it_will_do()
    {
        var (view, window) = Shown(Split());

        view.HideTitle = "Скрыть панель";
        Dispatcher.UIThread.RunJobs();

        var button = Button(Group(view, "bottom"));

        Assert.Equal("Скрыть панель", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.Equal("Скрыть панель", ToolTip.GetTip(button));
        Assert.Same(AxIcons.Minus, Assert.Single(button.GetVisualDescendants().OfType<AxIcon>()).Data);

        window.Close();
    }

    /// <summary>Деление сверху вниз: две группы с двумя вкладками в нижней.</summary>
    private static DockSplit Split() => new()
    {
        Orientation = DockOrientation.Vertical,
        Children =
        [
            new DockGroup { Id = "top", Items = ["solution"], Selected = "solution" },
            new DockGroup { Id = "bottom", Items = ["console", "problems"], Selected = "console" },
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
        group.GetVisualDescendants().OfType<AxButton>().Single(button => button.Name == "PART_Hide");

    /// <summary>Нажимает кнопку так, как это делает человек.</summary>
    private static void Press(AxButton button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
}
