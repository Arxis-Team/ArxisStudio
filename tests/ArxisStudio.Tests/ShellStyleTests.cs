using ArxisStudio.Controls;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
        var button = new AxToggleButton { Classes = { "nav" }, Content = "Проекты" };

        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = button,
        };

        window.Show();
        window.UpdateLayout();

        // Высота — из стилей оболочки и больше ниоткуда: без неё проверка кегля
        // прошла бы и на голом наследовании, то есть не проверяла бы стиль. Строка
        // навигации ростом с контрол: своих тридцати у неё больше нет.
        Assert.True(window.TryFindResource("AxControlHeight", window.ActualThemeVariant, out var height));
        Assert.Equal(height, button.Bounds.Height);
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
    /// Строка навигации была голым ToggleButton, и одевал его Fluent: текст он красит на
    /// PART_ContentPresenter, а оболочка ставила цвет на самой кнопке, и выигрывал белый
    /// Fluent-а. Теперь это переключатель студии, но проверка осталась — у презентера.
    /// <para>
    /// Светлая тема нужна здесь именно потому, что в тёмной подмена не видна:
    /// белое по AxSelectionActive (#263D68) читается. В светлой AxSelectionActive — бледно-голубой, и
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
        var button = new AxToggleButton { Classes = { "nav" }, Content = "Проекты", IsChecked = true };

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

        Assert.Equal(Token(window, "AxTextPrimaryBrush"), presenter.Foreground);
        Assert.Equal(Token(window, "AxSelectionActiveBrush"), presenter.Background);

        window.Close();
    }

    /// <summary>
    /// Линии под тулбаром и над статус-баром — по пикселю устройства на любом масштабе.
    /// </summary>
    /// <remarks>
    /// Обе стояли нижней и верхней рамкой своей полосы. Рамка мерится раскладочной единицей, а та
    /// ровна пикселю только при 100 и 200 %: при 125 и 150 % полосы хрома обрастали кантом в два
    /// пикселя — вдвое толще линий, которыми разрезаны доки между ними, — и окно выглядело
    /// собранным из обведённых плит. Границу области рисует разделитель, он же и считает пиксель.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    [InlineData(1.75d)]
    [InlineData(2d)]
    public void The_chrome_bands_are_parted_by_one_device_pixel(double scaling)
    {
        var shell = new StudioShell
        {
            TopBar = new AxTitleBar { ShowWindowControls = false, Content = new TextBlock() },
            StatusBar = new TextBlock { Text = "Готово" },
            Content = new Border(),
        };

        var window = new Window
        {
            Width = 400,
            Height = 300,
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = shell,
        };

        window.Show();
        window.SetRenderScaling(scaling);
        window.UpdateLayout();

        // Своя у каждой полосы и ещё одна у самой полосы заголовка — считаются все.
        var rules = shell.GetVisualDescendants().OfType<AxDivider>().ToList();

        Assert.Equal(3, rules.Count);

        foreach (var rule in rules)
            Assert.Equal(1d, Math.Round(rule.Bounds.Height * scaling, 6));

        window.Close();
    }

    /// <summary>Значение токена темы, как его видит это окно.</summary>
    private static object? Token(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) ? value : null;
}
