using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Вклады в контракт модуля: студия находит их классы у поднятых расширений, строит через шов, отдаёт
/// контекст хозяина и снимает с его выгрузкой (запись 358).
/// </summary>
/// <remarks>
/// Модуль с контрактом собирается один раз на прогон: контрактная сборка живёт в общем контексте до конца
/// процесса, и второй её копии с теми же типами компиляция плагинов не различила бы. Плагины — свои у
/// каждого теста, папкой каталога, как настоящие.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioContributionsTests : IDisposable
{
    private const string ModuleId = "probe.contributions";

    private static readonly Lazy<(Assembly Module, Assembly Contract)> Probe = new(() =>
        TestAssembly.EmitModuleWithContract(ModuleId, ContractSource, ModuleSource));

    private readonly string _root = TempFolder.Reserve("contributions");
    private readonly StudioPluginsHarness _studio = new();
    private readonly List<PluginFailure> _failures = [];

    public StudioContributionsTests()
    {
        _studio.Guard.Failed += (_, failure) => _failures.Add(failure);
        Journal.Clear();
        Hook("CollectOnStart")?.SetValue(null, false);
    }

    public void Dispose()
    {
        _studio.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        TempFolder.Erase(_root);
        Journal.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>Журнал контракта: прощания и события, общий для всех копий плагинов.</summary>
    private static List<string> Journal =>
        (List<string>)Probe.Value.Contract.GetType("ProbeContributions.Voice")!
            .GetField("Journal", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>
    /// Модуль собирает классы, унаследованные от типа его контракта, у всех поднятых расширений — и у себя.
    /// </summary>
    /// <remarks>
    /// Модули стоят раньше плагинов: так равный вклад плагина отличим от студийного.
    /// </remarks>
    [AvaloniaFact]
    public void A_module_collects_what_every_raised_extension_derives_from_its_contract()
    {
        Plugin("probe.loud", "LoudVoice", "public override string Say() => \"плагин\";");

        var plugins = Start();
        var set = Voices().Items;

        Assert.Equal(2, set.Count);
        Assert.True(set[0].IsBuiltIn, "вклад модуля стоит не первым");
        Assert.Equal("модуль", Say(set[0]));
        Assert.Equal("probe.loud", set[1].OwnerId);
        Assert.False(set[1].IsBuiltIn);
        Assert.Equal("плагин", Say(set[1]));
        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == "probe.loud");
    }

    /// <summary>Вклад получает контекст своего хозяина и берёт свои строки, а не строки собравшего.</summary>
    /// <remarks>
    /// Прежние рисовальщики свойств студии не видели вовсе и не могли взять даже своих строк — изъян, с
    /// которым они ушли из реестра вкладов.
    /// </remarks>
    [AvaloniaFact]
    public void A_contribution_is_attached_with_its_owners_context_and_reads_its_own_strings()
    {
        var folder = Plugin("probe.worded", "WordedVoice", "public override string Say() => Word(\"probe.word\");");

        Directory.CreateDirectory(Path.Combine(folder, "lang"));
        File.WriteAllText(Path.Combine(folder, "lang", "en.json"), """{ "probe.word": "своё слово" }""");

        Start();

        var worded = Assert.Single(Voices().Items, voice => voice.OwnerId == "probe.worded");

        Assert.Equal("своё слово", Say(worded));
    }

    /// <summary>Расширение, поднятое после сбора, входит в набор, и набор говорит об этом.</summary>
    [AvaloniaFact]
    public void A_plugin_raised_later_joins_the_set_and_the_set_says_so()
    {
        Plugin("probe.late", "LateVoice", "public override string Say() => \"поздний\";", activation: "onCommand:probe.late.go");

        var plugins = Start();
        var set = Voices();
        var changes = 0;

        set.Changed += (_, _) => changes++;

        Assert.Single(set.Items);

        plugins.Activate(waiting => waiting.Id == "probe.late");

        Assert.Equal(1, changes);
        Assert.Equal("поздний", Say(Assert.Single(set.Items, voice => voice.OwnerId == "probe.late")));
    }

    /// <summary>
    /// Выгруженный плагин уносит свои вклады — прощаясь, — и его контекст уходит из процесса.
    /// </summary>
    /// <remarks>
    /// Порядок держит обещание контракта: вклад прощается, пока его код есть, затем собравший слышит
    /// <c>Released</c> и снимает построенное, и только потом набор говорит о новом составе.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_unloaded_plugin_takes_its_contributions_away_and_its_context_leaves_the_process()
    {
        Plugin("probe.leaving", "LeavingVoice", "public override string Say() => \"уходящий\";");

        var plugins = Start();
        var set = Voices();

        Leaving(set);

        set.Changed += (_, _) => Journal.Add("набор");

        await plugins.ReloadAsync("probe.leaving");

        Assert.Equal(["прощание LeavingVoice", "отпущен", "набор", "набор"], Journal);
        Assert.Equal("уходящий", Say(Assert.Single(set.Items, voice => voice.OwnerId == "probe.leaving")));
        Assert.Empty(plugins.AwaitingRestart);
    }

    /// <summary>Сбой вклада засчитывается его хозяину, а не модулю, который его позвал.</summary>
    [AvaloniaFact]
    public void A_failure_counts_against_the_owner_not_the_collector()
    {
        Plugin("probe.broken", "BrokenVoice", "public override string Say() => throw new System.InvalidOperationException(\"голос сорван\");");

        Start();

        var broken = Assert.Single(Voices().Items, voice => voice.OwnerId == "probe.broken");

        Assert.Null(Say(broken));

        var failure = Assert.Single(_failures);

        Assert.Equal("probe.broken", failure.PluginId);
        Assert.Equal("голос", failure.What);
    }

    /// <summary>Вклад, упавший при постройке, соседям ничего не стоит, а сбой засчитывается хозяину.</summary>
    /// <remarks>
    /// Постройка — чужой код: пока её звали мимо шва, конструктор одного вклада обрывал сбор у всех.
    /// </remarks>
    [AvaloniaFact]
    public void A_contribution_that_falls_when_built_costs_the_others_nothing()
    {
        Plugin("probe.fragile", "SturdyVoice", "public override string Say() => \"цел\";", extra: """
            public sealed class FragileVoice : Voice
            {
                public FragileVoice() => throw new System.InvalidOperationException("не построился");

                public override string Say() => "разбит";
            }
            """);

        Start();

        var voices = Voices().Items.Where(voice => voice.OwnerId == "probe.fragile").ToList();

        Assert.Equal("цел", Say(Assert.Single(voices)));
        Assert.Contains(_failures, failure => failure.PluginId == "probe.fragile" && failure.What == "вклад FragileVoice");
    }

    /// <summary>
    /// Спящий, ждущий сбора в контракт, просыпается на первом сборе — и только он.
    /// </summary>
    /// <remarks>
    /// Плагину, вся работа которого — вклад в чужой модуль, своей кнопки и панели нет: без события он
    /// поднимался бы на каждом старте, а с событием чужого модуля или с командой спит дальше.
    /// </remarks>
    [AvaloniaFact]
    public void A_sleeping_plugin_waiting_for_the_contract_wakes_on_first_collect_and_no_other_does()
    {
        Plugin("probe.sleeper", "SleeperVoice", "public override string Say() => \"проснулся\";", activation: $"onContribution:{ModuleId}");
        Plugin("probe.dreamer", "DreamerVoice", "public override string Say() => \"спит\";", activation: "onCommand:probe.dream");
        Plugin("probe.elsewhere", "ElsewhereVoice", "public override string Say() => \"чужой\";", activation: "onContribution:other.module");

        var plugins = Start();

        Assert.DoesNotContain(plugins.Reloadable, plugin => plugin.Id == "probe.sleeper");

        // Собирает не хозяин контракта, а сосед: будит событие с именем хозяина, а не собравшего.
        var set = Voices(new PluginContributionFacade(_studio.Contributions, "probe.stranger"));

        Assert.Equal("проснулся", Say(Assert.Single(set.Items, voice => voice.OwnerId == "probe.sleeper")));
        Assert.DoesNotContain(set.Items, voice => voice.OwnerId is "probe.dreamer" or "probe.elsewhere");
        Assert.DoesNotContain(plugins.Reloadable, plugin => plugin.Id is "probe.dreamer" or "probe.elsewhere");
    }

    /// <summary>
    /// Модуль, начавший собирать при своём подъёме, получает и спящего, отложенного уже после него.
    /// </summary>
    /// <remarks>
    /// Модули поднимаются раньше плагинов: на их сборе ждущих ещё нет, и будить их, кроме конца подъёма
    /// плагинов, некому.
    /// </remarks>
    [AvaloniaFact]
    public void A_plugin_waiting_for_a_collection_begun_at_startup_wakes_after_the_plugins_load()
    {
        Plugin("probe.early", "EarlyVoice", "public override string Say() => \"к началу\";", activation: $"onContribution:{ModuleId}");

        Hook("CollectOnStart")!.SetValue(null, true);

        var plugins = Start();
        var early = (IStudioContributionSet<StudioContribution>)Hook("Early")!.GetValue(null)!;

        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == "probe.early");
        Assert.Equal("к началу", Say(Assert.Single(early.Items, voice => voice.OwnerId == "probe.early")));
    }

    /// <summary>Тип вклада из сборки самого плагина набором не становится, и студия говорит почему.</summary>
    [AvaloniaFact]
    public void A_contract_type_living_in_a_plugin_is_refused_with_a_reason()
    {
        TestAssembly.EmitPlugin(_root, "probe.selfish", "Probe.Selfish", source: """
            using ArxisStudio.Sdk;

            namespace Probe.Selfish;

            public abstract class OwnVoice : StudioContribution
            {
            }

            public sealed class Entry : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    context.GetService<IStudioContributions>()!.Collect<OwnVoice>();
            }
            """);

        var plugins = Start();

        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == "probe.selfish");
        Assert.Contains(_studio.Log.Records, record =>
            record.Level == StudioLogLevel.Warning &&
            record.Message.Contains("OwnVoice", StringComparison.Ordinal) &&
            record.Message.Contains("provides.contracts", StringComparison.Ordinal));
    }

    /// <summary>
    /// Чужой объект зовётся на счёт расширения, загрузившего его сборку, и отпускается с его выгрузкой.
    /// </summary>
    /// <remarks>
    /// Так модуль держит то, что ему вручили: цель, показанную плагином, поле, построенное его кодом.
    /// </remarks>
    [AvaloniaFact]
    public async Task Guard_blames_the_extension_that_loaded_the_objects_assembly()
    {
        TestAssembly.EmitPlugin(_root, "probe.giver", "Probe.Giver", source: """
            using ArxisStudio.Sdk;
            using ProbeContributions;

            namespace Probe.Giver;

            public sealed class Trick : Gift
            {
                public override void Open() => throw new System.InvalidOperationException("подарок с подвохом");
            }

            public sealed class Shop : IGiftShop
            {
                public Gift Take() => new Trick();
            }

            public sealed class Entry : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    context.GetService<IStudioExports>()!.Publish<IGiftShop>(new Shop());
            }
            """);

        var plugins = Start();
        var guarded = Take();
        var released = false;

        guarded.Released += (_, _) => released = true;

        Assert.Equal("probe.giver", guarded.OwnerId);
        Assert.False(guarded.Run("подарок", Open));
        Assert.Equal("probe.giver", Assert.Single(_failures).PluginId);

        await plugins.ReloadAsync("probe.giver");

        Assert.True(released, "ручка не сказала, что объект отпущен");
        Assert.True(guarded.IsReleased);
        Assert.False(guarded.Run("подарок", Open), "отпущенный объект позвали");
    }

    /// <summary>
    /// Построенное вкладом, упавшее на раскладке, заменяется строкой о сбое, и сбой засчитывается хозяину.
    /// </summary>
    [AvaloniaFact]
    public void A_hosted_view_failing_layout_is_stubbed_and_blamed_on_its_owner()
    {
        Plugin("probe.painter", "PainterVoice", """
            public override string Say() => "рисует";

            public override Avalonia.Controls.Control View() => new Falling();
            """, extra: """
            public sealed class Falling : Avalonia.Controls.Border
            {
                protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) =>
                    throw new System.InvalidOperationException("замер не удался");
            }
            """);

        Start();

        var painter = Assert.Single(Voices().Items, voice => voice.OwnerId == "probe.painter");

        Assert.True(painter.TryGet("вид", voice => (Control?)Call(voice, "View"), out var view));

        var hosted = Assert.IsType<PluginSurface>(painter.Host(view!));
        var window = new Window { Width = 300, Height = 100, Content = hosted };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(hosted.IsBroken, "упавший замер не заменён заглушкой");
        Assert.IsType<TextBlock>(hosted.Child);
        Assert.Contains(_failures, failure => failure.PluginId == "probe.painter" && failure.What.StartsWith("раскладка", StringComparison.Ordinal));

        window.Close();
    }

    /// <summary>
    /// Ушёл собиравший — его наборы закрываются: вклады в них прощаются, и его контекст не держат.
    /// </summary>
    [AvaloniaFact]
    public async Task The_sets_of_an_unloaded_collector_are_released()
    {
        TestAssembly.EmitPlugin(_root, "probe.collector", "Probe.Collector", source: """
            using ArxisStudio.Sdk;
            using ProbeContributions;

            namespace Probe.Collector;

            public sealed class Entry : StudioPlugin
            {
                private IStudioContributionSet<Voice>? _voices;

                public override void Activate(IStudioContext context)
                {
                    _voices = context.GetService<IStudioContributions>()!.Collect<Voice>();
                    _voices.Changed += (_, _) => { };
                }
            }
            """);

        var plugins = Start();

        await plugins.ReloadAsync("probe.collector");

        Assert.Contains("прощание ModuleVoice", Journal);
        Assert.Empty(plugins.AwaitingRestart);
    }

    private const string ContractSource = """
        using System.Collections.Generic;
        using ArxisStudio.Sdk;
        using Avalonia.Controls;

        namespace ProbeContributions;

        /// <summary>Вклад-проба.</summary>
        public abstract class Voice : StudioContribution
        {
            /// <summary>Журнал прощаний и событий: в контракте он переживает выгрузку вклада.</summary>
            public static readonly List<string> Journal = new();

            public abstract string Say();

            public virtual Control? View() => null;

            protected string Word(string key) => Context.Strings[key];

            public override void Release() => Journal.Add("прощание " + GetType().Name);
        }

        public abstract class Gift
        {
            public abstract void Open();
        }

        public interface IGiftShop
        {
            Gift Take();
        }
        """;

    private const string ModuleSource = """
        using ArxisStudio.Sdk;
        using ProbeContributions;

        namespace ProbeContributions.Module;

        public sealed class ProbeModule : StudioPlugin
        {
            public static bool CollectOnStart { get; set; }

            public static IStudioContext? Context { get; private set; }

            public static IStudioContributionSet<Voice>? Early { get; private set; }

            public override void Activate(IStudioContext context)
            {
                Context = context;
                Early = CollectOnStart ? context.GetService<IStudioContributions>()!.Collect<Voice>() : null;
            }

            public override void Deactivate()
            {
                Context = null;
                Early = null;
            }
        }

        public sealed class ModuleVoice : Voice
        {
            public override string Say() => "модуль";
        }
        """;

    /// <summary>Тип контракта по имени: тест компилируется раньше, чем контракт собран.</summary>
    private static Type Contract(string name) => Probe.Value.Contract.GetType("ProbeContributions." + name)!;

    /// <summary>Зовёт метод контракта у объекта — отражением, по той же причине.</summary>
    private static object? Call(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!.Invoke(target, null);

    private static void Open(object gift) => Call(gift, "Open");

    /// <summary>Открытое статическое свойство модуля-пробы.</summary>
    private static PropertyInfo? Hook(string name) =>
        Probe.Value.Module.GetType("ProbeContributions.Module.ProbeModule")!.GetProperty(name, BindingFlags.Public | BindingFlags.Static);

    /// <summary>Сбор вкладов глазами модуля-пробы.</summary>
    private static IStudioContributions Collector() =>
        ((IStudioContext)Hook("Context")!.GetValue(null)!).GetService<IStudioContributions>()!;

    /// <summary>
    /// Голоса, собранные модулем. Набор голосов виден набором вкладов: тип вклада в наборе ковариантен.
    /// </summary>
    /// <param name="collector">Кто собирает; null — модуль-проба, хозяин контракта.</param>
    private static IStudioContributionSet<StudioContribution> Voices(IStudioContributions? collector = null) =>
        (IStudioContributionSet<StudioContribution>)typeof(IStudioContributions)
            .GetMethod(nameof(IStudioContributions.Collect))!
            .MakeGenericMethod(Contract("Voice"))
            .Invoke(collector ?? Collector(), null)!;

    private static string? Say(IStudioGuarded<StudioContribution> voice) =>
        voice.TryGet("голос", item => (string?)Call(item, "Say"), out var said) ? said : null;

    /// <summary>Подписывается на уход голоса плагина probe.leaving — отдельным методом, чтобы ручка не осталась в кадре.</summary>
    private static void Leaving(IStudioContributionSet<StudioContribution> set) =>
        Assert.Single(set.Items, voice => voice.OwnerId == "probe.leaving").Released += (_, _) => Journal.Add("отпущен");

    /// <summary>Подарок плагина probe.giver за ручкой — отдельным методом, чтобы сам подарок не остался в кадре.</summary>
    private static IStudioGuarded<object> Take()
    {
        var context = (IStudioContext)Hook("Context")!.GetValue(null)!;
        var exports = context.GetService<IStudioExports>()!;
        var shop = typeof(IStudioExports).GetMethod(nameof(IStudioExports.Get))!
            .MakeGenericMethod(Contract("IGiftShop"))
            .Invoke(exports, null)!;

        return Collector().Guard(Contract("IGiftShop").GetMethod("Take")!.Invoke(shop, null)!);
    }

    /// <summary>Кладёт плагин с голосом папкой каталога.</summary>
    private string Plugin(string id, string voice, string body, string activation = "onStartup", string extra = "")
    {
        var name = "Probe." + string.Concat(id.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

        return TestAssembly.EmitPlugin(_root, id, name, activation: activation, source: $$"""
            using ArxisStudio.Sdk;
            using ProbeContributions;

            namespace {{name}};

            public sealed class Entry : StudioPlugin
            {
            }

            public sealed class {{voice}} : Voice
            {
                {{body}}
            }

            {{extra}}
            """);
    }

    /// <summary>Поднимает модуль-пробу и плагины папки.</summary>
    private StudioPlugins Start()
    {
        var plugins = _studio.Build(catalog: () => new PluginCatalog(_root).Scan(), modules: [Probe.Value.Module]);

        plugins.Start();
        Dispatcher.UIThread.RunJobs();

        return plugins;
    }
}
