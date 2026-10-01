using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
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

    public UiDesignerStudio(bool service = true)
    {
        var exports = new StudioExportRegistry();

        if (service)
            exports.Publish(typeof(IStudioProjects), Projects, "arxis.projects", "Проекты");

        var services = new Dictionary<Type, object> { [typeof(IStudioDocuments)] = Documents };
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
