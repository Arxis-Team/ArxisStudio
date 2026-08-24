using ArxisStudio.Extensibility;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каталог плагинов: чтение манифестов, состояние «включён», установка папкой.
/// </summary>
public class PluginCatalogTests : IDisposable
{
    private const string Manifest =
        """
        {
          "id": "arxis.figma-import",
          "name": "Figma Import",
          "version": "2.4.0",
          "publisher": "Arxis Labs",
          "description": "Импорт макетов Figma в дизайнер форм",
          "entry": "bin/Arxis.FigmaImport.dll",
          "contributions": {
            "commands": [ { "id": "figma.import", "title": "Импорт из Figma" } ],
            "toolWindows": [ { "id": "figma.panel", "title": "Figma", "zone": "right" } ]
          },
          "activation": [ "onCommand:figma.import" ]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-plugins-{Guid.NewGuid():N}");

    public PluginCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Reads_the_manifest_of_an_installed_plugin()
    {
        Install("arxis.figma-import", Manifest);

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.Equal("arxis.figma-import", plugin.Id);
        Assert.Equal("Figma Import", plugin.DisplayName);
        Assert.Equal("2.4.0", plugin.Manifest!.Version);
        Assert.True(plugin.IsEnabled);
    }

    [Fact]
    public void Reads_contributions_without_loading_the_assembly()
    {
        Install("arxis.figma-import", Manifest);

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());
        var contributions = plugin.Manifest!.Contributions;

        Assert.Equal("figma.import", Assert.Single(contributions.Commands).Id);
        Assert.Equal("right", Assert.Single(contributions.ToolWindows).Zone);
        Assert.Equal("onCommand:figma.import", Assert.Single(plugin.Manifest.Activation));
    }

    [Fact]
    public void A_broken_manifest_is_listed_with_its_error_rather_than_hidden()
    {
        Install("broken.plugin", "{ not json at all");

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.False(plugin.IsValid);
        Assert.NotNull(plugin.Error);
        Assert.Equal("broken.plugin", plugin.Id);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_not_a_plugin()
    {
        Directory.CreateDirectory(Path.Combine(_root, "just-a-folder"));

        Assert.Empty(new PluginCatalog(_root).Scan());
    }

    [Fact]
    public void Disabling_a_plugin_survives_a_restart()
    {
        Install("arxis.figma-import", Manifest);

        new PluginCatalog(_root).SetEnabled("arxis.figma-import", false);

        Assert.False(Assert.Single(new PluginCatalog(_root).Scan()).IsEnabled);
    }

    [Fact]
    public void Installing_copies_the_folder_into_the_catalog()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.WriteAllText(Path.Combine(source, "plugin.json"), Manifest);
        File.WriteAllText(Path.Combine(source, "bin", "Arxis.FigmaImport.dll"), "not really a dll");

        try
        {
            var (plugin, error) = new PluginCatalog(_root).InstallFromDirectory(source);

            Assert.Null(error);
            Assert.NotNull(plugin);
            Assert.True(File.Exists(Path.Combine(_root, "arxis.figma-import", "bin", "Arxis.FigmaImport.dll")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Installing_the_same_plugin_twice_is_refused()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "plugin.json"), Manifest);

        try
        {
            var catalog = new PluginCatalog(_root);
            catalog.InstallFromDirectory(source);

            var (plugin, error) = catalog.InstallFromDirectory(source);

            Assert.Null(plugin);
            Assert.Contains("arxis.figma-import", error);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Installing_from_a_folder_without_a_manifest_is_refused()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);

        try
        {
            var (plugin, error) = new PluginCatalog(_root).InstallFromDirectory(source);

            Assert.Null(plugin);
            Assert.Contains("plugin.json", error);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private void Install(string id, string manifest)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.json"), manifest);
    }
}
