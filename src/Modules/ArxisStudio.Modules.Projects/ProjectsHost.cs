using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Modules.Projects.Reporting;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Служба проектов: сессии, очередь работы над движком, доставка перемен и слежение за диском.
/// </summary>
/// <remarks>
/// <para>
/// <b>Один замок на состояние.</b> Сессия, её очередь и опубликованная запись меняются только под
/// <c>_gate</c>, и всякая перемена кончается <see cref="Republish"/>: запись выводится из сессии
/// целиком, а не правится по полю. Под замком не отменяется ни один токен, не завершается ни одна
/// задача и не начинается ни одно ожидание. Отмена синхронно зовёт чужие обработчики, и код,
/// продолжившийся на этом же потоке, вошёл бы в замок повторно — посреди перемены, которую замок и
/// защищает.
/// </para>
/// <para>
/// <b>Одна полоса на движок.</b> Загрузки идут очередью <see cref="Lane"/> по одной. Ждущая, но не
/// начатая загрузка у сессии одна, и новые просьбы к ней присоединяются. Движок ушедшей сессии
/// отпускается той же очередью — после того, что он дочитывает.
/// </para>
/// <para>
/// <b>Ожидаемое — результат.</b> Провал загрузки — это диагностики в итоге и в <c>LastLoad</c>. До
/// шва студии доходят только собственные ошибки службы: задача студии приписывает их модулю.
/// </para>
/// </remarks>
internal sealed class ProjectsHost : IStudioProjects
{
    private readonly IStudioContext _context;
    private readonly ProjectsHostOptions _options;
    private readonly IProjectsThread _thread;
    private readonly ChangePublisher _publisher;
    private readonly Lane _lane;
    private readonly Lock _gate = new();

    private ProjectsStatus _status = ProjectsStatus.Closed;
    private ProjectsSession? _session;
    private long _sessions;
    private long _sequence;
    private bool _stopped;
    private int _watching;
    private int _announced;

    /// <summary>Собирает службу.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="options">Из чего собрать.</param>
    public ProjectsHost(IStudioContext context, ProjectsHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _options = options;
        _thread = options.Thread ?? AvaloniaProjectsThread.Instance;

        var reporter = new ProblemsReporter(context.GetService<IStudioProblems>());

        _publisher = new ChangePublisher(this, _thread, reporter.Show, options.SubscriberFailed);
        _lane = new Lane(error => context.Log.Write(
            StudioLogLevel.Error, ProjectsModule.LogSource, $"Очередь службы проектов: {error}"));

        _watching = ProjectsSettings.Read(context.Settings).WatchFiles ? 1 : 0;
        context.Settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc/>
    public ProjectsStatus Status => Volatile.Read(ref _status);

    /// <inheritdoc/>
    public SolutionSnapshot? Current => Status.Snapshot;

    /// <inheritdoc/>
    public event EventHandler<ProjectsChangedEventArgs>? Changed
    {
        add => _publisher.Subscribe(value);
        remove => _publisher.Unsubscribe(value);
    }

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> OpenAsync(CanonicalPath entryPoint, CancellationToken cancellationToken = default)
    {
        if (entryPoint.IsEmpty)
            throw new ArgumentException("Открыть можно только файл, а путь пуст", nameof(entryPoint));

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<WorkspaceLoadResult>(cancellationToken);

        ProjectsSession? retired = null;
        var opened = false;
        LoadItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session || session.EntryPoint != entryPoint)
            {
                retired = _session;
                session = new ProjectsSession(++_sessions, entryPoint, _options.Workspace());
                _session = session;
                opened = true;
            }

            item = Enqueue(session, ProjectsLoadReason.Open, [], pinned: false);
        }

        if (retired is not null)
            _ = Retire(retired);

        if (opened)
            _context.Log.Write(StudioLogLevel.Info, ProjectsModule.LogSource, $"Открываю {entryPoint}");

        return item.Wait(cancellationToken, Leave);
    }

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<WorkspaceLoadResult>(cancellationToken);

        LoadItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session)
                return Task.FromResult(NothingOpen());

            item = Enqueue(session, ProjectsLoadReason.Reload, [], pinned: false);
        }

        return item.Wait(cancellationToken, Leave);
    }

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> SetConfigurationAsync(string? configuration, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<WorkspaceLoadResult>(cancellationToken);

        LoadItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session)
                return Task.FromResult(NothingOpen());

            var same = string.Equals(session.Configuration, configuration, StringComparison.Ordinal);

            // Та же конфигурация ничего не перечитывает: идущая загрузка уже читает под неё, а
            // законченная уже под неё прочитала.
            if (same && (session.Pending ?? session.Running) is { } going)
            {
                going.Join();
                item = going;
            }
            else if (same && session.LastLoad is { } last)
            {
                return Task.FromResult(last.Result);
            }
            else
            {
                session.Configuration = configuration;
                item = Enqueue(session, ProjectsLoadReason.Configuration, [], pinned: false);
            }
        }

        return item.Wait(cancellationToken, Leave);
    }

    /// <inheritdoc/>
    public Task CloseAsync()
    {
        ProjectsSession? retired;

        lock (_gate)
        {
            retired = _session;

            if (_stopped || retired is null)
                return Task.CompletedTask;

            _session = null;
            Republish(SnapshotStep.None);
        }

        _context.Log.Write(StudioLogLevel.Info, ProjectsModule.LogSource, $"Закрываю {retired.EntryPoint}");

        return Retire(retired);
    }

    /// <summary>
    /// Останавливает службу: модуль выключают.
    /// </summary>
    /// <remarks>
    /// Не ждёт движка. Открытое закрывается сразу, движок отпускается очередью, а очередь
    /// закрывается следом — стоящее в ней доделается, новое не встанет.
    /// </remarks>
    public void Stop()
    {
        ProjectsSession? retired;

        lock (_gate)
        {
            if (_stopped)
                return;

            _stopped = true;
            retired = _session;
            _session = null;
            Republish(SnapshotStep.None);
        }

        _context.Settings.Changed -= OnSettingsChanged;

        if (retired is not null)
            _ = Retire(retired);

        _lane.Complete();
    }

    /// <summary>Команда «перезагрузить»: итог не ждёт никто, а «нечего» стоит сказать человеку.</summary>
    internal void ReloadFromCommand() => _ = CommandAsync(async () =>
    {
        var result = await ReloadAsync();

        if (result.Diagnostics.Any(diagnostic => diagnostic.Code == ProjectsDiagnosticCodes.NothingOpen))
            _context.GetService<IStudioStatus>()?.Show(_context.Strings["module.projects.nothingOpen"]);
    });

    /// <summary>Команда «закрыть».</summary>
    internal void CloseFromCommand() => _ = CommandAsync(CloseAsync);

    private static async Task CommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // Отмена — решение человека, остановка — студии: ни то ни другое не сбой команды.
        }
    }

    /// <summary>
    /// Ставит загрузку сессии или присоединяет просьбу к уже стоящей. Под замком.
    /// </summary>
    /// <param name="session">Чья загрузка.</param>
    /// <param name="reason">Зачем просят.</param>
    /// <param name="causes">Файлы, перемены в которых привели к просьбе.</param>
    /// <param name="pinned">Ждёт служба, а не вызывающий: перемену на диске отменить некому.</param>
    private LoadItem Enqueue(
        ProjectsSession session,
        ProjectsLoadReason reason,
        ImmutableArray<CanonicalPath> causes,
        bool pinned)
    {
        if (session.Pending is not { } item)
        {
            item = new LoadItem(session, reason);
            session.Pending = item;
            _lane.Enqueue(() => RunAsync(item));
        }
        else
        {
            item.Strengthen(reason);
        }

        item.AddCauses(causes);

        if (pinned)
            item.Pin();
        else
            item.Join();

        Republish(SnapshotStep.None);

        return item;
    }

    /// <summary>
    /// Ждущий ушёл.
    /// </summary>
    /// <remarks>
    /// Зовётся вне замка — ожидание начинается только вне его, — поэтому разбирается на месте: к
    /// тому моменту, как ушедший узнал об отмене, его загрузка уже снята с очереди.
    /// </remarks>
    private void Leave(LoadItem item)
    {
        ProjectsSession? retired = null;

        lock (_gate)
        {
            item.Waiters--;

            if (item.Waiters > 0 || item.IsPinned || item.Result.IsCompleted)
                return;

            var session = item.Session;

            // Не начатая загрузка уходит из очереди сразу; начатую остановит отмена, и её итог
            // разберёт сама очередь.
            if (session.Pending == item)
            {
                session.Pending = null;

                if (_session == session)
                {
                    retired = Settle(session);
                    Republish(SnapshotStep.None);
                }
            }
        }

        item.Abandon();

        if (retired is not null)
            _ = Retire(retired);
    }

    private async Task RunAsync(LoadItem item)
    {
        var session = item.Session;
        WorkspaceLoadRequest request;
        ProjectsLoadReason reason;
        ImmutableArray<CanonicalPath> causes;

        lock (_gate)
        {
            if (session.Pending == item)
                session.Pending = null;

            if (item.Result.IsCompleted || _session != session)
            {
                if (_session == session)
                    Republish(SnapshotStep.None);

                return;
            }

            session.Running = item;
            request = session.Request();
            reason = item.Reason;
            causes = item.Causes;
        }

        var clock = Stopwatch.StartNew();
        WorkspaceLoadResult? result = null;
        Exception? error = null;

        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime, item.Abandoned);
            var token = stop.Token;

            result = await _thread.InvokeAsync(() => _context.Tasks.RunAsync(
                Title(reason, request.EntryPointPath),
                (_, cancel) => LoadAsync(session.Workspace, request, cancel, token)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            error = e;
        }

        // Итог, пришедший после отказа, — отменённый: ждущие уже узнали об отмене, и открытие,
        // отменённое человеком, не должно следом показаться открытым.
        if (session.Lifetime.IsCancellationRequested || item.Abandoned.IsCancellationRequested)
            result = null;

        var step = result?.Snapshot is { } snapshot ? SnapshotDiff.Step(session.Snapshot, snapshot) : SnapshotStep.None;
        ProjectsSession? retired = null;

        lock (_gate)
        {
            session.Running = null;

            if (_session == session)
            {
                if (result is not null)
                {
                    if (result.Snapshot is { } published)
                        session.Snapshot = published;

                    session.LastLoad = new ProjectsLoad { Reason = reason, Result = result, Causes = causes };
                }

                retired = Settle(session);
                Republish(step);
            }
        }

        if (retired is not null)
            _ = Retire(retired);

        if (result is not null)
        {
            Announce(reason, request, causes, result, clock.Elapsed);
            Watch(session);
            item.Complete(result);
        }
        else if (error is not null)
        {
            item.Fail(error);
        }
        else
        {
            item.Cancel();
        }
    }

    private static async Task<WorkspaceLoadResult> LoadAsync(
        ProjectWorkspace workspace,
        WorkspaceLoadRequest request,
        CancellationToken task,
        CancellationToken session)
    {
        using var both = CancellationTokenSource.CreateLinkedTokenSource(task, session);

        return await workspace.LoadAsync(request, both.Token);
    }

    /// <summary>
    /// Сессия, которой нечего показать и нечего ждать, кончается. Под замком.
    /// </summary>
    /// <returns>Сессия, которую пора отпустить; null — жить ей дальше.</returns>
    /// <remarks>
    /// Так кончается открытие, отменённое раньше, чем что-то дало: держать такую сессию — значит
    /// показывать «открывается» без конца.
    /// </remarks>
    private ProjectsSession? Settle(ProjectsSession session)
    {
        if (session.Snapshot is not null || session.LastLoad is not null
            || session.Pending is not null || session.Running is not null)
        {
            return null;
        }

        _session = null;
        return session;
    }

    /// <summary>Отпускает сессию: её работа отменяется сразу, движок — очередью.</summary>
    private Task Retire(ProjectsSession session)
    {
        if (!session.Retire())
            return Task.CompletedTask;

        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_lane.Enqueue(() => ReleaseAsync(session, released)))
            _ = ReleaseAsync(session, released);

        return released.Task;
    }

    private async Task ReleaseAsync(ProjectsSession session, TaskCompletionSource released)
    {
        try
        {
            await session.Workspace.DisposeAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _context.Log.Write(StudioLogLevel.Warning, ProjectsModule.LogSource,
                $"Движок сессии {session.EntryPoint.FileName} не отпустился: {e.Message}");
        }
        finally
        {
            released.TrySetResult();
        }
    }

    /// <summary>Перемена на диске: модель сессии устарела.</summary>
    private void Stale(ProjectsSession session, ImmutableArray<CanonicalPath> causes)
    {
        lock (_gate)
        {
            if (_stopped || _session != session)
                return;

            Enqueue(session, ProjectsLoadReason.FileSystem, causes, pinned: true);
        }
    }

    private void Watch(ProjectsSession session) =>
        session.Watch(Volatile.Read(ref _watching) == 1 ? _options.Watch : null, causes => Stale(session, causes));

    private void OnSettingsChanged(object? sender, string key)
    {
        if (!string.Equals(key, ProjectsSettings.WatchFilesKey, StringComparison.Ordinal))
            return;

        var watching = ProjectsSettings.Read(_context.Settings).WatchFiles ? 1 : 0;

        if (Interlocked.Exchange(ref _watching, watching) == watching)
            return;

        ProjectsSession? session;

        lock (_gate)
            session = _session;

        if (session is not null)
            Watch(session);
    }

    /// <summary>
    /// Выводит запись из сессии и публикует её, если что-то поменялось. Под замком.
    /// </summary>
    private void Republish(SnapshotStep step)
    {
        var next = Derive(_session);

        if (step.IsEmpty && Same(_status, next))
            return;

        next = next with { Sequence = ++_sequence };

        Volatile.Write(ref _status, next);
        _publisher.Publish(next, step);
    }

    private static ProjectsStatus Derive(ProjectsSession? session) => session is null
        ? ProjectsStatus.Closed
        : new ProjectsStatus
        {
            Session = session.Number,
            State = session.Snapshot is not null ? ProjectsState.Ready
                : session.LastLoad is not null ? ProjectsState.Failed
                : ProjectsState.Opening,
            EntryPoint = session.EntryPoint,
            Configuration = session.Configuration,
            Snapshot = session.Snapshot,
            LastLoad = session.LastLoad,
            IsLoading = session.Pending is not null || session.Running is not null,
        };

    private static bool Same(ProjectsStatus a, ProjectsStatus b) =>
        a.Session == b.Session
        && a.State == b.State
        && a.EntryPoint == b.EntryPoint
        && string.Equals(a.Configuration, b.Configuration, StringComparison.Ordinal)
        && ReferenceEquals(a.Snapshot, b.Snapshot)
        && ReferenceEquals(a.LastLoad, b.LastLoad)
        && a.IsLoading == b.IsLoading;

    private WorkspaceLoadResult NothingOpen() => WorkspaceLoadResult.Failure(new ProjectDiagnostic(
        ProjectsDiagnosticCodes.NothingOpen,
        _context.Strings["module.projects.nothingOpen"],
        ProjectDiagnosticSeverity.Error));

    private string Title(ProjectsLoadReason reason, CanonicalPath entryPoint) => string.Format(
        CultureInfo.CurrentCulture,
        _context.Strings[reason == ProjectsLoadReason.Open ? "module.projects.task.open" : "module.projects.task.reload"],
        entryPoint.FileName);

    /// <summary>Итог загрузки — в журнал, одной строкой, с причиной и временем.</summary>
    private void Announce(
        ProjectsLoadReason reason,
        WorkspaceLoadRequest request,
        ImmutableArray<CanonicalPath> causes,
        WorkspaceLoadResult result,
        TimeSpan elapsed)
    {
        // Какой MSBuild нашёлся — один раз за службу: регистрация одна на процесс, и первая
        // загрузка — первое место, где об этом можно сказать правду.
        if (MSBuildEnvironment.Current is { } registration && Interlocked.Exchange(ref _announced, 1) == 0)
            _context.Log.Write(StudioLogLevel.Info, ProjectsModule.LogSource, $"MSBuild: {registration}");

        var why = reason switch
        {
            ProjectsLoadReason.Open => "открыто",
            ProjectsLoadReason.Reload => "перезагружено",
            ProjectsLoadReason.Configuration => $"перезагружено под конфигурацию {request.Configuration ?? "проекта"}",
            _ => $"перезагружено, изменилось: {Causes(causes)}",
        };

        if (result.Snapshot is { } snapshot)
        {
            var errors = result.Diagnostics.Count(diagnostic => diagnostic.IsError);

            _context.Log.Write(
                errors > 0 ? StudioLogLevel.Warning : StudioLogLevel.Info,
                ProjectsModule.LogSource,
                $"{snapshot.Name}: {why} — проектов {snapshot.Projects.Length}, ошибок {errors}, {elapsed.TotalMilliseconds:F0} мс");
        }
        else
        {
            var first = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

            _context.Log.Write(
                StudioLogLevel.Error,
                ProjectsModule.LogSource,
                $"{request.EntryPointPath.FileName}: не {(reason == ProjectsLoadReason.Open ? "открылось" : "перезагрузилось")} — {first?.Code} {first?.Message}");
        }
    }

    private static string Causes(ImmutableArray<CanonicalPath> causes) => causes.Length <= 3
        ? string.Join(", ", causes.Select(cause => cause.FileName))
        : $"{string.Join(", ", causes.Take(3).Select(cause => cause.FileName))} и ещё {causes.Length - 3}";
}
