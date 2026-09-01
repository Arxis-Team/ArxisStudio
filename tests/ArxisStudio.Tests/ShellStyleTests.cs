using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Стили оболочки: слой между темой и докингом.
/// </summary>
/// <remarks>
/// Слой этот в тестовом приложении раньше отсутствовал вовсе, поэтому и проверок
/// на него не было. Первая — про размеры: они обязаны приходить из токенов темы,
/// а не стоять числом рядом с ней. Число, совпадающее с токеном, отличимо от
/// токена только тогда, когда токен меняют.
/// </remarks>
public class ShellStyleTests
{
    /// <summary>Строка навигации берёт кегль у темы, а не пишет его числом.</summary>
    [AvaloniaFact]
    public void The_navigation_row_takes_its_size_from_the_theme()
    {
        var button = new ToggleButton { Classes = { "nav" }, Content = "Проекты" };

        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = button,
        };

        window.Show();
        window.UpdateLayout();

        // Высота — из стилей оболочки и больше ниоткуда: без неё проверка кегля
        // прошла бы и на голом наследовании, то есть не проверяла бы стиль.
        Assert.Equal(30d, button.Bounds.Height);
        Assert.Equal(13d, button.FontSize);

        window.Resources["AxFontSize"] = 26d;
        window.UpdateLayout();

        Assert.Equal(26d, button.FontSize);

        window.Close();
    }
}
