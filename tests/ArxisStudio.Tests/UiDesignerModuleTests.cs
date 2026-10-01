using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Манифест дизайнера интерфейса и то, что студия по нему строит.
/// </summary>
/// <remarks>
/// Студия читает манифест, не заглядывая в сборку: панель, объявленная под одним именем, а помеченная
/// другим, просто не появится, а необъявленная настройка не запишется.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class UiDesignerModuleTests
{
    /// <summary>Объявленная панель есть в сборке, команда и настройка в коде — те же, что в манифесте.</summary>
    [Fact]
    public void The_manifest_and_the_code_name_the_same_panel_command_and_setting()
    {
        var manifest = Manifest();

        var built = typeof(UiDesignerModule).Assembly
            .GetTypes()
            .Select(type => type.GetCustomAttributes(typeof(ToolWindowAttribute), false).FirstOrDefault())
            .OfType<ToolWindowAttribute>()
            .Select(attribute => attribute.Id);

        Assert.Equal([UiDesignerModule.PanelId], built);
        Assert.Equal([UiDesignerModule.PanelId], manifest.Contributions.ToolWindows.Select(panel => panel.Id));
        Assert.Equal([UiDesignerModule.ShowCommand], manifest.Contributions.Commands.Select(command => command.Id));

        var grid = Assert.Single(manifest.Contributions.Settings);

        Assert.Equal(UiDesignerModule.GridKey, grid.Key);
        Assert.True(grid.IsBool, "сетка объявлена не переключателем");
        Assert.True(grid.Default is JsonElement { ValueKind: JsonValueKind.True }, "сетка по умолчанию выключена, а холст её рисует");
    }

    /// <summary>
    /// Доска встаёт в область документов: холст — рабочее место, как Scene в Unity, а не боковая панель.
    /// </summary>
    [Fact]
    public void The_board_asks_for_the_document_area()
    {
        var panel = Assert.Single(Manifest().Contributions.ToolWindows);

        Assert.Equal("center", panel.Wanted.Side);
        Assert.False(string.IsNullOrWhiteSpace(panel.Title), "у доски нет заголовка");
    }

    /// <summary>Команда показа выводит доску вперёд и отдаёт ей клавиатуру; модуль — в списке студии.</summary>
    [Fact]
    public void The_show_command_brings_the_board_forward_with_the_keyboard()
    {
        var commands = new StudioCommands();
        var windows = new WindowsProbe();
        var services = new Dictionary<Type, object>
        {
            [typeof(IStudioToolWindows)] = windows,
            [typeof(IStudioFocus)] = windows,
        };

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null, services));

        var loaded = host.LoadBuiltIn(typeof(UiDesignerModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Equal("arxis.ui-designer", loaded.Installed.Id);
        Assert.Contains(typeof(UiDesignerModule).Assembly, StudioModules.Assemblies);
        Assert.True(commands.Invoke(UiDesignerModule.ShowCommand));
        Assert.Equal(["show ui-designer.board", "focus ui-designer.board"], windows.Asked);
    }

    private static PluginManifest Manifest()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(UiDesignerModule).Assembly);

        Assert.Null(error);

        return manifest!;
    }

    private sealed class WindowsProbe : IStudioToolWindows, IStudioFocus
    {
        public List<string> Asked { get; } = [];

        public void Show(string toolWindowId) => Asked.Add($"show {toolWindowId}");

        public bool Focus(string toolWindowId)
        {
            Asked.Add($"focus {toolWindowId}");

            return true;
        }

        public bool IsFocused(string toolWindowId) => false;
    }
}
