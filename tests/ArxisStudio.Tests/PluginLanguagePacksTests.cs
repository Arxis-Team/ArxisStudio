using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Языковой пакет: плагин, который приносит студии язык и не приносит кода.
/// </summary>
/// <remarks>
/// Раздача, установка, обновление, включение и удаление достаются ему даром —
/// это тот же менеджер плагинов. Ради языка не заведено ни своего каталога,
/// ни своего формата архива, ни своего места на диске.
/// </remarks>
[Collection(LocalizationCollection.Name)]
public class PluginLanguagePacksTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        Localizer.Instance.UsePacks(null);
        Localizer.Instance.UseFolders();
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        foreach (var folder in _folders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Язык из пакета студия показывает наравне со своими.</summary>
    [Fact]
    public void A_pack_gives_the_studio_a_language()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "de", Name: "Deutsch" });
        Assert.True(Localizer.Instance.SetLanguage("de"), "язык из пакета не выбрался");
        Assert.Equal("Öffnen", Localizer.Instance["projects.open"]);
    }

    /// <summary>
    /// Непереведённое падает в английский, а не в <c>!ключ!</c>.
    /// </summary>
    /// <remarks>
    /// Студия растёт, ключей прибавляется, и пакет неизбежно отстаёт. Без
    /// этого правила он протухал бы на первом же нашем релизе целиком.
    /// </remarks>
    [Fact]
    public void What_a_pack_misses_falls_into_english()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Projects", Localizer.Instance["welcome.nav.projects"]);
    }

    /// <summary>
    /// Название языка берётся из манифеста, если словарь себя не назвал.
    /// </summary>
    /// <remarks>
    /// Манифест студия прочитала и так — открывать ради имени каждый
    /// словарь незачем.
    /// </remarks>
    [Fact]
    public void A_pack_names_its_language_in_the_manifest()
    {
        var pack = Pack("arxis.lang-fr", "fr", "Français", """{ "projects.open": "Ouvrir" }""");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "fr", Name: "Français" });
    }

    /// <summary>
    /// Выключенный пакет языка не даёт.
    /// </summary>
    /// <remarks>
    /// Выключение — способ убрать принесённое плагином, не удаляя его
    /// самого, и на язык оно обязано распространяться так же, как на
    /// панели и команды.
    /// </remarks>
    [Fact]
    public void A_disabled_pack_gives_nothing()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""") with { IsEnabled = false };

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));

        Assert.DoesNotContain(Localizer.Instance.Languages, language => language.Code == "de");
        Assert.False(Localizer.Instance.SetLanguage("de"), "выключенный пакет дал язык");
    }

    /// <summary>
    /// Два пакета на один язык: первый занимает, о втором сказано.
    /// </summary>
    /// <remarks>
    /// Молча взять второй значило бы отдать язык тому, чья папка раньше
    /// попалась при обходе каталога, — это не выбор, а гонка.
    /// </remarks>
    [Fact]
    public void Two_packs_claiming_one_language_are_not_a_race()
    {
        var first = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Первый" }""");
        var second = Pack("other.lang-de", "de", "Deutsch", """{ "projects.open": "Второй" }""");

        var packs = new PluginLanguages([first, second]);

        Localizer.Instance.UsePacks(packs);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Первый", Localizer.Instance["projects.open"]);
        Assert.Contains(packs.Problems, problem => problem.Contains("de", StringComparison.Ordinal));
    }

    /// <summary>
    /// Объявленный язык без словаря не предлагается, и об этом сказано.
    /// </summary>
    /// <remarks>
    /// Пакет установлен, языка в списке нет — без такой записи человеку
    /// неоткуда узнать, почему.
    /// </remarks>
    [Fact]
    public void A_language_without_its_file_is_not_offered()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", dictionary: null);

        var packs = new PluginLanguages([pack]);

        Localizer.Instance.UsePacks(packs);

        Assert.DoesNotContain(Localizer.Instance.Languages, language => language.Code == "de");
        Assert.Contains(packs.Problems, problem => problem.Contains("de", StringComparison.Ordinal));
    }

    /// <summary>
    /// Файл, положенный руками, сильнее пакета.
    /// </summary>
    /// <remarks>
    /// Правка руками — способ починить что угодно, включая чужой перевод;
    /// отнимать его у человека незачем.
    /// </remarks>
    [Fact]
    public void A_file_put_by_hand_wins_over_a_pack()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Из пакета" }""");
        var user = Folder();

        File.WriteAllText(Path.Combine(user, "de.json"), """{ "projects.open": "Из папки" }""");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));
        Localizer.Instance.UseFolders(user: user);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Из папки", Localizer.Instance["projects.open"]);
    }

    /// <summary>
    /// Пакет удалили — студия вернулась на запасной язык.
    /// </summary>
    /// <remarks>
    /// Иначе выбранным остался бы язык, которого больше нет: интерфейс
    /// показывался бы по-английски, а в настройках стоял бы немецкий.
    /// </remarks>
    [Fact]
    public void Removing_a_pack_returns_the_studio_to_the_base_language()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""");

        Localizer.Instance.UsePacks(new PluginLanguages([pack]));
        Localizer.Instance.SetLanguage("de");

        Localizer.Instance.UsePacks(new PluginLanguages([]));

        Assert.Equal(Localizer.FallbackLanguage, Localizer.Instance.Language);
        Assert.Equal("Open", Localizer.Instance["projects.open"]);
    }

    /// <summary>
    /// Языковой пакет не поднимается: кода в нём нет.
    /// </summary>
    /// <remarks>
    /// Ни entry-сборки, ни событий активации у пакета не объявлено, и это
    /// не оплошность: перевод — данные, выполнять ему нечего. Заодно пакет
    /// от незнакомого человека ничего не может сделать с машиной.
    /// </remarks>
    [Fact]
    public void A_language_pack_is_never_raised()
    {
        var pack = Pack("arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""");

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        Assert.Empty(host.LoadStartup([pack]));
    }

    /// <summary>
    /// Пакет ставится тем же менеджером, что и всякий плагин.
    /// </summary>
    /// <remarks>
    /// В этом и весь расчёт: раздача, установка, обновление и удаление
    /// достаются языку даром. Заведи мы ради переводов свой каталог и свой
    /// формат — всё это пришлось бы написать заново, и человеку пришлось
    /// бы держать в голове два способа ставить одно и то же.
    /// </remarks>
    [Fact]
    public void A_pack_installs_like_any_other_plugin()
    {
        var source = Folder();
        var archive = Path.Combine(Folder(), "arxis.lang-de.axplugin");
        var root = Folder();

        Directory.CreateDirectory(Path.Combine(source, "lang"));
        File.WriteAllText(
            Path.Combine(source, "plugin.json"),
            """
            {
              "id": "arxis.lang-de",
              "name": "Deutsch",
              "version": "1.0.0",
              "contributions": {
                "languages": [ { "code": "de", "name": "Deutsch", "file": "lang/de.json" } ]
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(source, "lang", "de.json"),
            """{ "projects.open": "Öffnen" }""");

        System.IO.Compression.ZipFile.CreateFromDirectory(source, archive);

        var catalog = new PluginCatalog(root);

        Assert.Null(catalog.InstallFromArchive(archive).Error);

        Localizer.Instance.UsePacks(new PluginLanguages(catalog.Scan()));
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Öffnen", Localizer.Instance["projects.open"]);
    }

    private string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-pack-{Guid.NewGuid():N}");

        _folders.Add(folder);
        Directory.CreateDirectory(folder);

        return folder;
    }

    private InstalledPlugin Pack(string id, string code, string name, string? dictionary)
    {
        var folder = Folder();
        var file = $"lang/{code}.json";

        if (dictionary is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, "lang"));
            File.WriteAllText(Path.Combine(folder, file), dictionary);
        }

        var manifest = new PluginManifest { Id = id, Name = name };

        manifest.Contributions.Languages.Add(new PluginLanguage(code, name, file));

        return new InstalledPlugin(folder, manifest, null, IsEnabled: true);
    }
}
