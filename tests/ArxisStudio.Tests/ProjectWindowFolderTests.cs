using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Avalonia.Headless.XUnit;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// Пустые папки окна проекта и слежение за ними: какая папка стоит вопроса к диску, что сигнал
/// доходит с настоящего диска и что окно с ним делает.
/// </summary>
/// <remarks>
/// Пустую папку дерево берёт с диска, а служба проектов о ней молчит — состав проекта от неё не
/// меняется. Поэтому окно следит за папками само, и проверяется это на настоящих папках во временной
/// папке теста, а ожидание — сигналом с потолком, а не паузой.
/// </remarks>
public class ProjectWindowFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-project-window-folders-{Guid.NewGuid():N}")).FullName;

    /// <inheritdoc/>
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
    /// Вопроса стоит только папка, которую показало бы дерево ближайшего проекта: выход сборки,
    /// служебная и ничья — нет.
    /// </summary>
    /// <remarks>
    /// Иначе каждая сборка, раскладывающая <c>obj</c>, перестраивала бы дерево. Проект, лежащий в
    /// папке другого, судит о своих папках сам: его <c>bin</c> — выход сборки, хоть для внешнего это
    /// просто папка.
    /// </remarks>
    [Fact]
    public void Only_a_folder_the_tree_would_show_is_worth_asking_about()
    {
        var solution = new ProjectWindowSolution(root: _root);
        var app = solution.Project("App");
        var tests = solution.Project("Tests", "src/App");

        app.Properties["OutputPath"] = "build\\Debug\\";

        using var watch = new FolderWatch(() => { });

        watch.Follow(solution.ToSnapshot());

        var home = app.ProjectFilePath.Directory;
        var nested = tests.ProjectFilePath.Directory;

        Assert.True(watch.Matters(home.Combine("Assets")), "папку проекта не спросили бы");
        Assert.True(watch.Matters(home.Combine("Views/Empty")), "папку в папке не спросили бы");
        Assert.True(watch.Matters(nested.Combine("Empty")), "папку вложенного проекта не спросили бы");

        foreach (var path in new[]
                 {
                     home.Combine("bin/Debug"), home.Combine("obj"), home.Combine("build"), home.Combine(".vs/App"),
                     home.Combine("Views/.cache"), nested.Combine("bin"), home, solution.Home.Combine("docs"),
                 })
        {
            Assert.False(watch.Matters(path), $"{path} спросили бы зря");
        }
    }

    /// <summary>
    /// Папка, появившаяся под проектом, доходит сигналом, и пропавшая — тоже.
    /// </summary>
    [Fact]
    public async Task A_folder_made_or_removed_under_a_project_is_heard()
    {
        var solution = new ProjectWindowSolution(root: _root);
        var app = solution.Project("App");

        solution.File(app, "Program.cs");
        solution.OnDisk();

        using var heard = new SemaphoreSlim(0);
        using var watch = new FolderWatch(() => heard.Release(), TimeSpan.FromMilliseconds(50));

        watch.Follow(solution.ToSnapshot());

        var assets = app.ProjectFilePath.Directory.Combine("Assets").Value;
        var token = TestContext.Current.CancellationToken;

        Directory.CreateDirectory(assets);

        Assert.True(await heard.WaitAsync(TimeSpan.FromSeconds(30), token), "новую папку не услышали");

        Directory.Delete(assets);

        Assert.True(await heard.WaitAsync(TimeSpan.FromSeconds(30), token), "пропавшую папку не услышали");
    }

    /// <summary>
    /// Папка, заведённая мимо студии, встаёт в дерево без перезагрузки модели, а убранная — уходит.
    /// </summary>
    /// <remarks>
    /// Служба проектов о пустой папке не скажет, и окно слышит её само: служба не получила ни одной
    /// просьбы перечитать модель.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_folder_made_or_removed_outside_the_studio_comes_and_goes()
    {
        using var studio = new ProjectWindowStudio();

        var snapshot = studio.Solution();

        await studio.Open(snapshot);

        var fonts = Fonts(snapshot);

        Directory.CreateDirectory(fonts.Value);

        await Settled(studio, () => studio.Rows.Any(row => row.Name == "Fonts"));

        Directory.Delete(fonts.Value);

        await Settled(studio, () => studio.Rows.All(row => row.Name != "Fonts"));

        Assert.Equal(0, studio.Projects.Reloads);
    }

    /// <summary>
    /// Пока служба перечитывает модель, сменившиеся папки ждут её снимка, а загрузка, закончившаяся
    /// без нового снимка, отдаёт вопрос прежнему.
    /// </summary>
    /// <remarks>
    /// Снимок, спрошенный посреди правки службы, показал бы переименованную папку пропавшей: её новое
    /// имя он ещё не знает, а старого на диске уже нет. Сигнал здесь подаёт тест — проверяется не
    /// диск, а то, что окно с сигналом делает.
    /// </remarks>
    [AvaloniaFact]
    public async Task While_the_model_reloads_moved_folders_wait_for_its_snapshot()
    {
        using var studio = new ProjectWindowStudio();

        var snapshot = studio.Solution();

        await studio.Open(snapshot);

        var built = studio.Model.Settled;

        studio.Projects.Publish(ProjectWindowStudio.Ready(2, snapshot) with { IsLoading = true });
        Directory.CreateDirectory(Fonts(snapshot).Value);
        studio.Model.FoldersMoved();

        Assert.Same(built, studio.Model.Settled);

        studio.Projects.Publish(ProjectWindowStudio.Ready(3, snapshot));
        await studio.Built();

        Assert.NotSame(built, studio.Model.Settled);
        Assert.Contains(studio.Rows, row => row.Name == "Fonts");
    }

    /// <summary>Папка <c>Fonts</c> приложения — её в решении нет.</summary>
    private static CanonicalPath Fonts(SolutionSnapshot snapshot) =>
        snapshot.Projects.Single(project => project.Name == "App").ProjectDirectory.Combine("Fonts");
}
