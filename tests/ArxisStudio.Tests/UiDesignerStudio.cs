using ArxisStudio.Dragging;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Доска форм в окне: модуль, поднятый хостом из своей папки, служба проектов и документы — пробами.
/// </summary>
/// <remarks>
/// Решение лежит на диске по-настоящему: доска читает корни разметки и пишет свой файл, и проверять это
/// без диска значило бы проверять подделку. Снимок служба отдаёт тот, что собрал тест.
/// </remarks>
internal sealed class UiDesignerStudio : IDisposable
{
    private readonly PluginHost _host;
    private readonly IStudioContext _context;

    /// <summary>Поднимает модуль и показывает доску в окне.</summary>
    /// <param name="service">Есть ли у студии служба проектов.</param>
    /// <param name="drags">Тяга студии, общая с соседним окном; пусто — своя, над одним этим окном.</param>
    public UiDesignerStudio(bool service = true, StudioDrags? drags = null)
    {
        Drags = drags ?? new StudioDrags(() => Window is { } window ? [window] : [], Log);

        var exports = new StudioExportRegistry();

        if (service)
            exports.Publish(typeof(IStudioProjects), Projects, "arxis.projects", "Проекты");

        var services = new Dictionary<Type, object>
        {
            [typeof(IStudioDocuments)] = Documents,
            [typeof(IStudioStatus)] = Status,
            [typeof(IStudioDragDrop)] = Drags,
        };
        var store = new PluginSettingsStore(null, Path.Combine(Root, "plugin-settings.json"));

        _host = new PluginHost(new StudioContextFactory(Log, new StudioCommands(), null, services, settings: store, exports: exports));

        var loaded = _host.LoadBuiltIn(typeof(UiDesignerModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);

        _context = loaded.Studio!;
        Strings = _context.Strings;

        Window = new Window { Width = 900, Height = 600 };
        Panel = new BoardPanel();
        Panel.Attach(_context);
        Window.Content = Panel.Content;
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    public string Root { get; } = TempFolder.Create("ui-designer");

    public ProjectsProbe Projects { get; } = new() { Accepts = true };

    public DocumentsProbe Documents { get; } = new();

    public StatusProbe Status { get; } = new();

    /// <summary>Тяга студии: ею на доску несут из соседнего окна.</summary>
    public StudioDrags Drags { get; }

    public StudioLog Log { get; } = new();

    public IStudioStrings Strings { get; }

    public IStudioSettings Settings => _context.Settings;

    public BoardPanel Panel { get; }

    public Window Window { get; }

    public BoardModel Model => Panel.Model!;

    public BoardView View => Panel.View!;

    /// <summary>Файл доски решения <see cref="Solution"/>.</summary>
    public string BoardFile => Path.Combine(Root, "Forms", ".arxis", "ui-designer", "board.json");

    /// <summary>
    /// Решение «Forms» на диске: проект App с разметкой, переданной как путь → текст.
    /// </summary>
    /// <param name="files">Файлы проекта: путь от его папки и содержимое.</param>
    public SolutionSnapshot Solution(params (string Include, string Text)[] files)
    {
        var solution = new ProjectWindowSolution("Forms", root: Root);
        var project = solution.Project("App");

        foreach (var (include, _) in files)
            solution.File(project, include, "AvaloniaXaml");

        solution.OnDisk();

        foreach (var (include, text) in files)
            File.WriteAllText(project.ProjectFilePath.Directory.Combine(include).Value, text);

        return solution.ToSnapshot();
    }

    /// <summary>Путь файла проекта App решения <see cref="Solution"/>.</summary>
    /// <param name="include">Путь от папки проекта.</param>
    public CanonicalPath PathOf(string include) =>
        CanonicalPath.Create(Path.Combine(Root, "Forms", "src", "App", include));

    /// <summary>Служба отдаёт снимок; доска дочитывает корни и встаёт.</summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="sequence">Номер состояния: растёт от публикации к публикации.</param>
    public async Task Open(SolutionSnapshot snapshot, long sequence = 1)
    {
        Projects.Publish(ProjectWindowStudio.Ready(sequence, snapshot));
        await Built();
    }

    /// <summary>Ждёт постройку доски и записи файла, а не время.</summary>
    public async Task Built()
    {
        Dispatcher.UIThread.RunJobs();
        await Model.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        Dispatcher.UIThread.RunJobs();
        await Model.Written.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public FormCard Card(string name) => Model.Cards.Single(card => card.Name == name);

    /// <summary>
    /// Даёт окнам кадр: попадание тяги идёт по сцене отрисовки, а новому окну её кладёт только такт.
    /// </summary>
    public static void Frame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Открывает тягу студии к доске — от её же вида, как открыл бы её сосед: несут пути.
    /// </summary>
    /// <param name="allowed">Что разрешает несущий.</param>
    /// <param name="files">Пути.</param>
    public IStudioDragSession Carry(DragDropEffects allowed, params string[] files)
    {
        Frame();

        return Drags.Begin(View, StudioDragData.FromFiles(files), allowed, new StudioDragVisual("…"));
    }

    /// <summary>Открывает тягу, разрешающую копию и ссылку, — как несёт окно проекта.</summary>
    /// <param name="files">Пути.</param>
    public IStudioDragSession Carry(params string[] files) =>
        Carry(DragDropEffects.Copy | DragDropEffects.Link, files);

    /// <summary>Точка холста в координатах вида — так её отдают сеансу тяги.</summary>
    /// <param name="inSheet">Точка в координатах холста.</param>
    public Point OnSheet(Point inSheet) => View.Sheet.TranslatePoint(inSheet, View)!.Value;

    /// <summary>Контейнер карточки на холсте; холст держит его, только пока карточка видна.</summary>
    public SurfaceItem Container(FormCard card) =>
        Assert.IsType<SurfaceItem>(View.Sheet.ContainerFromItem(card));

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

    /// <summary>Нажимает клавишу там, где стоит каретка.</summary>
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

    /// <summary>
    /// Нажимает переключатель так, как его нажимает мышь: он сперва переворачивает себя, потом сообщает
    /// о щелчке.
    /// </summary>
    public void Toggle(ToggleButton button)
    {
        button.IsChecked = button.IsChecked != true;
        Click(button);
    }

    /// <summary>Редактор документов модуля, подключённый к его контексту, — как его подключает студия.</summary>
    public FormEditor Editor()
    {
        var editor = new FormEditor();

        editor.Attach(_context);

        return editor;
    }

    /// <summary>
    /// Открывает форму редактором и ставит вкладку в окно вместо доски.
    /// </summary>
    /// <param name="include">Путь формы от папки проекта App.</param>
    public async Task<FormDocument> OpenTabAsync(string include)
    {
        var (view, error) = await Editor().OpenAsync(PathOf(include).Value);
        var document = Assert.IsType<FormDocument>(view);

        Assert.Null(error);

        Window.Content = document.Content;
        Dispatcher.UIThread.RunJobs();
        document.OnActivated();
        Dispatcher.UIThread.RunJobs();

        return document;
    }

    private Point Middle(Visual target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window);

        Assert.NotNull(at);

        return at.Value;
    }

    public void Dispose()
    {
        Panel.Release();
        Window.Close();
        _host.Dispose();
        TempFolder.Erase(Root);
    }

    /// <summary>Разметка окна.</summary>
    /// <param name="name">Имя класса.</param>
    /// <param name="size">Атрибуты размера.</param>
    public static string WindowXaml(string name, string size = "Width=\"800\" Height=\"450\"") =>
        $"""
         <Window xmlns="https://github.com/avaloniaui"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 x:Class="App.Views.{name}" {size}/>
         """;

    /// <summary>Разметка пользовательского элемента с размером времени разработки.</summary>
    /// <param name="name">Имя класса.</param>
    public static string ControlXaml(string name) =>
        $"""
         <UserControl xmlns="https://github.com/avaloniaui"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                      x:Class="App.Views.{name}" d:DesignWidth="320" d:DesignHeight="200"/>
         """;

    /// <summary>Разметка приложения: формой не бывает.</summary>
    public const string ApplicationXaml =
        """
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="App.App"/>
        """;
}
