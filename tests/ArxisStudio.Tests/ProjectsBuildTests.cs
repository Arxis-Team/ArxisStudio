using ArxisStudio.Modules.Projects;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба сборки: полоса, восстановление перед сборкой, события и находки.
/// </summary>
/// <remarks>
/// Движок здесь — провайдер теста: операция кончается тогда, когда тест её отпустил, и проверки
/// выходят точными — «ровно одна операция», «до движка не дошло». Настоящий MSBuild доводит до
/// конца один интеграционный тест. В общей очереди: модуль поднимается хостом, а контракты модулей
/// живут на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsBuildTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Сборка начинается с восстановления.</summary>
    /// <remarks>Цель Build у MSBuild пакетов не восстанавливает — этим она отличается от dotnet build.</remarks>
    [Fact]
    public async Task A_build_restores_before_it_builds()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var result = await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        Assert.Equal(ProjectOperationStatus.Succeeded, result.Status);
        Assert.Equal(
            [ProjectOperationKind.Restore, ProjectOperationKind.Build],
            studio.Provider.Operations.Select(operation => operation.Kind));
    }

    /// <summary>
    /// Провалившееся восстановление отменяет сборку.
    /// </summary>
    /// <remarks>
    /// Собирать по ненайденным пакетам значит показать человеку ошибки компилятора вместо причины.
    /// </remarks>
    [Fact]
    public async Task A_failed_restore_stops_the_build()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        studio.Provider.Executed = request => request.Kind == ProjectOperationKind.Restore
            ? ProjectOperationResult.Failed(new ProjectDiagnostic("APS2001", "пакетов не нашлось", ProjectDiagnosticSeverity.Error))
            : ProjectOperationResult.Succeeded();

        var result = await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        Assert.True(result.HasErrors, "сборка по невосстановленным пакетам прошла");
        Assert.Equal("APS2001", Assert.Single(result.Diagnostics).Code);
        Assert.Equal([ProjectOperationKind.Restore], studio.Provider.Operations.Select(operation => operation.Kind));
    }

    /// <summary>Удачное восстановление перечитывает модель, и к концу задачи она уже новая.</summary>
    [Fact]
    public async Task A_successful_restore_rereads_the_model()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        Assert.Equal(1, studio.Provider.Loads);

        await studio.Build.RunAsync(ProjectOperationKind.Restore, cancellationToken: Token);

        Assert.Equal(2, studio.Provider.Loads);
        Assert.Equal(ProjectsLoadReason.Restore, studio.Projects.Status.LastLoad?.Reason);
    }

    /// <summary>Сборка модель не перечитывает: файлов проекта она не меняет.</summary>
    [Fact]
    public async Task A_build_does_not_reread_the_model()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        Assert.Equal(1, studio.Provider.Loads);
        Assert.Equal(ProjectsLoadReason.Open, studio.Projects.Status.LastLoad?.Reason);
    }

    /// <summary>Операция идёт в той конфигурации, которую выбрал человек.</summary>
    [Fact]
    public async Task An_operation_carries_the_active_configuration()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await studio.Projects.SetConfigurationAsync("Release", Token);
        await studio.Build.RunAsync(ProjectOperationKind.Clean, cancellationToken: Token);

        Assert.Equal("Release", Assert.Single(studio.Provider.Operations).Configuration);
    }

    /// <summary>Собирать нечего: ничего не открыто.</summary>
    [Fact]
    public async Task Nothing_open_is_refused_with_the_code_of_the_service()
    {
        using var studio = new ProjectsStudio();

        var result = await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        Assert.Equal(ProjectsDiagnosticCodes.NothingOpen, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Проект не из открытого решения не собирается.</summary>
    [Fact]
    public async Task A_project_that_is_not_open_is_refused()
    {
        using var studio = new ProjectsStudio();

        var opened = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var stranger = ProjectIdentity.Create(
            opened.Snapshot!.Workspace,
            CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Other", "Other.csproj")));

        var result = await studio.Build.RunAsync(ProjectOperationKind.Build, [stranger], cancellationToken: Token);

        Assert.Equal(ProjectsDiagnosticCodes.ProjectNotOpen, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Начало и конец операции доходят до подписчика, а между ними она идёт.</summary>
    [Fact]
    public async Task The_start_and_the_end_of_an_operation_reach_the_subscriber()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var seen = new List<(string Which, ProjectOperationEventArgs Change)>();

        studio.Build.Started += (_, change) => seen.Add(("начало", change));
        studio.Build.Completed += (_, change) => seen.Add(("конец", change));

        ProjectOperation? running = null;

        studio.Provider.Inside = () => running ??= studio.Build.Running;

        await studio.Build.RunAsync(ProjectOperationKind.Clean, cancellationToken: Token);
        await studio.Thread.IdleAsync();

        Assert.Equal(["начало", "конец"], seen.Select(entry => entry.Which));
        Assert.Equal(seen[0].Change.Operation.Id, seen[1].Change.Operation.Id);
        Assert.Null(seen[0].Change.Result);
        Assert.Equal(ProjectOperationStatus.Succeeded, seen[1].Change.Result?.Status);
        Assert.False(seen[1].Change.IsCancelled, "законченная операция назвалась отменённой");
        Assert.Equal(ProjectOperationKind.Clean, running?.Kind);
        Assert.Null(studio.Build.Running);
    }

    /// <summary>Операция ждёт загрузку, стоящую перед ней: полоса у движка одна.</summary>
    [Fact]
    public async Task An_operation_waits_for_the_load_in_front_of_it()
    {
        using var studio = new ProjectsStudio();

        var gate = studio.Provider.Gate = new LoadGate();
        var opening = studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        await gate.Entered;

        var building = studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);

        Assert.Empty(studio.Provider.Operations);

        gate.Release();

        await opening;
        await building;

        Assert.NotEmpty(studio.Provider.Operations);
    }

    /// <summary>Отмена, пришедшая в очереди, останавливает операцию до движка.</summary>
    [Fact]
    public async Task A_cancelled_operation_never_reaches_the_engine()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var gate = studio.Provider.Gate = new LoadGate();
        var reloading = studio.Projects.ReloadAsync(Token);

        await gate.Entered;

        using var cancellation = new CancellationTokenSource();

        var building = studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => building);

        gate.Release();

        await reloading;
        await studio.Thread.IdleAsync();

        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Ошибки сборки уходят в журнал — каждая своей строкой и на своём уровне.</summary>
    [Fact]
    public async Task The_errors_of_a_build_go_to_the_log()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        studio.Provider.Executed = request => request.Kind == ProjectOperationKind.Build
            ? ProjectOperationResult.Failed(new ProjectDiagnostic("CS0103", "имя не найдено", ProjectDiagnosticSeverity.Error))
            : ProjectOperationResult.Succeeded();

        await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);
        await studio.Thread.IdleAsync();

        var found = Assert.Single(studio.Written, record => record.Message.StartsWith("CS0103", StringComparison.Ordinal));

        Assert.Equal(StudioLogLevel.Error, found.Level);
        Assert.Contains("имя не найдено", found.Message, StringComparison.Ordinal);

        studio.Provider.Executed = null;

        await studio.Build.RunAsync(ProjectOperationKind.Build, cancellationToken: Token);
        await studio.Thread.IdleAsync();

        // Удачная сборка своих ошибок не пишет, а прежние остаются написанными: журнал не стирают.
        Assert.Single(studio.Written, record => record.Message.StartsWith("CS0103", StringComparison.Ordinal));
    }

    /// <summary>Пакеты восстанавливаются при открытии — один раз за сессию.</summary>
    [Fact]
    public async Task Packages_are_restored_once_when_the_open_solution_waits_for_them()
    {
        using var studio = new ProjectsStudio();

        studio.Provider.Answer = request => Solutions.Of(
            request,
            studio.Provider.Projects,
            ProjectDiagnostic.ForFile(
                MSBuildDiagnosticCodes.RestoreAssetsMissing,
                "пакеты не восстановлены",
                ProjectDiagnosticSeverity.Warning,
                request.EntryPointPath));

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await studio.WhenAsync(status => status.LastLoad?.Reason == ProjectsLoadReason.Restore);

        Assert.Equal([ProjectOperationKind.Restore], studio.Provider.Operations.Select(operation => operation.Kind));

        await studio.Projects.ReloadAsync(Token);
        await studio.Thread.IdleAsync();

        Assert.Single(studio.Provider.Operations);
    }

    /// <summary>Выключенная настройка не даёт восстановлению начаться.</summary>
    [Fact]
    public async Task Nothing_is_restored_on_open_when_the_setting_is_off()
    {
        using var studio = new ProjectsStudio();

        studio.Settings.Set(ProjectsSettings.RestoreOnOpenKey, false);

        studio.Provider.Answer = request => Solutions.Of(
            request,
            studio.Provider.Projects,
            ProjectDiagnostic.ForFile(
                MSBuildDiagnosticCodes.RestoreAssetsMissing,
                "пакеты не восстановлены",
                ProjectDiagnosticSeverity.Warning,
                request.EntryPointPath));

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await studio.Thread.IdleAsync();

        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Команда сборки без открытого проекта говорит об этом человеку.</summary>
    [Fact]
    public void Build_with_nothing_open_says_so_in_the_status_bar()
    {
        using var studio = new ProjectsStudio();

        Assert.True(studio.Commands.Invoke(ProjectsModule.BuildCommand));
        Assert.Single(studio.Status.Said);
        Assert.Empty(studio.Provider.Operations);
    }

    /// <summary>Вида операции, которого нет, служба не берёт.</summary>
    [Fact]
    public async Task An_unknown_kind_of_operation_is_an_argument_error()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => studio.Build.RunAsync((ProjectOperationKind)42, cancellationToken: Token));
    }
}
