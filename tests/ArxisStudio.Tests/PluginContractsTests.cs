using System.IO.Compression;
using Arxis.Hello.Contracts;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контрактные сборки: тип один на всех.
/// </summary>
/// <remarks>
/// Тест ссылается на контракт примера обычной ссылкой и приводит объект
/// плагина к СВОЕМУ <see cref="IGreeter"/>. Успех этого приведения — весь
/// смысл контрактов: без общего контекста тот же интерфейс, загруженный в
/// контекст плагина, был бы другим типом, и каст падал бы с «IGreeter не
/// приводится к IGreeter».
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginContractsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-contracts-{Guid.NewGuid():N}");

    public PluginContractsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
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
    /// Тип из контракта один на всех: объект плагина приводится к
    /// интерфейсу, на который ссылается тест.
    /// </summary>
    [Fact]
    public void The_contract_type_is_one_for_everyone()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive()).Error);

        using var host = Host();
        var loaded = Assert.Single(host.LoadStartup(catalog.Scan()));

        Assert.True(loaded.IsLoaded, loaded.Error);

        // Реализация лежит в сборке плагина, интерфейс — в общем контексте.
        var implementation = loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Single(type => type.Name == "Greeter");

        var greeter = Assert.IsAssignableFrom<IGreeter>(
            Activator.CreateInstance(implementation, loaded.Studio));

        Assert.Contains("Проба", greeter.Greet("Проба"));
    }

    /// <summary>
    /// Объявленный и отсутствующий файлом контракт — отказ владельцу.
    /// </summary>
    /// <remarks>
    /// Это обещание манифеста: зависимые рассчитывают на типы, и поднять
    /// владельца без них значило бы уронить соседей там, куда автор не
    /// заглядывал.
    /// </remarks>
    [Fact]
    public void A_declared_contract_that_is_missing_refuses_the_owner()
    {
        var target = Path.Combine(_root, "con.broken");

        ZipFile.ExtractToDirectory(HelloArchive(), target);
        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            """
            {
              "id": "con.broken",
              "name": "con.broken",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "provides": { "contracts": [ "bin/No.Such.Contracts.dll" ] },
              "activation": [ "onStartup" ]
            }
            """);

        using var host = Host();
        var failed = Assert.Single(host.LoadStartup(new PluginCatalog(_root).Scan()));

        Assert.False(failed.IsLoaded);
        Assert.Contains("No.Such.Contracts", failed.Error);
        Assert.Empty(host.Deferred);
    }

    /// <summary>
    /// Копия контракта в bin потребителя не раскалывает тип.
    /// </summary>
    /// <remarks>
    /// Автор потребителя забыл исключить контракт из своей раскладки —
    /// обычная ошибка. Контекст плагина обязан взять общую копию, а не
    /// свою: иначе вернулась бы двойная идентичность, от которой контракты
    /// и заведены.
    /// </remarks>
    [Fact]
    public void A_copy_in_the_consumer_bin_does_not_split_the_type()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive()).Error);

        // Потребитель — клон примера: его bin содержит копию контракта
        // (приехала из архива), а манифест контракта не объявляет.
        var consumer = Path.Combine(_root, "con.consumer");

        ZipFile.ExtractToDirectory(HelloArchive(), consumer);
        File.WriteAllText(
            Path.Combine(consumer, "plugin.json"),
            """
            {
              "id": "con.consumer",
              "name": "con.consumer",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "dependencies": [ { "id": "arxis.hello" } ],
              "activation": [ "onStartup" ]
            }
            """);

        using var host = Host();
        var raised = host.LoadStartup(new PluginCatalog(_root).Scan());

        Assert.All(raised, loaded => Assert.True(loaded.IsLoaded, loaded.Error));

        var fromConsumer = raised
            .Single(loaded => loaded.Installed.Id == "con.consumer")
            .Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Single(type => type.Name == "Greeter");

        // Тип интерфейса у реализации потребителя — тот же, что у теста.
        Assert.True(
            typeof(IGreeter).IsAssignableFrom(fromConsumer),
            "потребитель получил собственную копию контракта — тип раскололся");
    }

    /// <summary>
    /// Изменившийся на диске контракт — заметка, а не молчание.
    /// </summary>
    /// <remarks>
    /// Выгрузить прежнюю копию из общего контекста нечем: новые типы студия
    /// увидит после перезапуска, и человек должен узнать об этом из
    /// перезагрузки, а не догадаться по странностям.
    /// </remarks>
    [Fact]
    public void A_changed_contract_is_noted_and_the_old_one_stays()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive()).Error);

        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        Start(host, catalog);
        commands.RemoveOwnedBy("arxis.hello");

        // «Пересборка» контракта: содержимое то же, но файл на диске новее.
        var contract = Path.Combine(_root, "arxis.hello", "bin", "Arxis.Hello.Contracts.dll");

        File.SetLastWriteTimeUtc(contract, DateTime.UtcNow.AddMinutes(1));

        var installed = catalog.Scan().Single(plugin => plugin.Id == "arxis.hello");
        var cascade = host.Reload(["arxis.hello"], [installed]);

        Assert.Contains(cascade.Notes, note => note.Contains("перезапуска", StringComparison.Ordinal));
        Assert.All(cascade.Raised, loaded => Assert.True(loaded.IsLoaded, loaded.Error));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Start(PluginHost host, PluginCatalog catalog) =>
        Assert.Single(host.LoadStartup(catalog.Scan()), loaded => loaded.IsLoaded);

    private static PluginHost Host() =>
        new(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

    private static string HelloArchive()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", "Arxis.HelloPlugin", "arxis.hello.axplugin");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найден архив примера arxis.hello.axplugin");
    }
}
