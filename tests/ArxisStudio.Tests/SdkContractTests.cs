using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт SDK: версия и команды, заявленные атрибутом.
/// </summary>
/// <remarks>
/// И то и другое — обещания, которые студия даёт автору плагина. Обещание,
/// которого никто не проверяет, живёт до первого плагина, написанного по нему
/// буквально.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SdkContractTests
{
    /// <summary>
    /// Студия годится плагину, которому нужен SDK не новее её.
    /// </summary>
    /// <remarks>
    /// Номера считаются от нынешней версии, а не пишутся числами: версия
    /// растёт, и тест, записанный под 1.0, при первом же росте проверял бы не
    /// правило, а прошлое.
    /// </remarks>
    [Fact]
    public void The_studio_satisfies_what_is_not_newer_than_itself()
    {
        var (major, minor) = Parse(StudioSdk.Version);

        Assert.True(StudioSdk.Satisfies(StudioSdk.Version), "своя же версия не подошла");
        Assert.False(StudioSdk.Satisfies($"{major}.{minor + 1}"), "приняли плагин, которому нужен SDK новее");
        Assert.False(StudioSdk.Satisfies($"{major + 1}.0"), "приняли плагин со следующим старшим номером");

        if (minor > 0)
            Assert.True(StudioSdk.Satisfies($"{major}.{minor - 1}"), "отвергли плагин, написанный под прежний младший номер");

        if (major > 1)
            Assert.True(StudioSdk.Satisfies($"{major - 1}.9"), "отвергли плагин прежнего старшего номера");
    }

    private static (int Major, int Minor) Parse(string version)
    {
        var parts = version.Split('.');

        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 0);
    }

    /// <summary>
    /// Непонятная или пустая версия отказом не считается.
    /// </summary>
    /// <remarks>
    /// Манифест пишет человек, и не запустить рабочий плагин из-за опечатки в
    /// номере — цена, несоразмерная поводу. О самой опечатке скажет проверка
    /// манифеста, а не отказ поднимать.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("следующая")]
    public void An_unreadable_version_is_not_a_refusal(string? required)
    {
        Assert.True(StudioSdk.Satisfies(required));
    }

    /// <summary>
    /// Плагину, которому нужен SDK новее, студия отказывает словами.
    /// </summary>
    /// <remarks>
    /// Отказ приходит до загрузки сборки. Подняв такой плагин, студия дала бы
    /// ему звать то, чего у неё нет, и он упал бы не там, где ошибся автор.
    /// </remarks>
    [Fact]
    public void A_plugin_that_needs_a_newer_sdk_is_refused_before_it_loads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-sdk-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "arxis.future"));
            File.WriteAllText(
                Path.Combine(root, "arxis.future", "plugin.json"),
                """
                {
                  "id": "arxis.future",
                  "name": "Из будущего",
                  "sdk": { "min": "9.0" },
                  "entry": "bin/Arxis.Future.dll",
                  "activation": [ "onStartup" ]
                }
                """);

            using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

            var loaded = Assert.Single(host.LoadStartup(new PluginCatalog(root).Scan()));

            Assert.False(loaded.IsLoaded);
            Assert.Contains("9.0", loaded.Error);
            Assert.Contains(StudioSdk.Version, loaded.Error);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Команда, помеченная атрибутом, заявляется сама.
    /// </summary>
    /// <remarks>
    /// Проверяется на настоящем примере: он и есть тот плагин, по которому
    /// автор будет писать свой.
    /// </remarks>
    [Fact]
    public void A_command_marked_by_the_attribute_registers_itself()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-attr-{Guid.NewGuid():N}");
        var commands = new StudioCommands();

        try
        {
            var catalog = new PluginCatalog(root);

            Assert.Null(catalog.InstallFromArchive(Archive()).Error);

            using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

            Assert.True(host.LoadStartup(catalog.Scan()).Count == 1, "плагин не поднялся");

            // Ни одного Register в коде примера нет — заявку сделал атрибут.
            // И заявлено ровно то, что объявляет манифест: разъехавшись, они
            // дали бы команду, которой нет в меню, или пункт меню, за которым
            // никого нет.
            Assert.All(
                Manifest().Contributions.Commands,
                command => Assert.Contains(command.Id, commands.Registered));

            Assert.True(commands.Invoke("hello.greet"), "команда не вызвалась");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Атрибут действует у объектов плагина и на статических методах — и
    /// больше нигде.
    /// </summary>
    /// <remarks>
    /// Экземплярный метод чужого класса пришлось бы звать на объекте,
    /// который студия создала сама и которому ничего не давала: без
    /// контекста и без состояния, заведённого при активации. Такая команда
    /// сделала бы не то, чего от неё ждут, и молча, — поэтому она не
    /// заявляется вовсе.
    /// </remarks>
    [Fact]
    public void The_attribute_works_on_plugin_objects_and_static_methods()
    {
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var loaded = host.LoadBuiltIn(TestAssembly.Emit("Arxis.Commands", Source, CommandsManifest));

        Assert.True(loaded.IsLoaded, loaded.Error);

        Assert.Contains("probe.entry", commands.Registered);
        Assert.Contains("probe.service", commands.Registered);
        Assert.Contains("probe.static", commands.Registered);
        Assert.DoesNotContain("probe.stray", commands.Registered);

        // Команда точки входа видит то же состояние, что и активация:
        // иначе её незачем было бы делать экземплярной.
        Assert.True(commands.Invoke("probe.entry"), "команда точки входа не вызвалась");
    }

    private const string CommandsManifest = """
        {
          "id": "arxis.commands",
          "name": "Команды",
          "contributions": {
            "commands": [ { "id": "probe.entry", "title": "Точка входа" } ]
          },
          "activation": [ "onStartup" ]
        }
        """;

    private const string Source = """
        using System;
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class CommandsPlugin : StudioPlugin
        {
            private IStudioContext? _context;

            public override void Activate(IStudioContext context) => _context = context;

            [Command("probe.entry")]
            private void Entry()
            {
                if (_context is null)
                    throw new InvalidOperationException("команда вызвана на объекте без контекста");
            }
        }

        public sealed class CommandsService : StudioService
        {
            public override void Start(IStudioContext context)
            {
            }

            [Command("probe.service")]
            public void FromService()
            {
            }
        }

        public sealed class Stray
        {
            [Command("probe.static")]
            public static void Static()
            {
            }

            [Command("probe.stray")]
            public void Instance()
            {
            }
        }
        """;

    private static PluginManifest Manifest()
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(Path.Combine(Sample(), "plugin.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);

        return manifest!;
    }

    private static string Archive() => Path.Combine(Sample(), "arxis.hello.axplugin");

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
