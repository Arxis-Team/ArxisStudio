using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перезагрузка плагина без перезапуска студии.
/// </summary>
/// <remarks>
/// Ради этого у внешнего плагина и заведён свой выгружаемый контекст: автор
/// собрал новую сборку, положил её в папку плагина — и увидел её, не закрывая
/// студию. До сих пор контекст только и делал, что ждал закрытия окна, и
/// работает ли выгрузка на самом деле, не проверял никто.
/// </remarks>
public class PluginReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-reload-{Guid.NewGuid():N}");

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Перезагруженный плагин — новая копия, поднятая заново.</summary>
    [Fact]
    public void Reloading_raises_a_fresh_copy()
    {
        var commands = new StudioCommands();

        using var host = Raise(commands);

        var before = Assert.Single(host.Loaded);
        var (after, error) = host.Reload(Installed());

        Assert.Null(error);
        Assert.NotNull(after);
        Assert.True(after!.IsLoaded, after.Error);
        Assert.NotSame(before, after);
        Assert.NotSame(before.Context, after.Context);
        Assert.Same(after, Assert.Single(host.Loaded));
        Assert.Contains("hello.greet", commands.Registered);
    }

    /// <summary>
    /// Прежний контекст загрузки действительно умирает.
    /// </summary>
    /// <remarks>
    /// Сама по себе выгрузка ничего не гарантирует: контекст живёт, пока на его
    /// типы кто-то ссылается, — а обработчик команды, оставленный в реестре
    /// студии, ссылается ровно на них. Поэтому здесь и снимаются команды
    /// плагина: то же самое обязана делать оболочка, иначе перезагрузка будет
    /// копить в памяти по контексту за раз.
    /// </remarks>
    [Fact]
    public void The_old_context_is_really_unloaded()
    {
        var commands = new StudioCommands();

        using var host = Raise(commands);
        var old = Forget(host, commands);

        for (var attempt = 0; attempt < 10 && old.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(old.IsAlive, "контекст загрузки не выгрузился: на его типы кто-то ещё ссылается");
    }

    /// <summary>
    /// Поднятый плагин не держит файлы в своей папке.
    /// </summary>
    /// <remarks>
    /// Загруженная сборка держит свой файл открытым, пока жив контекст, — и
    /// автор плагина не смог бы его пересобрать, пока студия открыта. Поэтому
    /// плагин грузится из теневой копии: перезагружать имеет смысл только то,
    /// что успели собрать заново.
    /// </remarks>
    [Fact]
    public void A_running_plugin_leaves_its_own_files_alone()
    {
        using var host = Raise(new StudioCommands());

        var entry = Path.Combine(_root, "arxis.hello", "bin", "Arxis.HelloPlugin.dll");

        // Открыть на запись без права разделения выйдет только у того, у кого
        // файл больше никем не занят.
        using var write = File.Open(entry, FileMode.Open, FileAccess.Write, FileShare.None);

        Assert.True(write.CanWrite);
    }

    /// <summary>
    /// Встроенный модуль перезагрузке не подлежит — и говорит почему.
    /// </summary>
    /// <remarks>
    /// Его сборки живут в основном контексте вместе со сборками самой студии, и
    /// «перезагрузка» подняла бы вторую копию поверх первой: две панели и два
    /// обработчика на каждую команду.
    /// </remarks>
    [Fact]
    public void A_built_in_module_says_it_cannot_be_reloaded()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        host.LoadBuiltIn(typeof(SampleModule).Assembly);

        var (plugin, error) = host.Reload(host.Loaded.Single().Installed);

        Assert.Null(plugin);
        Assert.NotNull(error);
        Assert.Contains("встроенный модуль", error);
    }

    /// <summary>Того, кто не поднят, перезагружать нечего.</summary>
    [Fact]
    public void Reloading_a_plugin_that_is_not_up_is_refused()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var (plugin, error) = host.Reload(
            new InstalledPlugin("нигде", new Sdk.Plugins.PluginManifest { Id = "arxis.nobody" }, null, true));

        Assert.Null(plugin);
        Assert.Contains("arxis.nobody", error);
    }

    /// <summary>
    /// Перезагружает плагин и отпускает всё, что держало прежнюю копию.
    /// </summary>
    /// <remarks>
    /// Отдельный метод не для красоты: пока ссылка на прежний
    /// <c>LoadedPlugin</c> лежит в переменной вызывающего, контекст не умрёт
    /// никогда, и проверка выгрузки проверяла бы только это.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Forget(PluginHost host, StudioCommands commands)
    {
        var context = new WeakReference(host.Loaded.Single().Context);

        commands.Remove(["hello.greet"]);
        host.Reload(host.Loaded.Single().Installed);

        return context;
    }

    /// <summary>Запись каталога о поставленном примере.</summary>
    private InstalledPlugin Installed() =>
        new PluginCatalog(_root).Scan().Single(plugin => plugin.Id == "arxis.hello");

    /// <summary>Ставит пример плагина во временную папку и поднимает его.</summary>
    private PluginHost Raise(StudioCommands commands)
    {
        var archive = Path.Combine(Sample(), "arxis.hello.axplugin");
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(archive).Error);

        var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        Assert.Single(host.LoadStartup(catalog.Scan()));

        return host;
    }

    private static string Sample()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "Arxis.HelloPlugin");

            if (File.Exists(Path.Combine(candidate, "Arxis.HelloPlugin.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден пример плагина samples/Arxis.HelloPlugin");
    }
}
