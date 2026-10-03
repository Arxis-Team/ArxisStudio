using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;

namespace ArxisStudio.Modules.Xaml.Session;

/// <summary>
/// Снимки и сборки для хоста поколения — из профиля дизайна службы проектов (ADR 0032 ProjectSystem).
/// </summary>
/// <remarks>
/// <para>
/// <b>Одно решение.</b> Профиль идёт за открытым: откроют другое решение — профиль прочтёт его. Хост
/// этого пережить не может — его документы и поколение принадлежат прежнему, — поэтому источник
/// привязан к номеру сессии службы проектов и отдаёт только её снимки. Сессия сменилась — источник
/// молчит и отвечает последним снимком своего решения, а служба XAML кончает сессию дизайна.
/// </para>
/// <para>
/// Снимок профиля оценён со свойствами дизайна (<see cref="IStudioProjectProfile.GlobalProperties"/>) —
/// с теми же, с какими хост собирает: так поколение грузит то, что пишут его сборки, а не выход IDE.
/// </para>
/// </remarks>
internal sealed class StudioDesignSource : IProjectDesignSource, IDisposable
{
    private readonly IStudioProjectProfile _profile;
    private readonly long _session;
    private readonly Lock _gate = new();

    private SolutionSnapshot? _last;
    private long _lastSequence = -1;

    /// <summary>Привязывает источник к профилю и сессии службы проектов.</summary>
    /// <param name="profile">Профиль дизайна.</param>
    /// <param name="session">Номер сессии, чьи снимки отдавать.</param>
    public StudioDesignSource(IStudioProjectProfile profile, long session)
    {
        _profile = profile;
        _session = session;

        _profile.Changed += OnProfileChanged;

        Remember(_profile.Status);
    }

    /// <inheritdoc/>
    public SolutionSnapshot? Snapshot
    {
        get
        {
            Remember(_profile.Status);

            lock (_gate)
                return _last;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? SnapshotChanged;

    /// <inheritdoc/>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken) =>
        await _profile.RefreshAsync(cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ProjectOperationResult> ExecuteAsync(
        ProjectOperationRequest request,
        IProgress<ProjectOperationProgress>? progress,
        CancellationToken cancellationToken) =>
        await _profile.ExecuteAsync(request, progress, cancellationToken);

    /// <summary>Дожидается первого снимка своего решения: профиль читает его со своей очереди.</summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <exception cref="InvalidOperationException">Профиль решения не прочёл — что сказала загрузка.</exception>
    public async Task ReadyAsync(CancellationToken cancellationToken)
    {
        if (Snapshot is not null)
            return;

        // Профиль ставит свою загрузку в очередь, когда его открывают; вызов присоединяется к ней.
        var result = await _profile.RefreshAsync(cancellationToken);

        if (Snapshot is not null)
            return;

        throw new InvalidOperationException(
            result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Error)?.Message
            ?? result.Status.ToString());
    }

    /// <inheritdoc/>
    public void Dispose() => _profile.Changed -= OnProfileChanged;

    /// <summary>Запоминает снимок своей сессии, если он новее запомненного.</summary>
    /// <returns>Запомнил новый.</returns>
    private bool Remember(ProjectsStatus status)
    {
        if (status.Session != _session || status.Snapshot is not { } snapshot)
            return false;

        lock (_gate)
        {
            if (status.Sequence <= _lastSequence || ReferenceEquals(snapshot, _last))
                return false;

            _last = snapshot;
            _lastSequence = status.Sequence;

            return true;
        }
    }

    private void OnProfileChanged(object? sender, ProjectsChangedEventArgs e)
    {
        if (Remember(e.Current))
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
}
