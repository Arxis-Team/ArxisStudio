using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Шаблон языкового пакета: с чего начинает переводчик.
/// </summary>
/// <remarks>
/// Начинает он не с чистого листа: словарь в шаблоне — это полный список
/// ключей студии с нашим английским текстом, и переводить надо правые
/// половины строк. Иначе первым делом пришлось бы выяснять, какие строки в
/// студии вообще бывают, — а список этот у нас, а не у него.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class LanguageTemplateTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        Localizer.Instance.UsePacks(null);
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        foreach (var folder in _folders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Словарь шаблона — это ровно ключи студии.
    /// </summary>
    /// <remarks>
    /// Список ключей растёт с каждым нашим релизом, а копия в шаблоне сама
    /// за ним не пойдёт. Тест и есть напоминание: разошлось — скопируйте
    /// <c>Localization/Strings/en.json</c> в <c>templates/Arxis.Language/lang/xx.json</c>.
    /// </remarks>
    [Fact]
    public void The_template_dictionary_holds_exactly_the_studio_keys()
    {
        var template = Keys(Path.Combine(Template(), "lang", "xx.json"));
        var studio = Localizer.Instance.Keys;

        Assert.Empty(studio.Except(template));
        Assert.Empty(template.Except(studio));

        // Название языка в шаблоне — подстановка, а не наше «English»:
        // словарь называет язык сам, и скопированный без правки английский
        // словарь показал бы новый язык английским.
        Assert.Contains(
            "LANGUAGE-NAME",
            File.ReadAllText(Path.Combine(Template(), "lang", "xx.json")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Из шаблона получается работающий пакет.
    /// </summary>
    /// <remarks>
    /// Проверяется то, что делает <c>dotnet new</c>: подстановка кода языка
    /// и названия. Дальше — обычный путь пакета: студия находит язык,
    /// показывает его в списке и берёт из него строки.
    /// </remarks>
    [Fact]
    public void A_pack_made_from_the_template_works()
    {
        var pack = Generate("de", "Deutsch");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "de", Name: "Deutsch" });
        Assert.True(Localizer.Instance.SetLanguage("de"), "язык из шаблонного пакета не выбрался");
    }

    /// <summary>
    /// Свежий пакет из шаблона закрывает все строки студии.
    /// </summary>
    /// <remarks>
    /// Значения в нём пока английские, и это честно: закрыты все ключи, а
    /// не все переводы. Полнота считает именно ключи — с ними переводчик и
    /// работает.
    /// </remarks>
    [Fact]
    public void A_fresh_pack_covers_every_key()
    {
        var coverage = Assert.Single(Generate("de", "Deutsch").Coverage);

        Assert.Equal(coverage.Total, coverage.Translated);
    }

    /// <summary>Собирает пакет так, как это сделал бы <c>dotnet new</c>.</summary>
    private InstalledPlugin Generate(string code, string name)
    {
        var source = Template();
        var target = Path.Combine(Path.GetTempPath(), $"arxis-template-{Guid.NewGuid():N}");

        _folders.Add(target);
        Directory.CreateDirectory(Path.Combine(target, "lang"));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            // Папка .template.config — служебная, в готовый пакет она не едет.
            if (file.Contains(".template.config", StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(source, file).Replace("xx", code, StringComparison.Ordinal);
            var text = File.ReadAllText(file)
                .Replace("xx", code, StringComparison.Ordinal)
                .Replace("LANGUAGE-NAME", name, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(target, relative), text);
        }

        var catalog = new PluginCatalog(Path.GetDirectoryName(target)!);

        return catalog.Scan().Single(plugin => plugin.Directory == target);
    }

    private static IReadOnlyCollection<string> Keys(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!.Keys;

    private static string Template()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "templates", "Arxis.Language");

            if (File.Exists(Path.Combine(candidate, ".template.config", "template.json")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден шаблон templates/Arxis.Language");
    }
}
