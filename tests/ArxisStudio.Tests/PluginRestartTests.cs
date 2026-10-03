using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Просьба плагина о перезапуске (SDK 7.14): служба своя у каждого, и просьба пишется на того, кто
/// попросил.
/// </summary>
/// <remarks>
/// Студия не перезапускается сама — она ставит расширение в ждущие перезапуска и при новом поводе
/// спрашивает человека. Очередь общая: поднимает модуль хост студии, а контексты загрузки — на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginRestartTests : IDisposable
{
    private readonly StudioPluginsHarness _studio = new();

    public void Dispose()
    {
        _studio.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Модуль просит перезапуск — и встаёт в ждущие со своей причиной, а журнал называет его.
    /// </summary>
    [AvaloniaFact]
    public void A_plugin_asks_for_a_restart_under_its_own_name()
    {
        var heard = new List<string>();
        var plugins = _studio.Build(modules: [TestAssembly.EmitModule("Probe.Restart", Source, Manifest)]);

        plugins.RestartRequired += (_, id) => heard.Add(id);
        plugins.LoadModules();

        Assert.Equal("пакет Avalonia проекта сменил версию", plugins.AwaitingRestart["probe.restart"]);
        Assert.Equal(["probe.restart"], heard);
        Assert.Contains(_studio.Log.Records, record => record.Message.Contains("просит перезапуск", StringComparison.Ordinal));
    }

    private const string Source = """
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class RestartModule : StudioPlugin
        {
            public override void Activate(IStudioContext context) =>
                context.GetService<IStudioRestart>()?.Require("пакет Avalonia проекта сменил версию");
        }
        """;

    private const string Manifest = """
        {
          "id": "probe.restart",
          "name": "Проба перезапуска",
          "version": "1.0.0",
          "activation": [ "onStartup" ]
        }
        """;
}
