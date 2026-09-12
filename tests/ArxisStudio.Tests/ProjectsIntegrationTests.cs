using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба проектов на настоящем MSBuild: решение из двух проектов, открытое с диска.
/// </summary>
/// <remarks>
/// Остальные тесты службы идут на провайдере теста — там точность. Здесь проверяется, что вся
/// дорога сходится с движком: регистрация MSBuild, чтение решения, папки, слежение. Фикстура
/// копируется во временную папку, чтобы подъём MSBuild по папкам не дошёл до настроек самой студии.
/// В общей очереди: регистрация MSBuild — одна на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsIntegrationTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-projects-hello-{Guid.NewGuid():N}");

    public ProjectsIntegrationTests() => Copy(Fixture(), _root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Настоящее решение восстанавливается и собирается.
    /// </summary>
    /// <remarks>
    /// Единственная проверка, доводящая операцию до настоящего MSBuild: остальное живёт на
    /// провайдере теста. Сборка идёт по копии фикстуры и уносится вместе с ней.
    /// </remarks>
    [Fact]
    public async Task A_real_solution_restores_and_builds()
    {
        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var opened = await studio.Projects.OpenAsync(Solution(), Token).WaitAsync(Patience, Token);

        Assert.True(opened.HasSnapshot, Why(opened.Diagnostics));

        var built = await studio.Build
            .RunAsync(ProjectOperationKind.Build, cancellationToken: Token)
            .WaitAsync(Patience, Token);

        await studio.Thread.IdleAsync();

        Assert.True(built.Status == ProjectOperationStatus.Succeeded, Why(built.Diagnostics));
        Assert.True(
            File.Exists(Path.Combine(_root, "src", "App", "bin", "Debug", "net10.0", "App.dll")),
            "сборка прошла, а собранного нет");
    }

    /// <summary>Настоящее решение открывается со своими проектами и папкой.</summary>
    [Fact]
    public async Task A_real_solution_opens_with_its_projects_and_its_folder()
    {
        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var result = await studio.Projects.OpenAsync(Solution(), Token).WaitAsync(Patience, Token);

        await studio.Thread.IdleAsync();

        Assert.True(result.HasSnapshot, string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}")));
        Assert.Equal(ProjectsState.Ready, studio.Projects.Status.State);

        var solution = result.Snapshot!;

        Assert.Equal(new[] { "App", "Lib" }, solution.Projects.Select(project => project.Name).Order());

        var folder = Assert.Single(solution.Folders);

        Assert.Equal("/src/", folder.Path);
        Assert.Equal(2, folder.Projects.Length);
        Assert.Contains(solution.Projects.Single(project => project.Name == "Lib").Items, item => item.FullPath.FileName == "Greeter.cs");
        Assert.Empty(studio.Strikes);
    }

    /// <summary>Файл, созданный на диске, появляется в модели после перезагрузки, которую он вызвал.</summary>
    [Fact]
    public async Task A_file_created_on_disk_appears_after_the_reload_it_causes()
    {
        using var studio = new ProjectsStudio(
            workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()),
            watch: static stale => new ProjectsWatch(stale, FileChangeCoalescingOptions.Default));

        await studio.Projects.OpenAsync(Solution(), Token).WaitAsync(Patience, Token);

        var extra = CanonicalPath.Create(Path.Combine(_root, "src", "Lib", "Extra.cs"));

        var reloaded = studio.WhenAsync(
            status => status.LastLoad?.Reason == ProjectsLoadReason.FileSystem
                && status.Snapshot?.Projects.Any(project => project.Items.Any(item => item.FullPath == extra)) == true,
            Patience);

        File.WriteAllText(extra.Value, "namespace Hello;\n\npublic static class Extra;\n");

        var status = await reloaded;

        Assert.Contains(extra, status.LastLoad!.Causes);
    }

    /// <summary>Решения нет на диске — и у настоящего движка это итог, а не исключение.</summary>
    [Fact]
    public async Task A_missing_solution_is_a_result_from_the_real_engine_too()
    {
        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var result = await studio.Projects
            .OpenAsync(CanonicalPath.Create(Path.Combine(_root, "Missing.slnx")), Token)
            .WaitAsync(Patience, Token);

        Assert.Equal(WorkspaceLoadStatus.Failed, result.Status);
        Assert.Equal(MSBuildDiagnosticCodes.ProjectFileNotFound, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(studio.Strikes);
    }

    private CanonicalPath Solution() => CanonicalPath.Create(Path.Combine(_root, "Hello.slnx"));

    /// <summary>Фикстура в репозитории: тесты бегут из bin, файлы лежат выше.</summary>
    private static string Fixture()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ArxisStudio.slnx")))
            folder = folder.Parent;

        Assert.True(folder is not null, "не нашёл корень репозитория");

        return Path.Combine(folder!.FullName, "tests", "Fixtures", "Projects", "Hello");
    }

    /// <summary>Диагностики одной строкой — чтобы падение говорило, что случилось.</summary>
    /// <param name="diagnostics">Диагностики.</param>
    private static string Why(IEnumerable<ProjectDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}"));

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
