using ArxisStudio.Extensibility;
using ArxisStudio.ProjectSystem;
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
    // некуда. Он же уходит в окно — журнал на студию один, и панель
    // «Консоль» показывает в том числе то, что записано до её рождения.
    private readonly StudioLog _log = new(Console.Out);

    private ISettingsStore _settings = null!;
    private RecentProjects _recent = null!;
    private PluginCatalog _plugins = null!;

    // Окно студии собирается на запуске и ждёт, пока его позовут: заставка в
    // студии одна и показывается при старте — значит и грузиться под ней должно
    // всё, включая модули и плагины. Показ окна после этого мгновенный.
    private MainWindow _studio = null!;

    // Проект, названный в командной строке: открывается он после того, как окно показано.
    private string? _asked;

    /// <inheritdoc/>
    public override void Initialize()
    {
        StudioLaunch.Mark("платформа");

        AvaloniaXamlLoader.Load(this);
        StudioLaunch.Mark("стили");

        StudioDevTools.Attach(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        StudioLaunch.Mark("оболочка");

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
        StudioLaunch.Mark("заставка");

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
            // Хранилище настроек передаётся, а не заводится окном: оно читает
            // файл в память и пишет его целиком, и второй экземпляр на процесс
            // потерял бы правку, сделанную в первом.
            .Add("splash.stage.shell", () => _studio = new MainWindow(_log) { Settings = _settings, Catalog = _plugins })
            .Add("splash.stage.modules", () =>
            {
                // Недавние отмечает студия, а не модуль: список принадлежит человеку и живёт
                // рядом с его настройками, а модуль знает только про открытое сейчас.
                _studio.Extensions.Project.Opened += (_, path) => _recent.Touch(path);

                _studio.Extensions.LoadModules();
            })
            .Add("splash.stage.extensions", () => _studio.Extensions.LoadPlugins())
            .Add("splash.stage.welcome", () => desktop.MainWindow = FirstWindow(desktop.Args));

        await startup.RunAsync();
        await splash.LingerAsync();

        // Настоящее окно открывается до того, как уходит заставка: студия
        // закрывается по последнему окну, и промежуток без единого окна был бы
        // промежутком без студии.
        desktop.MainWindow?.Show();
        splash.Close();

        StudioLaunch.Mark("окно");

        // Отчёт пишется один раз и одной строкой: следующему, кто спросит
        // «почему студия стартует секунду», отвечать будет журнал, а не
        // расставленные заново отметки.
        _log.Write(StudioLogLevel.Debug, "Startup", StudioLaunch.Report());

        // Проект открывается после показа окна, а не вместо него: чтение решения занимает секунды,
        // и смотреть их человеку лучше на студию с задачей в статус-баре, чем на заставку.
        if (_asked is { } asked)
            await OpenAsync(asked);
    }

    /// <summary>
    /// Окно, которым студия открывается: со сказанным проектом — своё, иначе — Welcome.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки.</param>
    /// <remarks>
    /// Названный проект — это просьба открыть его, а не выбрать из недавних: Welcome в этом случае
    /// только стоял бы на дороге. Сказанное, но негодное — запись в журнале и обычный Welcome:
    /// студия не должна ни падать, ни делать вид, что открыла.
    /// </remarks>
    private Window FirstWindow(string[]? arguments)
    {
        var asked = StudioArguments.Project(arguments);

        if (asked.Complaint is { } complaint)
            _log.Write(StudioLogLevel.Error, "Startup", $"Открывать нечего — {complaint}");

        if (asked.Path is not { } path)
            return CreateWelcome();

        if (_studio.Extensions.Projects is null)
        {
            _log.Write(StudioLogLevel.Warning, "Startup", $"{path} открывать некому: службы проектов нет");

            return CreateWelcome();
        }

        _asked = path;

        return _studio;
    }

    /// <summary>
    /// Открывает названный проект.
    /// </summary>
    /// <param name="path">Путь к решению или проекту.</param>
    /// <remarks>
    /// Провал открытия — не провал запуска: о нём скажут журнал службы и панель «Проблемы», а
    /// студия остаётся открытой. Ловится здесь только своё: отмена и остановленная служба.
    /// </remarks>
    private async Task OpenAsync(string path)
    {
        if (_studio.Extensions.Projects is not { } projects)
            return;

        try
        {
            await projects.OpenAsync(CanonicalPath.Create(path));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Write(StudioLogLevel.Error, "Startup", $"{path} не открылся: {e.Message}");
        }
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
        // Расширения студии передаются сюда затем, чтобы экран настроек,
        // открытый из Welcome, смотрел в то же хранилище, что и живые плагины.
        // Окно студии к этому мигу собрано, а модули и плагины подняты —
        // этапы «shell», «modules» и «extensions» идут раньше «welcome».
        var welcome = new WelcomeWindow(_settings, _recent, _plugins, _studio.Extensions, _log);
        welcome.StudioRequested += (_, _) =>
        {
            _studio.Show();
            welcome.Close();
        };

        return welcome;
    }
}
