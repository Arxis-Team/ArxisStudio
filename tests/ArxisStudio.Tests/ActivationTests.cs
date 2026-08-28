using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// События активации и меню, собранное из манифестов: и то и другое студия
/// делает, не загружая сборки плагинов.
/// </summary>
public class ActivationTests
{
    [Fact]
    public void A_plugin_without_events_is_raised_at_startup()
    {
        Assert.True(PluginActivation.IsEager(Manifest()));
        Assert.True(PluginActivation.IsEager(Manifest("onStartup")));
    }

    /// <summary>
    /// Показать панель, не подняв плагин, нечем — такой поднимается сразу.
    /// </summary>
    [Fact]
    public void A_plugin_with_a_panel_is_raised_at_startup()
    {
        Assert.True(PluginActivation.IsEager(Manifest("onToolWindow:hello.panel")));
    }

    [Fact]
    public void A_plugin_that_only_waits_for_a_command_is_not_raised_at_startup()
    {
        var manifest = Manifest("onCommand:hello.greet");

        Assert.False(PluginActivation.IsEager(manifest));
        Assert.True(PluginActivation.WaitsForCommand(manifest, "hello.greet"));
        Assert.False(PluginActivation.WaitsForCommand(manifest, "hello.other"));
    }

    [Fact]
    public void A_file_type_is_matched_regardless_of_case()
    {
        var manifest = Manifest("onFileType:.fig");

        Assert.True(PluginActivation.WaitsForFileType(manifest, ".FIG"));
        Assert.False(PluginActivation.WaitsForFileType(manifest, ".axaml"));
        Assert.False(PluginActivation.WaitsForFileType(manifest, ""));
    }

    /// <summary>
    /// Два плагина, добавивших «Tools/…», должны оказаться в одном «Tools», а
    /// не в двух одинаковых ветках рядом.
    /// </summary>
    [Fact]
    public void Plugins_share_the_branch_they_both_named()
    {
        var first = Installed("first", ("Инструменты/Первый", "first.run"));
        var second = Installed("second", ("Инструменты/Второй", "second.run"));

        var menu = StudioMenu.Build([first, second]);

        var tools = Assert.Single(menu);

        Assert.Equal("Инструменты", tools.Title);
        Assert.False(tools.IsCommand);
        Assert.Equal(["Первый", "Второй"], tools.Children.Select(item => item.Title));
        Assert.Equal("first.run", tools.Children[0].CommandId);
        Assert.Equal("first", tools.Children[0].PluginId);
    }

    [Fact]
    public void A_deep_path_becomes_nested_items()
    {
        var menu = StudioMenu.Build([Installed("figma", ("Tools/Figma/Импорт…", "figma.import"))]);

        var tools = Assert.Single(menu);
        var figma = Assert.Single(tools.Children);
        var command = Assert.Single(figma.Children);

        Assert.Equal("Figma", figma.Title);
        Assert.Equal("Импорт…", command.Title);
        Assert.True(command.IsCommand);
    }

    [Fact]
    public void A_disabled_plugin_adds_nothing_to_the_menu()
    {
        var plugin = Installed("off", ("Tools/Ничего", "off.run")) with { IsEnabled = false };

        Assert.Empty(StudioMenu.Build([plugin]));
    }

    /// <summary>
    /// Пункт встроенного модуля попадает в меню наравне с плагинами.
    /// </summary>
    /// <remarks>
    /// Модуль отличается от плагина способом доставки, а не правами. Своей
    /// панели у модуля может и не быть, а палитры команд в студии нет —
    /// меню для него единственная дорога к человеку.
    /// </remarks>
    [Fact]
    public void A_built_in_module_adds_its_item_too()
    {
        var module = BuiltIn("arxis.sample", ("Инструменты/О модуле", "sample.about"));

        var tools = Assert.Single(StudioMenu.Build([module]));
        var item = Assert.Single(tools.Children);

        Assert.Equal("О модуле", item.Title);
        Assert.Equal("sample.about", item.CommandId);
    }

    /// <summary>Ветка модуля и ветка плагина сходятся в одну.</summary>
    [Fact]
    public void A_module_and_a_plugin_share_the_branch_they_both_named()
    {
        var module = BuiltIn("arxis.sample", ("Инструменты/О модуле", "sample.about"));
        var plugin = Installed("figma", ("Инструменты/Импорт…", "figma.import"));

        var tools = Assert.Single(StudioMenu.Build([module, plugin]));

        Assert.Equal(["О модуле", "Импорт…"], tools.Children.Select(item => item.Title));
    }

    /// <summary>
    /// Модули идут первыми, в каком бы порядке их ни подали.
    /// </summary>
    /// <remarks>
    /// Своё выше принесённого: иначе порядок пунктов зависел бы от того, что
    /// человек успел установить, и знакомое меню перестраивалось бы после
    /// каждой установки.
    /// </remarks>
    [Fact]
    public void Modules_come_before_plugins()
    {
        var plugin = Installed("figma", ("Плагин/Импорт…", "figma.import"));
        var module = BuiltIn("arxis.sample", ("Модуль/О модуле", "sample.about"));

        var menu = StudioMenu.Build([plugin, module]);

        Assert.Equal(["Модуль", "Плагин"], menu.Select(branch => branch.Title));
    }

    private static PluginManifest Manifest(params string[] activation) =>
        new() { Id = "arxis.sample", Name = "Пример", Activation = activation };

    private static InstalledPlugin Installed(string id, params (string Path, string Command)[] menus)
    {
        var manifest = new PluginManifest { Id = id, Name = id };

        foreach (var (path, command) in menus)
            manifest.Contributions.Menus.Add(new PluginMenuItem(path, command));

        return new InstalledPlugin(Path.Combine(Path.GetTempPath(), id), manifest, null, IsEnabled: true);
    }

    /// <summary>Запись о встроенном модуле: своей папки у него нет.</summary>
    private static InstalledPlugin BuiltIn(string id, params (string Path, string Command)[] menus) =>
        Installed(id, menus) with { IsBuiltIn = true };
}
