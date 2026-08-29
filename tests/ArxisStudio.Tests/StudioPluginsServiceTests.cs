using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба соседей: кто поднят и какой версии — глазами одного плагина.
/// </summary>
/// <remarks>
/// Нужна необязательным зависимостям: прежде чем показать кнопку интеграции с
/// соседом, плагин спрашивает, есть ли сосед на самом деле.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioPluginsServiceTests
{
    /// <summary>Поднятый сосед — активен, ждущий и чужой — нет.</summary>
    [Fact]
    public void IsActive_answers_true_only_for_a_loaded_neighbour()
    {
        var roster = new StudioPluginRoster();

        using var host = Host(roster, out var installed);

        Assert.True(host.LoadBuiltIn(typeof(Modules.Sample.SampleModule).Assembly).IsLoaded);

        var neighbours = new PluginNeighbours(roster, Asking());

        Assert.True(neighbours.IsActive("arxis.sample"));
        Assert.False(neighbours.IsActive("arxis.nowhere"));
        _ = installed;
    }

    /// <summary>
    /// Сосед старее объявленной границы — отсутствует, хоть и поднят.
    /// </summary>
    /// <remarks>
    /// Спрашивающий написан под возможности, которых в старой версии нет:
    /// «да» означало бы падение на первом обращении. Проверка версий живёт в
    /// службе, одна на всех, — авторам не приходится сравнивать номера руками.
    /// </remarks>
    [Fact]
    public void A_stale_optional_neighbour_is_absent_for_IsActive()
    {
        var roster = new StudioPluginRoster();

        using var host = Host(roster, out var installed);

        Assert.True(host.LoadBuiltIn(typeof(Modules.Sample.SampleModule).Assembly).IsLoaded);

        installed.Add(host.Loaded.Single().Installed);

        var demanding = Asking(dependsOn: "arxis.sample", min: "9.0");
        var modest = Asking(dependsOn: "arxis.sample", min: "1.0");

        Assert.False(new PluginNeighbours(roster, demanding).IsActive("arxis.sample"));
        Assert.True(new PluginNeighbours(roster, modest).IsActive("arxis.sample"));
    }

    /// <summary>
    /// Версия отвечается по манифесту установленного, без загрузки.
    /// </summary>
    /// <remarks>
    /// Версия есть и у соседа, который ждёт своего события, и у языкового
    /// пакета, которому подниматься нечем.
    /// </remarks>
    [Fact]
    public void Version_answers_from_the_manifest_without_loading()
    {
        var roster = new StudioPluginRoster();

        using var host = Host(roster, out var installed);

        installed.Add(new InstalledPlugin(
            Path.Combine(Path.GetTempPath(), "arxis.lang-de"),
            new PluginManifest { Id = "arxis.lang-de", Name = "Deutsch", Version = "1.4.0" },
            null,
            IsEnabled: true));

        var neighbours = new PluginNeighbours(roster, Asking());

        Assert.Equal("1.4.0", neighbours.Version("arxis.lang-de"));
        Assert.Null(neighbours.Version("arxis.nowhere"));
        Assert.False(neighbours.IsActive("arxis.lang-de"));
    }

    /// <summary>Подъём и уход соседа объявляются событием.</summary>
    [Fact]
    public void Changed_is_raised_when_a_plugin_rises_or_goes()
    {
        var roster = new StudioPluginRoster();

        using var host = Host(roster, out _);

        var changes = 0;
        var neighbours = new PluginNeighbours(roster, Asking());

        neighbours.Changed += (_, _) => changes++;

        Assert.True(host.LoadBuiltIn(typeof(Modules.Sample.SampleModule).Assembly).IsLoaded);

        Assert.Equal(1, changes);
    }

    /// <summary>
    /// Без ядра службы в контексте просто нет.
    /// </summary>
    /// <remarks>
    /// Фабрика без ростера — обычное дело в тестах и у встраивающих: служба
    /// честно отсутствует, а не притворяется пустой.
    /// </remarks>
    [Fact]
    public void Without_a_roster_the_service_is_simply_absent()
    {
        var factory = new StudioContextFactory(new StudioLog(), new StudioCommands(), null);
        var context = factory.Create(Asking());

        Assert.Null(context.GetService<IStudioPlugins>());
    }

    /// <summary>С ядром служба в контексте есть.</summary>
    [Fact]
    public void With_a_roster_the_service_is_granted()
    {
        var roster = new StudioPluginRoster();
        var factory = new StudioContextFactory(
            new StudioLog(), new StudioCommands(), null, plugins: roster);

        var context = factory.Create(Asking());

        Assert.NotNull(context.GetService<IStudioPlugins>());
    }

    private static PluginHost Host(StudioPluginRoster roster, out List<InstalledPlugin> installed)
    {
        var list = new List<InstalledPlugin>();
        var host = new PluginHost(new StudioContextFactory(
            new StudioLog(), new StudioCommands(), null, plugins: roster));

        roster.Attach(host, () => list);
        installed = list;
        return host;
    }

    private static InstalledPlugin Asking(string? dependsOn = null, string? min = null)
    {
        var manifest = new PluginManifest { Id = "arxis.asking", Name = "Спрашивающий" };

        if (dependsOn is not null)
            manifest.Dependencies.Add(new PluginDependency { Id = dependsOn, Min = min, Optional = true });

        return new InstalledPlugin(
            Path.Combine(Path.GetTempPath(), "arxis.asking"), manifest, null, IsEnabled: true);
    }
}
