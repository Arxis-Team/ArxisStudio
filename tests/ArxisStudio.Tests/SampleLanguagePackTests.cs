using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell.Localization;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Пример языкового пакета из репозитория.
/// </summary>
/// <remarks>
/// По нему автор будет писать свой, и потому он проверяется как настоящий:
/// ключи студии переименовываются, ключи плагина — тоже, а пример за ними сам
/// не пойдёт. Устаревший пример хуже отсутствующего: он учит неправильному.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SampleLanguagePackTests : IDisposable
{
    public void Dispose()
    {
        Localizer.Instance.UsePacks(null);
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        GC.SuppressFinalize(this);
    }

    /// <summary>Студия принимает пример как обычный пакет и без замечаний.</summary>
    [Fact]
    public void The_sample_pack_is_accepted_as_it_is()
    {
        var packs = new PluginLanguages([Pack()]);

        Assert.Empty(packs.Problems);
        Assert.Contains("de", packs.Codes);

        Localizer.Instance.UsePacks(packs);

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "de", Name: "Deutsch" });
        Assert.True(Localizer.Instance.SetLanguage("de"), "язык примера не выбрался");
        Assert.Equal("Öffnen", Localizer.Instance["projects.open"]);
    }

    /// <summary>
    /// Пример переводит часть строк, а не все.
    /// </summary>
    /// <remarks>
    /// Это и показывается: перевод по частям — нормальный случай, остальное
    /// падает в английский. Полный пример врал бы о том, как выглядит живой
    /// перевод.
    /// </remarks>
    [Fact]
    public void The_sample_translates_a_part_and_says_so()
    {
        var coverage = Assert.Single(Pack().Coverage);

        Assert.InRange(coverage.Translated, 1, coverage.Total - 1);
    }

    /// <summary>Ключей, которых у студии нет, в примере нет.</summary>
    [Fact]
    public void The_sample_uses_only_studio_keys()
    {
        var stale = Keys(Path.Combine(Sample(), "lang", "de.json")).Except(Localizer.Instance.Keys).ToList();

        Assert.True(stale.Count == 0, $"ключи, которых у студии больше нет: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// Перевод плагина сделан по ключам самого плагина.
    /// </summary>
    /// <remarks>
    /// Ключ, которого у плагина нет, не покажется нигде: пакет переводил бы
    /// строку, о которой плагин не знает.
    /// </remarks>
    [Fact]
    public void The_translation_uses_only_the_keys_of_that_plugin()
    {
        var translated = Keys(Path.Combine(Sample(), "lang", "arxis.hello.de.json"));
        var own = Keys(Path.Combine(Hello(), "lang", "strings.json"));

        var stale = translated.Except(own).ToList();

        Assert.True(stale.Count == 0, $"ключи, которых у плагина нет: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// Ветка меню названа одним и тем же словом в обоих словарях.
    /// </summary>
    /// <remarks>
    /// Ветки меню сходятся по переведённому тексту: разойдись эти две
    /// строки — и в меню оказалось бы два одинаковых с виду раздела. Пример
    /// показывает именно этот случай, и потому обязан быть согласован.
    /// </remarks>
    [Fact]
    public void Both_dictionaries_name_the_menu_branch_alike()
    {
        var studio = Strings(Path.Combine(Sample(), "lang", "de.json"));
        var plugin = Strings(Path.Combine(Sample(), "lang", "arxis.hello.de.json"));

        Assert.Equal(studio["menu.tools"], plugin["menu.tools"]);
    }

    private static InstalledPlugin Pack()
    {
        var folder = Sample();
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(Path.Combine(folder, "plugin.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);

        return new InstalledPlugin(folder, manifest, null, IsEnabled: true);
    }

    private static Dictionary<string, string> Strings(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private static IEnumerable<string> Keys(string path) => Strings(path).Keys;

    private static string Sample() => Find("Arxis.Lang.De", "plugin.json");

    private static string Hello() => Find("Arxis.HelloPlugin", "Arxis.HelloPlugin.csproj");

    private static string Find(string plugin, string marker)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", plugin);

            if (File.Exists(Path.Combine(candidate, marker)))
                return candidate;
        }

        throw new InvalidOperationException($"Не найден src/Plugins/{plugin}");
    }
}
