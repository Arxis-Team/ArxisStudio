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
        // Английский, а не русский: при первом запуске студия говорит на том
        // языке, на котором написана, — на него же падает непереведённое.
        Assert.Equal("en", settings.Language);
        Assert.Equal(Localizer.FallbackLanguage, settings.Language);
        Assert.Equal(StudioDensity.Compact, settings.Density);
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
[Collection(StudioStateCollection.Name)]
public class LocalizerTests
{
    [Fact]
    public void Reads_strings_of_the_current_language()
    {
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Проекты", Localizer.Instance["welcome.nav.projects"]);
        Assert.Equal("Недавние", Localizer.Instance["projects.recent"]);
    }

    [Fact]
    public void Russian_locale_has_no_english_labels()
    {
        Localizer.Instance.SetLanguage("ru");

        Assert.Equal("Проекты", Localizer.Instance["welcome.nav.projects"]);
        Assert.Equal("Обучение", Localizer.Instance["welcome.nav.learn"]);
        Assert.Equal("Плагины", Localizer.Instance["welcome.nav.plugins"]);
        Assert.Equal("Настройки", Localizer.Instance["welcome.nav.settings"]);
        Assert.Equal("Перезапустить", Localizer.Instance["panel.reload"]);
        Assert.Equal("Установить из папки…", Localizer.Instance["plugins.install"]);
    }

    [Fact]
    public void Both_dictionaries_describe_the_same_keys()
    {
        Localizer.Instance.SetLanguage("ru");
        var russian = Keys("ru");
        var english = Keys("en");

        Assert.Empty(russian.Except(english));
        Assert.Empty(english.Except(russian));

        static IReadOnlyCollection<string> Keys(string language)
        {
            var name = $"ArxisStudio.Shell.Localization.Strings.{language}.json";
            using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);

            return System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(stream)!
                .Keys;
        }
    }

    [Fact]
    public void Switching_language_switches_strings()
    {
        Localizer.Instance.SetLanguage("en");
        var english = Localizer.Instance["projects.recent"];

        Localizer.Instance.SetLanguage("ru");
        var russian = Localizer.Instance["projects.recent"];

        Assert.Equal("Recent", english);
        Assert.Equal("Недавние", russian);
    }

    [Fact]
    public void A_missing_key_is_visible_rather_than_silent()
    {
        Assert.Equal("!no.such.key!", Localizer.Instance["no.such.key"]);
    }

    /// <summary>
    /// Язык, для которого нет ни одного словаря, не выбирается.
    /// </summary>
    /// <remarks>
    /// Выбрав его, студия показала бы весь интерфейс на запасном языке, а в
    /// настройках — выбранным тот, которого нет. Случай не выдуманный: язык
    /// записан в настройках, а словарь к нему могли удалить.
    /// </remarks>
    [Fact]
    public void An_unknown_language_is_not_selected()
    {
        try
        {
            Localizer.Instance.SetLanguage("ru");

            Assert.False(Localizer.Instance.SetLanguage("xx"), "приняли язык без единого словаря");

            Assert.Equal("ru", Localizer.Instance.Language);
            Assert.Equal("Недавние", Localizer.Instance["projects.recent"]);
        }
        finally
        {
            Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        }
    }

    /// <summary>
    /// Непереведённое падает на английский — язык, на котором написана студия.
    /// </summary>
    [Fact]
    public void What_is_not_translated_falls_into_english()
    {
        Assert.Equal("en", Localizer.FallbackLanguage);
    }
}
