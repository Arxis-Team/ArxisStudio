using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба файлов на настоящем MSBuild: что правка файла проекта значит для модели.
/// </summary>
/// <remarks>
/// Провайдер теста снимок собирает сам и файла проекта не читает, поэтому переписанная ссылка
/// проверяется только здесь — тем, что из неё вычислил движок. Фикстура копируется во временную
/// папку; в общей очереди, потому что регистрация MSBuild одна на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsFilesIntegrationTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-projects-files-hello-{Guid.NewGuid():N}");

    /// <summary>
    /// Копия фикстуры, у приложения которой есть файл с метаданными и окно с вложенным кодом.
    /// </summary>
    public ProjectsFilesIntegrationTests()
    {
        ProjectsIntegrationTests.Copy(ProjectsIntegrationTests.Fixture(), _root);

        Directory.CreateDirectory(At("Views").Value);
        File.WriteAllText(At("appsettings.json").Value, "{ }");
        File.WriteAllText(At("Views/MainWindow.axaml").Value, "<Window />");
        File.WriteAllText(At("Views/MainWindow.axaml.cs").Value, "namespace Hello;\n\npartial class MainWindow;\n");

        var project = At("App.csproj").Value;

        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", """
              <ItemGroup>
                <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
                <Compile Update="Views\MainWindow.axaml.cs">
                  <DependentUpon>MainWindow.axaml</DependentUpon>
                </Compile>
              </ItemGroup>

            </Project>
            """, StringComparison.Ordinal));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    /// Переименованное сохраняет в модели то, что о нём написано в файле проекта, а удалённое
    /// уносит свою запись.
    /// </summary>
    /// <remarks>
    /// Без переписанной ссылки <c>settings.json</c> молча перестал бы копироваться в выход, а
    /// <c>Main.axaml.cs</c> — стоять под своей разметкой: маска SDK находит файл под новым именем,
    /// но метаданные остаются у старого.
    /// </remarks>
    [Fact]
    public async Task A_renamed_file_keeps_what_the_project_file_says_about_it()
    {
        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var opened = await studio.Projects.OpenAsync(CanonicalPath.Create(Path.Combine(_root, "Hello.slnx")), Token)
            .WaitAsync(Patience, Token);

        Assert.True(opened.HasSnapshot, Why(opened.Diagnostics));

        await Succeeds(studio.Files.MoveAsync([Pair("appsettings.json", "settings.json")], "Переименование appsettings.json", Token));

        var settings = Assert.Single(App(studio).Items, item => item.FullPath == At("settings.json"));

        Assert.Equal("PreserveNewest", settings.Metadata.GetValueOrDefault("CopyToOutputDirectory"));
        Assert.Equal(ProjectsLoadReason.Files, studio.Projects.Status.LastLoad?.Reason);

        await Succeeds(studio.Files.MoveAsync(
        [
            Pair("Views/MainWindow.axaml", "Views/Main.axaml"),
            Pair("Views/MainWindow.axaml.cs", "Views/Main.axaml.cs"),
        ], "Переименование MainWindow.axaml", Token));

        var code = Assert.Single(App(studio).Items, item => item.ItemType == ProjectItemTypes.Compile && item.FullPath == At("Views/Main.axaml.cs"));

        Assert.Equal("Main.axaml", code.Metadata.GetValueOrDefault("DependentUpon"));

        await Succeeds(studio.Files.DeleteAsync([At("Views")], "Удаление Views", Token));

        Assert.DoesNotContain(App(studio).Items, item => item.FullPath.StartsWith(At("Views")));
        Assert.DoesNotContain("Views", File.ReadAllText(At("App.csproj").Value), StringComparison.Ordinal);
        Assert.Empty(studio.Strikes);
    }

    /// <summary>
    /// Созданный файл уже в модели, когда служба вернулась: SDK-проект взял его своей маской, и
    /// каталог, который он завёл, лежит на диске.
    /// </summary>
    [Fact]
    public async Task A_created_file_is_in_the_model_when_the_call_returns()
    {
        using var studio = new ProjectsStudio(workspace: static () => new ProjectWorkspace(new MSBuildProjectProvider()));

        var opened = await studio.Projects.OpenAsync(CanonicalPath.Create(Path.Combine(_root, "Hello.slnx")), Token)
            .WaitAsync(Patience, Token);

        Assert.True(opened.HasSnapshot, Why(opened.Diagnostics));

        await Succeeds(studio.Files.CreateAsync(
            [new FileCreation(At("Models/Person.cs")) { Content = "namespace Hello.Models;\n\nclass Person;\n"u8.ToArray() }],
            "Создание Person.cs", Token));

        Assert.Single(App(studio).Items, item => item.ItemType == ProjectItemTypes.Compile && item.FullPath == At("Models/Person.cs"));
        Assert.Equal(ProjectsLoadReason.Files, studio.Projects.Status.LastLoad?.Reason);
        Assert.Empty(studio.Strikes);
    }

    private static ProjectSnapshot App(ProjectsStudio studio) =>
        Assert.Single(studio.Projects.Status.Snapshot?.Projects ?? [], project => project.Name == "App");

    private CanonicalPath At(string relative) =>
        CanonicalPath.Create(Path.Combine(_root, "src", "App", relative.Replace('/', Path.DirectorySeparatorChar)));

    private FileMove Pair(string from, string to) => new(At(from), At(to));

    private static async Task Succeeds(Task<ProjectOperationResult> operation)
    {
        var result = await operation.WaitAsync(Patience, Token);

        Assert.False(result.HasErrors, Why(result.Diagnostics));
    }

    private static string Why(IEnumerable<ProjectDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}"));
}
