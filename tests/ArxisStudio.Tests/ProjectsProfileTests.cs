using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Профиль службы проектов: своя оценка открытого для дизайнера, общая для держателей, на той же
/// полосе, что и служба.
/// </summary>
/// <remarks>
/// Движок — провайдер теста: загрузки и операции профиля приходят ему со своими свойствами, и по ним
/// их видно среди загрузок службы. В общей очереди: модуль поднимается хостом, а контракты модулей
/// живут на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsProfileTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Профиль дизайна читает открытое решение с папками выхода дизайнера и тем, что просили прочесть.
    /// </summary>
    /// <remarks>
    /// Сборки дизайнера пишут рядом с выходом IDE, а не поверх него, и снимок обязан называть те папки,
    /// куда они пишут. Движок у профиля свой — идентичность его снимка не та, что у службы.
    /// </remarks>
    [Fact]
    public async Task A_design_profile_reads_the_open_solution_with_the_design_output()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        await using var profile = studio.Projects.OpenProfile(
            new ProjectProfileRequest(ProjectProfileKind.Design) { AdditionalProperties = ["IsTestProject"] });

        var status = await WhenAsync(profile, status => status.State == ProjectsState.Ready);
        var request = Assert.Single(studio.Provider.Requests, IsDesign);

        Assert.Equal("obj/ArxisStudio/", request.GlobalProperties["IntermediateOutputPath"]);
        Assert.Contains("IsTestProject", request.Options.AdditionalProperties);
        Assert.Equal(MSBuildDesignOutput.GlobalProperties, profile.GlobalProperties);
        Assert.Equal(studio.Projects.Status.Session, status.Session);
        Assert.NotEqual(studio.Projects.Current!.Workspace, status.Snapshot!.Workspace);
    }

    /// <summary>Каждая загрузка службы ставит следом загрузку профиля — по той же причине и в той же конфигурации.</summary>
    [Fact]
    public async Task A_profile_reads_again_after_every_load_of_the_service()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        await WhenAsync(profile, status => status.State == ProjectsState.Ready);

        await studio.Projects.ReloadAsync(Token);
        await WhenAsync(profile, status => status.LastLoad?.Reason == ProjectsLoadReason.Reload);

        await studio.Projects.SetConfigurationAsync("Release", Token);
        var released = await WhenAsync(profile, status => status.LastLoad?.Reason == ProjectsLoadReason.Configuration);

        Assert.Equal("Release", released.Configuration);
        Assert.Equal([null, null, "Release"], studio.Provider.Requests.Where(IsDesign).Select(request => request.Configuration));
    }

    /// <summary>Профиль идёт за службой к другому решению и закрывается вместе с ней.</summary>
    [Fact]
    public async Task A_profile_follows_the_service_to_another_solution_and_closes_with_it()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var first = await WhenAsync(profile, status => status.State == ProjectsState.Ready);

        await studio.Projects.OpenAsync(ProjectsStudio.Solution("Other"), Token);
        var other = await WhenAsync(profile, status => status is { State: ProjectsState.Ready } && status.EntryPoint == ProjectsStudio.Solution("Other"));

        Assert.NotEqual(first.Session, other.Session);
        Assert.Equal(studio.Projects.Status.Session, other.Session);

        await studio.Projects.CloseAsync();

        await WhenAsync(profile, status => status.State == ProjectsState.Closed);
    }

    /// <summary>
    /// Держатели делят один профиль: второй не перечитывает решения, а последний отпущенный отпускает и
    /// движок.
    /// </summary>
    [Fact]
    public async Task Holders_share_one_profile_and_the_last_lets_it_go()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var first = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var read = await WhenAsync(first, status => status.State == ProjectsState.Ready);
        var second = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));

        Assert.Same(read.Snapshot, second.Status.Snapshot);
        Assert.Equal(1, studio.Provider.Requests.Count(IsDesign));

        await first.DisposeAsync();

        Assert.Equal(ProjectsState.Ready, second.Status.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.RefreshAsync(Token));

        await second.DisposeAsync();

        Assert.Equal(ProjectsState.Closed, second.Status.State);

        await using var third = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var again = await WhenAsync(third, status => status.State == ProjectsState.Ready);

        Assert.NotEqual(read.Snapshot!.Workspace, again.Snapshot!.Workspace);
    }

    /// <summary>Держатель, попросивший прочесть новое свойство, перечитывает общий профиль.</summary>
    [Fact]
    public async Task A_new_property_reads_the_shared_profile_again()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var first = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        await WhenAsync(first, status => status.State == ProjectsState.Ready);

        await using var second = studio.Projects.OpenProfile(
            new ProjectProfileRequest(ProjectProfileKind.Design) { AdditionalProperties = ["AvaloniaUseCompiledBindingsByDefault"] });

        await WhenAsync(first, status => status.LastLoad?.Reason == ProjectsLoadReason.Reload);

        Assert.Contains("AvaloniaUseCompiledBindingsByDefault", studio.Provider.Requests.Last(IsDesign).Options.AdditionalProperties);
    }

    /// <summary>
    /// Операция профиля идёт как просили — сборка без восстановления, — но с папками дизайнера поверх
    /// свойств просившего, и окно сборки отличает её по профилю.
    /// </summary>
    [Fact]
    public async Task A_profile_operation_runs_as_asked_over_the_design_properties()
    {
        using var studio = new ProjectsStudio();
        var started = new List<ProjectOperation>();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var snapshot = (await WhenAsync(profile, status => status.State == ProjectsState.Ready)).Snapshot!;

        studio.Build.Started += (_, e) => started.Add(e.Operation);

        var result = await profile.ExecuteAsync(
            Request(snapshot, ProjectOperationKind.Build) with
            {
                GlobalProperties = ProjectMetadata.Create([new("OutputPath", "bin/Mine/"), new("Deterministic", "true")]),
            },
            cancellationToken: Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);

        var executed = Assert.Single(studio.Provider.Operations);

        Assert.Equal(ProjectOperationKind.Build, executed.Kind);
        Assert.Equal(snapshot.Workspace, executed.Workspace);
        Assert.Equal("bin/ArxisStudio/", executed.GlobalProperties["OutputPath"]);
        Assert.Equal("true", executed.GlobalProperties["Deterministic"]);

        await studio.Thread.IdleAsync();

        Assert.Equal(ProjectProfileKind.Design, Assert.Single(started).Profile);
        Assert.Contains(studio.Written, record => record.Message.Contains("сборка для дизайнера", StringComparison.Ordinal));
    }

    /// <summary>Операция профиля встаёт в очередь за сборкой человека: MSBuild один на процесс.</summary>
    [Fact]
    public async Task A_profile_operation_waits_behind_the_build_of_the_person()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var snapshot = (await WhenAsync(profile, status => status.State == ProjectsState.Ready)).Snapshot!;

        var gate = new LoadGate();
        studio.Provider.OperationGate = gate;

        var person = studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var design = profile.ExecuteAsync(Request(snapshot, ProjectOperationKind.Build), cancellationToken: Token);

        // Полоса одна, и её держит сборка человека: до движка дошло только её восстановление.
        Assert.Equal([ProjectOperationKind.Restore], studio.Provider.Operations.Select(operation => operation.Kind));

        gate.Release();
        await person;
        await design;

        Assert.Equal(
            [ProjectOperationKind.Restore, ProjectOperationKind.Build, ProjectOperationKind.Build],
            studio.Provider.Operations.Select(operation => operation.Kind));
        Assert.Equal(snapshot.Workspace, studio.Provider.Operations.Last().Workspace);
    }

    /// <summary>Запрос, собранный не по снимку профиля, не исполняется: он про другую оценку.</summary>
    [Fact]
    public async Task A_request_built_from_another_evaluation_is_refused()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        await WhenAsync(profile, status => status.State == ProjectsState.Ready);

        var result = await profile.ExecuteAsync(Request(studio.Projects.Current!, ProjectOperationKind.Build), cancellationToken: Token);

        Assert.Equal(ProjectsDiagnosticCodes.ProjectNotOpen, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>
    /// Удачное восстановление профиля перечитывает модель службы, а за ней и профиль: восстановление у
    /// них общее.
    /// </summary>
    [Fact]
    public async Task A_profile_restore_reads_the_service_and_then_the_profile_again()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));
        var snapshot = (await WhenAsync(profile, status => status.State == ProjectsState.Ready)).Snapshot!;

        var result = await profile.ExecuteAsync(Request(snapshot, ProjectOperationKind.Restore), cancellationToken: Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Equal(ProjectsLoadReason.Restore, studio.Projects.Status.LastLoad?.Reason);

        await WhenAsync(profile, status => status.LastLoad?.Reason == ProjectsLoadReason.Restore);

        Assert.Equal(2, studio.Provider.Requests.Count(request => !IsDesign(request)));
        Assert.Equal(2, studio.Provider.Requests.Count(IsDesign));
    }

    /// <summary>Профиль, открытый без решения, закрыт и отказывает словами службы — а откроют решение, начнёт читать.</summary>
    [Fact]
    public async Task A_profile_opened_with_nothing_open_waits_for_a_solution()
    {
        using var studio = new ProjectsStudio();

        await using var profile = studio.Projects.OpenProfile(new ProjectProfileRequest(ProjectProfileKind.Design));

        Assert.Equal(ProjectsState.Closed, profile.Status.State);
        Assert.Equal(ProjectsDiagnosticCodes.NothingOpen, Assert.Single((await profile.RefreshAsync(Token)).Diagnostics).Code);

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        await WhenAsync(profile, status => status.State == ProjectsState.Ready);
    }

    /// <summary>Загрузка профиля — у неё свойства дизайнера; у загрузок службы их нет.</summary>
    private static bool IsDesign(WorkspaceLoadRequest request) =>
        request.GlobalProperties.GetValueOrDefault("OutputPath") == "bin/ArxisStudio/";

    /// <summary>Запрос по снимку — так его строит хост дизайна.</summary>
    private static ProjectOperationRequest Request(SolutionSnapshot snapshot, ProjectOperationKind kind) => new()
    {
        Kind = kind,
        Workspace = snapshot.Workspace,
        EntryPointPath = snapshot.EntryPoint.Path,
    };

    /// <summary>Ждёт состояния профиля, отвечающего условию: доставленного или уже опубликованного.</summary>
    private static Task<ProjectsStatus> WhenAsync(IStudioProjectProfile profile, Func<ProjectsStatus, bool> condition)
    {
        var reached = new TaskCompletionSource<ProjectsStatus>(TaskCreationOptions.RunContinuationsAsynchronously);

        profile.Changed += (_, change) =>
        {
            if (condition(change.Current))
                reached.TrySetResult(change.Current);
        };

        if (condition(profile.Status))
            reached.TrySetResult(profile.Status);

        return reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }
}
