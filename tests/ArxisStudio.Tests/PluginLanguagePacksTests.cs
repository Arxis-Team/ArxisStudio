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
[Collection(StudioStateCollection.Name)]
public class PluginLanguagePacksTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        Localizer.Instance.UsePacks(null);
        PluginStrings.UseTranslations(null);
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

    /// <summary>
    /// Пакет переводит чужой плагин.
    /// </summary>
    /// <remarks>
    /// Автор переводит плагин на языки, которые знает сам, и на этом его
    /// силы кончаются. Немец с русско-английским плагином иначе так и
    /// остался бы с чужим языком в своей панели.
    /// </remarks>
    [Fact]
    public void A_pack_translates_someone_elses_plugin()
    {
        var plugin = Plugin("arxis.hello", ("strings.json", """{ "panel.main": "Панель" }"""));
        var pack = Pack(
            "arxis.lang-de",
            "de",
            "Deutsch",
            """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Fenster" }"""));

        Apply(pack);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Fenster", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Свой перевод плагина сильнее перевода из пакета.
    /// </summary>
    /// <remarks>
    /// Про свой продукт автор знает больше постороннего, и подменять его
    /// слова словами пакета студия не станет.
    /// </remarks>
    [Fact]
    public void The_plugin_own_translation_wins_over_a_pack()
    {
        var plugin = Plugin(
            "arxis.hello",
            ("strings.json", """{ "panel.main": "Панель" }"""),
            ("strings.de.json", """{ "panel.main": "Von Autor" }"""));

        var pack = Pack(
            "arxis.lang-de",
            "de",
            "Deutsch",
            """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Aus Paket" }"""));

        Apply(pack);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Von Autor", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Чего пакет не закрыл, берётся у самого плагина.
    /// </summary>
    /// <remarks>
    /// Перевод чужого плагина отстаёт так же, как перевод студии: пакет
    /// закрывает часть строк, остальные показываются на языке автора, а не
    /// пропадают.
    /// </remarks>
    [Fact]
    public void What_a_translation_misses_comes_from_the_plugin_itself()
    {
        var plugin = Plugin(
            "arxis.hello",
            ("strings.json", """{ "panel.main": "Панель", "panel.side": "Сбоку" }"""));

        var pack = Pack(
            "arxis.lang-de",
            "de",
            "Deutsch",
            """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Fenster" }"""));

        Apply(pack);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Fenster", plugin.Strings.Resolve("%panel.main%"));
        Assert.Equal("Сбоку", plugin.Strings.Resolve("%panel.side%"));
    }

    /// <summary>
    /// Пакет убрали — плагин снова говорит своими словами.
    /// </summary>
    /// <remarks>
    /// Иначе удалённый пакет продолжал бы переводить чужие панели до
    /// перезапуска студии.
    /// </remarks>
    [Fact]
    public void Removing_a_pack_returns_the_plugin_to_its_own_words()
    {
        var plugin = Plugin("arxis.hello", ("strings.json", """{ "panel.main": "Панель" }"""));
        var pack = Pack(
            "arxis.lang-de",
            "de",
            "Deutsch",
            """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Fenster" }"""));

        Apply(pack);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Fenster", plugin.Strings.Resolve("%panel.main%"));

        Apply();

        Assert.Equal("Панель", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Проигравший спор за язык не переводит и чужие плагины.
    /// </summary>
    /// <remarks>
    /// Иначе вышло бы полупринятое: язык студии от одного пакета, а
    /// панели плагинов — от другого, и объяснить человеку, почему так,
    /// было бы нечем.
    /// </remarks>
    [Fact]
    public void The_pack_that_loses_the_language_does_not_translate_either()
    {
        var plugin = Plugin("arxis.hello", ("strings.json", """{ "panel.main": "Панель" }"""));

        var first = Pack(
            "arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Первый" }"""));

        var second = Pack(
            "other.lang-de", "de", "Deutsch", """{ "projects.open": "Aufmachen" }""",
            ("arxis.hello", """{ "panel.main": "Второй" }"""));

        var packs = new PluginLanguages([first, second]);

        Localizer.Instance.UsePacks(packs);
        PluginStrings.UseTranslations(packs);
        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Öffnen", Localizer.Instance["projects.open"]);
        Assert.Equal("Первый", plugin.Strings.Resolve("%panel.main%"));
    }

    /// <summary>
    /// Сменился набор пакетов — сменился и перевод, без смены языка.
    /// </summary>
    /// <remarks>
    /// Прочитанное словари помнят, и помнят по языку: не забудь студия
    /// прочитанное при смене набора, снятый пакет продолжал бы переводить
    /// чужие панели до перезапуска.
    /// </remarks>
    [Fact]
    public void Changing_the_packs_changes_the_translation()
    {
        var plugin = Plugin("arxis.hello", ("strings.json", """{ "panel.main": "Панель" }"""));

        Apply(Pack(
            "arxis.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Из первого" }""")));

        Localizer.Instance.SetLanguage("de");

        Assert.Equal("Из первого", plugin.Strings.Resolve("%panel.main%"));

        // Язык остаётся тем же — сменился только пакет, который его принёс.
        Apply(Pack(
            "other.lang-de", "de", "Deutsch", """{ "projects.open": "Öffnen" }""",
            ("arxis.hello", """{ "panel.main": "Из второго" }""")));

        Assert.Equal("de", Localizer.Instance.Language);
        Assert.Equal("Из второго", plugin.Strings.Resolve("%panel.main%"));
    }

    private void Apply(params InstalledPlugin[] packs)
    {
        var languages = new PluginLanguages(packs);

        Localizer.Instance.UsePacks(languages);
        PluginStrings.UseTranslations(languages);
    }

    private InstalledPlugin Plugin(string id, params (string File, string Content)[] dictionaries)
    {
        var folder = Folder();

        Directory.CreateDirectory(Path.Combine(folder, PluginStrings.Folder));

        foreach (var (file, content) in dictionaries)
            File.WriteAllText(Path.Combine(folder, PluginStrings.Folder, file), content);

        return new InstalledPlugin(
            folder,
            new PluginManifest { Id = id, Name = id },
            null,
            IsEnabled: true);
    }

    private string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-pack-{Guid.NewGuid():N}");

        _folders.Add(folder);
        Directory.CreateDirectory(folder);

        return folder;
    }

    private InstalledPlugin Pack(
        string id,
        string code,
        string name,
        string? dictionary,
        params (string PluginId, string Content)[] translations)
    {
        var folder = Folder();
        var file = $"lang/{code}.json";

        Directory.CreateDirectory(Path.Combine(folder, "lang"));

        if (dictionary is not null)
            File.WriteAllText(Path.Combine(folder, file), dictionary);

        var declared = new List<PluginTranslation>();

        foreach (var (pluginId, content) in translations)
        {
            var path = $"lang/{pluginId}.{code}.json";

            File.WriteAllText(Path.Combine(folder, path), content);
            declared.Add(new PluginTranslation(pluginId, path));
        }

        var manifest = new PluginManifest { Id = id, Name = name };

        manifest.Contributions.Languages.Add(
            new PluginLanguage(code, name, file, declared.Count > 0 ? declared : null));

        return new InstalledPlugin(folder, manifest, null, IsEnabled: true);
    }
}
