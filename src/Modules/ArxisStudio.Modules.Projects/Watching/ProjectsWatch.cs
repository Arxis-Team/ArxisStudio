using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>
/// Слежение за диском одной сессии: входы оценки и состав папок проектов.
/// </summary>
/// <remarks>
/// <para>
/// Две дороги, и у каждой свой склейщик. Входы оценки — файлы проектов, импорты, решение — наблюдает
/// <see cref="ProjectFileWatcher"/> библиотеки, а устарела ли модель, решает сам снимок через
/// <see cref="SolutionSnapshot.Invalidate"/>. Состав папок наблюдает
/// <see cref="ProjectTreeWatcher"/>, а решает <see cref="MembershipFilter"/> — по диску, в момент
/// разбора пачки. Пачки разные, потому что и вопросы к ним разные.
/// </para>
/// <para>
/// Слежение модель не перечитывает: оно говорит, что она устарела, и называет причины. Решает
/// служба — она и склеивает перемены с загрузкой, уже стоящей в очереди.
/// </para>
/// </remarks>
internal sealed class ProjectsWatch : IProjectsWatch
{
    private readonly Action<ImmutableArray<CanonicalPath>> _stale;
    private readonly IDiskView _disk;
    private readonly FileChangeCoalescer _inputs;
    private readonly FileChangeCoalescer _members;
    private readonly ProjectFileWatcher _files;
    private readonly ProjectTreeWatcher _folders;
    private readonly Lock _gate = new();
    private ImmutableArray<CanonicalPath> _watched = [];
    private SolutionSnapshot? _snapshot;
    private bool _disposed;

    /// <summary>Заводит слежение.</summary>
    /// <param name="stale">Кому сказать, что модель устарела, и почему.</param>
    /// <param name="coalescing">Сколько ждать тишины и сколько копить самое большее.</param>
    /// <param name="disk">Диск для проверки состава; null — настоящий.</param>
    public ProjectsWatch(
        Action<ImmutableArray<CanonicalPath>> stale,
        FileChangeCoalescingOptions coalescing,
        IDiskView? disk = null)
    {
        _stale = stale;
        _disk = disk ?? DiskView.Instance;
        _inputs = new FileChangeCoalescer(OnInputs, coalescing);
        _members = new FileChangeCoalescer(OnMembers, coalescing);
        _files = new ProjectFileWatcher(_inputs.Add);
        _folders = new ProjectTreeWatcher(OnFolder, root => Report([root]));
    }

    /// <inheritdoc/>
    public void Follow(SolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Решение — тоже вход, хоть ни одному проекту и не принадлежит: пропавший из него проект —
        // ровно та перемена, о которой стоит услышать.
        ImmutableArray<CanonicalPath> watched =
        [
            .. snapshot.Projects
                .SelectMany(project => project.EvaluationInputs)
                .Append(snapshot.EntryPoint.Path)
                .Where(path => !path.IsEmpty)
                .Distinct()
                .Order(),
        ];

        lock (_gate)
        {
            if (_disposed)
                return;

            Volatile.Write(ref _snapshot, snapshot);

            // Тот же набор не пересоздаётся: наблюдатель библиотеки заменяет себя целиком, и
            // пересоздание после каждой перезагрузки открывало бы окно, где перемены теряются.
            if (!watched.SequenceEqual(_watched))
            {
                _files.Watch(watched);
                _watched = watched;
            }

            _folders.Watch(MembershipFilter.Roots(snapshot));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _files.Dispose();
        _folders.Dispose();
        _inputs.Dispose();
        _members.Dispose();
    }

    private void OnInputs(ImmutableArray<CanonicalPath> batch)
    {
        if (Volatile.Read(ref _snapshot) is not { } snapshot)
            return;

        var invalidation = snapshot.Invalidate(batch);

        if (!invalidation.IsEmpty)
            Report(invalidation.Causes);
    }

    private void OnFolder(CanonicalPath path)
    {
        if (Volatile.Read(ref _snapshot) is { } snapshot && MembershipFilter.IsCandidate(snapshot, path))
            _members.Add(path);
    }

    private void OnMembers(ImmutableArray<CanonicalPath> batch)
    {
        if (Volatile.Read(ref _snapshot) is not { } snapshot)
            return;

        var changed = MembershipFilter.Changed(snapshot, batch, _disk);

        if (!changed.IsEmpty)
            Report(changed);
    }

    private void Report(ImmutableArray<CanonicalPath> causes)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        _stale(causes);
    }
}
