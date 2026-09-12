using ArxisStudio.Modules.Projects;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба проектов: сессии, очередь, отмена и то, что видит подписчик.
/// </summary>
/// <remarks>
/// Движок здесь — провайдер теста: загрузка останавливается там, где тест велел, и идёт дальше,
/// когда тест отпустил. Поэтому проверки точные — «ровно одна загрузка», «сразу», — а не «скорее
/// всего». В общей очереди: модуль поднимается хостом, а контракты модулей живут на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Открытие видно как «открывается», а затем как готовое.</summary>
    [Fact]
    public async Task Opening_goes_through_opening_to_ready()
    {
        using var studio = new ProjectsStudio();

        var gate = studio.Provider.Gate = new LoadGate();
        var seen = studio.Record();

        var opening = studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        await gate.Entered;
        await studio.Thread.IdleAsync();

        var first = Assert.Single(seen);

        Assert.Equal(ProjectsState.Opening, first.Current.State);
        Assert.True(first.Current.IsLoading, "открытие идёт, а служба говорит, что не грузит");
        Assert.True(first.Changes.HasFlag(ProjectsChanges.Session), "первое открытие не назвалось новой сессией");
        Assert.Null(studio.Projects.Current);

        gate.Release();

        var result = await opening;

        await studio.Thread.IdleAsync();

        Assert.Equal(WorkspaceLoadStatus.Succeeded, result.Status);

        var ready = seen[^1];

        Assert.Equal(ProjectsState.Ready, ready.Current.State);
        Assert.False(ready.Current.IsLoading, "загрузка кончилась, а служба всё ещё грузит");
        Assert.Same(result.Snapshot, ready.Current.Snapshot);
        Assert.Equal(ProjectsLoadReason.Open, ready.Current.LastLoad?.Reason);
        Assert.Equal(new[] { "App", "Lib" }, ready.Added.Select(project => project.Name).Order());
        Assert.True(ready.SolutionChanged, "первый снимок решения не назвал решение изменившимся");

        Assert.True(
            seen.Zip(seen.Skip(1)).All(pair => pair.First.Current.Sequence < pair.Second.Current.Sequence),
            "номера доставленных состояний не растут");
    }

    /// <summary>
    /// Провал открытия — итог с диагностикой, а не исключение, и сбоем никому не засчитан.
    /// </summary>
    /// <remarks>
    /// Нет файла, нет SDK — это обычная жизнь инструмента, а не ошибка модуля. Засчитай шов их
    /// модулю, три неудачных открытия отключили бы службу проектов до конца сеанса.
    /// </remarks>
    [Theory]
    [InlineData("APS2003")]
    [InlineData("APS2001")]
    public async Task A_failed_open_is_a_result_with_diagnostics_and_strikes_nobody(string code)
    {
        using var studio = new ProjectsStudio();

        var solution = ProjectsStudio.Solution();

        studio.Provider.Answer = _ => Solutions.Failure(code, solution);

        var result = await studio.Projects.OpenAsync(solution, Token);

        await studio.Thread.IdleAsync();

        Assert.Equal(WorkspaceLoadStatus.Failed, result.Status);
        Assert.Equal(ProjectsState.Failed, studio.Projects.Status.State);
        Assert.Null(studio.Projects.Current);
        Assert.Equal(code, Assert.Single(studio.Projects.Status.LastLoad!.Result.Diagnostics).Code);
        Assert.Contains(studio.Written, record => record.Message.Contains(code, StringComparison.Ordinal));
        Assert.Empty(studio.Strikes);
    }

    /// <summary>Перезагрузки, попросившие, пока одна уже ждёт, склеиваются в неё.</summary>
    [Fact]
    public async Task Reloads_asked_while_one_waits_share_one_load()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var gate = studio.Provider.Gate = new LoadGate();
        var running = studio.Projects.ReloadAsync(Token);

        await gate.Entered;

        // Первая перезагрузка внутри движка; следующие три встают в очередь и обязаны стать одной.
        var queued = new[]
        {
            studio.Projects.ReloadAsync(Token),
            studio.Projects.ReloadAsync(Token),
            studio.Projects.ReloadAsync(Token),
        };

        gate.Release();

        await running;

        var results = await Task.WhenAll(queued);

        Assert.Equal(3, studio.Provider.Loads);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    /// <summary>Тот же путь — перезагрузка: сессия и идентичности проектов прежние.</summary>
    [Fact]
    public async Task Opening_the_same_path_again_keeps_the_session_and_its_identities()
    {
        using var studio = new ProjectsStudio();

        var first = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        var again = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        Assert.Equal(1, studio.Projects.Status.Session);
        Assert.Equal(
            first.Snapshot!.Projects.Select(project => project.Identity),
            again.Snapshot!.Projects.Select(project => project.Identity));
    }

    /// <summary>
    /// Другой путь кончает первую сессию сразу — не дожидаясь, пока движок её отпустит.
    /// </summary>
    [Fact]
    public async Task Opening_another_path_ends_the_first_session_at_once()
    {
        using var studio = new ProjectsStudio();

        var gate = studio.Provider.Gate = new LoadGate();
        var first = studio.Projects.OpenAsync(ProjectsStudio.Solution("First"), Token);

        await gate.Entered;

        var second = studio.Projects.OpenAsync(ProjectsStudio.Solution("Second"), Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Assert.Equal(2, studio.Projects.Status.Session);
        Assert.Equal(ProjectsStudio.Solution("Second"), studio.Projects.Status.EntryPoint);

        gate.Release();

        var result = await second;

        Assert.Equal("Second", result.Snapshot!.Name);
        Assert.Equal(ProjectsState.Ready, studio.Projects.Status.State);
    }

    /// <summary>Отменённое первое открытие оставляет студию без проекта, а не «открывающейся».</summary>
    [Fact]
    public async Task A_cancelled_first_open_leaves_nothing_open()
    {
        using var studio = new ProjectsStudio();
        using var cancel = new CancellationTokenSource();

        var gate = studio.Provider.Gate = new LoadGate();
        var opening = studio.Projects.OpenAsync(ProjectsStudio.Solution(), cancel.Token);

        await gate.Entered;

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);

        var closed = await studio.WhenAsync(status => status.State == ProjectsState.Closed);

        Assert.Equal(0, closed.Session);
        Assert.Null(studio.Projects.Current);
    }

    /// <summary>
    /// Ждущий, отменённый в очереди, отпускается сразу, а его загрузка не начинается вовсе.
    /// </summary>
    [Fact]
    public async Task A_waiter_cancelled_in_the_queue_is_released_at_once_and_its_load_never_runs()
    {
        using var studio = new ProjectsStudio();
        using var cancel = new CancellationTokenSource();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var gate = studio.Provider.Gate = new LoadGate();
        var running = studio.Projects.ReloadAsync(Token);

        await gate.Entered;

        var queued = studio.Projects.ReloadAsync(cancel.Token);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        Assert.False(running.IsCompleted, "проверка потеряла смысл: первая перезагрузка уже кончилась");

        gate.Release();

        await running;

        // Следующая перезагрузка встанет за отменённой: дойдя до неё, очередь знает, что та снята.
        await studio.Projects.ReloadAsync(Token);

        Assert.Equal(3, studio.Provider.Loads);
    }

    /// <summary>Перезагрузить, когда ничего не открыто, — ответ с кодом, а не исключение.</summary>
    [Fact]
    public async Task Reload_with_nothing_open_answers_with_a_diagnostic()
    {
        using var studio = new ProjectsStudio();

        var result = await studio.Projects.ReloadAsync(Token);

        Assert.Equal(WorkspaceLoadStatus.Failed, result.Status);
        Assert.Equal(ProjectsDiagnosticCodes.NothingOpen, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(0, studio.Provider.Loads);
    }

    /// <summary>Новая конфигурация видна сразу, до загрузки, и доходит до движка.</summary>
    [Fact]
    public async Task A_new_configuration_is_visible_at_once_and_reaches_the_engine()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var gate = studio.Provider.Gate = new LoadGate();
        var changing = studio.Projects.SetConfigurationAsync("Release", Token);

        Assert.Equal("Release", studio.Projects.Status.Configuration);

        await gate.Entered;

        gate.Release();

        var result = await changing;

        Assert.Equal("Release", studio.Provider.Requests.Last().Configuration);
        Assert.Equal(ProjectsLoadReason.Configuration, studio.Projects.Status.LastLoad?.Reason);
        Assert.All(result.Snapshot!.Projects, project => Assert.Equal("Release", project.ActiveConfiguration));
    }

    /// <summary>Та же конфигурация ничего не перечитывает.</summary>
    [Fact]
    public async Task The_same_configuration_reads_nothing_again()
    {
        using var studio = new ProjectsStudio();

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        var first = await studio.Projects.SetConfigurationAsync("Release", Token);
        var again = await studio.Projects.SetConfigurationAsync("Release", Token);

        Assert.Same(first, again);
        Assert.Equal(2, studio.Provider.Loads);
    }

    /// <summary>
    /// Провалившаяся перезагрузка оставляет прежний снимок, а в «Проблемах» — свой провал.
    /// </summary>
    [Fact]
    public async Task A_failed_reload_keeps_the_previous_snapshot()
    {
        using var studio = new ProjectsStudio();

        var opened = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        studio.Provider.Answer = request => Solutions.Failure("APS2004", request.EntryPointPath);

        var reloaded = await studio.Projects.ReloadAsync(Token);

        await studio.Thread.IdleAsync();

        Assert.Equal(WorkspaceLoadStatus.Failed, reloaded.Status);
        Assert.Equal(ProjectsState.Ready, studio.Projects.Status.State);
        Assert.Same(opened.Snapshot, studio.Projects.Current);
        Assert.Same(reloaded, studio.Projects.Status.LastLoad?.Result);
        Assert.Contains(studio.Written, record => record.Message.Contains("APS2004", StringComparison.Ordinal));
    }

    /// <summary>
    /// Находка уходит в журнал следом за итогом: код, объяснение и место, на своём уровне.
    /// </summary>
    /// <remarks>
    /// Журнал — летопись, а не состояние: запись остаётся и после закрытия проекта. Снимать её
    /// нечем и незачем — время рядом с ней говорит, когда это было правдой.
    /// </remarks>
    [Fact]
    public async Task A_finding_goes_to_the_log_beside_the_line_that_found_it()
    {
        using var studio = new ProjectsStudio();

        studio.Provider.Answer = request => Solutions.Of(
            request,
            studio.Provider.Projects,
            ProjectDiagnostic.ForFile("APS2005", "не восстановлено", ProjectDiagnosticSeverity.Warning, request.EntryPointPath));

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);
        await studio.Thread.IdleAsync();

        var found = Assert.Single(studio.Written, record => record.Message.StartsWith("APS2005", StringComparison.Ordinal));

        Assert.Equal(StudioLogLevel.Warning, found.Level);
        Assert.Contains("не восстановлено", found.Message, StringComparison.Ordinal);
        Assert.Contains(ProjectsStudio.Solution().FileName, found.Message, StringComparison.Ordinal);

        // Итог загрузки написан раньше находки: сначала что вышло, потом что сказано.
        Assert.True(
            studio.Written.ToList().FindIndex(record => record.Message.Contains("открыто", StringComparison.Ordinal))
            < studio.Written.ToList().FindIndex(record => record.Message.StartsWith("APS2005", StringComparison.Ordinal)),
            "находка написана раньше итога");

        await studio.Projects.CloseAsync();
        await studio.Thread.IdleAsync();

        Assert.Contains(studio.Written, record => record.Message.StartsWith("APS2005", StringComparison.Ordinal));
        Assert.Equal(ProjectsState.Closed, studio.Projects.Status.State);
        Assert.Null(studio.Projects.Current);
    }

    /// <summary>Перемена на диске перезагружает модель и называет причины.</summary>
    [Fact]
    public async Task A_change_on_disk_reloads_with_its_causes()
    {
        FakeWatch? watch = null;

        using var studio = new ProjectsStudio(watch: stale => watch = new FakeWatch(stale));

        var opened = await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        Assert.NotNull(watch);
        Assert.Same(opened.Snapshot, watch.Followed[^1]);

        var cause = opened.Snapshot!.Projects[0].ProjectFilePath;
        var reloaded = studio.WhenAsync(status => status.LastLoad?.Reason == ProjectsLoadReason.FileSystem);

        watch.Stale([cause]);

        var status = await reloaded;

        Assert.Equal(cause, Assert.Single(status.LastLoad!.Causes));
        Assert.Equal(2, studio.Provider.Loads);
    }

    /// <summary>Выключенное слежение гаснет, включённое — заводится заново.</summary>
    [Fact]
    public async Task Turning_watching_off_stops_the_watch_and_on_brings_it_back()
    {
        var watches = new List<FakeWatch>();

        using var studio = new ProjectsStudio(watch: stale =>
        {
            var watch = new FakeWatch(stale);
            watches.Add(watch);
            return watch;
        });

        await studio.Projects.OpenAsync(ProjectsStudio.Solution(), Token);

        studio.Settings.Set(ProjectsSettings.WatchFilesKey, false);

        Assert.True(Assert.Single(watches).IsDisposed, "слежение выключили, а оно осталось");

        studio.Settings.Set(ProjectsSettings.WatchFilesKey, true);

        Assert.Equal(2, watches.Count);
        Assert.False(watches[1].IsDisposed, "слежение включили, а оно не завелось");
    }

    /// <summary>Остановленная служба новой работы не берёт.</summary>
    [Fact]
    public void A_stopped_service_refuses_new_work()
    {
        var studio = new ProjectsStudio();
        var projects = studio.Projects;

        studio.Dispose();

        // Отказ синхронный: служба бросает до того, как у вызывающего появилась задача.
        Assert.Throws<ObjectDisposedException>(() => { _ = projects.OpenAsync(ProjectsStudio.Solution(), Token); });
    }
}
