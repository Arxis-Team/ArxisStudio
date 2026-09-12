using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.NuGet;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба пакетов: что правится в файле проекта, когда и что при этом говорится.
/// </summary>
/// <remarks>
/// Файлы здесь настоящие — правку проверять больше не на чем, — а движок остаётся провайдером
/// теста: восстановление в конце правки кончается тогда, когда тест велел, и его провал
/// разыгрывается, а не ждётся от NuGet. Саму правку XML проверяет подмодуль; здесь — что служба
/// выбрала верный вид правки, нашла файл версий и довела дело до модели. В общей очереди: модуль
/// поднимается хостом.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsPackagesTests : IDisposable
{
    private const string Bare = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

        </Project>
        """;

    private const string WithSerilog = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Serilog" Version="3.0.0" />
          </ItemGroup>

        </Project>
        """;

    private const string Versions = """
        <Project>

          <ItemGroup>
            <PackageVersion Include="Xunit" Version="2.9.0" />
          </ItemGroup>

        </Project>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-packages-{Guid.NewGuid():N}")).FullName;

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

    /// <summary>Пакет, которого в проекте не было, ставится и восстанавливается.</summary>
    [Fact]
    public async Task A_package_the_project_does_not_have_is_installed_and_restored()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project);

        var result = await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Contains("""<PackageReference Include="Serilog" Version="4.1.0" />""", File.ReadAllText(project.Value), StringComparison.Ordinal);

        var restore = Assert.Single(studio.Provider.Operations);

        Assert.Equal(ProjectOperationKind.Restore, restore.Kind);
        Assert.Equal(project, restore.EntryPointPath);
    }

    /// <summary>Установка поверх объявленного — это смена версии, а не вторая ссылка.</summary>
    [Fact]
    public async Task Installing_over_a_declared_package_changes_its_version()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", WithSerilog);
        var identity = await OpenAsync(studio, project, Declared("Serilog", "3.0.0"));

        var result = await studio.Packages.InstallAsync(identity, "serilog", "4.1.0", Token);
        var text = File.ReadAllText(project.Value);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Contains("""Include="Serilog" Version="4.1.0" """.TrimEnd(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("3.0.0", text, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(text, "PackageReference Include=\"Serilog\""));
    }

    /// <summary>Ссылку, пришедшую импортом, служба не трогает.</summary>
    [Fact]
    public async Task A_reference_that_comes_from_an_import_is_refused()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project, Imported("Serilog", "3.0.0"));

        var result = await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);

        Assert.Equal(ProjectsDiagnosticCodes.ReferenceNotInProjectFile, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(Bare, File.ReadAllText(project.Value));
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Пакет убирается из проекта.</summary>
    [Fact]
    public async Task A_package_is_taken_out_of_the_project()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", WithSerilog);
        var identity = await OpenAsync(studio, project, Declared("Serilog", "3.0.0"));

        var result = await studio.Packages.UninstallAsync(identity, "Serilog", Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.DoesNotContain("Serilog", File.ReadAllText(project.Value), StringComparison.Ordinal);
        Assert.Single(studio.Provider.Operations);
    }

    /// <summary>У проекта с централизованными версиями версия уходит в их файл.</summary>
    [Fact]
    public async Task Central_package_management_writes_the_version_into_its_own_file()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var versions = Write("Directory.Packages.props", Versions);
        var identity = await OpenAsync(studio, project, versions: versions);

        var result = await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Contains("""<PackageReference Include="Serilog" />""", File.ReadAllText(project.Value), StringComparison.Ordinal);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.1.0" />""", File.ReadAllText(versions.Value), StringComparison.Ordinal);
    }

    /// <summary>Провалившееся восстановление возвращает проект на место.</summary>
    [Fact]
    public async Task A_failed_restore_puts_the_project_back()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project);

        studio.Provider.Executed = _ => ProjectOperationResult.Failed(
            new ProjectDiagnostic("APS2001", "восстановление не прошло", ProjectDiagnosticSeverity.Error));

        var result = await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);

        Assert.True(result.HasErrors, "восстановление провалилось, а правка назвалась удачной");
        Assert.Equal(Bare, File.ReadAllText(project.Value));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ChangeUndone);
    }

    /// <summary>Удачная правка перечитывает модель своей причиной.</summary>
    [Fact]
    public async Task A_successful_edit_rereads_the_model_with_its_own_reason()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project);

        Assert.Equal(1, studio.Provider.Loads);

        await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);

        Assert.Equal(2, studio.Provider.Loads);
        Assert.Equal(ProjectsLoadReason.Packages, studio.Projects.Status.LastLoad?.Reason);
    }

    /// <summary>Правка того, что не открыто.</summary>
    [Fact]
    public async Task Nothing_open_is_refused_with_the_code_of_the_service()
    {
        using var studio = new ProjectsStudio();

        var result = await studio.Packages.InstallAsync(default, "Serilog", "4.1.0", Token);

        Assert.Equal(ProjectsDiagnosticCodes.NothingOpen, Assert.Single(result.Diagnostics).Code);
    }

    /// <summary>Правка проекта не из открытого решения.</summary>
    [Fact]
    public async Task A_project_that_is_not_open_is_refused()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);

        await OpenAsync(studio, project);

        var stranger = ProjectIdentity.Create(
            studio.Projects.Current!.Workspace,
            CanonicalPath.Create(Path.Combine(_root, "Other.csproj")));

        var result = await studio.Packages.InstallAsync(stranger, "Serilog", "4.1.0", Token);

        Assert.Equal(ProjectsDiagnosticCodes.ProjectNotOpen, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Пустое имя пакета и пустая версия — ошибка вызова, а не результат.</summary>
    [Fact]
    public async Task An_empty_package_id_or_version_is_an_argument_error()
    {
        using var studio = new ProjectsStudio();

        await Assert.ThrowsAsync<ArgumentException>(
            () => studio.Packages.InstallAsync(default, " ", "4.1.0", Token));

        await Assert.ThrowsAsync<ArgumentException>(
            () => studio.Packages.InstallAsync(default, "Serilog", "", Token));

        await Assert.ThrowsAsync<ArgumentException>(
            () => studio.Packages.UninstallAsync(default, "", Token));
    }

    /// <summary>Для тех, кто смотрит за сборкой, правка выглядит восстановлением: полоса у них общая.</summary>
    [Fact]
    public async Task An_edit_shows_up_as_a_restore_for_those_watching_builds()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project);

        var seen = new List<ProjectOperationEventArgs>();

        studio.Build.Started += (_, change) => seen.Add(change);

        await studio.Packages.InstallAsync(identity, "Serilog", "4.1.0", Token);
        await studio.Thread.IdleAsync();

        var started = Assert.Single(seen);

        Assert.Equal(ProjectOperationKind.Restore, started.Operation.Kind);
        Assert.Equal(identity, Assert.Single(started.Operation.Projects));
    }

    /// <summary>Убрать то, чего в проекте нет, — не ошибка, но и не работа.</summary>
    [Fact]
    public async Task Removing_a_package_the_project_does_not_have_changes_nothing()
    {
        using var studio = new ProjectsStudio();

        var project = Write("App.csproj", Bare);
        var identity = await OpenAsync(studio, project);

        var result = await studio.Packages.UninstallAsync(identity, "Serilog", Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Equal(PackageDiagnosticCodes.NothingToChange, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(Bare, File.ReadAllText(project.Value));
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Кладёт файл на диск и отдаёт путь к нему.</summary>
    /// <param name="name">Имя файла.</param>
    /// <param name="content">Что в нём.</param>
    private CanonicalPath Write(string name, string content)
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, content);

        return CanonicalPath.Create(path);
    }

    /// <summary>Открывает решение с одним проектом на диске и отдаёт его идентичность.</summary>
    /// <param name="studio">Студия.</param>
    /// <param name="project">Файл проекта.</param>
    /// <param name="references">Что проект объявляет.</param>
    /// <param name="versions">Файл централизованных версий; пусто — версии рядом со ссылкой.</param>
    private static async Task<ProjectIdentity> OpenAsync(
        ProjectsStudio studio,
        CanonicalPath project,
        PackageReferenceInfo? references = null,
        CanonicalPath versions = default)
    {
        studio.Provider.Answer = request =>
        {
            var snapshot = new ProjectSnapshotBuilder
            {
                Identity = ProjectIdentity.Create(request.Workspace, project),
                ProjectFilePath = project,
                Name = "App",
                ProviderName = "Scripted",
            };

            if (references is not null)
                snapshot.PackageReferences.Add(references);

            if (!versions.IsEmpty)
            {
                snapshot.Properties["ManagePackageVersionsCentrally"] = "true";
                snapshot.Properties["DirectoryPackagesPropsPath"] = versions.Value;
            }

            var solution = new SolutionSnapshotBuilder
            {
                Workspace = request.Workspace,
                Solution = SolutionIdentity.Create(request.Workspace, request.EntryPointPath),
                Name = "Packages",
                ProviderName = "Scripted",
                Request = request,
            };

            solution.Projects.Add(snapshot.ToSnapshot());

            return WorkspaceLoadResult.Success(solution.ToSnapshot());
        };

        var opened = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        return Assert.Single(opened.Snapshot!.Projects).Identity;
    }

    /// <summary>Ссылка, объявленная в самом файле проекта.</summary>
    /// <param name="packageId">Пакет.</param>
    /// <param name="version">Версия.</param>
    private static PackageReferenceInfo Declared(string packageId, string version) =>
        new() { PackageId = packageId, VersionText = version, Origin = ProjectItemOrigin.Declared };

    /// <summary>Ссылка, пришедшая импортом.</summary>
    /// <param name="packageId">Пакет.</param>
    /// <param name="version">Версия.</param>
    private static PackageReferenceInfo Imported(string packageId, string version) =>
        new() { PackageId = packageId, VersionText = version, Origin = ProjectItemOrigin.Imported };

    /// <summary>Сколько раз строка встречается в тексте.</summary>
    /// <param name="text">Где искать.</param>
    /// <param name="what">Что искать.</param>
    private static int Occurrences(string text, string what)
    {
        var count = 0;

        for (var index = text.IndexOf(what, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(what, index + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
