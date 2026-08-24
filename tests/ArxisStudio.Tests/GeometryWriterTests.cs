using ArxisStudio.Markup.Xaml;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перевод жеста на канве в свойства разметки.
/// </summary>
/// <remarks>
/// Канва отдаёт координаты в своей системе отсчёта — форма лежит на ней со
/// смещением, — поэтому новое положение считается от того, что написано в
/// документе, а не от того, что канва уже проставила контролу.
/// </remarks>
public class GeometryWriterTests
{
    [AvaloniaFact]
    public void Resizing_writes_the_size_the_canvas_applied()
    {
        var button = new Button { Width = 180 };
        _ = new DockPanel { Children = { button } };

        var node = Node("<Button/>", button);
        var values = GeometryWriter.Describe(node, new Rect(10, 10, 80, 30), new Rect(10, 10, 180, 30));

        Assert.Equal(("Width", "180"), Assert.Single(values));
    }

    /// <summary>
    /// Канва могла упереться в минимальный размер контрола, и тогда в разметку
    /// идёт то, что вышло, а не то, куда тянули.
    /// </summary>
    [AvaloniaFact]
    public void A_size_the_control_clamped_is_written_as_the_control_has_it()
    {
        var button = new Button { Width = 64 };
        _ = new DockPanel { Children = { button } };

        var node = Node("<Button/>", button);
        var values = GeometryWriter.Describe(node, new Rect(10, 10, 80, 30), new Rect(10, 10, 20, 30));

        Assert.Equal(("Width", "64"), Assert.Single(values));
    }

    [AvaloniaFact]
    public void Moving_inside_a_panel_writes_a_margin()
    {
        var button = new Button();
        _ = new DockPanel { Children = { button } };

        var node = Node("<Button Margin=\"4,4,0,0\"/>", button);
        var values = GeometryWriter.Describe(node, new Rect(10, 10, 80, 30), new Rect(30, 25, 80, 30));

        var (name, text) = Assert.Single(values);

        Assert.Equal("Margin", name);
        Assert.Equal("24,19,-20,-15", text);
    }

    /// <summary>
    /// Координаты канвы к родителю элемента отношения не имеют: складывается
    /// смещение, а не подставляется положение на канве.
    /// </summary>
    [AvaloniaFact]
    public void Moving_on_a_canvas_adds_the_shift_to_what_the_markup_says()
    {
        var button = new Button();
        var canvas = new Canvas { Children = { button } };

        // Канва уже подвинула контрол и записала в него свои координаты —
        // именно их брать нельзя.
        Canvas.SetLeft(button, 559);
        Canvas.SetTop(button, 329);

        var node = Node("<Button Canvas.Left=\"330\" Canvas.Top=\"14\"/>", button);
        var values = GeometryWriter.Describe(node, new Rect(559, 329, 85, 29), new Rect(599, 389, 85, 29));

        Assert.Equal(2, values.Count);
        Assert.Equal(("Canvas.Left", "370"), values[0]);
        Assert.Equal(("Canvas.Top", "74"), values[1]);
        Assert.Same(canvas, button.Parent);
    }

    /// <summary>
    /// Жест, который ничего не сдвинул, не должен доходить до разметки: иначе
    /// каждый щелчок по элементу оставлял бы запись в истории.
    /// </summary>
    [AvaloniaFact]
    public void A_gesture_that_moved_nothing_writes_nothing()
    {
        var button = new Button();
        _ = new DockPanel { Children = { button } };

        var node = Node("<Button/>", button);
        var values = GeometryWriter.Describe(node, new Rect(10, 10, 80, 30), new Rect(10.2, 10.1, 80.3, 30));

        Assert.Empty(values);
    }

    /// <summary>Собирает узел вокруг разобранного элемента и живого контрола.</summary>
    private static HierarchyNode Node(string markup, Control control)
    {
        var document = XamlDocument.Parse(markup);

        return new HierarchyNode(document.Root!, control, XamlElementPath.Of(document.Root!));
    }
}
