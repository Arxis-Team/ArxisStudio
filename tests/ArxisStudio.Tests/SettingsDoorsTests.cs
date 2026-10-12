using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Двери в окно настроек — у студии и у Welcome: обе открывают его одной дорогой и одинаково говорят, если оно не
/// открылось.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class SettingsDoorsTests
{
    /// <summary>
    /// Окно настроек, упавшее при открытии из Welcome, не роняет студию: Welcome говорит об ошибке, журнал её помнит, а
    /// дверь снова отпущена.
    /// </summary>
    /// <remarks>Перехват был только у двери студии, и у Welcome падение уходило из обработчика щелчка (запись 413).</remarks>
    [AvaloniaFact]
    public void Settings_that_fail_to_open_from_welcome_say_why()
    {
        using var harness = new SettingsHarness();
        var welcome = harness.Welcome(keys: () => throw new InvalidOperationException("страница клавиш сломалась"));

        welcome.Show();
        Dispatcher.UIThread.RunJobs();

        var door = welcome.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Single(candidate => AutomationProperties.GetName(candidate) == Localizer.Instance["welcome.nav.settings"]);

        door.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(welcome.OwnedWindows.OfType<SettingsWindow>());
        Assert.Contains("страница клавиш сломалась", Assert.IsType<WelcomeViewModel>(welcome.DataContext).Status, StringComparison.Ordinal);
        Assert.Contains(Assert.IsType<StudioLog>(harness.Plugins.Log).Records, record => record is { Source: "Settings", IsError: true });
        Assert.False(door.IsChecked, "дверь осталась отмеченной");

        welcome.Close();
    }
}
