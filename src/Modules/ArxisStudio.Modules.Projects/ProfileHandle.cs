using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Держатель профиля: то, что получает открывший его, — своё у каждого, а профиль под ними общий.
/// </summary>
/// <remarks>
/// Свои подписки держатель помнит и снимает, когда его отпускают: забытая подписка на общем профиле
/// пережила бы держателя и звала бы его обработчик, пока профиль держит кто-то другой.
/// </remarks>
/// <param name="host">Служба.</param>
/// <param name="profile">Общий профиль.</param>
internal sealed class ProfileHandle(ProjectsHost host, ProjectProfile profile) : IStudioProjectProfile
{
    private readonly Lock _gate = new();
    private readonly List<EventHandler<ProjectsChangedEventArgs>> _handlers = [];
    private bool _released;

    /// <inheritdoc/>
    public ProjectProfileKind Kind => profile.Kind;

    /// <inheritdoc/>
    public ProjectMetadata GlobalProperties => profile.GlobalProperties;

    /// <inheritdoc/>
    public ProjectsStatus Status => profile.Status;

    /// <inheritdoc/>
    public event EventHandler<ProjectsChangedEventArgs>? Changed
    {
        add
        {
            if (value is null)
                return;

            lock (_gate)
            {
                if (_released)
                    return;

                _handlers.Add(value);
            }

            profile.Publisher.Subscribe(value);
        }

        remove
        {
            if (value is null)
                return;

            lock (_gate)
            {
                if (!_handlers.Remove(value))
                    return;
            }

            profile.Publisher.Unsubscribe(value);
        }
    }

    /// <inheritdoc/>
    public Task<WorkspaceLoadResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfReleased();

        return host.RefreshProfileAsync(profile, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> ExecuteAsync(
        ProjectOperationRequest request,
        IProgress<ProjectOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfReleased();

        return host.ExecuteProfileAsync(profile, request, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        List<EventHandler<ProjectsChangedEventArgs>> handlers;

        lock (_gate)
        {
            if (_released)
                return ValueTask.CompletedTask;

            _released = true;
            handlers = [.. _handlers];
            _handlers.Clear();
        }

        foreach (var handler in handlers)
            profile.Publisher.Unsubscribe(handler);

        return new ValueTask(host.ReleaseProfileAsync(profile));
    }

    private void ThrowIfReleased()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_released, this);
    }
}
