using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Выбранная строка навигации красится палитрой студии, а не Fluent-ом.
    /// </summary>
    /// <remarks>
    /// ToggleButton — единственный контрол оболочки без своей темы, и берёт его
    /// Fluent. Текст Fluent красит на PART_ContentPresenter, а оболочка ставила
    /// цвет на самой кнопке: до текста он доходил наследованием, а наследование
    /// слабее значения на том же элементе. Выигрывал белый Fluent-а.
    /// <para>
    /// Светлая тема нужна здесь именно потому, что в тёмной подмена не видна:
    /// белое по AxSel (#2E436E) читается. В светлой AxSel — бледно-голубой, и
    /// подпись пропадала совсем.
    /// </para>
    /// <para>
    /// Спрашивается презентер, а не кнопка: у кнопки цвет был правильным и
    /// тогда, когда на экране была белая надпись.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_selected_navigation_row_takes_its_colour_from_the_theme()
    {
        var button = new ToggleButton { Classes = { "nav" }, Content = "Проекты", IsChecked = true };

        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Light,
            Content = button,
        };

        window.Show();
        window.UpdateLayout();

        var presenter = button.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(found => found.Name == "PART_ContentPresenter");

        Assert.Equal(Token(window, "AxFgBrush"), presenter.Foreground);
        Assert.Equal(Token(window, "AxSelBrush"), presenter.Background);

        window.Close();
    }

    /// <summary>Значение токена темы, как его видит это окно.</summary>
    private static object? Token(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) ? value : null;
}
