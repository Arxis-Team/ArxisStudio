using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Цвета студии идут за темой, а не остаются той, в которой их взяли.
/// </summary>
/// <remarks>
/// Ошибка этого рода не видна ни в одной теме по отдельности: в тёмной всё
/// тёмное, в светлой — светлое. Видна она только после переключения, когда
/// кисть, снятая у темы один раз, остаётся прежней. Живой замер нашёл так
/// подпись зависимости плагина: тёмный второстепенный на светлой плашке, 1,7:1.
/// </remarks>
public class ThemeFollowingTests
{
    /// <summary>
    /// Подпись зависимости переключается вместе с темой — и обычная, и проблемная.
    /// </summary>
    [AvaloniaFact]
    public void A_dependency_chip_follows_the_theme()
    {
        var chip = new AxChip { Classes = { "dependency" }, Content = "Hello — установлен" };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = chip };

        window.Show();

        try
        {
            Assert.Equal(Resource(window, "AxFg2Color", ThemeVariant.Dark), Colour(chip.Foreground));

            window.RequestedThemeVariant = ThemeVariant.Light;

            Assert.Equal(Resource(window, "AxFg2Color", ThemeVariant.Light), Colour(chip.Foreground));

            chip.Classes.Add("problem");

            Assert.Equal(Resource(window, "AxYellowTextColor", ThemeVariant.Light), Colour(chip.Foreground));
        }
        finally
        {
            window.Close();
        }
    }

    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Resource(Window window, string key, ThemeVariant variant)
    {
        Assert.True(window.TryFindResource(key, variant, out var value), $"нет ресурса {key}");

        return Assert.IsType<Color>(value);
    }
}
