using System.Reflection;
using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Меню встаёт карточкой туда, куда сказано местом, а не на поле под тень дальше.
/// </summary>
/// <remarks>
/// Вокруг карточки меню тема оставляет поле, на котором рисуется тень, и попап отмеряет место от
/// своего края, а не от карточки: «под кнопкой по левому краю» ставило меню на ширину поля правее
/// и ниже кнопки. Замер до поправки — восемь точек в обе стороны при поле 8; при поле, в которое
/// тень помещается целиком, было бы шестнадцать и двадцать.
/// <para>
/// Тестов у подмодуля контролов нет, поэтому проверка живёт здесь — там же, где меню пользуется
/// студия.
/// </para>
/// </remarks>
public class MenuFlyoutPlacementTests
{
    /// <summary>Карточка прилегает к якорю той стороной и тем краем, которые назвало место.</summary>
    [AvaloniaTheory]
    [InlineData(PlacementMode.BottomEdgeAlignedLeft)]
    [InlineData(PlacementMode.BottomEdgeAlignedRight)]
    [InlineData(PlacementMode.TopEdgeAlignedLeft)]
    [InlineData(PlacementMode.RightEdgeAlignedTop)]
    [InlineData(PlacementMode.Bottom)]
    public void The_card_lands_where_the_placement_says(PlacementMode placement)
    {
        var (window, anchor) = Shown();
        var flyout = Menu(placement);

        flyout.ShowAt(anchor);
        Dispatcher.UIThread.RunJobs();

        var target = Screen(anchor);
        var card = Screen(Card(flyout));

        var (expectedX, expectedY) = placement switch
        {
            PlacementMode.BottomEdgeAlignedLeft => (target.Left, target.Bottom),
            PlacementMode.BottomEdgeAlignedRight => (target.Right - card.Width, target.Bottom),
            PlacementMode.TopEdgeAlignedLeft => (target.Left, target.Top - card.Height),
            PlacementMode.RightEdgeAlignedTop => (target.Right, target.Top),
            _ => (target.Center.X - card.Width / 2, target.Bottom),
        };

        Assert.True(
            Math.Abs(card.X - expectedX) < 1 && Math.Abs(card.Y - expectedY) < 1,
            $"{placement}: карточка на {card.Position}, а место говорит {expectedX}, {expectedY} (якорь {target})");

        flyout.Hide();
        window.Close();
    }

    /// <summary>
    /// Смещение, заданное меню снаружи, прибавляется к месту и остаётся у меню.
    /// </summary>
    /// <remarks>
    /// Поправку на поле меню вносит только в сам показ: попап получает смещения один раз при
    /// открытии, и свойства после него возвращаются к тому, что задал автор.
    /// </remarks>
    [AvaloniaFact]
    public void An_offset_given_from_outside_adds_to_the_placement_and_stays()
    {
        var (window, anchor) = Shown();
        var flyout = Menu(PlacementMode.BottomEdgeAlignedLeft);

        flyout.HorizontalOffset = 5;
        flyout.VerticalOffset = 3;

        flyout.ShowAt(anchor);
        Dispatcher.UIThread.RunJobs();

        var target = Screen(anchor);
        var card = Screen(Card(flyout));

        Assert.True(Math.Abs(card.X - (target.Left + 5)) < 1, $"по горизонтали {card.X}, а ждали {target.Left + 5}");
        Assert.True(Math.Abs(card.Y - (target.Bottom + 3)) < 1, $"по вертикали {card.Y}, а ждали {target.Bottom + 3}");
        Assert.Equal(5, flyout.HorizontalOffset);
        Assert.Equal(3, flyout.VerticalOffset);

        flyout.Hide();
        window.Close();
    }

    /// <summary>
    /// Вложенное меню встаёт первым пунктом вровень с пунктом, который его открыл, и в четырёх
    /// точках правее его края.
    /// </summary>
    /// <remarks>
    /// Сдвиг вложенного меню задан в теме и выведен из поля под тень, отбивки меню и рамки. Пока
    /// поле было 8, а сдвиг по вертикали нулевой, первый пункт стоял на тринадцать ниже.
    /// </remarks>
    [AvaloniaFact]
    public void A_submenu_lines_its_first_item_up_with_its_parent()
    {
        var (window, anchor) = Shown();
        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var parent = new MenuItem { Header = "Панели" };

        parent.Items.Add(new MenuItem { Header = "Консоль" });
        parent.Items.Add(new MenuItem { Header = "Терминал" });
        flyout.Items.Add(new MenuItem { Header = "Открыть" });
        flyout.Items.Add(parent);

        flyout.ShowAt(anchor);
        Dispatcher.UIThread.RunJobs();

        parent.Open();
        Dispatcher.UIThread.RunJobs();

        var first = Assert.IsAssignableFrom<MenuItem>(parent.ContainerFromIndex(0));
        var item = Screen(parent);
        var child = Screen(first);
        var card = Screen(first.GetVisualAncestors().OfType<Border>().First(border => border.BoxShadow.Count > 0));

        Assert.True(Math.Abs(child.Y - item.Y) < 1, $"первый пункт вложенного меню на {child.Y}, а его пункт — на {item.Y}");
        Assert.True(Math.Abs(card.X - (item.Right + 4)) < 1, $"карточка вложенного меню на {card.X}, а край пункта — {item.Right}");

        parent.Close();
        flyout.Hide();
        window.Close();
    }

    private static (Window Window, AxButton Anchor) Shown()
    {
        var anchor = new AxButton
        {
            Content = "Якорь",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(300),
        };

        var window = new Window { Width = 900, Height = 800, Content = anchor };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, anchor);
    }

    private static AxMenuFlyout Menu(PlacementMode placement)
    {
        var flyout = new AxMenuFlyout { Placement = placement };

        flyout.Items.Add(new MenuItem { Header = "Открыть" });
        flyout.Items.Add(new MenuItem { Header = "Убрать из списка" });

        return flyout;
    }

    /// <summary>Карточка меню — рамка с тенью в его попапе.</summary>
    /// <remarks>
    /// Попап у меню внутренний, и достать его можно только отражением: другого пути к окну, в
    /// котором стоит карточка, у открытого меню нет.
    /// </remarks>
    private static Border Card(AxMenuFlyout flyout)
    {
        var popup = typeof(PopupFlyoutBase)
            .GetProperty("Popup", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(flyout) as Popup;

        var child = Assert.IsAssignableFrom<Control>(popup?.Child);

        return child.GetVisualDescendants().OfType<Border>().First(border => border.BoxShadow.Count > 0);
    }

    private static Rect Screen(Visual visual)
    {
        var topLeft = visual.PointToScreen(default);

        return new Rect(topLeft.X, topLeft.Y, visual.Bounds.Width, visual.Bounds.Height);
    }
}
