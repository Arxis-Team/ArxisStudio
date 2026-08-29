using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Описание встроенных модулей для менеджера плагинов.
/// </summary>
/// <remarks>
/// Модуль — годная цель зависимости, и карточка обязана считать его
/// присутствующим: иначе менеджер показывал бы «не установлен» там, где
/// студия говорит «есть».
/// </remarks>
public class StudioModulesTests
{
    /// <summary>Модули описываются по манифестам, не поднимаясь.</summary>
    [Fact]
    public void Modules_are_described_from_their_manifests()
    {
        var module = Assert.Single(StudioModules.Describe());

        Assert.Equal("arxis.sample", module.Id);
        Assert.True(module.IsBuiltIn);
        Assert.True(module.IsEnabled);
    }

    /// <summary>На карточке зависимость от модуля выглядит присутствующей.</summary>
    [Fact]
    public void A_module_target_shows_as_present_on_the_card()
    {
        var manifest = new PluginManifest { Id = "arxis.probe", Name = "Проба" };

        manifest.Dependencies.Add(new PluginDependency { Id = "arxis.sample" });

        var plugin = new InstalledPlugin(
            Path.Combine(Path.GetTempPath(), "arxis.probe"), manifest, null, IsEnabled: true);

        var all = new[] { plugin }.Concat(StudioModules.Describe()).ToList();
        var state = Assert.Single(PluginGraph.Describe(plugin, all));

        Assert.Equal(PluginDependencyHealth.Present, state.Health);
        Assert.False(state.IsProblem);
    }
}
