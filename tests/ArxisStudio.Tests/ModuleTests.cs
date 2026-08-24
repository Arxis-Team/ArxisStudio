using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Designer;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Встроенные модули: подъём тем же контрактом, что и у плагинов, и манифест из
/// сборки.
/// </summary>
public class ModuleTests
{
    [Fact]
    public void The_designer_manifest_is_read_from_the_assembly()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(DesignerModule).Assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal("arxis.designer", manifest!.Id);
        Assert.Equal(3, manifest.Contributions.ToolWindows.Count);
        Assert.Contains(manifest.Contributions.FileTypes, type => type.Ext == ".axaml");
    }

    [Fact]
    public void The_designer_module_raises_and_registers_its_editor()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));
        var registry = new PluginContributionRegistry();

        var loaded = host.LoadBuiltIn(typeof(DesignerModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.NotEmpty(loaded.Entries);

        registry.Add(loaded);

        // Редактор модуля берётся за .axaml и не берётся за остальное.
        Assert.NotNull(registry.EditorFor(Path.Combine(Path.GetTempPath(), "MainWindow.axaml")));
        Assert.Null(registry.EditorFor(Path.Combine(Path.GetTempPath(), "Program.cs")));
    }

    /// <summary>
    /// Сборка без манифеста — ошибка записи, а не падение студии.
    /// </summary>
    [Fact]
    public void An_assembly_without_a_manifest_reports_why()
    {
        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(typeof(ModuleTests).Assembly);

        Assert.False(loaded.IsLoaded);
        Assert.Contains("module.json", loaded.Error);
    }
}
