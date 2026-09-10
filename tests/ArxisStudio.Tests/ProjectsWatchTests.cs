using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Слежение за диском на настоящих файлах: обе дороги доходят до «устарело».
/// </summary>
/// <remarks>
/// Склейка укорочена, а ожидание — сигналом с потолком, а не паузой: тест кончается, как только
/// слежение сказало своё.
/// </remarks>
public class ProjectsWatchTests : IDisposable
{
    private static readonly FileChangeCoalescingOptions Quick = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(50),
        MaximumDelay = TimeSpan.FromMilliseconds(500),
    };

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-projects-watch-{Guid.NewGuid():N}")).FullName;

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
    /// Правка файла решения доходит до «устарело» и называет его.
    /// </summary>
    /// <remarks>
    /// Решение — не вход оценки ни одного проекта, и следить за ним служба обязана отдельно:
    /// пропавший из решения проект — ровно та перемена, о которой стоит услышать.
    /// </remarks>
    [Fact]
    public async Task An_edit_of_the_solution_file_asks_for_a_reload_and_names_it()
    {
        var snapshot = OnDisk();
        var stale = new TaskCompletionSource<ImmutableArray<CanonicalPath>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = new ProjectsWatch(causes => stale.TrySetResult(causes), Quick);

        watch.Follow(snapshot);

        File.AppendAllText(snapshot.EntryPoint.Path.Value, "<!-- правка -->");

        var causes = await stale.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Contains(snapshot.EntryPoint.Path, causes);
    }

    /// <summary>
    /// Файл, появившийся под проектом, доходит до «устарело», хотя ни один вход оценки не менялся.
    /// </summary>
    [Fact]
    public async Task A_file_created_under_a_project_asks_for_a_reload_and_names_it()
    {
        var snapshot = OnDisk();
        var stale = new TaskCompletionSource<ImmutableArray<CanonicalPath>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = new ProjectsWatch(causes => stale.TrySetResult(causes), Quick);

        watch.Follow(snapshot);

        var extra = Path.Combine(_root, "Lib", "Extra.cs");

        File.WriteAllText(extra, "class Extra { }");

        var causes = await stale.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Contains(CanonicalPath.Create(extra), causes);
    }

    /// <summary>Решение и проект Lib на диске.</summary>
    private SolutionSnapshot OnDisk()
    {
        var solution = Path.Combine(_root, "Hello.slnx");

        File.WriteAllText(solution, "<Solution />");
        Directory.CreateDirectory(Path.Combine(_root, "Lib"));
        File.WriteAllText(Path.Combine(_root, "Lib", "Lib.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "Lib", "Greeter.cs"), "class Greeter { }");

        var request = new WorkspaceLoadRequest
        {
            EntryPointPath = CanonicalPath.Create(solution),
            Workspace = WorkspaceIdentity.New(),
        };

        return Solutions.Of(request, [("Lib", new[] { "Greeter.cs" })]).Snapshot!;
    }
}
