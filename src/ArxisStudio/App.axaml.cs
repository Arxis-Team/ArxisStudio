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
            // На время запуска студия не закрывается по последнему окну.
            // Пока главного окна нет, единственное окно — заставка, и всякая
            // ошибка порядка уносила бы студию тем же движением, каким
            // заставка уходит с экрана: упавшая сборка оболочки оставляла
            // MainWindow пустым, Show ничего не делал, а Close закрывал
            // последнее окно — процесс кончался без окна и без слова. Обычный
            // режим возвращается, когда окно показано.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);

        // Модель слушает язык студии, а Localizer один на процесс: без этой
        // строки заставка пережила бы себя подпиской на синглтон.
        splash.Closed += (_, _) => model.Dispose();

        splash.Show();
        StudioLaunch.Mark("заставка");

        // Задача, а не async-лямбда: лямбда, поданная сюда как Action, — это
        // async void, и исключение из неё минует всякий catch и уносит процесс.
        // RunGuardedAsync не бросает по построению, поэтому брошенная задача
        // здесь честная, а не забытая.
        Dispatcher.UIThread.Post(
            () => _ = RunGuardedAsync(desktop, splash),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Проходит запуск и отвечает за то, что из него ничего не вылетит.
    /// </summary>
    /// <remarks>
    /// Верхний <c>catch</c> здесь не перестраховка, а условие задачи: выше —
    /// только диспетчер, и брошенное туда исключение уносит студию. Что бы ни
    /// случилось, человек обязан увидеть окно и причину.
    /// </remarks>
    private async Task RunGuardedAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        try
        {
            await RunAsync(desktop, splash);
        }
        catch (Exception e)
        {
            Fail(desktop, splash, $"{e.GetType().Name}: {e.Message}", e.ToString());
        }
    }

    /// <summary>
    /// Студии не будет: сказать человеку и дождаться, пока он закроет.
    /// </summary>
    /// <param name="desktop">Жизненный цикл — им же студия и завершается.</param>
    /// <param name="splash">Заставка, которая остаётся на экране отчётом.</param>
    /// <param name="shown">Что читает человек.</param>
    /// <param name="logged">Что уходит в журнал — со стеком.</param>
    /// <remarks>
    /// Завершает студию закрытие заставки, а не этот метод: окно с причиной,
    /// закрытое через миг после появления, ничем не лучше отсутствия окна.
    /// Код возврата не нулевой — запуск не удался, и тот, кто звал студию
    /// скриптом, обязан это узнать.
    /// </remarks>
    private void Fail(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        string shown,
        string logged)
    {
        _log.Write(StudioLogLevel.Error, "Startup", logged);

        ((SplashViewModel)splash.DataContext!).Fail(shown);

        splash.Closed += (_, _) => desktop.Shutdown(1);
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
            // Роковой: без окна студии нет ни модулей, ни плагинов, ни
            // экрана приветствия — следующие три этапа брались бы за пустое
            // место по очереди.
            .Add(
                "splash.stage.shell",
                () => _studio = new MainWindow(_log) { Settings = _settings, Catalog = _plugins },
                fatal: true)
            .Add("splash.stage.modules", () =>
            {
                // Недавние отмечает студия, а не модуль: список принадлежит человеку и живёт
                // рядом с его настройками, а модуль знает только про открытое сейчас.
                _studio.Extensions.Project.Opened += (_, path) => _recent.Touch(path);

                _studio.Extensions.LoadModules();
            })
            .Add("splash.stage.extensions", () => _studio.Extensions.LoadPlugins())
            // Роковой по той же причине: запуск без окна — это запуск без
            // студии, чем бы он ни кончился до того.
            .Add("splash.stage.welcome", () => desktop.MainWindow = FirstWindow(desktop.Args), fatal: true);

        var report = await startup.RunAsync();

        await splash.LingerAsync();

        // Окна может не быть вовсе: этап сборки оболочки падает, следующие за
        // ним падают об него же, и до присвоения дело не доходит. Показывать
        // нечего — значит надо сказать, а не уйти.
        if (desktop.MainWindow is not { } first)
        {
            // Причина берётся у рокового этапа, а не выдумывается заново:
            // «окна нет» — это следствие, а человеку нужна причина. Запасной
            // текст остаётся на случай, когда окна нет, а роковой не падал:
            // так бывает, если этап отдал null вместо окна.
            var reason = report.Failed.FirstOrDefault(failure => failure.Fatal)?.Reason
                ?? Localizer.Instance["splash.failed.nowindow"];

            Fail(desktop, splash, reason, $"Запуск не дал ни одного окна: {reason}");

            return;
        }

        // Настоящее окно открывается до того, как уходит заставка: промежуток
        // без единого окна был бы промежутком без студии.
        first.Show();

        // Поднять его надо вслух. Заставка стоит поверх всех, студия — нет, и
        // показанное под Topmost-окном на передний план не выходит: студия
        // вставала под заставкой и оставалась там, а если человек успел уйти в
        // чужое окно — то и после её ухода.
        first.Activate();

        // Topmost снимается до закрытия, а не после: после закрытия снимать уже
        // не с чего, а оставленный до конца он держит заставку над студией весь
        // промежуток, пока открыты оба окна.
        splash.Topmost = false;
        splash.Close();

        // Окно есть — студию снова можно закрывать последним окном.
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

        // Неполный запуск говорит о себе тем местом, которое у окна уже есть.
        // Своего для этого не заводится: сообщение появляется на запуске,
        // который прошёл, и место под него стояло бы пустым всегда.
        if (report.Complaint is { } complaint)
        {
            switch (first)
            {
                case MainWindow studio:
                    studio.Say(complaint);
                    break;

                case WelcomeWindow welcome:
                    welcome.Say(complaint);
                    break;
            }
        }

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
    /// Провал открытия — не провал запуска: студия остаётся открытой, а о причине говорит журнал.
    /// Говорить приходится здесь: провалившаяся загрузка возвращается итогом, а не исключением, и
    /// пока итог выбрасывали, о ней знала одна служба.
    /// </remarks>
    private async Task OpenAsync(string path)
    {
        if (_studio.Extensions.Projects is not { } projects)
            return;

        try
        {
            var result = await projects.OpenAsync(CanonicalPath.Create(path));

            if (!result.HasSnapshot)
            {
                var why = string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}"));

                _log.Write(StudioLogLevel.Error, "Startup", $"{path} не открылся: {why}");
            }
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

        // Порядок здесь несущий дважды. Окно студии показывается раньше, чем закрывается Welcome:
        // студия закрывается по последнему окну, и промежуток без единого окна был бы промежутком
        // без студии. А загрузка идёт после закрытия: чтение решения занимает секунды, и смотреть
        // их человеку лучше на студию с задачей в статус-баре, чем на окно, которому пора уйти.
        welcome.ProjectRequested += async (_, path) =>
        {
            _studio.Show();
            welcome.Close();

            await OpenAsync(path);
        };

        return welcome;
    }
}
