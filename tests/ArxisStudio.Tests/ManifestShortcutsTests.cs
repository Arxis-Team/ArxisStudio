using ArxisStudio.Docking;
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
/// Сочетания, объявленные манифестом.
/// </summary>
/// <remarks>
/// Читаются они без загрузки сборки — там же, где читаются кнопки полосы и
/// пункты меню, — и нажатие будит спящего хозяина той же дорогой, что и щелчок
/// по кнопке. Это и есть причина объявлять жест в манифесте, а не в коде:
/// объявленный в коде потребовал бы поднять плагин, чтобы узнать о клавише, то
/// есть поднять при старте всё установленное.
/// <para>
/// Очередь общая: подписи берутся из словарей, а <c>Localizer</c> один на
/// процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ManifestShortcutsTests : IDisposable
{
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly PluginContributionRegistry _contributions = new();

    private StudioPlugins? _plugins;

    public void Dispose()
    {
        _plugins?.Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>Объявленное манифестом сочетание достаётся команде.</summary>
    [AvaloniaFact]
    public void A_gesture_declared_in_the_manifest_goes_to_its_command()
    {
        var keys = Raised(out _);

        var bound = Assert.Single(keys.All);

        Assert.Equal("probe.say", bound.CommandId);
        Assert.Equal("Ctrl+Alt+R", bound.Gesture.ToString());
    }

    /// <summary>
    /// Занятое сочетание манифесту не достаётся, и отказ не молчит.
    /// </summary>
    /// <remarks>
    /// Сочетания студии раздаются раньше, чем читаются манифесты, и это не
    /// случайность порядка: своё старше принесённого, иначе установка плагина
    /// молча отнимала бы у человека Ctrl+W.
    /// </remarks>
    [AvaloniaFact]
    public void A_taken_gesture_does_not_go_to_the_manifest_and_the_refusal_is_told()
    {
        var keys = Raised(out var log, taken: "Ctrl+Alt+R");

        Assert.Equal("studio.close", Assert.Single(keys.All).CommandId);

        var refused = Assert.Single(keys.Refused);

        Assert.Equal("probe.say", refused.CommandId);
        Assert.Equal("studio.close", refused.Winner);

        Assert.Contains(log.Records, record =>
            record.Level == StudioLogLevel.Warning && record.Message.Contains("studio.close"));
    }

    /// <summary>
    /// Неразобранное сочетание не роняет подъём и остаётся записью.
    /// </summary>
    /// <remarks>
    /// Строку пишет человек, и опечатка в ней не повод не поднять плагин — то
    /// же правило, что у <c>sdk.min</c>.
    /// </remarks>
    [AvaloniaFact]
    public void An_unreadable_gesture_is_a_record_and_not_a_refusal_to_rise()
    {
        var keys = Raised(out var log, gesture: "Ctrl+Шифт");

        Assert.Empty(keys.All);
        Assert.Empty(keys.Refused);

        Assert.Contains(log.Records, record =>
            record.Level == StudioLogLevel.Warning && record.Message.Contains("не разобралось"));
    }

    /// <summary>Поднимает модуль, объявивший команду с сочетанием.</summary>
    /// <param name="log">Журнал, в который писали.</param>
    /// <param name="gesture">Что объявлено манифестом.</param>
    /// <param name="taken">Сочетание, занятое студией до чтения манифестов.</param>
    private StudioShortcuts Raised(out StudioLog log, string gesture = "Ctrl+Alt+R", string? taken = null)
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        new Window { Width = 900, Height = 600, Content = view }.Show();
        dock.Shown();

        var keys = new StudioShortcuts(_ => true);

        if (taken is not null)
            keys.Bind(taken, "studio.close");

        _plugins = new StudioPlugins(_log, _guard, new StudioTaskRegistry(), _contributions)
        {
            Assemblies = [TestAssembly.EmitModule("Probe.Keys", Source, Manifest(gesture))],
            Commands = new StudioCommands(),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Quiet()),
            Shortcuts = keys,
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },
            Catalog = () => [],
        };

        _plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        log = _log;

        return keys;
    }

    /// <summary>Статус, которому некому докладывать.</summary>
    private sealed class Quiet : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }

    private const string Source = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class KeysModule : StudioPlugin
        {
            public override void Activate(IStudioContext context) =>
                context.Commands.Register("probe.say", () => { });
        }
        """;

    private static string Manifest(string gesture) => $$"""
        {
          "id": "arxis.keys",
          "name": "Клавиши",
          "version": "1.0.0",
          "contributions": {
            "commands": [ { "id": "probe.say", "title": "Сказать", "key": "{{gesture}}" } ]
          },
          "activation": [ "onStartup" ]
        }
        """;
}
