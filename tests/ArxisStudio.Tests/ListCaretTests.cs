using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Стрелки списка студии ведут выбор и каретку, и кольцо фокуса идёт с ней.
/// </summary>
/// <remarks>
/// Список Avalonia 12 двигает выбор стрелкой через <c>MoveSelection</c>, а тот отдаёт каретку соседу
/// без способа, и кольцо фокуса — оно горит только у пришедшего с клавиатуры — гасло на первой же
/// стрелке во всех списках студии: в дереве окна проекта, в плитках, в списках окон. Клавиши идут
/// вводом окна, туда, где каретка.
/// </remarks>
public class ListCaretTests
{
    /// <summary>
    /// Стрелка сдвигает выбор на одну строку, и каретка на ней с кольцом; Shift растягивает выбор,
    /// End ведёт к последней.
    /// </summary>
    [AvaloniaFact]
    public void An_arrow_moves_the_choice_one_row_and_the_ring_goes_along()
    {
        var (window, list) = Framed(SelectionMode.Multiple);

        list.SelectedIndex = 0;
        Row(list, 0).Focus(NavigationMethod.Directional);

        Press(window, Key.Down);

        Assert.Equal(1, list.SelectedIndex);
        Assert.Same(Row(list, 1), window.FocusManager?.GetFocusedElement());
        Assert.Contains(":focus-visible", Row(list, 1).Classes);

        Press(window, Key.Down, RawInputModifiers.Shift);

        Assert.Equal([1, 2], list.Selection.SelectedIndexes.Order());
        Assert.Contains(":focus-visible", Row(list, 2).Classes);

        Press(window, Key.End);

        Assert.Equal(9, list.SelectedIndex);
        Assert.Contains(":focus-visible", Row(list, 9).Classes);

        window.Close();
    }

    /// <summary>
    /// Без каретки в строках стрелка начинает с края; с Ctrl стрелка ведёт одну каретку, выбор стоит.
    /// </summary>
    [AvaloniaFact]
    public void Without_a_caret_the_arrow_starts_at_the_edge_and_with_ctrl_the_choice_stays()
    {
        var (window, list) = Framed(SelectionMode.Single);

        list.Focusable = true;
        list.Focus(NavigationMethod.Directional);

        Press(window, Key.Down);

        Assert.Equal(0, list.SelectedIndex);
        Assert.Contains(":focus-visible", Row(list, 0).Classes);

        Press(window, Key.Down, RawInputModifiers.Control);

        Assert.Equal(0, list.SelectedIndex);
        Assert.Same(Row(list, 1), window.FocusManager?.GetFocusedElement());

        window.Close();
    }

    private static (Window Window, AxListBox List) Framed(SelectionMode mode)
    {
        var list = new AxListBox { ItemsSource = Enumerable.Range(0, 10).Select(at => $"строка {at}").ToList(), SelectionMode = mode };
        var window = new Window { Width = 300, Height = 400, Content = list };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, list);
    }

    private static Control Row(AxListBox list, int at) => Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(at));

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }
}
