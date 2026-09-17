using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.Welcome;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    /// <param name="keys">Страница клавиш — так окно открывает студия; null — как из Welcome.</param>
    /// <param name="page">На каком разделе открыть; null — на первом.</param>
    /// <returns>Хозяина, само окно и задачу, которая завершится с его закрытием.</returns>
    public (Window Owner, SettingsWindow Settings, Task Shown) Open(KeysPage? keys = null, string? page = null)
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
            catalog,
            page,
            keys);

        Dispatcher.UIThread.RunJobs();

        return (owner, Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>()), shown);
    }

    /// <summary>Открывает настройки так, как их открывает Welcome, — щелчком по двери в его полосе.</summary>
    /// <param name="door">Ключ подписи двери: <c>welcome.nav.settings</c> или <c>welcome.nav.plugins</c>.</param>
    /// <param name="keys">Чем Welcome спрашивает страницу клавиш; null — не спрашивает.</param>
    /// <returns>Экран Welcome и открытое им окно настроек.</returns>
    public (WelcomeWindow Welcome, SettingsWindow Settings) OpenFromWelcome(string door, Func<KeysPage>? keys)
    {
        var catalog = new PluginCatalog(Path.Combine(_home, "plugins"));
        var welcome = new WelcomeWindow(
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            new RecentProjects(Path.Combine(_home, "recent-projects.json")),
            catalog,
            Extensions(catalog))
        {
            Keys = keys,
        };

        welcome.Show();
        Dispatcher.UIThread.RunJobs();

        var button = welcome.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Single(candidate => AutomationProperties.GetName(candidate) == Localizer.Instance[door]);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return (welcome, Assert.Single(welcome.OwnedWindows.OfType<SettingsWindow>()));
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
    /// <remarks>Папка — настоящая папка терминала: из неё берутся подписи настроек.</remarks>
    private static InstalledPlugin Module() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
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
