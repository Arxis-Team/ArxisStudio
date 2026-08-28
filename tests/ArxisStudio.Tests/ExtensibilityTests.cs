using System.IO.Compression;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Расширяемость: каталог плагинов, установка из архива и поднятие плагина в
/// своём контексте загрузки.
/// </summary>
public class ExtensibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "arxis-tests-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_plugin_installs_from_an_archive()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));
        var archive = PackSample("arxis.sample", "Пример");

        var (plugin, error) = catalog.InstallFromArchive(archive);

        Assert.Null(error);
        Assert.NotNull(plugin);
        Assert.Equal("arxis.sample", plugin!.Id);
        Assert.Equal("Пример", plugin.DisplayName);
        Assert.Contains(catalog.Scan(), found => found.Id == "arxis.sample");
    }

    /// <summary>
    /// Архив — файл из чужих рук, и путь вида <c>../</c> в нём означает не
    /// установку, а запись куда попало.
    /// </summary>
    [Fact]
    public void An_archive_entry_that_climbs_out_of_the_folder_is_dropped()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));
        var archive = Path.Combine(_root, "evil.axplugin");
        var escaped = Path.Combine(_root, "escaped.txt");

        Directory.CreateDirectory(_root);

        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Write(zip, "plugin.json", Manifest("arxis.evil", "Злой"));
            Write(zip, "../../escaped.txt", "сюда писать нельзя");
        }

        var (plugin, error) = catalog.InstallFromArchive(archive);

        Assert.Null(error);
        Assert.NotNull(plugin);
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void An_archive_without_a_manifest_is_refused()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));
        var archive = Path.Combine(_root, "empty.axplugin");

        Directory.CreateDirectory(_root);

        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            Write(zip, "readme.txt", "тут ничего нет");

        var (plugin, error) = catalog.InstallFromArchive(archive);

        Assert.Null(plugin);
        Assert.NotNull(error);
    }

    /// <summary>
    /// Плагин с неверной entry-сборкой попадает в список с ошибкой, а не роняет
    /// студию и не пропадает молча.
    /// </summary>
    [Fact]
    public void A_plugin_that_cannot_be_loaded_reports_why()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));
        catalog.InstallFromArchive(PackSample("arxis.broken", "Сломанный"));

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = Assert.Single(host.LoadStartup(catalog.Scan()));

        Assert.False(loaded.IsLoaded);
        Assert.NotNull(loaded.Error);
    }

    /// <summary>Выключенный плагин не поднимается вовсе.</summary>
    [Fact]
    public void A_disabled_plugin_is_not_loaded()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));
        catalog.InstallFromArchive(PackSample("arxis.off", "Выключенный"));
        catalog.SetEnabled("arxis.off", false);

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        Assert.Empty(host.LoadStartup(catalog.Scan()));
    }

    [Fact]
    public void Commands_are_registered_and_invoked_by_id()
    {
        var commands = new StudioCommands();
        var called = 0;

        commands.Register("hello.greet", () => called++);

        Assert.True(commands.Invoke("hello.greet"));
        Assert.False(commands.Invoke("hello.missing"));
        Assert.Equal(1, called);
    }

    [Fact]
    public void The_log_keeps_what_was_written_with_its_level()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Warning, "Build", "что-то не так");

        var entry = Assert.Single(log.Records);

        Assert.Equal("WARN", entry.LevelName);
        Assert.True(entry.IsWarning);
        Assert.Equal("Build", entry.Source);

        log.Clear();
        Assert.Empty(log.Records);
    }

    /// <summary>
    /// Журнал отражается в поток тем же видом, каким его показала бы панель.
    /// </summary>
    /// <remarks>
    /// Панели, которая показывала бы журнал, в студии нет, и без отражения он
    /// виден только сам себе: студия пишет о сбое плагина, а прочесть это
    /// негде. Проверяется, что в строку попало всё, по чему потом ищут: время,
    /// уровень, источник и сообщение.
    /// </remarks>
    [Fact]
    public void The_journal_echoes_to_the_stream_it_was_given()
    {
        var stream = new StringWriter();
        var log = new StudioLog(stream);

        log.Write(StudioLogLevel.Error, "Plugins", "Figma Import: панель figma.panel — объект не создан");

        var line = stream.ToString().TrimEnd();

        Assert.Contains("ERROR", line);
        Assert.Contains("Plugins", line);
        Assert.Contains("figma.panel", line);
        Assert.Contains(Assert.Single(log.Records).Stamp, line);
    }

    /// <summary>
    /// Без потока журнал никуда не пишет.
    /// </summary>
    /// <remarks>
    /// Считать за библиотеку, что у процесса есть консоль, нельзя: решает это
    /// приложение. Тесты — тот самый случай, когда лишний вывод только мешает.
    /// </remarks>
    [Fact]
    public void Without_a_stream_the_journal_stays_silent()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "молча");

        Assert.Single(log.Records);
    }

    private string PackSample(string id, string name)
    {
        Directory.CreateDirectory(_root);

        var archive = Path.Combine(_root, id + ".axplugin");

        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        Write(zip, "plugin.json", Manifest(id, name));

        return archive;
    }

    private static string Manifest(string id, string name) =>
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "version": "1.0.0",
          "entry": "bin/Missing.dll",
          "activation": [ "onStartup" ]
        }
        """;

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(content);
    }
}
