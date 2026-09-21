using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Project;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно проекта, собранное так, как его собирает студия, — и руки, которыми тест им пользуется.
/// </summary>
/// <remarks>
/// Модуль поднимает хост студии, а не одна фабрика контекстов: разметка окна берёт словарь по своей
/// сборке, и связь «сборка → словарь» кладёт подъём модуля. Контекст, собранный в обход, оставил бы
/// разметке одни ключи. Служба проектов и редакторы подделаны — тест велит, что открыто, и видит,
/// что попросили открыть, — а файлы решения лежат на диске во временной папке: окно спрашивает диск.
/// <para>
/// Заводится в теле теста и прощается там же: окно и хост живут в потоке интерфейса.
/// </para>
/// </remarks>
internal sealed class ProjectWindowStudio : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-{Guid.NewGuid():N}");
    private readonly PluginHost _host;
    private readonly IStudioContext _context;

    /// <summary>Поднимает модуль и показывает окно.</summary>
    /// <param name="service">Есть ли у студии служба проектов.</param>
    /// <param name="width">Ширина окна.</param>
    /// <param name="projects">Служба проектов, которую тест завёл заранее; пусто — новая.</param>
    /// <param name="twoColumns">
    /// Раскладка окна. По умолчанию — одна колонка: большинство тестов проверяет дерево, и файлы в нём
    /// есть только в одну колонку. Пусто — не трогать настройку и получить умолчание самого окна.
    /// </param>
    /// <param name="files">Служба файлов; пусто — её нет, как у студии без службы проектов 1.3.</param>
    public ProjectWindowStudio(
        bool service = true, double width = 520, ProjectsProbe? projects = null, bool? twoColumns = false, FilesProbe? files = null)
    {
        Directory.CreateDirectory(_root);

        Projects = projects ?? new ProjectsProbe { Accepts = true };
        Files = files;

        var exports = new StudioExportRegistry();

        if (service)
            exports.Publish(typeof(IStudioProjects), Projects, "arxis.projects", "Проекты");

        if (files is not null)
            exports.Publish(typeof(IStudioFiles), files, "arxis.projects", "Проекты");

        var services = new Dictionary<Type, object> { [typeof(IStudioDocuments)] = Documents, [typeof(IStudioStatus)] = Status };
        var store = new PluginSettingsStore(null, Path.Combine(_root, "plugin-settings.json"));

        _host = new PluginHost(new StudioContextFactory(Log, new StudioCommands(), null, services, settings: store, exports: exports));

        var loaded = _host.LoadBuiltIn(typeof(ProjectModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);

        _context = loaded.Studio!;

        // Словарь — тот же, которым подписывает окно: сверять подписи со словарём студии значило
        // бы проверять совпадение двух словарей, а не работу окна.
        Strings = _context.Strings;

        if (twoColumns is { } columns)
            _context.Settings.Set(ProjectSettings.TwoColumnsKey, columns);

        Window = new Window { Width = width, Height = 720 };
        Panel = Build();
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Служба проектов: что открыто, велит тест.</summary>
    public ProjectsProbe Projects { get; }

    /// <summary>Редакторы студии: что попросили открыть.</summary>
    public DocumentsProbe Documents { get; } = new();

    /// <summary>Служба файлов, если она есть.</summary>
    public FilesProbe? Files { get; }

    /// <summary>Строка состояния: что окно сказало.</summary>
    public StatusProbe Status { get; } = new();

    /// <summary>Журнал студии.</summary>
    public StudioLog Log { get; } = new();

    /// <summary>Словари модуля.</summary>
    public IStudioStrings Strings { get; }

    /// <summary>Настройки модуля — той же службой, что у окна.</summary>
    public IStudioSettings Settings => _context.Settings;

    /// <summary>Панель.</summary>
    public ProjectPanel Panel { get; private set; }

    /// <summary>Окно, в котором панель показана.</summary>
    public Window Window { get; }

    /// <summary>Модель окна.</summary>
    public ProjectModel Model => Panel.Model!;

    /// <summary>Разметка окна.</summary>
    public ProjectPanelView View => Panel.View!;

    /// <summary>Строки дерева.</summary>
    public List<Row> Rows => [.. Model.Tree.Rows];

    /// <summary>Выделенная строка.</summary>
    public Row Selected => Assert.IsType<Row>(View.Tree.SelectedItem);

    /// <summary>Обычное решение, положенное на диск во временную папку теста.</summary>
    /// <param name="name">Имя решения.</param>
    /// <param name="extra">Ещё один файл приложения.</param>
    /// <param name="window">Имя главного окна.</param>
    /// <param name="without">Файлы приложения, которых нет.</param>
    public SolutionSnapshot Solution(
        string name = "Hello", string? extra = null, string window = "MainWindow", IReadOnlyCollection<string>? without = null) =>
        ProjectWindowSolution.Avalonia(name, extra: extra, root: _root, window: window, without: without).OnDisk().ToSnapshot();

    /// <summary>То же решение как построитель — когда тесту нужны его пути.</summary>
    public ProjectWindowSolution Avalonia() => ProjectWindowSolution.Avalonia(root: _root).OnDisk();

    /// <summary>
    /// Прощается с окном и строит новое на том же контексте — так студия открывает закрытую панель.
    /// </summary>
    public ProjectPanel Reopen()
    {
        Panel.Release();
        Panel = Build();
        Dispatcher.UIThread.RunJobs();

        return Panel;
    }

    /// <summary>Открывает решение и ждёт, пока окно его покажет.</summary>
    /// <param name="snapshot">Снимок; пусто — обычное решение.</param>
    public async Task Open(SolutionSnapshot? snapshot = null)
    {
        Projects.Publish(Ready(1, snapshot ?? Solution()));
        await Built();
    }

    /// <summary>Ждёт постройку дерева и раскладку.</summary>
    public async Task Built()
    {
        Dispatcher.UIThread.RunJobs();
        await Model.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Состояние службы: решение открыто и прочитано.</summary>
    public static ProjectsStatus Ready(long sequence, SolutionSnapshot snapshot, long session = 1) => new()
    {
        Sequence = sequence,
        Session = session,
        State = ProjectsState.Ready,
        EntryPoint = snapshot.EntryPoint.Path,
        Snapshot = snapshot,
    };

    /// <summary>Строка по имени.</summary>
    public Row Row(string name) => Rows.Single(row => row.Name == name);

    /// <summary>Выделяет строку, как выделил бы её человек, и отдаёт ей клавиатуру.</summary>
    public Row Select(string name)
    {
        var row = Row(name);

        View.Tree.SelectedItem = row;
        Item(row).Focus();

        return row;
    }

    /// <summary>Контейнер строки — прокрутив до неё.</summary>
    public TreeRow Item(Row row)
    {
        View.Tree.ScrollIntoView(row);
        Dispatcher.UIThread.RunJobs();

        return Assert.IsType<TreeRow>(View.Tree.ContainerFromItem(row));
    }

    /// <summary>Ячейка шеврона строки.</summary>
    public Control Chevron(Row row) =>
        Item(row).GetVisualDescendants().OfType<Control>().Single(control => control.Name == "Chevron");

    /// <summary>Значок строки — тот, что стоит после клетки шеврона.</summary>
    public AxIcon Glyph(string name)
    {
        var item = Item(Row(name));
        var chevron = item.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "Chevron");

        return item.GetVisualDescendants().OfType<AxIcon>().Single(icon => !icon.GetVisualAncestors().Contains(chevron));
    }

    /// <summary>Ресурс темы по ключу — таким, каким его видит окно.</summary>
    public object? Resource(string key) =>
        Window.TryFindResource(key, Window.ActualThemeVariant, out var value) ? value : null;

    /// <summary>Подпись строки — то, во что человек целится мышью.</summary>
    public TextBlock Label(string name) =>
        Item(Row(name)).GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == name);

    /// <summary>Виден ли в окне текст — с такой подписью и видимый вместе с предками.</summary>
    public bool Shown(string text) =>
        View.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);

    /// <summary>Щёлкает мышью в середину.</summary>
    public void Press(Visual target)
    {
        var at = Middle(target);

        Window.MouseDown(at, MouseButton.Left);
        Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Щёлкает правой кнопкой в середину — так просят меню мышью.</summary>
    public void RightClick(Visual target)
    {
        var at = Middle(target);

        Window.MouseDown(at, MouseButton.Right);
        Window.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Наводит мышь на середину и оставляет её там.</summary>
    public void Hover(Visual target)
    {
        Window.MouseMove(Middle(target));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Крутит колесо над серединой, держа клавиши, как их держит человек.</summary>
    /// <param name="target">Над чем.</param>
    /// <param name="notches">Щелчков колеса: от себя — больше нуля; тачпад шлёт доли.</param>
    /// <param name="modifiers">Зажатые клавиши.</param>
    public void Wheel(Visual target, double notches, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseWheel(Middle(target), new Vector(0, notches), modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Щёлкает мышью дважды в середину.</summary>
    public void DoubleClick(Visual target)
    {
        var at = Middle(target);

        for (var click = 0; click < 2; click++)
        {
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Нажимает клавишу там, где стоит фокус.</summary>
    public void Press(Control where, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        where.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = where,
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Нажимает кнопку.</summary>
    public void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = button });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Нажимает пункт меню по подписи — событием: попап — отдельное окно, которого здесь нет.</summary>
    public void Click(IEnumerable<AxMenuItem> items, string header)
    {
        items.Single(item => Equals(item.Header, header)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Reveal.Override = null;
        SolutionPicker.Override = null;
        Panel.Release();
        Window.Close();
        _host.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private ProjectPanel Build()
    {
        var panel = new ProjectPanel();

        panel.Attach(_context);
        Window.Content = panel.Content;

        return panel;
    }

    private Point Middle(Visual target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window);

        Assert.NotNull(at);

        return at.Value;
    }
}

/// <summary>
/// Служба файлов, которая помнит, что её просили, и отвечает, как велит тест.
/// </summary>
/// <remarks>
/// Диска служба теста не трогает: что стало после правки, тест говорит сам — новым снимком службы
/// проектов в <see cref="After"/>, как настоящая служба перечитывает модель раньше, чем вернуться.
/// </remarks>
internal sealed class FilesProbe : IStudioFiles
{
    /// <summary>Переносы, по порядку.</summary>
    public List<IReadOnlyList<FileMove>> Moved { get; } = [];

    /// <summary>Удаления, по порядку.</summary>
    public List<IReadOnlyList<CanonicalPath>> Deleted { get; } = [];

    /// <summary>Метки действий, по порядку.</summary>
    public List<string> Labels { get; } = [];

    /// <summary>Что ответить; пусто — удача без диагностик.</summary>
    public Func<ProjectOperationResult>? Answer { get; set; }

    /// <summary>Что случилось на диске и в модели, пока служба работала; зовётся до ответа.</summary>
    public Action? After { get; set; }

    /// <inheritdoc/>
    public event EventHandler<FilesChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public Task<ProjectOperationResult> MoveAsync(IReadOnlyList<FileMove> moves, string label, CancellationToken cancellationToken = default)
    {
        Moved.Add(moves);

        return Done(label, new FilesChangedEventArgs([.. moves], [], []));
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> CopyAsync(IReadOnlyList<FileMove> copies, string label, CancellationToken cancellationToken = default) =>
        Done(label, new FilesChangedEventArgs([], [.. copies], []));

    /// <inheritdoc/>
    public Task<ProjectOperationResult> DeleteAsync(IReadOnlyList<CanonicalPath> paths, string label, CancellationToken cancellationToken = default)
    {
        Deleted.Add(paths);

        return Done(label, new FilesChangedEventArgs([], [], [.. paths]));
    }

    private Task<ProjectOperationResult> Done(string label, FilesChangedEventArgs change)
    {
        Labels.Add(label);

        var result = Answer?.Invoke() ?? ProjectOperationResult.Succeeded();

        if (!result.HasErrors)
        {
            After?.Invoke();
            Changed?.Invoke(this, change);
        }

        return Task.FromResult(result);
    }
}

/// <summary>Редакторы студии, которые помнят, что их просили открыть.</summary>
internal sealed class DocumentsProbe : IStudioDocuments
{
    /// <summary>Пути, по порядку.</summary>
    public List<string> Opened { get; } = [];

    /// <inheritdoc/>
    public Task OpenAsync(string filePath)
    {
        Opened.Add(filePath);

        return Task.CompletedTask;
    }
}
