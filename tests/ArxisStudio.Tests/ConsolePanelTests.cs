using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Console;
using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Modules.Console.Panels;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панели консоли на живом дереве контролов.
/// </summary>
/// <remarks>
/// Здесь проверяется то, чего не видно в чистой работе отбора: следует ли
/// список за журналом, доходит ли до него запись из чужого потока, отпускает
/// ли панель ленту, когда студия с ней прощается.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ConsolePanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-console-{Guid.NewGuid():N}");

    private IStudioSettings _settings = null!;

    public ConsolePanelTests()
    {
        Directory.CreateDirectory(_root);
        ConsoleHub.Reset();
    }

    public void Dispose()
    {
        // Хаб держит панель статикой: не отпустив её, следующий тест получил бы
        // чужую. Ровно та причина, по которой у панели есть Release.
        ConsoleHub.Reset();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Панель показывает то, что записали до её рождения.
    /// </summary>
    /// <remarks>
    /// Ради этого журнал и сведён к одному экземпляру: этапы запуска пишут в
    /// него раньше, чем появляется окно, и панель обязана их застать.
    /// </remarks>
    [AvaloniaFact]
    public void The_log_panel_shows_what_was_written_before_it_was_built()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Startup", "запуск 1257 мс");
        log.Write(StudioLogLevel.Warning, "Plugins", "сосед устарел");

        var panel = LogPanel(log);

        Assert.Equal(2, Records(panel).ItemCount);
    }

    /// <summary>Запись, сделанная после, доходит до списка.</summary>
    [AvaloniaFact]
    public void A_record_written_afterwards_reaches_the_panel()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Error, "Plugins", "расширение упало");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, Records(panel).ItemCount);
    }

    /// <summary>
    /// Пачка записей перестраивает список один раз.
    /// </summary>
    /// <remarks>
    /// Журнал зовёт событие на каждую запись, и писать в него могут пачкой.
    /// Перестроение на каждую значило бы работу, итог которой человек не
    /// увидит: он увидит только последний.
    /// </remarks>
    [AvaloniaFact]
    public void A_burst_of_records_rebuilds_the_list_once()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        var before = panel.Rebuilds;

        for (var index = 0; index < 500; index++)
            log.Write(StudioLogLevel.Info, "Плагин", $"строка {index}");

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 1, panel.Rebuilds);
        Assert.Equal(500, Records(panel).ItemCount);
    }

    /// <summary>
    /// Запись из чужого потока доходит до панели и ничего не роняет.
    /// </summary>
    /// <remarks>
    /// Писать в журнал могут из пула: фоновая задача расширения отчитывается о
    /// своей отмене именно оттуда. Пока журнал никто не наблюдал, это было
    /// незаметно; панель делает это настоящим — трогать дерево контролов из
    /// чужого потока нельзя.
    /// </remarks>
    [AvaloniaFact]
    public void A_record_written_off_the_ui_thread_reaches_the_panel()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        var writer = new Thread(() => log.Write(StudioLogLevel.Debug, "Tasks", "отменено"));

        writer.Start();
        writer.Join();

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, Records(panel).ItemCount);
    }

    /// <summary>Очистка журнала опустошает панель.</summary>
    [AvaloniaFact]
    public void Clearing_the_log_empties_the_panel()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");

        var panel = LogPanel(log);

        Assert.Equal(1, Records(panel).ItemCount);

        log.Clear();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, Records(panel).ItemCount);
        Assert.True(Part<TextBlock>(panel, "Empty").IsVisible);
    }

    /// <summary>
    /// Выбранная запись показывается целиком.
    /// </summary>
    /// <remarks>
    /// Это единственная дорога, по которой до человека доезжает стек: в записи
    /// журнала отдельного поля под него нет, и исключение приходит в сообщении
    /// многострочным. Список показывает первую строку, подробности — всё.
    /// </remarks>
    [AvaloniaFact]
    public void Selecting_a_record_shows_its_whole_text()
    {
        var message = "Не вышло" + Environment.NewLine + "   в методе Раз()" + Environment.NewLine + "   в методе Два()";

        var log = new StudioLog();
        log.Write(StudioLogLevel.Error, "Settings", message);

        var panel = LogPanel(log);

        Records(panel).SelectedIndex = 0;

        Assert.Equal(message, Part<AxTextArea>(panel, "DetailsText").Text);
    }

    /// <summary>
    /// Подробности не съедают невысокую панель целиком.
    /// </summary>
    /// <remarks>
    /// Панель в доке бывает ростом в полтораста пикселей, и подробности,
    /// заданные числом пикселей, забирали бы её целиком — список пропадал бы
    /// с глаз. Найдено на живой студии: с включёнными подробностями список
    /// оставался без единой строки.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_the_details_leaves_room_for_the_list()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");

        var panel = LogPanel(log);

        // Окно нужно настоящее: без него шаблон корня панели не разворачивается
        // и мерить нечего. Высота — та, при которой беда и вылезла.
        var window = new Window { Width = 900, Height = 140, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Part<ConsoleToggle>(panel, "Details").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var details = Part<Border>(panel, "DetailsPane");
        var records = Records(panel);

        Assert.True(details.IsVisible, "подробности не показались");
        Assert.True(details.Bounds.Height > 0, $"подробностям не досталось места: {details.Bounds}");

        // Не «больше нуля»: при заданной пикселями высоте списку доставалась
        // пара пикселей, и такая проверка прошла бы, ничего не заметив.
        Assert.True(records.Bounds.Height >= 20, $"списку осталось {records.Bounds.Height:F0} пикселей");

        window.Close();
    }

    /// <summary>
    /// Подробности приводят с собой границу и уводят её обратно.
    /// </summary>
    /// <remarks>
    /// Долю между списком и подробностями правит человек, а не только
    /// переключатель, — для этого между ними стоит разделитель. Пока
    /// подробностей нет, границе не с чем граничить, и стоять ей незачем.
    /// </remarks>
    [AvaloniaFact]
    public void The_details_bring_a_grip_and_take_it_away()
    {
        var panel = LogPanel(new StudioLog());

        var handle = Part<AxSplitter>(panel, "Handle");
        var details = Part<ConsoleToggle>(panel, "Details");

        Assert.False(handle.IsVisible, "граница стоит, а граничить ей не с чем");

        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(handle.IsVisible, "подробности показаны, а границы нет");

        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(handle.IsVisible, "подробности убрали, а граница осталась");
    }

    /// <summary>
    /// Следование за хвостом переключается, не пересобирая список.
    /// </summary>
    /// <remarks>
    /// Настройки пишутся парой, поэтому щелчок по прокрутке будит и ключ
    /// времени — с прежним значением. Панель, перестраивавшая список на любой
    /// из двух, собирала его заново новыми строками: выделение слетало, а
    /// открытые подробности гасли — на кнопке, которая решает только, куда
    /// смотреть.
    /// </remarks>
    [AvaloniaFact]
    public void Following_the_tail_is_switched_without_rebuilding_the_list()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Error, "Плагин", "не вышло");
        log.Write(StudioLogLevel.Info, "Плагин", "и это тоже");
        Dispatcher.UIThread.RunJobs();

        var records = Records(panel);

        records.SelectedIndex = 0;

        var chosen = records.SelectedItem;
        var before = panel.Rebuilds;

        Part<ConsoleToggle>(panel, "Autoscroll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, panel.Rebuilds);
        Assert.Same(chosen, records.SelectedItem);
    }

    /// <summary>
    /// Показ времени меняет сами строки, и список пересобирается.
    /// </summary>
    /// <remarks>
    /// Обратная половина того же правила: перестраивать на всякую настройку
    /// нельзя, а на эту — обязательно. Время впечатано в строку при сборке, и
    /// без пересборки столбец остался бы на месте.
    /// </remarks>
    [AvaloniaFact]
    public void Turning_the_timestamps_off_rebuilds_the_rows()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Info, "Плагин", "строка");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(panel)[0].HasStamp, "время не показано с самого начала");

        _settings.Set(ConsoleSettings.TimestampsKey, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Shown(panel)[0].HasStamp, "столбец времени остался после того, как его выключили");
    }

    /// <summary>
    /// Отпущенная панель больше не следует за журналом.
    /// </summary>
    /// <remarks>
    /// Студия зовёт <c>Release</c> перед тем, как отпустить панель. Панель,
    /// оставшаяся подписанной, продолжала бы перестраивать список, которого
    /// никто не видит, — и держала бы собой всё, до чего дотянулась.
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_lets_go_of_the_log_when_it_is_released()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        panel.Release();

        var after = panel.Rebuilds;

        log.Write(StudioLogLevel.Info, "A", "уже неинтересно");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(after, panel.Rebuilds);
    }

    private LogPanel LogPanel(StudioLog log)
    {
        var panel = new LogPanel();

        panel.Attach(Context(log, new Dictionary<Type, object> { [typeof(IStudioLogFeed)] = log }));

        // Обращение к содержимому и строит панель — так же, как это делает студия.
        _ = panel.Content;

        return panel;
    }

    /// <summary>
    /// Контекст, какой студия выдаёт встроенному модулю.
    /// </summary>
    /// <remarks>
    /// Собирается настоящей фабрикой, а не подделкой: обёртки, которые она
    /// надевает на службы, — часть того, что проверяется. Хранилище настроек
    /// подставлено своё, во временную папку: настоящее лежит в данных
    /// пользователя, и тест не вправе его трогать.
    /// </remarks>
    private IStudioContext Context(StudioLog log, IReadOnlyDictionary<Type, object> services)
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ConsoleModule).Assembly);

        Assert.Null(error);

        var installed = new InstalledPlugin(AppContext.BaseDirectory, manifest, null, IsEnabled: true, IsBuiltIn: true);
        var store = new PluginSettingsStore(null, Path.Combine(_root, "plugin-settings.json"));

        var context = new StudioContextFactory(log, new StudioCommands(), null, services, settings: store)
            .Create(installed);

        // Настройки запоминаются: менять их надо той же службой, что и панель.
        // Запись мимо неё — прямо в хранилище — панель бы не разбудила: о
        // правке со стороны модулю говорит студия, отдельным уведомлением.
        _settings = context.Settings;

        return context;
    }

    private static AxListBox Records(LogPanel panel) => Part<AxListBox>(panel, "Records");

    private static List<LogRow> Shown(LogPanel panel) =>
        [.. (Records(panel).ItemsSource as IEnumerable<LogRow>)!];

    private static T Part<T>(ToolWindow panel, string name) where T : Control =>
        panel.Content.GetLogicalDescendants().OfType<T>().Single(part => part.Name == name);
}
