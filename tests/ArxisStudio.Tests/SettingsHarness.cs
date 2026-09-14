using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Settings;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно настроек, открытое так, как его открывает студия, — над своей временной папкой.
/// </summary>
/// <remarks>
/// Служба расширений собрана, но не поднята, и файлы настроек у неё свои: окно, открытое в тесте,
/// не читает и не пишет настоящую папку данных. Среди расширений — один модуль с одной настройкой,
/// чтобы у окна была и страница расширения, и несохранённое, которое можно набрать.
/// </remarks>
internal sealed class SettingsHarness : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"arxis-settings-{Guid.NewGuid():N}");
    private readonly PluginGuard _guard = new();
    private readonly PluginContributionRegistry _contributions = new();

    public SettingsHarness() => Directory.CreateDirectory(Path.Combine(_home, "plugins"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    /// <summary>Открывает настройки модальным окном поверх своего хозяина.</summary>
    /// <returns>Хозяина, само окно и задачу, которая завершится с его закрытием.</returns>
    public (Window Owner, SettingsWindow Settings, Task Shown) Open()
    {
        var owner = new Window { Width = 400, Height = 300 };

        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var catalog = new PluginCatalog(Path.Combine(_home, "plugins"));
        var shown = SettingsWindow.ShowAsync(
            owner,
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            Extensions(catalog),
            [Module()],
            catalog);

        Dispatcher.UIThread.RunJobs();

        return (owner, Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>()), shown);
    }

    private StudioPlugins Extensions(PluginCatalog catalog)
    {
        var dock = new StudioDock(new DockView());

        return new StudioPlugins(new StudioLog(), _guard, new StudioTaskRegistry(), _contributions)
        {
            Commands = new StudioCommands(_guard),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Silence()),
            Services = new Dictionary<Type, object>(),
            Catalog = catalog.Scan,
            Assemblies = [],
            Settings = new PluginSettingsStore(userFile: Path.Combine(_home, "plugin-settings.json")),
        };
    }

    /// <summary>Модуль, объявивший одну настройку.</summary>
    private static InstalledPlugin Module() => new(
        AppContext.BaseDirectory,
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13) },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Строка состояния, которая молчит.</summary>
    private sealed class Silence : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }
}
