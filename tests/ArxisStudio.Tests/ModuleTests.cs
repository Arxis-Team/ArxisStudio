using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Designer;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Встроенные модули: подъём тем же контрактом, что и у плагинов, и манифест из
/// сборки.
/// </summary>
/// <remarks>
/// Оболочка ничего не знает о панелях: она читает манифест модуля и ищет по нему
/// классы панелей в сборке. Разойтись эти две записи могут молча — панель
/// переименовали в коде, а в манифесте забыли, — и человек увидит пустую вкладку.
/// Здесь они сверяются на каждой сборке.
/// </remarks>
public class ModuleTests
{
    private static readonly Assembly[] BuiltIn =
    [
        typeof(Modules.Project.ProjectModule).Assembly,
        typeof(Modules.Console.ConsoleModule).Assembly,
        typeof(DesignerModule).Assembly,
    ];

    /// <summary>Сборки встроенных модулей — те же, что поднимает студия.</summary>
    public static TheoryData<Assembly> Modules => [.. BuiltIn];

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

    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_module_carries_a_manifest_with_an_entry_point(Assembly assembly)
    {
        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest!.Id);
        Assert.NotEmpty(manifest.Name);

        Assert.Contains(assembly.GetTypes(), type =>
            type is { IsAbstract: false, IsPublic: true } && typeof(StudioPlugin).IsAssignableFrom(type));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_declared_panel_has_a_class_behind_it(Assembly assembly)
    {
        var (manifest, _) = ModuleManifest.Load(assembly);

        Assert.NotNull(manifest);

        var declared = manifest!.Contributions.ToolWindows;
        var built = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(Sdk.ToolWindow).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<ToolWindowAttribute>()?.Id)
            .Where(id => id is not null)
            .ToList();

        Assert.NotEmpty(declared);

        foreach (var panel in declared)
        {
            Assert.Contains(panel.Id, built);
            Assert.Contains(panel.Zone, new[] { "left", "right", "bottom" });
        }

        // Обратная сторона: панель, о которой манифест молчит, никуда не встанет.
        foreach (var id in built)
            Assert.Contains(declared, panel => panel.Id == id);
    }

    [Fact]
    public void Exactly_one_panel_claims_the_output_role()
    {
        // Вывод сборки и запуска студия показывает, не зная, кто его показывает,
        // — по роли. Двое с одной ролью означали бы, что выбор случаен.
        var roles = BuiltIn
            .Select(assembly => ModuleManifest.Load(assembly).Manifest)
            .Where(manifest => manifest is not null)
            .SelectMany(manifest => manifest!.Contributions.ToolWindows)
            .Where(panel => panel.Role == "output")
            .ToList();

        Assert.Single(roles);
    }
}
