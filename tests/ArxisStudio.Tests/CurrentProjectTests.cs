using ArxisStudio.Extensibility;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Открытый проект глазами студии: путь для контекста плагина и слово «открыли» для недавних.
/// </summary>
/// <remarks>
/// Служба проектов здесь подделана: настоящая живёт в модуле и приходит экспортом, а студии от неё
/// нужны ровно две вещи. Их и проверяем — не движок и не модель.
/// </remarks>
public class CurrentProjectTests
{
    private static readonly CanonicalPath Solution =
        CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Волна", "Волна.slnx"));

    private static readonly CanonicalPath Another =
        CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Другая", "Другая.slnx"));

    /// <summary>Путь идёт за тем, что открыла служба.</summary>
    [Fact]
    public void The_path_follows_what_the_service_opened()
    {
        var project = new CurrentProject();
        var seen = new List<string?>();

        project.Changed += (_, path) => seen.Add(path);
        project.Apply(Ready(1, Solution));

        Assert.Equal(Solution.Value, project.Path);
        Assert.Equal([Solution.Value], seen);
    }

    /// <summary>Закрытие оставляет студию без пути.</summary>
    [Fact]
    public void Closing_leaves_the_studio_without_a_path()
    {
        var project = new CurrentProject();
        var seen = new List<string?>();

        project.Apply(Ready(1, Solution));
        project.Changed += (_, path) => seen.Add(path);
        project.Apply(ProjectsStatus.Closed);

        Assert.Null(project.Path);
        Assert.Equal([null], seen);
    }

    /// <summary>
    /// Перезагрузка не считается новым открытием.
    /// </summary>
    /// <remarks>
    /// Недавние отмечают открытие, а перезагрузок на одну сессию приходится сколько угодно: за
    /// каждой правкой файла проекта, за сменой конфигурации, за восстановлением пакетов.
    /// </remarks>
    [Fact]
    public void A_reload_is_not_a_second_opening()
    {
        var project = new CurrentProject();
        var opened = new List<string>();

        project.Opened += (_, path) => opened.Add(path);

        project.Apply(Ready(1, Solution));
        project.Apply(Ready(1, Solution) with { Sequence = 2 });

        Assert.Equal([Solution.Value], opened);
    }

    /// <summary>Другая сессия — другое открытие.</summary>
    [Fact]
    public void A_new_session_opens_again()
    {
        var project = new CurrentProject();
        var opened = new List<string>();

        project.Opened += (_, path) => opened.Add(path);

        project.Apply(Ready(1, Solution));
        project.Apply(Ready(2, Another));

        Assert.Equal([Solution.Value, Another.Value], opened);
    }

    /// <summary>
    /// Не прочитавшийся проект открытым не считается.
    /// </summary>
    /// <remarks>
    /// В недавних он был бы обещанием вернуться к тому же провалу; путь при этом студия знает —
    /// проектные настройки едут за ним, открылся он или нет.
    /// </remarks>
    [Fact]
    public void A_project_that_did_not_read_is_not_opened()
    {
        var project = new CurrentProject();
        var opened = new List<string>();

        project.Opened += (_, path) => opened.Add(path);

        project.Apply(new ProjectsStatus
        {
            Sequence = 1,
            Session = 1,
            State = ProjectsState.Failed,
            EntryPoint = Solution,
        });

        Assert.Equal(Solution.Value, project.Path);
        Assert.Empty(opened);
    }

    /// <summary>Подписка берёт и то, что служба успела открыть раньше.</summary>
    [Fact]
    public void Following_a_service_takes_what_it_already_opened()
    {
        var projects = new ProjectsProbe();
        var project = new CurrentProject();

        projects.Publish(Ready(1, Solution));
        project.Follow(projects);

        Assert.Equal(Solution.Value, project.Path);

        projects.Publish(Ready(2, Another));

        Assert.Equal(Another.Value, project.Path);
    }

    /// <summary>
    /// Путь в контексте плагина живой.
    /// </summary>
    /// <remarks>
    /// Контекст выдают один раз, при подъёме, а проект человек открывает потом — и плагин, который
    /// спросил путь после открытия, обязан получить его, а не то, чего не было при подъёме.
    /// </remarks>
    [Fact]
    public void The_project_path_in_a_context_is_alive()
    {
        var home = Path.Combine(Path.GetTempPath(), $"arxis-context-{Guid.NewGuid():N}");
        var project = new CurrentProject();

        try
        {
            var factory = new StudioContextFactory(
                new StudioLog(),
                new StudioCommands(),
                () => project.Path,
                settings: new PluginSettingsStore(userFile: Path.Combine(home, "plugin-settings.json")));

            var context = factory.Create(new InstalledPlugin(
                AppContext.BaseDirectory,
                new PluginManifest { Id = "arxis.one", Name = "Один" },
                null,
                IsEnabled: true,
                IsBuiltIn: true));

            Assert.Null(context.ProjectPath);

            project.Apply(Ready(1, Solution));

            Assert.Equal(Solution.Value, context.ProjectPath);
        }
        finally
        {
            if (Directory.Exists(home))
                Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>Состояние службы: открыто и прочитано.</summary>
    /// <param name="session">Номер сессии.</param>
    /// <param name="entryPoint">Что открыто.</param>
    private static ProjectsStatus Ready(long session, CanonicalPath entryPoint) => new()
    {
        Sequence = session,
        Session = session,
        State = ProjectsState.Ready,
        EntryPoint = entryPoint,
    };
}

/// <summary>Служба проектов, которой управляет тест.</summary>
/// <remarks>
/// Отдаёт ровно то, что ей велели, и тем же событием, что настоящая. Методы, до которых студия не
/// доходит, честно отказывают: позови их кто-нибудь — это будет видно, а не тихо сойдёт.
/// </remarks>
internal sealed class ProjectsProbe : IStudioProjects
{
    /// <inheritdoc/>
    public ProjectsStatus Status { get; private set; } = ProjectsStatus.Closed;

    /// <inheritdoc/>
    public SolutionSnapshot? Current => Status.Snapshot;

    /// <inheritdoc/>
    public event EventHandler<ProjectsChangedEventArgs>? Changed;

    /// <summary>Отдаёт новое состояние, как отдала бы настоящая служба.</summary>
    /// <param name="status">Что стало.</param>
    public void Publish(ProjectsStatus status)
    {
        var previous = Status;

        Status = status;
        Changed?.Invoke(this, new ProjectsChangedEventArgs(previous, status, [], [], [], solutionChanged: true));
    }

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> OpenAsync(CanonicalPath entryPoint, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> ReloadAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> SetConfigurationAsync(string? configuration, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task CloseAsync() => throw new NotSupportedException();
}
