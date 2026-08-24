using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>Настройки студии и словари локализации.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"arxis-settings-{Guid.NewGuid():N}");

    private string SettingsFile => Path.Combine(_directory, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Defaults_match_the_design_specification()
    {
        var settings = new JsonSettingsStore(SettingsFile).Current;

        Assert.Equal(StudioTheme.Dark, settings.Theme);
        Assert.Equal("ru", settings.Language);
        Assert.Equal(StudioDensity.Compact, settings.Density);
        Assert.True(settings.ShowCanvasGrid);
        Assert.True(settings.AutoSave);
        Assert.False(settings.OpenLastProject);
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        var store = new JsonSettingsStore(SettingsFile);
        store.Current.Theme = StudioTheme.Light;
        store.Current.Language = "en";
        store.Save();

        var reopened = new JsonSettingsStore(SettingsFile).Current;

        Assert.Equal(StudioTheme.Light, reopened.Theme);
        Assert.Equal("en", reopened.Language);
    }

    [Fact]
    public void Corrupted_settings_start_the_studio_with_defaults()
    {
        File.WriteAllText(SettingsFile, "}{");

        Assert.Equal(StudioTheme.Dark, new JsonSettingsStore(SettingsFile).Current.Theme);
    }

    [Fact]
    public void Saving_raises_the_event()
    {
        var store = new JsonSettingsStore(SettingsFile);
        var raised = 0;
        store.Saved += (_, _) => raised++;

        store.Save();

        Assert.Equal(1, raised);
    }
}

/// <summary>Словарь строк интерфейса.</summary>
public class LocalizerTests
{
    [Fact]
    public void Reads_strings_of_the_current_language()
    {
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Projects", Localizer.Instance["welcome.nav.projects"]);
        Assert.Equal("Открыть", Localizer.Instance["projects.open"]);
    }

    [Fact]
    public void Switching_language_switches_strings()
    {
        Localizer.Instance.SetLanguage("en");
        var english = Localizer.Instance["projects.open"];

        Localizer.Instance.SetLanguage("ru");
        var russian = Localizer.Instance["projects.open"];

        Assert.Equal("Open", english);
        Assert.Equal("Открыть", russian);
    }

    [Fact]
    public void A_missing_key_is_visible_rather_than_silent()
    {
        Assert.Equal("!no.such.key!", Localizer.Instance["no.such.key"]);
    }

    [Fact]
    public void An_unknown_language_falls_back_to_the_base_locale()
    {
        try
        {
            Localizer.Instance.SetLanguage("xx");

            Assert.Equal("Открыть", Localizer.Instance["projects.open"]);
        }
        finally
        {
            Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        }
    }
}
