using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Дороги из чужого манифеста остаются внутри папки плагина.
/// </summary>
/// <remarks>
/// Манифест приходит из архива, который человек скачал, и всё, что в нём похоже на путь, путём и
/// становится. Проверка одна — <see cref="PluginPaths"/>, — и тесты ходят по каждой дороге, на
/// которой она стоит: идентификатор как имя папки, entry-сборка, значок, словарь пакета.
/// <para>
/// Каталог здесь заведён на два уровня глубже временной папки теста. Это страховка самих тестов:
/// сними кто-нибудь проверку, <c>..</c> увёл бы удаление на уровень выше — и этим уровнем обязана
/// быть папка теста, а не <c>%TEMP%</c>.
/// </para>
/// </remarks>
public class PluginPathsTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"arxis-paths-{Guid.NewGuid():N}");
    private readonly string _plugins;

    public PluginPathsTests()
    {
        _plugins = Path.Combine(_sandbox, "data", "plugins");

        Directory.CreateDirectory(_plugins);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("arxis.hello")]
    [InlineData("Arxis_Hello-2")]
    [InlineData("a")]
    [InlineData(".hidden")]
    public void A_usual_identifier_is_a_folder_name(string id) =>
        Assert.True(PluginPaths.IsFolderName(id), $"«{id}» обязан годиться именем папки");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("abc.")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:\\Temp\\evil")]
    [InlineData("/tmp/evil")]
    [InlineData("a b")]
    [InlineData("плагин")]
    [InlineData("abc\n")]
    public void An_identifier_that_is_not_one_folder_is_refused(string? id) =>
        Assert.False(PluginPaths.IsFolderName(id), $"«{id}» не должен годиться именем папки");

    /// <summary>Соседняя папка с тем же началом имени — не внутренность.</summary>
    [Fact]
    public void A_sibling_with_the_same_prefix_is_outside()
    {
        var folder = Path.Combine(_sandbox, "ab");

        Assert.Null(PluginPaths.Inside(folder, Path.Combine("..", "abc", "file.json")));
        Assert.Null(PluginPaths.Inside(folder, Path.Combine(_sandbox, "abc", "file.json")));
        Assert.Null(PluginPaths.Inside(folder, null));
        Assert.Null(PluginPaths.Inside(folder, "  "));

        Assert.Equal(
            Path.Combine(folder, "lang", "de.json"),
            PluginPaths.Inside(folder, "lang/de.json"));

        // Разделитель на конце папки — не повод отказать.
        Assert.Equal(
            Path.Combine(folder, "bin", "x.dll"),
            PluginPaths.Inside(folder + Path.DirectorySeparatorChar, "bin/x.dll"));
    }

    /// <summary>
    /// Идентификатор, уводящий из папки плагинов, — отказ, а не удаление чужой папки.
    /// </summary>
    /// <remarks>
    /// Обе кнопки менеджера ставят с заменой, а замена — это рекурсивное удаление цели. С
    /// <c>"id": ".."</c> целью становилась папка данных студии: настройки, раскладка и все плагины.
    /// </remarks>
    [Theory]
    [InlineData("..")]
    [InlineData("../data")]
    [InlineData("..\\..\\data")]
    [InlineData("%ROOTED%")]
    public void An_identifier_that_walks_out_never_touches_what_lies_outside(string id)
    {
        var outside = Path.Combine(_sandbox, "data", "settings.json");
        var rooted = Path.Combine(_sandbox, "victim");

        File.WriteAllText(outside, "{}");
        Directory.CreateDirectory(rooted);
        File.WriteAllText(Path.Combine(rooted, "precious.txt"), "не трогать");

        var source = Source(id == "%ROOTED%" ? rooted : id);

        var (plugin, error) = new PluginCatalog(_plugins).InstallFromDirectory(source, replace: true);

        Assert.Null(plugin);
        Assert.NotNull(error);
        Assert.Contains("не годится именем каталога", error, StringComparison.Ordinal);

        Assert.True(File.Exists(outside), "файл над папкой плагинов обязан уцелеть");
        Assert.True(File.Exists(Path.Combine(rooted, "precious.txt")), "чужая папка обязана уцелеть");
        Assert.Empty(Directory.GetDirectories(_plugins));
    }

    /// <summary>Тот же отказ у архива: дорога установки одна.</summary>
    [Fact]
    public void An_archive_with_a_walking_identifier_is_refused_the_same_way()
    {
        var outside = Path.Combine(_sandbox, "data", "settings.json");

        File.WriteAllText(outside, "{}");

        var archive = Path.Combine(_sandbox, "evil.axplugin");

        System.IO.Compression.ZipFile.CreateFromDirectory(Source(".."), archive);

        var (plugin, error) = new PluginCatalog(_plugins).InstallFromArchive(archive, replace: true);

        Assert.Null(plugin);
        Assert.Contains("не годится именем каталога", error, StringComparison.Ordinal);
        Assert.True(File.Exists(outside), "файл над папкой плагинов обязан уцелеть");
    }

    /// <summary>
    /// Копирование, сорвавшееся на середине, — слово, а не исключение, и без полуплагина.
    /// </summary>
    /// <remarks>
    /// Установку зовёт обработчик окна настроек, и исключение оттуда доезжает до диспетчера как
    /// дефект самой студии — то есть роняет её. А недокопированная папка с манифестом и без
    /// сборки при следующем запуске выглядела бы установленным плагином.
    /// </remarks>
    [Fact]
    public void A_copy_that_fails_is_a_word_and_leaves_no_half_plugin()
    {
        var source = Source("arxis.busy");

        using var hold = File.Open(
            Path.Combine(source, "bin", "Plugin.dll"), FileMode.Open, FileAccess.Read, FileShare.None);

        var (plugin, error) = new PluginCatalog(_plugins).InstallFromDirectory(source);

        Assert.Null(plugin);
        Assert.NotNull(error);
        Assert.Contains("не скопировался", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_plugins, "arxis.busy")), "полуплагин обязан быть убран");
    }

    /// <summary>
    /// Состояние, которое не записалось, — слово, и в памяти оно тоже не меняется.
    /// </summary>
    /// <remarks>
    /// Файл состояния подменён папкой с тем же именем: запись в неё невозможна на любой системе, и
    /// прав для этого менять не надо.
    /// </remarks>
    [Fact]
    public void A_state_that_cannot_be_written_is_a_word_and_changes_nothing()
    {
        var catalog = new PluginCatalog(_plugins);

        Assert.Null(catalog.InstallFromDirectory(Source("arxis.kept")).Error);

        Directory.CreateDirectory(Path.Combine(_plugins, ".disabled.json"));

        var error = catalog.SetEnabled("arxis.kept", false);

        Assert.NotNull(error);
        Assert.Contains(".disabled.json", error, StringComparison.Ordinal);
        Assert.True(Assert.Single(catalog.Scan()).IsEnabled, "не записанное выключение не должно действовать");
    }

    /// <summary>Значок, названный путём наружу, не значок.</summary>
    [Fact]
    public void An_icon_outside_the_plugin_folder_is_not_an_icon()
    {
        var folder = Path.Combine(_plugins, "arxis.icon");

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(_plugins, "outside.png"), "png");
        File.WriteAllText(Path.Combine(folder, "inside.png"), "png");

        InstalledPlugin With(string icon) =>
            new(folder, new PluginManifest { Id = "arxis.icon", Name = "Icon", Icon = icon }, null, IsEnabled: true);

        Assert.Null(With("../outside.png").IconPath);
        Assert.Null(With(Path.Combine(_plugins, "outside.png")).IconPath);
        Assert.Equal(Path.Combine(folder, "inside.png"), With("inside.png").IconPath);
    }

    /// <summary>
    /// Entry-сборка вне папки плагина не грузится, и сказано почему.
    /// </summary>
    /// <remarks>
    /// Плагин, положенный в папку руками, проверку установки не проходил — этот отказ стоит на
    /// самой загрузке.
    /// </remarks>
    [Fact]
    public void An_entry_outside_the_plugin_folder_is_not_loaded()
    {
        var folder = Path.Combine(_plugins, "arxis.entry");

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(_plugins, "Outside.dll"), "not really a dll");

        var installed = new InstalledPlugin(
            folder,
            new PluginManifest { Id = "arxis.entry", Name = "Entry", Entry = "../Outside.dll", Activation = ["onStartup"] },
            null,
            IsEnabled: true);

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = Assert.Single(host.LoadStartup([installed]));

        Assert.False(loaded.IsLoaded);
        Assert.Contains("за пределы", loaded.Error, StringComparison.Ordinal);
    }

    /// <summary>Словарь пакета вне его папки не читается, и язык не предлагается.</summary>
    [Fact]
    public void A_dictionary_outside_the_pack_folder_is_not_read()
    {
        var folder = Path.Combine(_plugins, "arxis.lang-xx");

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(_plugins, "stolen.json"), """{ "menu.file": "украдено" }""");

        var manifest = new PluginManifest { Id = "arxis.lang-xx", Name = "XX" };

        manifest.Contributions.Languages.Add(new PluginLanguage("xx", "XX", "../stolen.json", null));

        var pack = new InstalledPlugin(folder, manifest, null, IsEnabled: true);
        var packs = new PluginLanguages([pack]);

        Assert.DoesNotContain("xx", packs.Codes);
        Assert.Contains(packs.Problems, problem => problem.Contains("xx", StringComparison.Ordinal));
        Assert.Empty(pack.Coverage);
    }

    /// <summary>Папка-источник с манифестом и «сборкой».</summary>
    private string Source(string id)
    {
        var source = Path.Combine(_sandbox, $"source-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(source, "bin"));

        File.WriteAllText(
            Path.Combine(source, "plugin.json"),
            System.Text.Json.JsonSerializer.Serialize(new { id, name = "Probe", entry = "bin/Plugin.dll" }));

        File.WriteAllText(Path.Combine(source, "bin", "Plugin.dll"), "not really a dll");

        return source;
    }
}
