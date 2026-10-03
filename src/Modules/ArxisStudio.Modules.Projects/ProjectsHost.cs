using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Modules.Projects.Files;
using ArxisStudio.Modules.Projects.History;
using ArxisStudio.Projects;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using ArxisStudio.ProjectSystem.NuGet;
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
/// <b>Одна полоса на движок.</b> Загрузки и операции идут очередью <see cref="Lane"/> по одной.
/// Ждущая, но не начатая загрузка у сессии одна, и новые просьбы к ней присоединяются; операции не
/// склеиваются вовсе. Движок ушедшей сессии отпускается той же очередью — после того, что он
/// дочитывает.
/// </para>
/// <para>
/// <b>Ожидаемое — результат.</b> Провал загрузки — это диагностики в итоге и в <c>LastLoad</c>. До
/// шва студии доходят только собственные ошибки службы: задача студии приписывает их модулю.
/// </para>
/// <para>
/// <b>Профили</b> — своя оценка открытого со своими свойствами — живут под тем же замком и той же
/// полосой. Профиль следует за сессией: она сменилась — его оценка заводится заново
/// (<see cref="RebindProfiles"/>); загрузка службы дала снимок — следом встаёт загрузка профиля.
/// </para>
/// </remarks>
internal sealed class ProjectsHost : IStudioProjects, IStudioBuild, IStudioPackages
{
    private readonly IStudioContext _context;
    private readonly ProjectsHostOptions _options;
    private readonly IProjectsThread _thread;
    private readonly ChangePublisher _publisher;
    private readonly OperationPublisher _operations;
    private readonly Lane _lane;
    private readonly HistoryRecorder _history;
    private readonly ProjectsJournal _journal;
    private readonly Lock _gate = new();
    private readonly Dictionary<ProjectProfileKind, ProjectProfile> _profiles = [];

    private ProjectsStatus _status = ProjectsStatus.Closed;
    private ProjectsSession? _session;
    private long _sessions;
    private long _sequence;
    private bool _stopped;
    private int _watching;
    private long _operationNumber;
    private ProjectOperation? _running;

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

        _publisher = new ChangePublisher(this, _thread, options.SubscriberFailed);

        _operations = new OperationPublisher(this, _thread, options.SubscriberFailed);
        _journal = new ProjectsJournal(context.Log);
        _lane = new Lane(error => context.Log.Write(
            StudioLogLevel.Error, ProjectsModule.LogSource, $"Очередь службы проектов: {error}"));

        _watching = ProjectsSettings.Read(context.Settings).WatchFiles ? 1 : 0;
        _history = new HistoryRecorder(context, HistoryRecorder.Root(options.HistoryRoot), options.HistoryCoalescing);
        Files = new FilesService(this, context, _thread);
        HistoryService = new HistoryService(this, context, _thread);
        context.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>Локальная история службы.</summary>
    internal HistoryRecorder History => _history;

    /// <summary>Служба файлов — отдельным лицом: своё событие перемен у неё своё.</summary>
    internal FilesService Files { get; }

    /// <summary>Служба локальной истории — тоже отдельным лицом и по той же причине.</summary>
    internal HistoryService HistoryService { get; }

    /// <summary>Служба остановлена.</summary>
    internal bool IsStopped
    {
        get
        {
            lock (_gate)
                return _stopped;
        }
    }

    /// <summary>
    /// Перечитывает модель после правки файлов — полосой движка — и ждёт, пока перечитает.
    /// </summary>
    /// <param name="session">Чья модель.</param>
    /// <param name="reason">Почему.</param>
    /// <returns>
    /// Модель перечитана после правки и снимок опубликован; <c>false</c> — загрузка не удалась или
    /// не состоялась: служба остановлена, сессия сменилась или перечитать взялась загрузка, вставшая
    /// в очередь в тот же миг.
    /// </returns>
    /// <remarks>
    /// Загрузка, уже стоящая в очереди, ждётся вместо своей: она начнётся позже правки и прочтёт тот
    /// же диск. Ждать её приходится вне полосы: изнутри полосы ожидание дела, стоящего в ней следом,
    /// не кончилось бы никогда.
    /// </remarks>
    internal async Task<bool> RereadAsync(ProjectsSession session, ProjectsLoadReason reason)
    {
        var queued = new TaskCompletionSource<LoadItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reread = false;

        var accepted = _lane.Enqueue(async () =>
        {
            LoadItem? pending;

            lock (_gate)
                pending = _session == session ? session.Pending : null;

            if (pending is not null)
            {
                queued.TrySetResult(pending);
                return;
            }

            try
            {
                reread = await Reread(session, reason);
            }
            finally
            {
                queued.TrySetResult(null);
            }
        });

        if (!accepted)
            return false;

        if (await queued.Task.ConfigureAwait(false) is not { } waiting)
            return reread;

        try
        {
            return (await waiting.Result.ConfigureAwait(false)).HasSnapshot;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Провал загрузки служба уже записала в LastLoad; правке файлов он не отказ.
            return false;
        }
    }

    /// <summary>Открытая сессия; null — ничего не открыто. Тестам — чтобы дотянуться до её истории.</summary>
    internal ProjectsSession? Session
    {
        get
        {
            lock (_gate)
                return _session;
        }
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
        List<ProfileSession> gone = [];
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
                gone = RebindProfiles();
            }

            item = Enqueue(session, ProjectsLoadReason.Open, [], pinned: false);
        }

        ReleaseProfiles(gone);

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
                return Task.FromResult(WorkspaceLoadResult.Failure(Refusals.Diagnostic(ProjectsDiagnosticCodes.NothingOpen, NothingOpen)));

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
                return Task.FromResult(WorkspaceLoadResult.Failure(Refusals.Diagnostic(ProjectsDiagnosticCodes.NothingOpen, NothingOpen)));

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
    public IStudioProjectProfile OpenProfile(ProjectProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Kind))
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Такого профиля нет");

        List<ProfileSession> gone;
        ProfileHandle handle;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (!_profiles.TryGetValue(request.Kind, out var profile))
            {
                profile = new ProjectProfile(
                    request.Kind,
                    PropertiesOf(request.Kind),
                    created => new ChangePublisher(created, _thread, _options.SubscriberFailed));

                _profiles.Add(request.Kind, profile);
            }

            profile.Holders++;

            var widened = profile.Widen(request.AdditionalProperties);
            var fresh = profile.Session is null;

            gone = RebindProfiles();

            // Читать сразу есть смысл, когда службе есть что показать и её загрузка не идёт: идущая
            // поставит загрузку профиля следом сама.
            if ((fresh || widened)
                && profile.Session is { } bound
                && bound.Owner is { Snapshot: not null, Pending: null, Running: null })
            {
                EnqueueProfile(profile, bound, fresh ? ProjectsLoadReason.Open : ProjectsLoadReason.Reload, pinned: true);
            }

            RepublishProfile(profile, SnapshotStep.None);
            handle = new ProfileHandle(this, profile);
        }

        ReleaseProfiles(gone);

        return handle;
    }

    /// <summary>Перечитывает профиль — или присоединяется к загрузке профиля, уже стоящей в очереди.</summary>
    /// <param name="profile">Чей профиль.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    internal Task<WorkspaceLoadResult> RefreshProfileAsync(ProjectProfile profile, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<WorkspaceLoadResult>(cancellationToken);

        LoadItem item;
        ProfileSession bound;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (profile.Session is not { } session)
                return Task.FromResult(WorkspaceLoadResult.Failure(Refusals.Diagnostic(ProjectsDiagnosticCodes.NothingOpen, NothingOpen)));

            bound = session;
            item = EnqueueProfile(profile, session, ProjectsLoadReason.Reload, pinned: false);
        }

        return item.Wait(cancellationToken, left => LeaveProfile(profile, bound, left));
    }

    /// <summary>Ставит операцию профиля в очередь службы.</summary>
    /// <param name="profile">Чей профиль.</param>
    /// <param name="request">Запрос просившего.</param>
    /// <param name="progress">Куда говорить о ходе просившему.</param>
    /// <param name="cancellationToken">Отмена и ожидания, и операции.</param>
    internal Task<ProjectOperationResult> ExecuteProfileAsync(
        ProjectProfile profile,
        ProjectOperationRequest request,
        IProgress<ProjectOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind))
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Такой операции над проектом нет");

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ProjectOperationResult>(cancellationToken);

        OperationItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session || profile.Session is not { } bound || bound.Owner != session)
                return Task.FromResult(Refusals.Of(ProjectsDiagnosticCodes.NothingOpen, NothingOpen));

            // Запрос строят по снимку профиля: запрос к прежней оценке собрал бы не то, что видно.
            if (request.Workspace != bound.Workspace.Identity)
                return Task.FromResult(Refusals.Of(ProjectsDiagnosticCodes.ProjectNotOpen, _context.Strings["module.projects.profile.stale"]));

            var operation = new ProjectOperation
            {
                Id = ++_operationNumber,
                Kind = request.Kind,
                EntryPoint = request.EntryPointPath,
                Projects = request.Projects.IsDefault ? [] : request.Projects,
                Configuration = request.Configuration,
                Profile = profile.Kind,
            };

            item = new OperationItem(session, operation, progress)
            {
                Profile = new ProfileRun(profile, bound, request with
                {
                    GlobalProperties = Over(request.GlobalProperties, profile.GlobalProperties),
                }),
            };

            _lane.Enqueue(() => RunOperationAsync(item));
        }

        return item.Wait(cancellationToken);
    }

    /// <summary>
    /// Держатель отпустил профиль; последний отпускает и его оценку.
    /// </summary>
    /// <param name="profile">Чей профиль.</param>
    /// <returns>Задача, которая завершится, когда движок оценки отпущен.</returns>
    internal Task ReleaseProfileAsync(ProjectProfile profile)
    {
        ProfileSession? gone = null;

        lock (_gate)
        {
            profile.Holders--;

            if (profile.Holders > 0)
                return Task.CompletedTask;

            gone = profile.Session;
            profile.Session = null;
            RepublishProfile(profile, SnapshotStep.None);
        }

        return gone is null ? Task.CompletedTask : ReleaseProfile(gone);
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
        _history.Dispose();
    }

    /// <inheritdoc/>
    public ProjectOperation? Running => Volatile.Read(ref _running);

    /// <inheritdoc/>
    public event EventHandler<ProjectOperationEventArgs>? Started
    {
        add => _operations.SubscribeStarted(value);
        remove => _operations.UnsubscribeStarted(value);
    }

    /// <inheritdoc/>
    public event EventHandler<ProjectOperationEventArgs>? Completed
    {
        add => _operations.SubscribeCompleted(value);
        remove => _operations.UnsubscribeCompleted(value);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> RunAsync(
        ProjectOperationKind kind,
        ImmutableArray<ProjectIdentity> projects = default,
        IProgress<ProjectOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Такой операции над проектом нет");

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ProjectOperationResult>(cancellationToken);

        OperationItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session)
                return Task.FromResult(Refusals.Of(ProjectsDiagnosticCodes.NothingOpen, NothingOpen));

            if (Stranger(session, projects) is { } stranger)
            {
                return Task.FromResult(Refusals.Of(
                    ProjectsDiagnosticCodes.ProjectNotOpen,
                    string.Format(CultureInfo.CurrentCulture, _context.Strings["module.projects.projectNotOpen"], stranger)));
            }

            item = Begin(session, kind, projects, progress);
        }

        return item.Wait(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> InstallAsync(
        ProjectIdentity project,
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return EditAsync(project, packageId, version, remove: false, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> UninstallAsync(
        ProjectIdentity project,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return EditAsync(project, packageId, version: null, remove: true, cancellationToken);
    }

    /// <summary>Команда «перезагрузить»: итог не ждёт никто, а «нечего» стоит сказать человеку.</summary>
    internal void ReloadFromCommand() => _ = CommandAsync(async () =>
    {
        var result = await ReloadAsync();

        SayIfNothingOpen(result.Diagnostics);
    });

    /// <summary>Команда «закрыть».</summary>
    internal void CloseFromCommand() => _ = CommandAsync(CloseAsync);

    /// <summary>Команда сборки: итога не ждёт никто, а «нечего» стоит сказать человеку.</summary>
    /// <param name="kind">Что делать.</param>
    internal void RunFromCommand(ProjectOperationKind kind) => _ = CommandAsync(async () =>
    {
        var result = await RunAsync(kind);

        SayIfNothingOpen(result.Diagnostics);
    });

    /// <summary>Команде, которой нечего делать, отвечают словом в строке состояния: итога не ждёт никто.</summary>
    /// <param name="diagnostics">Что ответила служба.</param>
    private void SayIfNothingOpen(IEnumerable<ProjectDiagnostic> diagnostics)
    {
        if (diagnostics.Any(diagnostic => diagnostic.Code == ProjectsDiagnosticCodes.NothingOpen))
            _context.GetService<IStudioStatus>()?.Show(NothingOpen);
    }

    /// <summary>«Ничего не открыто» — словами человека.</summary>
    private string NothingOpen => _context.Strings["module.projects.nothingOpen"];

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

                // Профили следуют за службой: загрузка, давшая снимок, ставит следом загрузку каждого.
                if (result?.Snapshot is not null)
                    FollowProfiles(session, reason, causes);
            }
        }

        if (retired is not null)
            _ = Retire(retired);

        if (result is not null)
        {
            _journal.Loaded(reason, request, causes, result, clock.Elapsed);
            Watch(session);
            item.Complete(result);
            RestoreOnOpen(session, result);
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
    /// Ставит операцию в очередь. Под замком.
    /// </summary>
    /// <param name="session">Чья операция.</param>
    /// <param name="kind">Что делать.</param>
    /// <param name="projects">Какие проекты; пусто — открытое целиком.</param>
    /// <param name="progress">Куда говорить о ходе просившему; null — некуда.</param>
    /// <param name="edit">Правка пакетов, если операция — она.</param>
    /// <param name="layout">Где живут версии пакетов правки.</param>
    private OperationItem Begin(
        ProjectsSession session,
        ProjectOperationKind kind,
        ImmutableArray<ProjectIdentity> projects,
        IProgress<ProjectOperationProgress>? progress,
        PackageEditRequest? edit = null,
        PackageVersionLayout? layout = null)
    {
        var operation = new ProjectOperation
        {
            Id = ++_operationNumber,
            Kind = kind,
            EntryPoint = session.EntryPoint,
            Projects = projects.IsDefault ? [] : projects,
            Configuration = session.Configuration,
        };

        var item = new OperationItem(session, operation, progress) { Edit = edit, Layout = layout };

        _lane.Enqueue(() => RunOperationAsync(item));

        return item;
    }

    /// <summary>
    /// Выполняет операцию, когда до неё дошла очередь.
    /// </summary>
    /// <remarks>
    /// Отменённая в очереди сюда доходит, но движка не касается: её итог уже отменён. Операция
    /// сессии, которую успели сменить, не начинается по той же причине.
    /// </remarks>
    /// <param name="item">Операция.</param>
    private async Task RunOperationAsync(OperationItem item)
    {
        var session = item.Session;
        var operation = item.Operation;

        lock (_gate)
        {
            if (item.Result.IsCompleted || _session != session)
                return;

            // Оценку профиля отпустили, пока операция стояла: собирать её не для кого.
            if (item.Profile is { Session.IsRetired: true })
            {
                item.Cancel();
                return;
            }

            Volatile.Write(ref _running, operation);
        }

        _operations.Start(operation);

        var clock = Stopwatch.StartNew();
        ProjectOperationResult? result = null;
        Exception? error = null;

        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(
                session.Lifetime, item.Abandoned, item.Profile?.Session.Lifetime ?? CancellationToken.None);

            result = await _thread.InvokeAsync(() => _context.Tasks.RunAsync(
                Title(item),
                (progress, cancel) => ExecuteAsync(item, progress, cancel, stop.Token)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            error = e;
        }

        // Итог, пришедший после отказа, — отменённый: просивший уже узнал об отмене.
        if (session.Lifetime.IsCancellationRequested || item.Abandoned.IsCancellationRequested
            || item.Profile is { Session.IsRetired: true })
        {
            result = null;
        }

        Volatile.Write(ref _running, null);

        _journal.Finished(item, result, error, clock.Elapsed);
        _operations.Complete(operation, result, isCancelled: result is null && error is null);

        if (result is not null)
            item.Complete(result);
        else if (error is not null)
            item.Fail(error);
        else
            item.Cancel();
    }

    /// <summary>
    /// Работа операции: восстановление перед сборкой, сама операция, перезагрузка после удачного
    /// восстановления.
    /// </summary>
    /// <param name="item">Операция.</param>
    /// <param name="task">Куда говорить о ходе задаче студии.</param>
    /// <param name="cancel">Отмена задачи — крестик на ней.</param>
    /// <param name="owner">Отмена сессии и просившего.</param>
    private async Task<ProjectOperationResult> ExecuteAsync(
        OperationItem item,
        IStudioProgress task,
        CancellationToken cancel,
        CancellationToken owner)
    {
        using var both = CancellationTokenSource.CreateLinkedTokenSource(cancel, owner);

        var session = item.Session;
        var operation = item.Operation;
        var progress = item.Progress(task, Blame);

        if (item.Profile is { } run)
            return await ExecuteProfileAsync(session, run, progress, both.Token);

        // Правка пакетов — своя работа с тем же концом: файл, потом восстановление. Отменяет
        // провалившееся восстановление сама библиотека, возвращая файлы байт в байт; здесь —
        // очередь, задача человека и перечитывание модели после удачи.
        if (item.Edit is { } edit)
        {
            var edited = await PackageInstaller.ApplyAndRestoreAsync(
                edit, session.Workspace, item.Layout, progress, both.Token);

            if (!edited.HasErrors)
                await Reread(session, ProjectsLoadReason.Packages);

            return edited;
        }

        // Цель Build у MSBuild пакетов не восстанавливает — этим она и отличается от dotnet build, —
        // а сборка по ненайденным пакетам показала бы человеку ошибки компилятора вместо причины.
        if (operation.Kind is ProjectOperationKind.Build or ProjectOperationKind.Rebuild)
        {
            var restored = await session.Workspace.ExecuteAsync(
                Request(session, operation, ProjectOperationKind.Restore), progress, both.Token);

            if (restored.HasErrors)
                return restored;
        }

        var result = await session.Workspace.ExecuteAsync(
            Request(session, operation, operation.Kind), progress, both.Token);

        // Восстановление переписывает то, из чего собирается модель. Перечитывается она здесь же,
        // на полосе: поставить перезагрузку в очередь значило бы ждать самого себя.
        if (operation.Kind == ProjectOperationKind.Restore && !result.HasErrors)
            await Reread(session, ProjectsLoadReason.Restore);

        return result;
    }

    /// <summary>
    /// Работа операции профиля: запрос как есть над его движком; удачное восстановление перечитывает
    /// модель службы, а за ней и профиль.
    /// </summary>
    /// <param name="session">Сессия службы.</param>
    /// <param name="run">Операция профиля.</param>
    /// <param name="progress">Куда говорить о ходе.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <remarks>
    /// Восстановление у профиля и службы общее — <c>obj/project.assets.json</c>, — и после него модель
    /// службы устарела тоже. Слежение службы на это время придержано, а выходы восстановления
    /// запомнены такими, какими оно их оставило: иначе слежение попросило бы вторую загрузку ради
    /// того, что перечитывание здесь уже прочло.
    /// </remarks>
    private async Task<ProjectOperationResult> ExecuteProfileAsync(
        ProjectsSession session,
        ProfileRun run,
        IProgress<ProjectOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var restore = run.Request.Kind == ProjectOperationKind.Restore;
        using var hold = restore ? session.Hold() : NoWatchHold.Instance;

        var result = await run.Session.Workspace.ExecuteAsync(run.Request, progress, cancellationToken);

        if (restore && !result.HasErrors)
        {
            if (session.Snapshot is { } snapshot)
                hold.Expect(snapshot.Projects.SelectMany(project => project.RestoreOutputs));

            if (await Reread(session, ProjectsLoadReason.Restore))
                hold.Confirm();
        }

        return result;
    }

    /// <summary>Что просить у движка.</summary>
    /// <param name="session">Чья операция.</param>
    /// <param name="operation">Операция.</param>
    /// <param name="kind">Что делать сейчас: у сборки первым шагом идёт восстановление.</param>
    private static ProjectOperationRequest Request(
        ProjectsSession session,
        ProjectOperation operation,
        ProjectOperationKind kind) => new()
    {
        Kind = kind,
        Workspace = session.Workspace.Identity,
        EntryPointPath = operation.EntryPoint,
        Projects = operation.Projects,
        Configuration = operation.Configuration,
    };

    /// <summary>
    /// Перечитывает модель вслед за восстановлением — здесь же, на полосе.
    /// </summary>
    /// <remarks>
    /// Загрузка, уже стоящая за операцией, эту работу сделает сама: поставить вторую значило бы
    /// перечитать решение дважды подряд.
    /// </remarks>
    /// <param name="session">Чью модель перечитать.</param>
    /// <param name="reason">Почему её перечитывают.</param>
    /// <returns>Модель перечитана здесь же и снимок опубликован.</returns>
    private async Task<bool> Reread(ProjectsSession session, ProjectsLoadReason reason)
    {
        LoadItem item;

        lock (_gate)
        {
            if (_stopped || _session != session || session.Pending is not null)
                return false;

            item = new LoadItem(session, reason);
            session.Pending = item;
            item.Pin();
            Republish(SnapshotStep.None);
        }

        await RunAsync(item);

        return item.Result.IsCompletedSuccessfully && item.Result.Result.HasSnapshot;
    }

    /// <summary>
    /// Восстановление при открытии: один раз за сессию и только если пакеты не восстановлены.
    /// </summary>
    /// <remarks>
    /// Человек, открывший только что склонированный репозиторий, ждёт от студии модели, а не
    /// списка ненайденных пакетов. Настройка — на случай, когда чужой restore запускать не хочется.
    /// </remarks>
    /// <param name="session">Чья загрузка кончилась.</param>
    /// <param name="result">Её итог.</param>
    private void RestoreOnOpen(ProjectsSession session, WorkspaceLoadResult result)
    {
        if (!result.HasSnapshot || !NeedsRestore.From(result))
            return;

        // Настройка читается до замка: под ним чужого не зовут.
        var wanted = ProjectsSettings.Read(_context.Settings).RestoreOnOpen;

        lock (_gate)
        {
            if (_stopped || _session != session || session.Restored)
                return;

            session.Restored = true;

            if (!wanted)
                return;

            _ = Begin(session, ProjectOperationKind.Restore, [], progress: null);
        }

        _context.Log.Write(
            StudioLogLevel.Info,
            ProjectsModule.LogSource,
            $"{session.EntryPoint.FileName}: пакеты не восстановлены — восстанавливаю");
    }

    /// <summary>
    /// Ставит правку пакетов в очередь.
    /// </summary>
    /// <remarks>
    /// Чем правка станет — установкой, обновлением или удалением, — решает то, что проект объявил в
    /// своём файле: поставить поверх объявленного значит поменять версию, а не завести вторую
    /// ссылку. Имена пакетов сравниваются без учёта регистра, как их сравнивает NuGet.
    /// </remarks>
    /// <param name="project">Какой проект.</param>
    /// <param name="packageId">Какой пакет.</param>
    /// <param name="version">Какую версию писать; null — у удаления.</param>
    /// <param name="remove">Убрать ссылку, а не поставить.</param>
    /// <param name="cancellationToken">Отмена.</param>
    private Task<ProjectOperationResult> EditAsync(
        ProjectIdentity project,
        string packageId,
        string? version,
        bool remove,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ProjectOperationResult>(cancellationToken);

        OperationItem item;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is not { } session)
                return Task.FromResult(Refusals.Of(ProjectsDiagnosticCodes.NothingOpen, NothingOpen));

            if (Where(session, project) is not { } snapshot)
            {
                return Task.FromResult(Refusals.Of(
                    ProjectsDiagnosticCodes.ProjectNotOpen,
                    string.Format(CultureInfo.CurrentCulture, _context.Strings["module.projects.projectNotOpen"], project)));
            }

            var declared = snapshot.PackageReferences.FirstOrDefault(
                reference => string.Equals(reference.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

            // Ссылку, пришедшую импортом, редактор в файле проекта не найдёт, а править чужой файл
            // за человека — не его дело.
            if (declared is { Origin: ProjectItemOrigin.Imported })
            {
                return Task.FromResult(Refusals.Of(
                    ProjectsDiagnosticCodes.ReferenceNotInProjectFile,
                    string.Format(CultureInfo.CurrentCulture, _context.Strings["module.projects.packageNotInProject"], packageId)));
            }

            var edit = new PackageEditRequest
            {
                Kind = remove ? PackageEditKind.Uninstall
                    : declared is null ? PackageEditKind.Install
                    : PackageEditKind.Update,
                ProjectFilePath = snapshot.ProjectFilePath,
                PackageId = packageId,
                Version = version,
            };

            item = Begin(
                session,
                ProjectOperationKind.Restore,
                [project],
                progress: null,
                edit,
                PackageVersionLayout.From(snapshot));
        }

        return item.Wait(cancellationToken);
    }

    /// <summary>Снимок названного проекта; null — его в сессии нет. Под замком.</summary>
    /// <param name="session">Чей снимок.</param>
    /// <param name="project">Какой проект.</param>
    private static ProjectSnapshot? Where(ProjectsSession session, ProjectIdentity project) =>
        session.Snapshot?.Projects.FirstOrDefault(candidate => candidate.Identity == project);

    /// <summary>Первый из названных проектов, которого нет в снимке сессии; null — все свои. Под замком.</summary>
    /// <param name="session">Чей снимок.</param>
    /// <param name="projects">Названные проекты.</param>
    private static string? Stranger(ProjectsSession session, ImmutableArray<ProjectIdentity> projects)
    {
        if (projects.IsDefaultOrEmpty)
            return null;

        foreach (var project in projects)
        {
            if (Where(session, project) is null)
                return project.ToString();
        }

        return null;
    }

    /// <summary>Сбой чужого приёмника хода — в журнал: операцию он не роняет.</summary>
    /// <param name="error">Сбой.</param>
    private void Blame(Exception error) => _context.Log.Write(
        StudioLogLevel.Warning, ProjectsModule.LogSource, $"Приёмник хода операции упал: {error.Message}");

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

    /// <summary>Отпускает сессию: её работа отменяется сразу, движок — очередью; оценки профилей — тоже.</summary>
    private Task Retire(ProjectsSession session)
    {
        if (!session.Retire())
            return Task.CompletedTask;

        List<ProfileSession> gone;

        lock (_gate)
            gone = RebindProfiles();

        ReleaseProfiles(gone);

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

    /// <summary>
    /// Ставит оценку каждого держимого профиля на текущую сессию службы. Под замком.
    /// </summary>
    /// <returns>Оценки, которые кончились: их движки отпускает <see cref="ReleaseProfiles"/> вне замка.</returns>
    /// <remarks>
    /// Зовётся везде, где сессия могла смениться: при открытии другого пути и при конце сессии. Оценка
    /// той же сессии остаётся; у профиля без держателей её нет вовсе.
    /// </remarks>
    private List<ProfileSession> RebindProfiles()
    {
        var gone = new List<ProfileSession>();

        foreach (var profile in _profiles.Values)
        {
            var wanted = profile.Holders > 0 && _session is { IsRetired: false } ? _session : null;

            if (profile.Session?.Owner == wanted)
                continue;

            if (profile.Session is { } previous)
                gone.Add(previous);

            profile.Session = wanted is null ? null : new ProfileSession(wanted, _options.Workspace());
            RepublishProfile(profile, SnapshotStep.None);
        }

        return gone;
    }

    /// <summary>Отпускает кончившиеся оценки профилей: работа отменяется сразу, движки — очередью.</summary>
    private void ReleaseProfiles(List<ProfileSession> gone)
    {
        foreach (var session in gone)
            _ = ReleaseProfile(session);
    }

    private Task ReleaseProfile(ProfileSession session)
    {
        if (!session.Retire())
            return Task.CompletedTask;

        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_lane.Enqueue(() => DisposeProfileAsync(session, released)))
            _ = DisposeProfileAsync(session, released);

        return released.Task;
    }

    private async Task DisposeProfileAsync(ProfileSession session, TaskCompletionSource released)
    {
        try
        {
            await session.Workspace.DisposeAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _context.Log.Write(StudioLogLevel.Warning, ProjectsModule.LogSource,
                $"Движок профиля {session.Owner.EntryPoint.FileName} не отпустился: {e.Message}");
        }
        finally
        {
            released.TrySetResult();
        }
    }

    /// <summary>Ставит следом за загрузкой службы загрузку каждого профиля её сессии. Под замком.</summary>
    private void FollowProfiles(ProjectsSession session, ProjectsLoadReason reason, ImmutableArray<CanonicalPath> causes)
    {
        foreach (var profile in _profiles.Values)
        {
            if (profile.Session is { } bound && bound.Owner == session)
                EnqueueProfile(profile, bound, reason, pinned: true).AddCauses(causes);
        }
    }

    /// <summary>
    /// Ставит загрузку профиля или присоединяет просьбу к уже стоящей. Под замком.
    /// </summary>
    private LoadItem EnqueueProfile(ProjectProfile profile, ProfileSession session, ProjectsLoadReason reason, bool pinned)
    {
        if (session.Pending is not { } item)
        {
            item = new LoadItem(session.Owner, reason);
            session.Pending = item;
            _lane.Enqueue(() => RunProfileLoadAsync(profile, session, item));
        }
        else
        {
            item.Strengthen(reason);
        }

        if (pinned)
            item.Pin();
        else
            item.Join();

        RepublishProfile(profile, SnapshotStep.None);

        return item;
    }

    /// <summary>Ждущий загрузку профиля ушёл; не начатая и никем больше не ждущая снимается.</summary>
    private void LeaveProfile(ProjectProfile profile, ProfileSession session, LoadItem item)
    {
        lock (_gate)
        {
            item.Waiters--;

            if (item.Waiters > 0 || item.IsPinned || item.Result.IsCompleted)
                return;

            if (session.Pending == item)
            {
                session.Pending = null;
                RepublishProfile(profile, SnapshotStep.None);
            }
        }

        item.Abandon();
    }

    /// <summary>Загрузка профиля, когда до неё дошла очередь.</summary>
    private async Task RunProfileLoadAsync(ProjectProfile profile, ProfileSession session, LoadItem item)
    {
        WorkspaceLoadRequest request;
        ProjectsLoadReason reason;
        ImmutableArray<CanonicalPath> causes;

        lock (_gate)
        {
            if (session.Pending == item)
                session.Pending = null;

            if (item.Result.IsCompleted || profile.Session != session || _session != session.Owner)
            {
                if (profile.Session == session)
                    RepublishProfile(profile, SnapshotStep.None);

                item.Cancel();
                return;
            }

            session.Running = item;
            reason = item.Reason;
            causes = item.Causes;
            request = new WorkspaceLoadRequest
            {
                EntryPointPath = session.Owner.EntryPoint,
                Workspace = session.Workspace.Identity,
                Configuration = session.Owner.Configuration,
                GlobalProperties = profile.GlobalProperties,
                Options = WorkspaceLoadOptions.Default with { AdditionalProperties = profile.Properties },
            };
        }

        var clock = Stopwatch.StartNew();
        WorkspaceLoadResult? result = null;
        Exception? error = null;

        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime, item.Abandoned);
            var token = stop.Token;

            result = await _thread.InvokeAsync(() => _context.Tasks.RunAsync(
                ProfileTitle(request.EntryPointPath),
                (_, cancel) => LoadAsync(session.Workspace, request, cancel, token)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            error = e;
        }

        if (session.IsRetired || item.Abandoned.IsCancellationRequested)
            result = null;

        var step = result?.Snapshot is { } snapshot ? SnapshotDiff.Step(session.Snapshot, snapshot) : SnapshotStep.None;

        lock (_gate)
        {
            session.Running = null;

            if (profile.Session == session)
            {
                if (result is not null)
                {
                    if (result.Snapshot is { } published)
                        session.Snapshot = published;

                    session.LastLoad = new ProjectsLoad { Reason = reason, Result = result, Causes = causes };
                }

                RepublishProfile(profile, step);
            }
        }

        if (result is not null)
        {
            _journal.Loaded(reason, request, causes, result, clock.Elapsed, profile.Kind);
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

    /// <summary>Выводит запись профиля и публикует её, если что-то поменялось. Под замком.</summary>
    private static void RepublishProfile(ProjectProfile profile, SnapshotStep step)
    {
        var next = profile.Derive();

        if (step.IsEmpty && SessionStatus.Same(profile.Status, next))
            return;

        next = next with { Sequence = ++profile.Sequence };

        profile.Status = next;
        profile.Publisher.Publish(next, step);
    }

    /// <summary>Глобальные свойства профиля этого вида.</summary>
    private static ProjectMetadata PropertiesOf(ProjectProfileKind kind) => kind switch
    {
        ProjectProfileKind.Design => MSBuildDesignOutput.GlobalProperties,
        _ => ProjectMetadata.Empty,
    };

    /// <summary>Свойства просившего, а поверх — свойства профиля: имена MSBuild без учёта регистра.</summary>
    private static ProjectMetadata Over(ProjectMetadata asked, ProjectMetadata profile)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in asked)
            merged[name] = value;

        foreach (var (name, value) in profile)
            merged[name] = value;

        return ProjectMetadata.Create(merged);
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

    private void Watch(ProjectsSession session)
    {
        session.Watch(Volatile.Read(ref _watching) == 1 ? _options.Watch : null, causes => Stale(session, causes));
        session.Remember(_history.IsOn ? _history.Watch : null);
        session.Content(Files.IsContentWatched ? WatchContentOf : null);
    }

    /// <summary>Подписчики на перемены содержимого появились или ушли: слежению текущей сессии — жить или гаснуть.</summary>
    internal void WatchContent()
    {
        if (Session is { } session)
            session.Content(Files.IsContentWatched ? WatchContentOf : null);
    }

    private ContentWatch WatchContentOf() => new(Files.OnContent, _options.ContentCoalescing);

    private void OnSettingsChanged(object? sender, string key)
    {
        if (ProjectsSettings.HistoryKeys.Contains(key, StringComparer.Ordinal))
        {
            if (_history.Configure(ProjectsSettings.Read(_context.Settings)) && Session is { } current)
                current.Remember(_history.IsOn ? _history.Watch : null);

            return;
        }

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
        var next = SessionStatus.Of(_session);

        if (step.IsEmpty && SessionStatus.Same(_status, next))
            return;

        next = next with { Sequence = ++_sequence };

        Volatile.Write(ref _status, next);
        _publisher.Publish(next, step);
    }

    private string Title(ProjectsLoadReason reason, CanonicalPath entryPoint) => string.Format(
        CultureInfo.CurrentCulture,
        _context.Strings[reason == ProjectsLoadReason.Open ? "module.projects.task.open" : "module.projects.task.reload"],
        entryPoint.FileName);

    /// <summary>Имя задачи студии для загрузки профиля; вид у профилей пока один — дизайн.</summary>
    private string ProfileTitle(CanonicalPath entryPoint) => string.Format(
        CultureInfo.CurrentCulture,
        _context.Strings["module.projects.task.design.load"],
        entryPoint.FileName);

    /// <summary>Имя задачи студии: у правки пакетов оно называет пакет, а не решение; у профиля — его.</summary>
    /// <param name="item">Операция.</param>
    private string Title(OperationItem item) => item.Profile is not null
        ? string.Format(
            CultureInfo.CurrentCulture,
            _context.Strings[item.Operation.Kind switch
            {
                ProjectOperationKind.Restore => "module.projects.task.design.restore",
                ProjectOperationKind.Build => "module.projects.task.design.build",
                ProjectOperationKind.Rebuild => "module.projects.task.design.rebuild",
                _ => "module.projects.task.design.clean",
            }],
            item.Operation.EntryPoint.FileName)
        : item.Edit is { } edit
        ? string.Format(
            CultureInfo.CurrentCulture,
            _context.Strings[edit.Kind == PackageEditKind.Uninstall
                ? "module.projects.task.uninstall"
                : "module.projects.task.install"],
            edit.PackageId)
        : string.Format(
            CultureInfo.CurrentCulture,
            _context.Strings[item.Operation.Kind switch
            {
                ProjectOperationKind.Restore => "module.projects.task.restore",
                ProjectOperationKind.Build => "module.projects.task.build",
                ProjectOperationKind.Rebuild => "module.projects.task.rebuild",
                _ => "module.projects.task.clean",
            }],
            item.Operation.EntryPoint.FileName);

}
