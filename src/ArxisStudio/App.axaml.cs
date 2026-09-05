using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.Splash;
using ArxisStudio.ViewModels;
using ArxisStudio.Welcome;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ArxisStudio;

/// <summary>
/// Точка сборки студии: показывает заставку, проходит этапы запуска и открывает
/// экран Welcome. Главное окно открывается, когда выбран проект.
/// </summary>
/// <remarks>
/// Порядок запуска — это список этапов, а не порядок строк: его видно целиком,
/// об этапах рассказывает заставка, и упавший этап не мешает остальным. Само
/// приложение здесь ничего не делает руками — только называет этапы.
/// </remarks>
public class App : Application
{
    // Журнал нужен ещё до окна студии: пакеты языков разбираются при
    // запуске, и сказать о занятом коде или потерянном словаре больше
    // некуда.
    private readonly StudioLog _log = new(Console.Out);

    private ISettingsStore _settings = null!;
    private RecentProjects _recent = null!;
    private PluginCatalog _plugins = null!;

    // Окно студии собирается на запуске и ждёт, пока его позовут: заставка в
    // студии одна и показывается при старте — значит и грузиться под ней должно
    // всё, включая модули и плагины. Показ окна после этого мгновенный.
    private MainWindow _studio = null!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        StudioDevTools.Attach(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            Raise(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Поднимает студию: заставка на экран, этапы — следом.
    /// </summary>
    /// <remarks>
    /// Этапы идут отложенно, а не здесь же: заставка показана, но не
    /// нарисована — рисует её тот же поток, который сейчас читает эти строки.
    /// Начав работу немедленно, студия показала бы пустую раму и заполнила её
    /// один раз в самом конце.
    /// </remarks>
    private void Raise(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splash = new SplashWindow(new SplashViewModel());

        splash.Show();

        _log.Write(StudioLogLevel.Debug, "Startup",
            $"Заставка на экране через {StudioStartup.SinceLaunch.TotalMilliseconds:F0} мс после запуска");

        Dispatcher.UIThread.Post(
            async () => await RunAsync(desktop, splash),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Что студия успевает до первого окна.
    /// </summary>
    /// <remarks>
    /// Языковые пакеты ставятся раньше выбора языка: выбранный язык вполне
    /// может быть тем, который принёс пакет, — не поставив их, студия отказала
    /// бы ему как несуществующему.
    /// </remarks>
    private async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var model = (SplashViewModel)splash.DataContext!;

        var startup = new StudioStartup(model, _log)
            .Add("splash.stage.paths", StudioPaths.EnsureUserData)
            .Add("splash.stage.settings", () =>
            {
                _settings = new JsonSettingsStore();
                _recent = new RecentProjects();
            })
            .Add("splash.stage.plugins", () =>
            {
                _plugins = new PluginCatalog();
                LanguagePacks.Apply(_plugins, _log);
            })
            .Add("splash.stage.language", () => Localizer.Instance.SetLanguage(_settings.Current.Language))
            .Add("splash.stage.theme", () => StudioTheming.Apply(_settings.Current.Theme))
            .Add("splash.stage.shell", () => _studio = new MainWindow())
            .Add("splash.stage.modules", () => _studio.Extensions.LoadModules())
            .Add("splash.stage.extensions", () => _studio.Extensions.LoadPlugins())
            .Add("splash.stage.welcome", () => desktop.MainWindow = CreateWelcome());

        await startup.RunAsync();
        await splash.LingerAsync();

        // Настоящее окно открывается до того, как уходит заставка: студия
        // закрывается по последнему окну, и промежуток без единого окна был бы
        // промежутком без студии.
        desktop.MainWindow?.Show();
        splash.Close();
    }

    /// <summary>
    /// Экран Welcome поверх уже собранной студии.
    /// </summary>
    /// <remarks>
    /// Окно студии к этому времени построено и наполнено: показать его —
    /// значит только показать. Ждать при этом человеку нечего, и заставка
    /// второй раз не нужна.
    /// </remarks>
    private WelcomeWindow CreateWelcome()
    {
        var welcome = new WelcomeWindow(_settings, _recent, _plugins, _log);
        welcome.StudioRequested += (_, _) =>
        {
            _studio.Show();
            welcome.Close();
        };

        return welcome;
    }
}
