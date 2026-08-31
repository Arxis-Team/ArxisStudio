using ArxisStudio.Controls;
using ArxisStudio.Docking;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Мышь над раскладкой: взять вкладку и бросить её в другое место.
/// </summary>
/// <remarks>
/// Перетаскивание проверяется настоящей мышью, а не вызовом события: между
/// нажатием на вкладку и правкой дерева лежит вся дорога — порог, захват
/// указателя, поиск цели, — и обрыв на любом её шаге выглядит одинаково.
/// </remarks>
public static class DockMouse
{
    /// <summary>Ведёт мышь от точки к точке с нажатой кнопкой.</summary>
    /// <param name="window">Окно, которому шлём ввод.</param>
    /// <param name="from">Откуда.</param>
    /// <param name="to">Куда.</param>
    public static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);

        // Шагами, а не рывком: тяга — это цепочка, и порог берётся не с первого
        // движения.
        for (var step = 1; step <= 4; step++)
        {
            window.MouseMove(new Point(
                from.X + ((to.X - from.X) * step / 4),
                from.Y + ((to.Y - from.Y) * step / 4)));
        }

        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Полоса вкладок показанной группы.</summary>
    /// <param name="group">Вид группы.</param>
    public static AxTabStrip Tabs(DockGroupView group) =>
        group.GetVisualDescendants().OfType<AxTabStrip>().Single();

    /// <summary>Середина вкладки в координатах окна.</summary>
    /// <param name="group">Вид группы.</param>
    /// <param name="at">Номер вкладки.</param>
    /// <param name="window">Окно, в чьих координатах нужна точка.</param>
    public static Point Tab(DockGroupView group, int at, Window window)
    {
        var tab = Assert.IsAssignableFrom<Control>(Tabs(group).Items[at]);
        var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window);

        Assert.NotNull(point);

        return point.Value;
    }

    /// <summary>Середина крестика на вкладке, в координатах окна.</summary>
    /// <param name="group">Вид группы.</param>
    /// <param name="at">Номер вкладки.</param>
    /// <param name="window">Окно, в чьих координатах нужна точка.</param>
    public static Point Cross(DockGroupView group, int at, Window window)
    {
        var tab = Assert.IsAssignableFrom<Control>(Tabs(group).Items[at]);
        var close = tab.GetVisualDescendants().OfType<Control>().First(part => part.Name == "PART_Close");
        var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window);

        Assert.NotNull(point);

        return point.Value;
    }

    /// <summary>
    /// Точка одного окна в координатах другого — через экран.
    /// </summary>
    /// <param name="from">Окно, в чьих координатах точка задана.</param>
    /// <param name="at">Точка.</param>
    /// <param name="to">Окно, которому шлют ввод.</param>
    /// <remarks>
    /// Во время тяги ввод идёт окну, захватившему указатель, и точка обязана
    /// быть в его координатах — даже когда целятся в чужое окно. Экран здесь
    /// общий язык, на котором эти координаты и переводятся.
    /// </remarks>
    public static Point Across(Window from, Point at, Window to) =>
        to.PointToClient(from.PointToScreen(at));

    /// <summary>Щёлкает мышью в точке.</summary>
    /// <param name="window">Окно, которому шлём ввод.</param>
    /// <param name="at">Куда.</param>
    public static void Click(Window window, Point at)
    {
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Точка внутри группы по долям её ширины и высоты.</summary>
    /// <param name="group">Вид группы.</param>
    /// <param name="x">Доля ширины.</param>
    /// <param name="y">Доля высоты.</param>
    /// <param name="window">Окно, в чьих координатах нужна точка.</param>
    public static Point Inside(DockGroupView group, double x, double y, Window window)
    {
        var point = group.TranslatePoint(new Point(group.Bounds.Width * x, group.Bounds.Height * y), window);

        Assert.NotNull(point);

        return point.Value;
    }
}
