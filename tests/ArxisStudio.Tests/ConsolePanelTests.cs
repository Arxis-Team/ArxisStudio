using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Console;
using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Panels;
using ArxisStudio.Modules.Console.Problems;
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

    /// <summary>Панель находок показывает сообщённое, ошибки первыми.</summary>
    [AvaloniaFact]
    public void The_problems_panel_lists_what_was_reported_with_errors_first()
    {
        var problems = new StudioProblems();
        var panel = ProblemsPanel(problems);

        problems.Report("проверка",
        [
            new StudioProblem(StudioProblemSeverity.Info, "I1", "к сведению"),
            new StudioProblem(StudioProblemSeverity.Error, "E1", "сломалось"),
            new StudioProblem(StudioProblemSeverity.Warning, "W1", "подозрительно"),
        ]);

        Dispatcher.UIThread.RunJobs();

        var rows = Rows(panel);

        Assert.Equal(3, rows.Count);
        Assert.Equal("E1", rows[0].Code);
        Assert.Equal("W1", rows[1].Code);
        Assert.Equal("I1", rows[2].Code);
    }

    /// <summary>
    /// Пустой список находок объясняет себя, а не выглядит поломкой.
    /// </summary>
    /// <remarks>
    /// Пусто здесь — обычное состояние студии: сообщать находки некому, пока не
    /// встанет расширение, которое умеет. Панель, молчащая об этом, читается
    /// как сломанная.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_problems_list_says_so_rather_than_looking_broken()
    {
        var panel = ProblemsPanel(new StudioProblems());

        Assert.True(Part<StackPanel>(panel, "Empty").IsVisible);
        Assert.True(Part<TextBlock>(panel, "EmptyHint").IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(Part<TextBlock>(panel, "EmptyText").Text));
    }

    /// <summary>
    /// Находка с файлом открывает его.
    /// </summary>
    /// <remarks>
    /// Панель просит «открой это», а не «покажи мне вот такой редактор»: кто
    /// возьмётся за файл, решает оболочка по объявленному типу.
    /// </remarks>
    [AvaloniaFact]
    public void A_finding_with_a_file_opens_it()
    {
        var problems = new StudioProblems();
        var documents = new Documents();

        var panel = ProblemsPanel(problems, documents);

        problems.Report("проверка",
        [
            new StudioProblem(StudioProblemSeverity.Error, "E1", "сломалось", "Окно.axaml", 12),
        ]);

        Dispatcher.UIThread.RunJobs();

        var open = Part<AxButton>(panel, "Open");

        Assert.False(open.IsEnabled, "кнопка открыта, пока ничего не выбрано");

        Findings(panel).SelectedIndex = 0;

        Assert.True(open.IsEnabled);

        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("Окно.axaml", Assert.Single(documents.Opened));
    }

    /// <summary>Находка без файла открывать нечего — и кнопка это говорит.</summary>
    [AvaloniaFact]
    public void A_finding_without_a_file_has_nothing_to_open()
    {
        var problems = new StudioProblems();
        var panel = ProblemsPanel(problems, new Documents());

        problems.Report("проверка", [new StudioProblem(StudioProblemSeverity.Warning, "W1", "везде")]);

        Dispatcher.UIThread.RunJobs();

        Findings(panel).SelectedIndex = 0;

        Assert.False(Part<AxButton>(panel, "Open").IsEnabled);
    }

    private LogPanel LogPanel(StudioLog log)
    {
        var panel = new LogPanel();

        panel.Attach(Context(log, new Dictionary<Type, object> { [typeof(IStudioLogFeed)] = log }));

        // Обращение к содержимому и строит панель — так же, как это делает студия.
        _ = panel.Content;

        return panel;
    }

    private ProblemsPanel ProblemsPanel(StudioProblems problems, IStudioDocuments? documents = null)
    {
        var services = new Dictionary<Type, object> { [typeof(IStudioProblems)] = problems };

        if (documents is not null)
            services[typeof(IStudioDocuments)] = documents;

        var panel = new ProblemsPanel();

        panel.Attach(Context(new StudioLog(), services));

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

        return new StudioContextFactory(log, new StudioCommands(), null, services, settings: store)
            .Create(installed);
    }

    private static AxListBox Records(LogPanel panel) => Part<AxListBox>(panel, "Records");

    private static AxDataGrid Findings(ProblemsPanel panel) => Part<AxDataGrid>(panel, "Findings");

    private static List<ProblemRow> Rows(ProblemsPanel panel) =>
        [.. (Findings(panel).ItemsSource as IEnumerable<ProblemRow>)!];

    private static T Part<T>(ToolWindow panel, string name) where T : Control =>
        panel.Content.GetLogicalDescendants().OfType<T>().Single(part => part.Name == name);

    /// <summary>Служба документов, которая только запоминает, о чём просили.</summary>
    private sealed class Documents : IStudioDocuments
    {
        public List<string> Opened { get; } = [];

        public Task OpenAsync(string filePath)
        {
            Opened.Add(filePath);
            return Task.CompletedTask;
        }
    }
}
