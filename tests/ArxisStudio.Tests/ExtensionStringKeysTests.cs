using System.Text.Json;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Словари расширений репозитория: состав ключей и общая ветка меню.
/// </summary>
/// <remarks>
/// Словарь расширения называется кодом языка, и английский среди них особый: его читают, когда
/// файла текущего языка нет. Отсюда два правила, которые нарушаются молча и потому проверяются
/// здесь, а не глазами.
/// <para>
/// Языковые пакеты сюда не идут: своего интерфейса у них нет, а файлы в их <c>lang/</c> — это
/// словарь студии и переводы чужих расширений, у которых свои ключи и свой хозяин.
/// </para>
/// </remarks>
public class ExtensionStringKeysTests
{
    /// <summary>Расширения репозитория, у которых есть свой словарь.</summary>
    public static TheoryData<string> Extensions
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var extension in Found())
                data.Add(Path.GetFileName(extension));

            return data;
        }
    }

    /// <summary>
    /// Языки одного расширения описывают одни и те же ключи.
    /// </summary>
    /// <remarks>
    /// Ключ, забытый в переводе, виден только тому, кто открыл студию на этом языке; ключ, забытый
    /// в английском, — всем остальным сразу, потому что английский запасной. Правило то же, что у
    /// словарей самой студии (<c>SettingsAndLocalizationTests</c>), и держать его нужно так же
    /// строго: словари пишут руками.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Extensions))]
    public void Every_extension_describes_the_same_keys_in_every_language(string extension)
    {
        var folder = Found().Single(found => Path.GetFileName(found) == extension);
        var languages = Dictionaries(folder);

        Assert.True(languages.ContainsKey("en"), $"{extension}: нет английского словаря, а он запасной");

        var english = languages["en"].Keys.ToList();

        foreach (var (code, strings) in languages.Where(pair => pair.Key != "en"))
        {
            var missing = english.Except(strings.Keys, StringComparer.Ordinal).ToList();
            var extra = strings.Keys.Except(english, StringComparer.Ordinal).ToList();

            Assert.True(
                missing.Count == 0,
                $"{extension}: в {code}.json нет ключей: {string.Join(", ", missing)}");

            Assert.True(
                extra.Count == 0,
                $"{extension}: в {code}.json ключи, которых нет в английском: {string.Join(", ", extra)}");
        }
    }

    /// <summary>
    /// Общую ветку меню все называют одним словом.
    /// </summary>
    /// <remarks>
    /// Ветки меню сходятся по переведённому тексту, а не по ключу: своего словаря студии у
    /// расширения нет, и <c>%menu.tools%</c> каждое несёт у себя. Разойдись эти строки — и в меню
    /// оказалось бы два раздела, одинаковых с виду и разных по содержимому.
    /// </remarks>
    [Fact]
    public void Every_extension_names_the_shared_menu_branch_alike()
    {
        const string Branch = "menu.tools";

        var named = new Dictionary<string, List<(string Extension, string Text)>>(StringComparer.Ordinal);

        foreach (var folder in Found().Where(Declares(Branch)))
        {
            foreach (var (code, strings) in Dictionaries(folder))
            {
                if (!strings.TryGetValue(Branch, out var text))
                    continue;

                if (!named.TryGetValue(code, out var said))
                    named[code] = said = [];

                said.Add((Path.GetFileName(folder), text));
            }
        }

        Assert.NotEmpty(named);

        foreach (var (code, said) in named)
        {
            var apart = said.Select(pair => pair.Text).Distinct(StringComparer.Ordinal).ToList();

            Assert.True(
                apart.Count == 1,
                $"на языке {code} ветка названа по-разному: " +
                string.Join("; ", said.Select(pair => $"{pair.Extension} — «{pair.Text}»")));
        }
    }

    /// <summary>Объявляет ли расширение этот ключ в своём манифесте.</summary>
    private static Func<string, bool> Declares(string key) =>
        folder => File.ReadAllText(Manifest(folder)).Contains($"%{key}%", StringComparison.Ordinal);

    /// <summary>Словари расширения по коду языка.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Dictionaries(string folder)
    {
        var found = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, "lang"), "*.json"))
        {
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));

            Assert.NotNull(strings);

            found[Path.GetFileNameWithoutExtension(file)] = strings!;
        }

        return found;
    }

    /// <summary>
    /// Папки расширений репозитория со своим словарём.
    /// </summary>
    /// <remarks>
    /// Обходом, а не списком: новый модуль или плагин попадает под правило тем, что он есть, а не
    /// тем, что его сюда дописали.
    /// </remarks>
    private static IReadOnlyList<string> Found()
    {
        var repository = SharedAssemblies.Repository();

        var roots = new[]
        {
            Path.Combine(repository, "src", "Modules"),
            Path.Combine(repository, "src", "Plugins"),
            Path.Combine(repository, "templates"),
        };

        return
        [
            .. roots
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Where(folder => Directory.Exists(Path.Combine(folder, "lang")))
                .Where(folder => Manifest(folder) is { Length: > 0 })
                .Where(folder => !IsLanguagePack(folder))
                .OrderBy(folder => folder, StringComparer.Ordinal),
        ];
    }

    /// <summary>Манифест расширения — плагина или модуля; пусто, если его нет.</summary>
    private static string Manifest(string folder)
    {
        foreach (var name in new[] { "plugin.json", "module.json" })
        {
            var path = Path.Combine(folder, name);

            if (File.Exists(path))
                return path;
        }

        return string.Empty;
    }

    /// <summary>Языковой пакет: в его <c>lang/</c> лежат чужие словари, а не его собственные.</summary>
    private static bool IsLanguagePack(string folder) =>
        File.ReadAllText(Manifest(folder)).Contains("\"languages\"", StringComparison.Ordinal);
}
