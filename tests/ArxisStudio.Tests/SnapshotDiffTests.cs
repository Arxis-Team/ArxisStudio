using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сравнение снимков: что задела загрузка.
/// </summary>
/// <remarks>
/// Каждая загрузка строит все проекты заново, поэтому ссылки ничего не говорят, а сгенерированное
/// равенство записей с массивами сравнивает массивы по ссылке. Первая проверка здесь поэтому
/// главная: одинаковое, собранное дважды, — одинаково.
/// </remarks>
public class SnapshotDiffTests
{
    private static readonly WorkspaceLoadRequest Request = new()
    {
        EntryPointPath = ProjectsStudio.Solution(),
        Workspace = WorkspaceIdentity.New(),
    };

    /// <summary>Проекты, собранные дважды с тем же содержимым, не задеты.</summary>
    [Fact]
    public void Projects_rebuilt_with_the_same_content_are_not_touched()
    {
        var before = Rich();
        var after = Rich();

        Assert.NotSame(before, after);

        var step = SnapshotDiff.Step(before, after);

        Assert.Empty(step.Touched);
        Assert.False(step.SolutionChanged, "одинаковое решение, собранное дважды, назвалось изменившимся");
    }

    /// <summary>Новый файл задевает свой проект и больше ничего.</summary>
    [Fact]
    public void A_new_item_touches_its_project_and_nothing_else()
    {
        var step = SnapshotDiff.Step(Rich(), Rich(extraItem: "Extra.cs"));

        Assert.Equal("App.csproj", Assert.Single(step.Touched).ProjectFilePath.FileName);
        Assert.False(step.SolutionChanged, "файл в проекте назвал изменившимся всё решение");
    }

    /// <summary>Пришедший и ушедший проекты задеты оба.</summary>
    [Fact]
    public void A_project_that_came_and_one_that_went_are_both_touched()
    {
        var before = Solutions.Of(Request, [("App", Array.Empty<string>()), ("Lib", Array.Empty<string>())]).Snapshot!;
        var after = Solutions.Of(Request, [("App", Array.Empty<string>()), ("Tool", Array.Empty<string>())]).Snapshot!;

        var step = SnapshotDiff.Step(before, after);

        Assert.Equal(new[] { "Lib.csproj", "Tool.csproj" }, step.Touched.Select(project => project.ProjectFilePath.FileName).Order());
        Assert.True(step.SolutionChanged, "состав решения поменялся, а решение — нет");
    }

    /// <summary>Первый снимок и снимок другой сессии задевают всё.</summary>
    [Fact]
    public void A_first_snapshot_or_one_of_another_session_touches_everything()
    {
        var first = Solutions.Of(Request, [("App", Array.Empty<string>())]).Snapshot!;
        var fresh = SnapshotDiff.Step(null, first);

        Assert.True(fresh.SolutionChanged);
        Assert.Equal(first.Projects[0].Identity, Assert.Single(fresh.Touched));

        var other = Solutions.Of(Request with { Workspace = WorkspaceIdentity.New() }, [("App", Array.Empty<string>())]).Snapshot!;
        var step = SnapshotDiff.Step(first, other);

        Assert.True(step.SolutionChanged, "другая сессия не назвала решение другим");
        Assert.Equal(other.Projects[0].Identity, Assert.Single(step.Touched));
    }

    /// <summary>Переименованная папка решения меняет решение, но не его проекты.</summary>
    [Fact]
    public void A_renamed_solution_folder_changes_the_solution_but_not_its_projects()
    {
        var step = SnapshotDiff.Step(Rich(), Rich(folder: "source"));

        Assert.True(step.SolutionChanged);
        Assert.Empty(step.Touched);
    }

    /// <summary>
    /// Решение, в котором есть всё, где сгенерированное равенство подвело бы: псевдонимы,
    /// разрешённые пакеты, папки со списками проектов.
    /// </summary>
    private static SolutionSnapshot Rich(string? extraItem = null, string folder = "src")
    {
        var root = Request.EntryPointPath.Directory;
        var app = root.Combine(Path.Combine("App", "App.csproj"));
        var lib = root.Combine(Path.Combine("Lib", "Lib.csproj"));
        var appIdentity = ProjectIdentity.Create(Request.Workspace, app);
        var libIdentity = ProjectIdentity.Create(Request.Workspace, lib);

        var project = new ProjectSnapshotBuilder { Identity = appIdentity, ProjectFilePath = app, Name = "App" };

        project.Properties["TargetFramework"] = "net10.0";
        project.EvaluationInputs.Add(app);
        project.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Compile, Include = "Program.cs", FullPath = app.Directory.Combine("Program.cs") });

        if (extraItem is not null)
            project.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Compile, Include = extraItem, FullPath = app.Directory.Combine(extraItem) });

        project.ProjectReferences.Add(new ProjectReferenceInfo { ProjectFilePath = lib, Project = libIdentity, Aliases = ["global", "lib"] });
        project.AssemblyReferences.Add(new AssemblyReferenceInfo { Name = "Legacy", Aliases = ["legacy"] });
        project.ResolvedPackages.Add(new ResolvedPackage
        {
            PackageId = "Serilog",
            Version = "4.1.0",
            IsDirect = true,
            CompileAssemblies = [app.Directory.Combine("Serilog.dll")],
            RuntimeAssemblies = [app.Directory.Combine("Serilog.dll")],
            Dependencies = ["System.Memory"],
        });

        var library = new ProjectSnapshotBuilder { Identity = libIdentity, ProjectFilePath = lib, Name = "Lib" };

        var solution = new SolutionSnapshotBuilder
        {
            Workspace = Request.Workspace,
            Solution = SolutionIdentity.Create(Request.Workspace, Request.EntryPointPath),
            Name = "Hello",
            Request = Request,
        };

        solution.Projects.Add(project.ToSnapshot());
        solution.Projects.Add(library.ToSnapshot());
        solution.Folders.Add(new SolutionFolder { Name = folder, Path = $"/{folder}/", Projects = [appIdentity, libIdentity] });
        solution.Configurations.Add("Debug");
        solution.Configurations.Add("Release");

        return solution.ToSnapshot();
    }
}
