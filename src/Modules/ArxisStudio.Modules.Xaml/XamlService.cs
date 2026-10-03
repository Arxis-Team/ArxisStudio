using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using ArxisStudio.Modules.Xaml.Documents;
using ArxisStudio.Modules.Xaml.Session;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml;

/// <summary>
/// Служба XAML: документы разметки открытого решения и поколение типов проекта, в котором они строятся.
/// </summary>
/// <remarks>
/// <para>
/// <b>Сессия на решение.</b> Профиль дизайна, хост поколения и открытые документы живут одной
/// сессией (<see cref="XamlDesignSession"/>), и она одна: поднимается первым открытым документом,
/// кончается с решением — его закрыли, открыли другое, сменили конфигурацию, — со службой или после
/// простоя. Подъём и конец идут по очереди (<see cref="_turn"/>): поколение следующей сессии не
/// загрузится рядом с поколением прежней, пока та не отпустила своё.
/// </para>
/// <para>
/// <b>Переживает решение</b> то, что принадлежит не ему: участники замены и отсрочки. Взятые до
/// первого документа, они держат и поколение, которое поднимется потом.
/// </para>
/// <para>
/// <b>Потоки.</b> Всё, кроме отсрочек и вида файла, — в потоке интерфейса: хост поколения зовёт и
/// слышит там же.
/// </para>
/// </remarks>
internal sealed class XamlService : IStudioXamlDocuments, IStudioXamlDesign
{
    private readonly IStudioContext _context;
    private readonly IStudioProjects? _projects;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly List<IXamlDesignParticipant> _participants = [];
    private readonly Lock _gate = new();
    private readonly List<Deferral> _deferrals = [];

    private XamlDesignSession? _session;
    private ProjectDesignSwapGate? _swapGate;
    private ITimer? _idle;

    // Почему не поднялась последняя сессия — до следующей попытки или до смены решения.
    private string? _failure;
    private long _failedIn;

    private XamlDesignState _reportedState = XamlDesignState.Idle;
    private string? _reportedReason;
    private XamlBuildOutcome? _reportedBuild;
    private bool _stopped;

    public XamlService(IStudioContext context, XamlServiceOptions options)
    {
        _context = context;
        Options = options;
        Words = new XamlWords(context.Strings);

        _projects = context.Projects();

        if (_projects is not null)
            _projects.Changed += OnProjectsChanged;
    }

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <summary>Шов службы.</summary>
    public XamlServiceOptions Options { get; }

    /// <summary>Слова модуля.</summary>
    public XamlWords Words { get; }

    /// <summary>Контекст модуля.</summary>
    public IStudioContext Context => _context;

    /// <summary>Сессия открытого решения, пока она есть.</summary>
    internal XamlDesignSession? Session => _session;

    /// <inheritdoc/>
    public XamlDesignState State => Describe().State;

    /// <inheritdoc/>
    public string? StateReason => Describe().Reason;

    /// <inheritdoc/>
    public XamlBuildOutcome? LastBuild { get; private set; }

    /// <summary>Участники замены сейчас, по порядку постановки.</summary>
    internal IReadOnlyList<IXamlDesignParticipant> Participants => [.. _participants];

    /// <inheritdoc/>
    public async Task<IXamlDocumentHandle> OpenAsync(CanonicalPath path, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_stopped, this);

        if (path.IsEmpty || !XamlFileKinds.IsMarkup(path))
            throw new ArgumentException(Words.NotMarkup(path.IsEmpty ? string.Empty : path.Value), nameof(path));

        var projects = _projects ?? throw new InvalidOperationException(Words.NoProjects);
        var status = projects.Status;

        if (status.Snapshot is not { } snapshot)
            throw new InvalidOperationException(Words.NothingOpen);

        if (!snapshot.TryGetProjectForFile(path, out _))
            throw new ArgumentException(Words.NotInSolution(path.Value), nameof(path));

        var session = await SessionAsync(status, cancellationToken);

        return await session.OpenAsync(path, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Открытый документ отвечает своим текстом — но только в потоке интерфейса, где живёт таблица
    /// документов; из другого потока читается файл.
    /// </remarks>
    public XamlFileKind Classify(CanonicalPath path)
    {
        if (path.IsEmpty)
            return XamlFileKind.Unknown;

        if (Dispatcher.UIThread.CheckAccess() && _session?.SyntaxOf(path) is { } open)
            return XamlFileKinds.Of(open);

        return XamlFileKinds.Read(path);
    }

    /// <inheritdoc/>
    public IDisposable Register(IXamlDesignParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Dispatcher.UIThread.VerifyAccess();

        _participants.Add(participant);

        return new Registration(this, participant);
    }

    /// <inheritdoc/>
    public IDisposable Defer(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var deferral = new Deferral(this, reason);
        ProjectDesignSwapGate? gate;

        lock (_gate)
        {
            _deferrals.Add(deferral);
            gate = _swapGate;
        }

        deferral.Bind(gate);

        return deferral;
    }

    /// <inheritdoc/>
    public async Task<XamlBuildOutcome> RebuildAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (_session is not { IsStarted: true } session)
            throw new InvalidOperationException(Words.NoGeneration);

        var result = await session.Host.BuildAsync(Words.Rebuild, default, cancellationToken);

        return XamlDesignSession.Outcome(result);
    }

    /// <summary>
    /// Останавливает службу: сессия кончается, не дожидаясь поколения, — закрытие студии ждать его не
    /// обязано.
    /// </summary>
    public void Stop() => _ = StopAsync();

    /// <summary>Останавливает службу и ждёт, пока сессия отпустит своё. Тестам.</summary>
    internal async Task StopAsync()
    {
        if (_stopped)
            return;

        _stopped = true;

        if (_projects is not null)
            _projects.Changed -= OnProjectsChanged;

        CancelIdle();

        if (_session is { } session)
        {
            session.Cancel();
            await RetireAsync(session);
        }
    }

    /// <summary>Сессия решения: та, что есть, или новая — поднятая по очереди.</summary>
    /// <param name="status">Состояние службы проектов, по которому открывают.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    private async Task<XamlDesignSession> SessionAsync(ProjectsStatus status, CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_stopped, this);

            if (_session is { IsStarted: true, IsRetiring: false } current && current.Fits(status))
            {
                CancelIdle();

                return current;
            }

            if (_session is { } stale)
            {
                _session = null;
                await stale.DisposeAsync();
            }

            // Служба файлов приходит тем же модулем, что и проекты: без неё сохранять было бы нечем.
            var files = _context.Files() ?? throw new InvalidOperationException(Words.NoProjects);
            var session = new XamlDesignSession(this, _projects!, files, status);

            _session = session;
            _failure = null;
            Report();

            try
            {
                await session.StartAsync(cancellationToken);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _session = null;
                Bind(null);

                await session.DisposeAsync();

                if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    Report();
                    throw;
                }

                // Сессию отменили изнутри: решение закрыли или сменили, пока она поднималась.
                if (e is OperationCanceledException)
                {
                    Report();
                    throw new InvalidOperationException(Words.NothingOpen, e);
                }

                _failure = e.Message;
                _failedIn = status.Session;
                Log(StudioLogLevel.Error, Words.StartFailed(e.Message));
                Report();

                throw new InvalidOperationException(Words.StartFailed(e.Message), e);
            }

            Report();

            return session;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Кончает сессию по очереди: следующая не поднимется, пока эта не отпустила своё.</summary>
    /// <param name="session">Сессия.</param>
    internal async Task RetireAsync(XamlDesignSession session)
    {
        session.Cancel();

        await _turn.WaitAsync(CancellationToken.None);

        try
        {
            if (!ReferenceEquals(_session, session))
                return;

            _session = null;
            Bind(null);

            try
            {
                await session.DisposeAsync();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log(StudioLogLevel.Warning, $"Сессия дизайна закрылась со сбоем: {e.Message}");
            }
        }
        finally
        {
            _turn.Release();
        }

        Report();
    }

    /// <summary>Сессия поставила хост: отсрочки, взятые раньше, держат и его замену.</summary>
    /// <param name="gate">Ворота замены хоста; null — хоста больше нет.</param>
    internal void Bind(ProjectDesignSwapGate? gate)
    {
        Deferral[] held;

        lock (_gate)
        {
            _swapGate = gate;
            held = [.. _deferrals];
        }

        foreach (var deferral in held)
            deferral.Bind(gate);
    }

    /// <summary>У сессии не осталось документов: через простой она кончится.</summary>
    /// <param name="session">Сессия.</param>
    internal void Idle(XamlDesignSession session)
    {
        CancelIdle();

        if (_stopped || Options.IdleRelease == Timeout.InfiniteTimeSpan)
            return;

        _idle = Options.TimeProvider.CreateTimer(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_session, session) && session.Leases == 0)
                    _ = RetireAsync(session);
            }),
            null,
            Options.IdleRelease < TimeSpan.Zero ? TimeSpan.Zero : Options.IdleRelease,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Документ снова открыт: простой отменяется.</summary>
    internal void CancelIdle()
    {
        _idle?.Dispose();
        _idle = null;
    }

    /// <summary>Сборка дизайна кончилась.</summary>
    /// <param name="outcome">Что она сделала.</param>
    internal void Built(XamlBuildOutcome outcome)
    {
        LastBuild = outcome;
        Report();
    }

    /// <summary>Говорит о сдвиге состояния, если оно сдвинулось с прошлого раза.</summary>
    internal void Report()
    {
        var (state, reason) = Describe();

        if (state == _reportedState && reason == _reportedReason && ReferenceEquals(LastBuild, _reportedBuild))
            return;

        _reportedState = state;
        _reportedReason = reason;
        _reportedBuild = LastBuild;

        Raise(StateChanged, this);
    }

    /// <summary>Пишет в журнал от имени модуля.</summary>
    internal void Log(StudioLogLevel level, string message) =>
        _context.Log.Write(level, XamlModule.LogSource, message);

    /// <summary>Зовёт подписчиков по одному; упавший соседям не мешает.</summary>
    internal void Raise<T>(EventHandler<T>? handler, object sender, T args)
        where T : EventArgs
    {
        if (handler is null)
            return;

        foreach (var single in handler.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                single(sender, args);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(e);
            }
        }
    }

    /// <summary>Зовёт подписчиков события без данных по одному; упавший соседям не мешает.</summary>
    internal void Raise(EventHandler? handler, object sender)
    {
        if (handler is null)
            return;

        foreach (var single in handler.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                single(sender, EventArgs.Empty);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(e);
            }
        }
    }

    /// <summary>
    /// Отдаёт сбой чужого кода студии — в потоке интерфейса и с его собственным стеком.
    /// </summary>
    /// <remarks>
    /// Стек сохраняется захватом, поэтому студия находит в нём кадр того, кто бросил, и приписывает
    /// сбой ему. Отложено, а не брошено на месте: соседи своё уже получили.
    /// </remarks>
    internal void Fail(Exception error)
    {
        if (Options.SubscriberFailed is { } failed)
        {
            failed(error);
            return;
        }

        var captured = ExceptionDispatchInfo.Capture(error);

        Dispatcher.UIThread.Post(captured.Throw);
    }

    private (XamlDesignState State, string? Reason) Describe()
    {
        if (_session is { } session)
            return session.Describe();

        return _failure is { } failure ? (XamlDesignState.Failed, failure) : (XamlDesignState.Idle, null);
    }

    /// <summary>Решение закрыли, сменили или перечитали в другой конфигурации — сессия кончается.</summary>
    private void OnProjectsChanged(object? sender, ProjectsChangedEventArgs e)
    {
        if (_session is { } session && !session.Fits(e.Current))
            _ = RetireAsync(session);

        if (_failure is not null && e.Current.Session != _failedIn)
        {
            _failure = null;
            Report();
        }
    }

    private void Forget(Deferral deferral)
    {
        lock (_gate)
            _deferrals.Remove(deferral);
    }

    /// <summary>Отсрочка замены, переживающая сессии.</summary>
    private sealed class Deferral(XamlService owner, string reason) : IDisposable
    {
        private readonly Lock _sync = new();
        private IDisposable? _held;
        private bool _disposed;

        /// <summary>Держит замену хоста — или ничего, когда хоста нет.</summary>
        public void Bind(ProjectDesignSwapGate? gate)
        {
            IDisposable? previous;

            lock (_sync)
            {
                if (_disposed)
                    return;

                previous = _held;
                _held = gate?.Defer(reason);
            }

            previous?.Dispose();
        }

        public void Dispose()
        {
            IDisposable? held;

            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                held = _held;
                _held = null;
            }

            owner.Forget(this);
            held?.Dispose();
        }
    }

    /// <summary>
    /// Постановка участника. Плагин, выгруженный без снятия своего участника, теряет его сам: иначе
    /// замена позвала бы код мертвеца, а ссылка держала бы его сборку в памяти.
    /// </summary>
    private sealed class Registration : IDisposable
    {
        private readonly XamlService _owner;
        private readonly IXamlDesignParticipant _participant;
        private readonly AssemblyLoadContext? _context;
        private bool _disposed;

        public Registration(XamlService owner, IXamlDesignParticipant participant)
        {
            _owner = owner;
            _participant = participant;

            if (AssemblyLoadContext.GetLoadContext(participant.GetType().Assembly) is { IsCollectible: true } context)
            {
                _context = context;
                context.Unloading += OnUnloading;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_context is not null)
                _context.Unloading -= OnUnloading;

            if (Dispatcher.UIThread.CheckAccess())
                _owner._participants.Remove(_participant);
            else
                Dispatcher.UIThread.Post(() => _owner._participants.Remove(_participant));
        }

        private void OnUnloading(AssemblyLoadContext context) => Dispatcher.UIThread.Post(Dispose);
    }
}
