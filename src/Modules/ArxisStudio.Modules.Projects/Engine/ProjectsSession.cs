using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Одно открытие одного пути: движок, его снимок и то, что стоит за ним в очереди.
/// </summary>
/// <remarks>
/// Сессия кончается закрытием или открытием другого пути, и кончается сразу: её работа отменяется,
/// слежение гаснет, а движок отпускается очередью — после того, что он дочитывает. Поля очереди
/// (<see cref="Pending"/>, <see cref="Running"/>, <see cref="Configuration"/>,
/// <see cref="LastLoad"/>) меняет служба под своим замком. Снимок пишет только очередь, а читают
/// его ещё и потоки слежения — поэтому он без замка, одним атомарным чтением.
/// </remarks>
internal sealed class ProjectsSession
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _watchGate = new();
    private IProjectsWatch? _watch;
    private SolutionSnapshot? _snapshot;
    private int _retired;

    /// <summary>Заводит сессию.</summary>
    /// <param name="number">Номер сессии.</param>
    /// <param name="entryPoint">Что открыто.</param>
    /// <param name="workspace">Движок сессии: его идентичность носят все её проекты.</param>
    public ProjectsSession(long number, CanonicalPath entryPoint, ProjectWorkspace workspace)
    {
        Number = number;
        EntryPoint = entryPoint;
        Workspace = workspace;
    }

    /// <summary>Номер сессии.</summary>
    public long Number { get; }

    /// <summary>Что открыто.</summary>
    public CanonicalPath EntryPoint { get; }

    /// <summary>Движок сессии.</summary>
    public ProjectWorkspace Workspace { get; }

    /// <summary>Отменяется, когда сессия кончилась.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>Сессия кончилась.</summary>
    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    /// <summary>Активная конфигурация.</summary>
    public string? Configuration { get; set; }

    /// <summary>Загрузка, стоящая в очереди и ещё не начатая.</summary>
    public LoadItem? Pending { get; set; }

    /// <summary>Загрузка, которая идёт.</summary>
    public LoadItem? Running { get; set; }

    /// <summary>Последняя законченная загрузка.</summary>
    public ProjectsLoad? LastLoad { get; set; }

    /// <summary>
    /// Восстановление при открытии этой сессии уже решалось.
    /// </summary>
    /// <remarks>
    /// Один раз за сессию: перезагрузки идут одна за другой, и каждая, увидев непрочитанные пакеты,
    /// просила бы восстановление заново — а первое ещё и не кончилось.
    /// </remarks>
    public bool Restored { get; set; }

    /// <summary>Последний снимок сессии.</summary>
    public SolutionSnapshot? Snapshot
    {
        get => Volatile.Read(ref _snapshot);
        set => Volatile.Write(ref _snapshot, value);
    }

    /// <summary>Что просить у движка сейчас. Зовётся под замком службы.</summary>
    public WorkspaceLoadRequest Request() => new()
    {
        EntryPointPath = EntryPoint,
        Workspace = Workspace.Identity,
        Configuration = Configuration,
    };

    /// <summary>
    /// Следит за диском по текущему снимку, меняет набор слежения или гасит его.
    /// </summary>
    /// <param name="factory">Как завести слежение; null — не следить.</param>
    /// <param name="stale">Кому сказать, что модель устарела.</param>
    public void Watch(
        Func<Action<ImmutableArray<CanonicalPath>>, IProjectsWatch?>? factory,
        Action<ImmutableArray<CanonicalPath>> stale)
    {
        lock (_watchGate)
        {
            if (IsRetired)
                return;

            if (factory is null || Snapshot is not { } snapshot)
            {
                _watch?.Dispose();
                _watch = null;
                return;
            }

            _watch ??= factory(stale);
            _watch?.Follow(snapshot);
        }
    }

    /// <summary>
    /// Кончает сессию: гасит слежение и отменяет её работу.
    /// </summary>
    /// <returns><c>false</c> — сессия уже кончилась раньше.</returns>
    /// <remarks>Зовётся вне замка службы: отмена синхронно зовёт обработчики движка.</remarks>
    public bool Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) != 0)
            return false;

        lock (_watchGate)
        {
            _watch?.Dispose();
            _watch = null;
        }

        _lifetime.Cancel();
        return true;
    }
}
