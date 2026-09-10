using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Что считать переменой состава проекта.
/// </summary>
/// <remarks>
/// Правило сверяется с диском в момент разбора, и проверяется оно здесь на диске из двух множеств:
/// на настоящих файлах тест проверял бы ещё и файловую систему с её задержками.
/// </remarks>
public class MembershipFilterTests
{
    private static readonly WorkspaceLoadRequest Request = new()
    {
        EntryPointPath = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "arxis-membership", "Hello.slnx")),
        Workspace = WorkspaceIdentity.New(),
    };

    /// <summary>Выход сборки, служебные папки и файлы, которых MSBuild не перечисляет, не разбираются.</summary>
    [Fact]
    public void Build_output_and_service_folders_are_not_candidates()
    {
        var snapshot = Snapshot();

        Assert.False(MembershipFilter.IsCandidate(snapshot, At("App", "bin", "Debug", "App.dll")));
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("App", "obj", "project.assets.json")));
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("App", "build", "Debug", "App.dll")), "свой OutputPath проекта не отброшен");
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("Lib", ".vs", "state.db")));
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("Lib", "web", "node_modules", "left-pad", "index.js")));
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("Lib", "Lib.csproj.user")));
        Assert.False(MembershipFilter.IsCandidate(snapshot, At("Elsewhere", "Note.cs")), "файл вне проектов разобран");

        Assert.True(MembershipFilter.IsCandidate(snapshot, At("Lib", "Models", "bin", "Thing.cs")), "bin не в корне проекта — обычная папка");
        Assert.True(MembershipFilter.IsCandidate(snapshot, At("Lib", ".editorconfig")), "файл с точкой проект перечисляет — это не служебная папка");
    }

    /// <summary>Временные файлы редакторов не разбираются.</summary>
    [Theory]
    [InlineData("Greeter.cs~RF1a2b.TMP")]
    [InlineData(".#Greeter.cs")]
    [InlineData("Greeter.cs___jb_tmp___")]
    [InlineData("~$Notes.docx")]
    [InlineData("Greeter.cs.swp")]
    [InlineData("Greeter.cs~")]
    public void Editor_temporary_files_are_not_candidates(string name)
    {
        Assert.False(MembershipFilter.IsCandidate(Snapshot(), At("Lib", name)));
    }

    /// <summary>Новый файл под проектом — перемена.</summary>
    [Fact]
    public void A_new_file_under_a_project_is_a_change()
    {
        var disk = new FakeDisk();
        var extra = At("Lib", "Extra.cs");

        disk.Files.Add(extra);

        Assert.Equal(extra, Assert.Single(MembershipFilter.Changed(Snapshot(), [extra], disk)));
    }

    /// <summary>Атомарное сохранение файла, который проект перечисляет, — не перемена.</summary>
    [Fact]
    public void An_atomic_save_of_a_listed_file_changes_nothing()
    {
        // Редактор переименовал прежний файл во временный, записал новый и удалил временный:
        // к разбору пачки файл на месте, а проект его уже перечислял.
        var disk = new FakeDisk();
        var greeter = At("Lib", "Greeter.cs");

        disk.Files.Add(greeter);

        Assert.Empty(MembershipFilter.Changed(Snapshot(), [greeter, At("Lib", "Greeter.cs~RF4a2.TMP")], disk));
    }

    /// <summary>Удалённая папка — перемена, только если проект что-то в ней перечислял.</summary>
    [Fact]
    public void A_deleted_folder_counts_only_when_the_project_listed_something_under_it()
    {
        var disk = new FakeDisk();

        Assert.Equal(At("App", "Views"), Assert.Single(MembershipFilter.Changed(Snapshot(), [At("App", "Views")], disk)));
        Assert.Empty(MembershipFilter.Changed(Snapshot(), [At("App", "Empty")], disk));
    }

    /// <summary>Новая папка — перемена, только если в ней лежит файл, которого проект не знает.</summary>
    [Fact]
    public void A_new_folder_counts_only_when_it_holds_a_file_the_project_does_not_list()
    {
        var disk = new FakeDisk();

        disk.Directories.Add(At("Lib", "Models"));
        disk.Files.Add(At("Lib", "Models", "Thing.cs"));
        disk.Directories.Add(At("Lib", "Assets"));

        Assert.Equal(At("Lib", "Models"), Assert.Single(MembershipFilter.Changed(Snapshot(), [At("Lib", "Models"), At("Lib", "Assets")], disk)));
    }

    /// <summary>Файл принадлежит ближайшему проекту.</summary>
    /// <remarks>
    /// Иначе каждое сохранение во вложенном проекте сверялось бы с внешним, который этот файл не
    /// перечисляет, — и будило бы перезагрузку.
    /// </remarks>
    [Fact]
    public void A_file_belongs_to_the_nearest_project()
    {
        var snapshot = Snapshot();

        Assert.Equal("Tool", MembershipFilter.Owner(snapshot, At("App", "Tools", "Tool.cs"))?.Name);
        Assert.Equal("App", MembershipFilter.Owner(snapshot, At("App", "Program.cs"))?.Name);

        var disk = new FakeDisk();

        disk.Files.Add(At("App", "Tools", "Tool.cs"));

        Assert.Empty(MembershipFilter.Changed(snapshot, [At("App", "Tools", "Tool.cs")], disk));
    }

    /// <summary>Следят за верхними папками проектов: вложенная отдельного наблюдателя не получает.</summary>
    [Fact]
    public void The_roots_are_the_topmost_project_folders()
    {
        Assert.Equal(new[] { At("App"), At("Lib") }, MembershipFilter.Roots(Snapshot()));
    }

    private static CanonicalPath At(params string[] parts) =>
        Request.EntryPointPath.Directory.Combine(Path.Combine(parts));

    /// <summary>App с перенесённым выходом сборки, вложенный в него Tool и Lib рядом.</summary>
    private static SolutionSnapshot Snapshot()
    {
        var solution = new SolutionSnapshotBuilder { Workspace = Request.Workspace, Name = "Hello", Request = Request };

        solution.Projects.Add(Project(
            "App",
            "App",
            ["Program.cs", Path.Combine("Views", "Main.cs")],
            ("OutputPath", Path.Combine("build", "Debug") + Path.DirectorySeparatorChar)));

        solution.Projects.Add(Project(Path.Combine("App", "Tools"), "Tool", ["Tool.cs"]));
        solution.Projects.Add(Project("Lib", "Lib", ["Greeter.cs"]));

        return solution.ToSnapshot();
    }

    private static ProjectSnapshot Project(string folder, string name, string[] items, params (string Key, string Value)[] properties)
    {
        var file = At(folder, name + ".csproj");

        var project = new ProjectSnapshotBuilder
        {
            Identity = ProjectIdentity.Create(Request.Workspace, file),
            ProjectFilePath = file,
            Name = name,
        };

        foreach (var item in items)
            project.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Compile, Include = item, FullPath = file.Directory.Combine(item) });

        foreach (var (key, value) in properties)
            project.Properties[key] = value;

        return project.ToSnapshot();
    }
}
