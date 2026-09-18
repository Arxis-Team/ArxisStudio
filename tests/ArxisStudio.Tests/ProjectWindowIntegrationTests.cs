using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Дерево окна проекта из настоящего снимка MSBuild.
/// </summary>
/// <remarks>
/// Руками собранный снимок показывает только то, что в него положили. Настоящий приносит всё, что
/// вычисляет SDK: кандидатов <c>PotentialEditorConfigFiles</c> на каждую папку до корня диска,
/// служебные элементы, ссылки на платформу и анализаторы. До проверки диска дерево показывало эти
/// фантомы файлами проекта; здесь видно, что их нет. Фикстура копируется во временную папку, как у
/// интеграционных тестов службы; в общей очереди — регистрация MSBuild одна на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowIntegrationTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-hello-{Guid.NewGuid():N}");

    public ProjectWindowIntegrationTests() => ProjectsIntegrationTests.Copy(ProjectsIntegrationTests.Fixture(), _root);

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
    /// Настоящее решение показывает свои папки и файлы — и ничего сверх них.
    /// </summary>
    [Fact]
    public async Task A_real_solution_shows_its_files_and_nothing_else()
    {
        var token = TestContext.Current.CancellationToken;

        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var result = await studio.Projects
            .OpenAsync(CanonicalPath.Create(Path.Combine(_root, "Hello.slnx")), token)
            .WaitAsync(Patience, token);

        Assert.True(result.HasSnapshot, string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}")));

        var snapshot = result.Snapshot!;
        var tree = SolutionTree.Build(snapshot, DiskProbe.Present(snapshot, token), Words.English);

        // Зависимости — дело SDK: состав анализаторов меняется от версии к версии, и форма дерева
        // сверяется без их содержимого.
        Assert.Equal(
            """
            Hello · 2 projects
              src
                App
                  Dependencies net10.0
                  Program.cs
                Lib
                  Dependencies net10.0
                  Greeter.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(tree, dependencies: false));

        var app = tree.Descendants().Single(node => node is { Kind: NodeKind.Project, Name: "App" });
        var groups = app.Children[0].Children;

        Assert.Contains(groups, group => group.Name == "Frameworks" && group.Children.Any(framework => framework.Name == "Microsoft.NETCore.App"));
        Assert.Contains(groups, group => group.Name == "Projects" && group.Children.Any(project => project.Name == "Lib"));
        Assert.DoesNotContain(tree.Descendants(), node => node.Name is ".editorconfig" or ".globalconfig" or "bin" or "obj");
    }
}
