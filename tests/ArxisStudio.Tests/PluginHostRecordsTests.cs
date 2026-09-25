using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Учёт хоста: одна запись на плагин, и подъём, который не роняет ничего вокруг себя.
/// </summary>
/// <remarks>
/// Плагины здесь настоящие, с диска: проверяется дорога, которой идёт установленное, — теневая
/// копия, свой контекст загрузки, выгрузка. Сборка у каждого теста своего имени: два контекста с
/// одноимённой сборкой жить могут, но путать их в отчёте незачем.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginHostRecordsTests : IDisposable
{
    private readonly string _root = TempFolder.Reserve("records");

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Плагин, упавший и поднятый заново, остаётся одной записью.
    /// </summary>
    /// <remarks>
    /// Пока запись об ошибке оставалась лежать рядом с живой, хост находил первой её: починенный
    /// плагин на «Перезагрузить» получал отказ словами о встроенном модуле, а отключение за сбои
    /// снимало мёртвую запись и оставляло живую копию работать.
    /// </remarks>
    [Fact]
    public void A_plugin_that_fell_and_rose_again_keeps_one_record()
    {
        using var host = Host(out _);

        var broken = Plugin("arxis.mended", "Probe.Mended", Falling, "onStartup");

        Assert.False(Assert.Single(host.LoadStartup([broken])).IsLoaded);

        var mended = Plugin("arxis.mended", "Probe.Mended", Working, "onStartup");
        var cascade = host.Reload([], [mended]);

        Assert.True(Assert.Single(cascade.Raised).IsLoaded, cascade.Raised[0].Error);
        Assert.True(Assert.Single(host.Loaded).IsLoaded, "прежняя запись об ошибке обязана уйти");

        var reload = host.Reload(mended);

        Assert.Null(reload.Error);
        Assert.True(Assert.Single(host.Loaded).IsLoaded);

        Assert.True(host.Drop("arxis.mended"));
        Assert.Empty(host.Loaded);
    }

    /// <summary>Упавший внешний плагин — не встроенный модуль: ему положена новая попытка.</summary>
    [Fact]
    public void A_fallen_external_plugin_is_offered_another_try_not_called_a_module()
    {
        using var host = Host(out _);

        var broken = Plugin("arxis.fallen", "Probe.Fallen", Falling, "onStartup");

        host.LoadStartup([broken]);

        var reload = host.Reload(broken);

        Assert.NotNull(reload.Error);
        Assert.DoesNotContain("встроенный модуль", reload.Error, StringComparison.Ordinal);
        Assert.Contains("плагин уронил подъём", reload.Error, StringComparison.Ordinal);
        Assert.False(Assert.Single(host.Loaded).IsLoaded);
    }

    /// <summary>
    /// Испорченный <c>.deps.json</c> — запись об ошибке на любой дороге, а не исключение из хоста.
    /// </summary>
    /// <remarks>
    /// Файл читает конструктор контекста загрузки, а он стоял перед швом. Старт это переживал своим
    /// <c>catch</c>, а пробуждение и перезагрузка — нет: плагин пропадал и из ждущих, и из
    /// поднятых, а исключение уходило в щелчок по кнопке.
    /// </remarks>
    [Fact]
    public void A_broken_deps_file_is_a_record_on_every_road()
    {
        using var host = Host(out _);

        var sleeping = Plugin("arxis.deps", "Probe.Deps", Working, "onCommand:deps.run");

        File.WriteAllText(Path.Combine(sleeping.Directory, "bin", "Probe.Deps.deps.json"), "{ это не json");

        Assert.Empty(host.LoadStartup([sleeping]));
        Assert.Single(host.Deferred);

        var woken = Assert.Single(host.Activate("arxis.deps"));

        Assert.False(woken.IsLoaded, "с испорченным .deps.json плагин подняться не должен");
        Assert.Empty(host.Deferred);
        Assert.Same(woken, Assert.Single(host.Loaded));

        var again = Assert.Single(host.Reload(["arxis.deps"], [sleeping]).Raised);

        Assert.False(again.IsLoaded);
        Assert.Same(again, Assert.Single(host.Loaded));
    }

    /// <summary>
    /// Подъём, упавший на середине, останавливает тех, кого успел позвать.
    /// </summary>
    /// <remarks>
    /// Точка входа подписалась и поднялась, служба рядом упала на запуске. Записью становится
    /// ошибка, но поднятая половина оставалась работать: подписка держала контекст загрузки, а
    /// «несостоявшийся» плагин получал события студии.
    /// </remarks>
    [Fact]
    public void A_rise_that_falls_halfway_lets_go_of_those_it_had_called()
    {
        using var host = Host(out _);

        var module = TestAssembly.EmitModule("Probe.Halfway", Halfway, """
            { "id": "arxis.halfway", "name": "Наполовину", "version": "1.0.0", "activation": [ "onStartup" ] }
            """);

        var loaded = host.LoadBuiltIn(module);

        Assert.False(loaded.IsLoaded);
        Assert.Contains("служба упала", loaded.Error, StringComparison.Ordinal);

        var entry = module.GetType("Probe.HalfwayEntry")!;
        var service = module.GetType("Probe.HalfwayService")!;

        Assert.Equal("activated,deactivated", (string)entry.GetField("Trace")!.GetValue(null)!);
        Assert.Equal("started,stopped", (string)service.GetField("Trace")!.GetValue(null)!);
    }

    /// <summary>
    /// Команда статического класса заявляется, как и всякая статическая.
    /// </summary>
    /// <remarks>
    /// Статический класс для среды исполнения — <c>abstract sealed</c>, и отбор по одному
    /// <c>IsAbstract</c> молча оставлял его команды незаявленными: пункт меню есть, а обработчика нет.
    /// <para>
    /// Метод с параметром командой не становится и в статическом классе: команда — это «сделай», и
    /// параметру взяться неоткуда, а молча передать null было бы хуже отказа.
    /// </para>
    /// <para>
    /// Абстрактная заготовка точки входа не поднимается вовсе: создать её нельзя, и попытка
    /// уронила бы подъём всего модуля.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_command_of_a_static_class_is_registered()
    {
        using var host = Host(out var commands);

        var module = TestAssembly.EmitModule("Probe.StaticCommands", """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class StaticCommandsModule : StudioPlugin
            {
                public override void Activate(IStudioContext context)
                {
                }
            }

            public static class Commands
            {
                public static int Calls;

                [Command("probe.static-class")]
                public static void Run() => Calls++;

                [Command("probe.with-argument")]
                public static void Take(string what) => Calls += what.Length;
            }

            public abstract class Unfinished : StudioPlugin
            {
                [Command("probe.abstract-class")]
                public static void Run()
                {
                }
            }
            """, """
            { "id": "arxis.static-commands", "name": "Статические", "version": "1.0.0", "activation": [ "onStartup" ] }
            """);

        Assert.True(host.LoadBuiltIn(module).IsLoaded);

        Assert.Contains("probe.static-class", commands.Registered);
        Assert.True(commands.Invoke("probe.static-class"));
        Assert.Equal(1, (int)module.GetType("Probe.Commands")!.GetField("Calls")!.GetValue(null)!);

        // Абстрактный класс — заготовка под наследника, а не дом для команд.
        Assert.DoesNotContain("probe.abstract-class", commands.Registered);
        Assert.DoesNotContain("probe.with-argument", commands.Registered);
    }

    /// <summary>
    /// Поднятый, пока ждал, второй раз не поднимается.
    /// </summary>
    /// <remarks>
    /// Включённый в настройках плагин поднимается сразу, не дожидаясь своего события. Запись о нём
    /// в ожидании при этом оставалась, и первое же событие поднимало вторую копию: два обработчика
    /// на каждую команду и две панели на одно имя в раскладке.
    /// </remarks>
    [Fact]
    public void A_plugin_raised_while_it_waited_is_not_raised_twice()
    {
        using var host = Host(out _);

        var sleeping = Plugin("arxis.twice", "Probe.Twice", Working, "onCommand:twice.run");

        Assert.Empty(host.LoadStartup([sleeping]));
        Assert.Single(host.Deferred);

        Assert.True(Assert.Single(host.Reload([], [sleeping]).Raised).IsLoaded);
        Assert.Empty(host.Deferred);

        Assert.Empty(host.Activate("arxis.twice"));
        Assert.Single(host.Loaded);

        // И обратной дорогой: просьба поднять уже поднятого — заметка, а не вторая копия.
        var cascade = host.Reload([], [sleeping]);

        Assert.Empty(cascade.Raised);
        Assert.Contains(cascade.Notes, note => note.Contains("уже поднят", StringComparison.Ordinal));
        Assert.Single(host.Loaded);
    }

    /// <summary>Снятый с ожидания больше не будится: его выключили.</summary>
    [Fact]
    public void A_withdrawn_plugin_no_longer_wakes()
    {
        using var host = Host(out _);

        var sleeping = Plugin("arxis.withdrawn", "Probe.Withdrawn", Working, "onCommand:withdrawn.run");

        host.LoadStartup([sleeping]);

        // Идентификатор сравнивается без регистра — так же, как в графе зависимостей.
        Assert.True(host.Withdraw("Arxis.Withdrawn"));
        Assert.False(host.Withdraw("arxis.withdrawn"), "второй раз снимать некого");

        Assert.Empty(host.Activate("arxis.withdrawn"));
        Assert.Empty(host.Loaded);
    }

    /// <summary>
    /// Ушедший плагин отпускается в обратном порядке подъёма — службы с конца, потом точки входа с
    /// конца, — так же, как подъём, упавший на середине.
    /// </summary>
    /// <remarks>
    /// Поднятое позже опирается на поднятое раньше. Прежде ушедший плагин останавливался с начала, а
    /// несостоявшийся — с конца, и две дороги одного прощания расходились.
    /// </remarks>
    [Fact]
    public void A_leaving_plugin_lets_go_in_the_reverse_order_of_its_rise()
    {
        var trace = new List<string>();
        var leaving = new LoadedPlugin(
            new InstalledPlugin(_root, new PluginManifest { Id = "probe.order", Name = "Порядок" }, null, IsEnabled: true),
            null,
            [],
            null,
            [new TracedEntry("вход 1", trace), new TracedEntry("вход 2", trace)],
            [new TracedService("служба 1", trace), new TracedService("служба 2", trace)],
            null);

        leaving.Unload();

        Assert.Equal(["служба 2", "служба 1", "вход 2", "вход 1"], trace);
    }

    /// <summary>
    /// Список поднятых отдаётся снимком.
    /// </summary>
    /// <remarks>
    /// Его читают и не из потока интерфейса: служба соседей — из фоновой работы плагина, разбор
    /// исключения забытой задачи — из потока финализатора. Живой список под правкой бросал бы
    /// «коллекция изменена» в чужом кадре.
    /// </remarks>
    [Fact]
    public void The_list_of_the_raised_is_a_snapshot()
    {
        using var host = Host(out _);

        host.LoadStartup([Plugin("arxis.snapshot", "Probe.Snapshot", Working, "onStartup")]);

        // Снимок берётся уже непустым: пустой начальный от живого списка не отличить.
        var before = host.Loaded;

        host.Reload([], [Plugin("arxis.snapshot-two", "Probe.SnapshotTwo", Working, "onStartup")]);

        Assert.Single(before);
        Assert.Equal(2, host.Loaded.Count);
    }

    /// <summary>
    /// Библиотека, которая только начинается как общая, грузится из папки плагина.
    /// </summary>
    /// <remarks>
    /// Проверяется сам резолвер, а не пересказ его правила: соседние тесты упаковки читают правило
    /// из исходника и исполняют свою копию, и подмена тела метода мимо них проходит. Библиотека
    /// названа по образцу <c>AvaloniaEdit</c> — чужая, в студии не лежит. Пока общую узнавали по
    /// первым буквам, резолвер отказывался брать её из папки плагина, основной контекст её не
    /// находил, и плагин падал на первом обращении.
    /// </remarks>
    [Fact]
    public void A_library_that_only_starts_like_a_shared_one_loads_from_the_plugin_folder()
    {
        using var host = Host(out _);

        var plugin = Plugin("arxis.lookalike", "Probe.Lookalike", """
            using System.Reflection;
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class LookalikePlugin : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    Assembly.Load(new AssemblyName("AvaloniaProbe"));
            }
            """, "onStartup");

        TestAssembly.EmitFile(Path.Combine(plugin.Directory, "bin", "AvaloniaProbe.dll"), "AvaloniaProbe", """
            namespace AvaloniaProbe;

            public static class Answer
            {
                public const int Value = 42;
            }
            """);

        var loaded = Assert.Single(host.LoadStartup([plugin]));

        Assert.True(loaded.IsLoaded, loaded.Error);
    }

    /// <summary>Настройки, выданные плагину, фабрика забывает по просьбе.</summary>
    [Fact]
    public void The_factory_forgets_the_settings_of_the_one_who_left()
    {
        var factory = new StudioContextFactory(new StudioLog(), new StudioCommands(), null);
        var plugin = Plugin("arxis.issued", "Probe.Issued", Working, "onStartup");

        factory.Create(plugin);

        Assert.Contains("arxis.issued", factory.Issued.Keys);

        factory.Forget("arxis.issued");

        Assert.DoesNotContain("arxis.issued", factory.Issued.Keys);
    }

    private static PluginHost Host(out StudioCommands commands)
    {
        commands = new StudioCommands();

        return new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));
    }

    /// <summary>Кладёт плагин в папку каталога и отдаёт запись о нём.</summary>
    private InstalledPlugin Plugin(string id, string assembly, string source, string activation)
    {
        TestAssembly.EmitPlugin(_root, id, assembly, source, activation);

        return new PluginCatalog(_root).Scan().Single(plugin => plugin.Id == id);
    }

    /// <summary>Точка входа, которая записывает своё прощание.</summary>
    private sealed class TracedEntry(string name, List<string> trace) : StudioPlugin
    {
        public override void Deactivate() => trace.Add(name);
    }

    /// <summary>Служба, которая записывает свою остановку.</summary>
    private sealed class TracedService(string name, List<string> trace) : StudioService
    {
        public override void Start(IStudioContext context)
        {
        }

        public override void Stop() => trace.Add(name);
    }

    private const string Working = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class WorkingPlugin : StudioPlugin
        {
            public override void Activate(IStudioContext context)
            {
            }
        }
        """;

    private const string Falling = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class FallingPlugin : StudioPlugin
        {
            public override void Activate(IStudioContext context) =>
                throw new System.InvalidOperationException("плагин уронил подъём");
        }
        """;

    private const string Halfway = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class HalfwayEntry : StudioPlugin
        {
            public static string Trace = "";

            public override void Activate(IStudioContext context) => Trace += "activated";

            public override void Deactivate() => Trace += ",deactivated";
        }

        public sealed class HalfwayService : StudioService
        {
            public static string Trace = "";

            public override void Start(IStudioContext context)
            {
                Trace += "started";

                throw new System.InvalidOperationException("служба упала на запуске");
            }

            public override void Stop() => Trace += ",stopped";
        }
        """;
}
