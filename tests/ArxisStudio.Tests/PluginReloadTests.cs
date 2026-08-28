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

    /// <summary>
    /// Перезагруженный плагин — новая копия, поднятая заново, а прежняя ушла.
    /// </summary>
    /// <remarks>
    /// Прежнюю копию тест нарочно не держит даже в переменной: ссылка на неё —
    /// такая же помеха выгрузке, как забытая подписка, и утверждение «новая
    /// копия отличается от старой», купленное такой ценой, обошлось бы дороже
    /// самой проверки.
    /// </remarks>
    [Fact]
    public void Reloading_raises_a_fresh_copy()
    {
        var commands = new StudioCommands();

        using var host = Raise(commands);

        // Команды прежней копии снимает оболочка — здесь то же самое руками:
        // оставленный обработчик держит типы плагина, и контекст не умрёт.
        commands.Remove(["hello.greet"]);

        var reload = host.Reload(Installed());

        Assert.Null(reload.Error);
        Assert.NotNull(reload.Plugin);
        Assert.True(reload.Plugin!.IsLoaded, reload.Plugin.Error);
        Assert.NotNull(reload.Plugin.Context);
        Assert.Same(reload.Plugin, Assert.Single(host.Loaded));
        Assert.Contains("hello.greet", commands.Registered);
        Assert.True(reload.Released, "прежний контекст не выгрузился");
    }

    /// <summary>
    /// Прежний контекст загрузки действительно умирает.
    /// </summary>
    /// <remarks>
    /// Выгрузка в .NET кооперативная: <c>Unload</c> её только начинает.
    /// Проверяется здесь то же, что проверяет хост, — но своей слабой ссылкой:
    /// ответ хоста мог бы быть и выдумкой.
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
    /// Оставленный обработчик команды не даёт выгрузиться — и хост об этом
    /// говорит.
    /// </summary>
    /// <remarks>
    /// Это и есть тот случай, ради которого проверка заведена: плагин поднят
    /// заново, а прежняя копия осталась в памяти и продолжает получать то, на
    /// что подписалась. Промолчать значило бы оставить человека с двумя
    /// копиями плагина и без единого слова о том, откуда они взялись.
    /// <para>
    /// Обработчик здесь оставлен нарочно: так поступает всякий, кто забыл
    /// отписаться в <c>Deactivate</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_plugin_still_referenced_is_reported_as_not_released()
    {
        var commands = new StudioCommands();

        using var host = Raise(commands);

        var reload = host.Reload(Installed());

        Assert.Null(reload.Error);
        Assert.NotNull(reload.Plugin);
        Assert.False(reload.Released, "хост назвал выгруженным контекст, который держит обработчик команды");
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

        var reload = host.Reload(host.Loaded.Single().Installed);

        Assert.Null(reload.Plugin);
        Assert.NotNull(reload.Error);
        Assert.Contains("встроенный модуль", reload.Error);
    }

    /// <summary>Того, кто не поднят, перезагружать нечего.</summary>
    [Fact]
    public void Reloading_a_plugin_that_is_not_up_is_refused()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var reload = host.Reload(
            new InstalledPlugin("нигде", new Sdk.Plugins.PluginManifest { Id = "arxis.nobody" }, null, true));

        Assert.Null(reload.Plugin);
        Assert.Contains("arxis.nobody", reload.Error);
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

        // Считаем поднятые, но саму запись не берём: ссылка на неё осталась бы
        // в кадре и держала бы контекст плагина живым — а тесту потом
        // спрашивать, выгрузился ли он. Отсюда и счёт вместо Assert.Single,
        // который вернул бы саму запись.
        Assert.True(host.LoadStartup(catalog.Scan()).Count == 1, "плагин не поднялся");

        return host;
    }

    private static string Sample()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", "Arxis.HelloPlugin");

            if (File.Exists(Path.Combine(candidate, "Arxis.HelloPlugin.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден пример плагина src/Plugins/Arxis.HelloPlugin");
    }
}
