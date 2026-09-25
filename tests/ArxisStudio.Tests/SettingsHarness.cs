using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
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
/// <para>
/// Помощники чтения окна — статические: наборы зовут их через <c>using static</c>, как диалоги окна
/// проекта.
/// </para>
/// </remarks>
internal sealed class SettingsHarness : IDisposable
{
    /// <summary>Сколько ждут закрытия окна: дольше — значит, оно не закроется вовсе.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _home = TempFolder.Reserve("settings");
    private readonly StudioPluginsHarness _studio = new();
    private StudioPlugins? _extensions;

    public SettingsHarness() => Directory.CreateDirectory(Path.Combine(_home, "plugins"));

    /// <summary>
    /// Служба расширений харнесса — одна на все окна, которые он открывает.
    /// </summary>
    /// <remarks>
    /// Одна, как у студии: перезапуск, заведённый тестом над ней, слышит те же поводы, что видит
    /// менеджер в окне. Собрана, но не поднята.
    /// </remarks>
    public StudioPlugins Plugins =>
        _extensions ??= _studio.Build(catalog: new PluginCatalog(Path.Combine(_home, "plugins")).Scan);

    public void Dispose()
    {
        _studio.Dispose();

        TempFolder.Erase(_home, strict: true);
    }

    /// <summary>Открывает настройки модальным окном поверх своего хозяина.</summary>
    /// <param name="keys">Страница клавиш — так окно открывает студия; null — как из Welcome.</param>
    /// <param name="page">На каком разделе открыть; null — на первом.</param>
    /// <param name="declaring">Кто объявляет настройки; null — один модуль харнесса с одной настройкой.</param>
    /// <param name="restart">Перезапуск, который окно предлагает; null — окно без него.</param>
    /// <param name="restore">С чем окно застал перезапуск; null — открыть как обычно.</param>
    /// <returns>Хозяина, само окно и задачу, которая завершится с его закрытием.</returns>
    public (Window Owner, SettingsWindow Settings, Task Shown) Open(
        KeysPage? keys = null,
        string? page = null,
        IReadOnlyList<InstalledPlugin>? declaring = null,
        StudioRestart? restart = null,
        SettingsSession? restore = null)
    {
        var owner = new Window { Width = 400, Height = 300 };

        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var catalog = new PluginCatalog(Path.Combine(_home, "plugins"));
        var shown = SettingsWindow.ShowAsync(
            owner,
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            Plugins,
            declaring ?? [Module()],
            catalog,
            page,
            keys,
            restart,
            restore);

        Dispatcher.UIThread.RunJobs();

        return (owner, Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>()), shown);
    }

    /// <summary>Открывает настройки так, как их открывает Welcome, — щелчком по двери в его полосе.</summary>
    /// <param name="door">Ключ подписи двери: <c>welcome.nav.settings</c> или <c>welcome.nav.plugins</c>.</param>
    /// <param name="keys">Чем Welcome спрашивает страницу клавиш; null — не спрашивает.</param>
    /// <param name="restart">Перезапуск, который Welcome предлагает; null — без него.</param>
    /// <returns>Экран Welcome и открытое им окно настроек.</returns>
    public (WelcomeWindow Welcome, SettingsWindow Settings) OpenFromWelcome(
        string door, Func<KeysPage>? keys, StudioRestart? restart = null)
    {
        var welcome = Welcome(keys, restart);

        welcome.Show();
        Dispatcher.UIThread.RunJobs();

        var button = welcome.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Single(candidate => AutomationProperties.GetName(candidate) == Localizer.Instance[door]);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return (welcome, Assert.Single(welcome.OwnedWindows.OfType<SettingsWindow>()));
    }

    /// <summary>Экран Welcome над папкой харнесса — не показанный.</summary>
    /// <param name="keys">Чем Welcome спрашивает страницу клавиш; null — не спрашивает.</param>
    /// <param name="restart">Перезапуск, который Welcome предлагает; null — без него.</param>
    public WelcomeWindow Welcome(Func<KeysPage>? keys = null, StudioRestart? restart = null)
    {
        var catalog = new PluginCatalog(Path.Combine(_home, "plugins"));

        return new WelcomeWindow(
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            new RecentProjects(Path.Combine(_home, "recent-projects.json")),
            catalog,
            Plugins)
        {
            Keys = keys,
            Restart = restart,
        };
    }

    /// <summary>
    /// Кладёт в каталог харнесса плагин из одного манифеста — без сборки, как языковой пакет.
    /// </summary>
    /// <param name="id">Идентификатор — он же имя папки.</param>
    /// <param name="name">Имя в списке.</param>
    /// <param name="dependsOn">От кого плагин зависит обязательно; null — ни от кого.</param>
    /// <remarks>Список и подробности читают манифест, а не сборку: этого довольно странице плагинов.</remarks>
    public void Install(string id, string name, string? dependsOn = null)
    {
        var folder = Path.Combine(_home, "plugins", id);
        var manifest = new Dictionary<string, object>
        {
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["publisher"] = "Тест",
            ["description"] = $"Пример плагина «{name}» для проверки страницы плагинов",
        };

        if (dependsOn is not null)
            manifest["dependencies"] = new[] { new Dictionary<string, object> { ["id"] = dependsOn } };

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
    }

    /// <summary>Закрывает окно «Отменой» и ждёт, пока завершится задача его показа.</summary>
    /// <param name="settings">Окно настроек.</param>
    /// <param name="shown">Задача, которую отдал его показ.</param>
    public static async Task CloseAsync(SettingsWindow settings, Task shown)
    {
        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await ClosedAsync(shown);
    }

    /// <summary>Ждёт закрытия окна, но не дольше <see cref="Patience"/>.</summary>
    /// <param name="shown">Задача, которую отдал показ окна.</param>
    public static async Task ClosedAsync(Task shown) =>
        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

    /// <summary>Строки текста, которые окно показывает сейчас.</summary>
    public static List<string?> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)];

    /// <summary>Страница плагинов, открытая в окне.</summary>
    public static PluginsPage PluginsOf(SettingsWindow settings) =>
        Assert.IsType<PluginsPage>(((SettingsViewModel)settings.DataContext!).Page);

    /// <summary>Строка списка, в которой стоит плагин.</summary>
    public static AxListBoxItem Row(SettingsWindow settings, PluginCard card) =>
        settings.GetVisualDescendants().OfType<AxListBoxItem>().Single(row => ReferenceEquals(row.DataContext, card));

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
}
