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
        var described = StudioModules.Describe();

        // Каждая заявленная сборка описана ровно одной записью. Модуль с
        // забытым манифестом выпал бы отсюда молча: студия показывала бы его
        // панель, а менеджер плагинов считал бы его отсутствующим — и
        // зависимость на него выглядела бы невыполненной.
        Assert.Equal(StudioModules.Assemblies.Count, described.Count);
        Assert.All(described, module => Assert.True(module.IsBuiltIn, module.Id));
        Assert.All(described, module => Assert.True(module.IsEnabled, module.Id));

        var ids = described.Select(module => module.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("arxis.sample", ids);
        Assert.Contains("arxis.terminal", ids);
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
