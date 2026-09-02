using ArxisStudio.Controls;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Лента полосы и её кнопка: раскладка, разделители, включённое состояние.
/// </summary>
/// <remarks>
/// Лента не знает ни манифестов, ни хозяев — только их имена, и проверяется
/// без всего этого: порядок ей дают, разделители она выводит сама.
/// </remarks>
public class ToolBarStripTests
{
    /// <summary>
    /// Разделитель стоит только там, где меняется хозяин.
    /// </summary>
    /// <remarks>
    /// Ни в начале, ни в конце, ни между соседями одного плагина: объявленный
    /// разделитель пережил бы выгрузку своего плагина и остался бы висеть
    /// двойным, а выведенный исчезает вместе с вкладом.
    /// </remarks>
    [AvaloniaFact]
    public void Dividers_stand_only_between_different_owners()
    {
        var strip = new ToolBarStrip();
        var (first, second, third) = (new Border(), new Border(), new Border());

        strip.Place([("hello", first), ("hello", second), ("friend", third)]);

        Assert.Equal(4, strip.Children.Count);
        Assert.Same(first, strip.Children[0]);
        Assert.Same(second, strip.Children[1]);
        Assert.IsType<AxDivider>(strip.Children[2]);
        Assert.Same(third, strip.Children[3]);
    }

    /// <summary>Один хозяин — ни одного разделителя.</summary>
    [AvaloniaFact]
    public void A_single_owner_needs_no_divider()
    {
        var strip = new ToolBarStrip();

        strip.Place([("hello", new Border()), ("hello", new Border())]);

        Assert.Equal(2, strip.Children.Count);
        Assert.DoesNotContain(strip.Children, child => child is AxDivider);
    }

    /// <summary>Повторная раскладка кладёт заново, а не добавляет к прежней.</summary>
    [AvaloniaFact]
    public void Placing_again_replaces_rather_than_appends()
    {
        var strip = new ToolBarStrip();
        var views = new List<(string, Control)> { ("hello", new Border()), ("friend", new Border()) };

        strip.Place(views);
        strip.Place(views);

        Assert.Equal(3, strip.Children.Count);
    }

    /// <summary>
    /// Включённая иконочная кнопка берёт вид включённого инструмента у темы.
    /// </summary>
    /// <remarks>
    /// Тема пишет это состояние псевдоклассом <c>:selected</c>; свойство кнопки
    /// его лишь ставит. Заливка — выделения, глиф — акцентом, как у включённого
    /// инструмента в карточке.
    /// </remarks>
    [AvaloniaFact]
    public void A_checked_icon_button_takes_the_selected_look()
    {
        var button = new ToolBarButton { Classes = { "icon" }, Content = new AxIcon { Data = AxIcons.Play } };
        var window = Shown(button);

        var plate = Plate(button);
        var before = Colour(plate.Background);

        button.IsChecked = true;
        window.UpdateLayout();

        Assert.Contains(":selected", button.Classes);
        Assert.Equal(Colour(Brush(window, "AxSelBrush")), Colour(plate.Background));
        Assert.NotEqual(before, Colour(plate.Background));

        button.IsChecked = false;
        window.UpdateLayout();

        Assert.DoesNotContain(":selected", button.Classes);

        window.Close();
    }

    /// <summary>
    /// У текстовой кнопки включённое состояние рисует оболочка.
    /// </summary>
    /// <remarks>
    /// В теме его нет: нужно оно только полосе, и класть его в тему значило бы
    /// обещать состояние каждой призрачной кнопке студии.
    /// </remarks>
    [AvaloniaFact]
    public void A_checked_text_button_is_filled_by_the_shell()
    {
        var button = new ToolBarButton { Classes = { "ghost", "compact" }, Content = "Debug" };
        var window = Shown(button);

        button.IsChecked = true;
        window.UpdateLayout();

        Assert.Equal(Colour(Brush(window, "AxSelBrush")), Colour(Plate(button).Background));

        window.Close();
    }

    /// <summary>
    /// Кнопка полосы одета темой обычной кнопки.
    /// </summary>
    /// <remarks>
    /// Наследник ищет тему по своему типу и без оговорки остался бы голым —
    /// без шаблона, а значит и без плашки, которую красит состояние.
    /// </remarks>
    [AvaloniaFact]
    public void The_button_keeps_the_theme_of_a_button()
    {
        var button = new ToolBarButton { Classes = { "icon" } };
        var window = Shown(button);

        Assert.NotNull(button.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault());

        window.Close();
    }

    /// <summary>Разделитель полосы — двадцать высотой, цветом нажатия.</summary>
    [AvaloniaFact]
    public void The_toolbar_divider_is_twenty_high()
    {
        var divider = new AxDivider { Orientation = Orientation.Vertical, Classes = { "toolbar" } };
        var strip = new ToolBarStrip();

        strip.Children.Add(divider);

        var window = Shown(strip);

        Assert.Equal(20d, divider.Bounds.Height);
        Assert.Equal(1d, divider.Bounds.Width);
        Assert.Equal(Colour(Brush(window, "AxBg4Brush")), Colour(divider.Background));

        window.Close();
    }

    private static Window Shown(Control content)
    {
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = content,
        };

        window.Show();
        window.UpdateLayout();

        return window;
    }

    /// <summary>Плашка кнопки: она несёт заливку состояния.</summary>
    private static ContentPresenter Plate(Control button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().First(c => c.Name == "PART_ContentPresenter");

    private static Color? Colour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static IBrush Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);

        return (IBrush)value!;
    }
}
