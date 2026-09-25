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
        Assert.Equal("Zuletzt verwendet", Localizer.Instance["projects.recent"]);
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
        var own = Keys(Path.Combine(Hello(), "lang", "en.json"));

        var stale = translated.Except(own).ToList();

        Assert.True(stale.Count == 0, $"ключи, которых у плагина нет: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// Ветка меню названа одним и тем же словом во всех переводах пакета.
    /// </summary>
    /// <remarks>
    /// Ветки меню сходятся по переведённому тексту: разойдись эти строки — и в меню оказалось бы
    /// два одинаковых с виду раздела. Пакет переводит и модули, и плагин, каждого своим файлом, и
    /// согласовать ветку обязан во всех сразу: студия здесь ни при чём, ключа <c>menu.tools</c> у
    /// неё нет — его несёт у себя каждое расширение.
    /// </remarks>
    [Fact]
    public void Every_translation_in_the_pack_names_the_menu_branch_alike()
    {
        const string Branch = "menu.tools";

        var named = Directory
            .EnumerateFiles(Path.Combine(Sample(), "lang"), "*.de.json")
            .Select(path => (Name: Path.GetFileName(path), Strings: Strings(path)))
            .Where(translation => translation.Strings.ContainsKey(Branch))
            .ToList();

        Assert.True(named.Count > 1, "пакет переводит ветку меню меньше чем у одного расширения");

        var apart = named.Select(translation => translation.Strings[Branch]).Distinct(StringComparer.Ordinal).ToList();

        Assert.True(
            apart.Count == 1,
            "ветка названа по-разному: " +
            string.Join("; ", named.Select(translation => $"{translation.Name} — «{translation.Strings[Branch]}»")));

        // И ключ студии она больше не занимает: там его нет.
        Assert.DoesNotContain(Branch, Strings(Path.Combine(Sample(), "lang", "de.json")).Keys);
    }

    /// <summary>
    /// Пакет переводит встроенный модуль тем же полем, что и плагин.
    /// </summary>
    /// <remarks>
    /// До того, как словари модулей переехали в их папки, эта дорога была закрыта: словарь модуля
    /// короткозамыкал в словарь студии, и перевод, объявленный на модуль, не спрашивался никогда.
    /// Пример показывает, что разницы между модулем и плагином у пакета нет.
    /// </remarks>
    [Fact]
    public void The_pack_translates_a_built_in_module()
    {
        var declared = Pack().Manifest!.Contributions.Languages
            .Single(language => language.Code == "de")
            .Translations ?? [];

        Assert.Contains(declared, translation => translation.Id == "arxis.terminal");

        var translations = new PluginLanguages([Pack()]);
        var terminal = translations.Read("arxis.terminal", "de");

        Assert.NotEmpty(terminal);
        Assert.Equal(terminal["menu.tools"], Strings(Path.Combine(Sample(), "lang", "arxis.hello.de.json"))["menu.tools"]);
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

    private static string Sample() => Repository.Path("src", "Plugins", "Arxis.Lang.De");

    private static string Hello() => Repository.Path("src", "Plugins", "Arxis.HelloPlugin");
}
