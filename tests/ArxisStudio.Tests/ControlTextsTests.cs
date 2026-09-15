using ArxisStudio.Controls;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Имена иконочных кнопок в шаблонах темы — на языке студии и вместе с ним.
/// </summary>
/// <remarks>
/// Тема держит запасное имя ресурсом, студия кладёт перевод тем же ключом в ресурсы приложения.
/// Проверяется то, что прочтёт экранный диктор: имя крестика в живом поле поиска.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ControlTextsTests : IDisposable
{
    private readonly List<string> _added = [];

    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        foreach (var key in _added)
            Application.Current!.Resources.Remove(key);

        GC.SuppressFinalize(this);
    }

    /// <summary>Крестик поля поиска называется словом студии и переводится с ней.</summary>
    [AvaloniaFact]
    public void The_clear_button_of_a_search_field_speaks_the_language_of_the_studio()
    {
        Localizer.Instance.SetLanguage("ru");

        using var attached = ControlTexts.Attach(Application.Current!);

        _added.AddRange(["AxTextSearchClear", "AxTextMessageClose", "AxTextDialogClose"]);

        var field = new AxSearchField { Text = "запрос" };
        var window = new Window { Content = field };

        window.Show();
        window.UpdateLayout();

        var clear = field.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Clear");

        Assert.Equal(Localizer.Instance["controls.search.clear"], AutomationProperties.GetName(clear));
        Assert.Equal("Очистить поиск", AutomationProperties.GetName(clear));

        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        Assert.Equal("Clear search", AutomationProperties.GetName(clear));

        window.Close();
    }
}
