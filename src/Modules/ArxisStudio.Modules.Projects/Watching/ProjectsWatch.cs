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
/// <para>
/// <b>Своя правка службы.</b> Служба файлов перечитывает модель сама, и пачка её же перемен,
/// разобранная посреди этого перечитывания, сверилась бы со снимком, который оно заменит, —
/// вторая загрузка ради прочитанного первой. Поэтому на время правки и перечитывания приговоры
/// придерживаются (<see cref="Hold"/>) и сверяются потом со свежим снимком. Состав сверяется с
/// диском точно; входы оценки снимок сверяет по пути, и для них правка оставляет отпечатки
/// содержимого: эхо с тем же содержимым модель уже прочла, другое содержимое — перемена снаружи.
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
    private readonly List<CanonicalPath> _heldInputs = [];
    private readonly List<CanonicalPath> _heldMembers = [];

    // Входы, чьё содержимое прочла модель после правки службы: путь — отпечаток, null — файла нет.
    private readonly Dictionary<CanonicalPath, string?> _expected = [];

    private ImmutableArray<CanonicalPath> _watched = [];
    private SolutionSnapshot? _snapshot;
    private int _holds;
    private bool _disposed;

    /// <summary>Заводит слежение.</summary>
    /// <param name="stale">Кому сказать, что модель устарела, и почему.</param>
    /// <param name="coalescing">Сколько ждать тишины и сколько копить самое большее.</param>
    /// <param name="disk">Диск для проверки состава и отпечатков; null — настоящий.</param>
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
        _folders = new ProjectTreeWatcher(OnFolder, root => Tell([root]));
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
    public IWatchHold Hold()
    {
        lock (_gate)
        {
            if (_disposed)
                return NoWatchHold.Instance;

            _holds++;
        }

        return new Holding(this);
    }

    /// <summary>
    /// Сообщает о пути так, как сообщили бы оба наблюдателя: тестам — чтобы не ждать настоящих событий.
    /// </summary>
    /// <param name="fullPath">Полный путь.</param>
    internal void Report(string fullPath)
    {
        if (!CanonicalPath.TryCreate(fullPath, out var path))
            return;

        bool input;

        lock (_gate)
            input = _watched.Contains(path);

        if (input)
            _inputs.Add(path);

        OnFolder(path);
    }

    /// <summary>Отдаёт накопленное сразу, не дожидаясь тишины.</summary>
    internal void Flush()
    {
        _inputs.Flush();
        _members.Flush();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _heldInputs.Clear();
            _heldMembers.Clear();
            _expected.Clear();
        }

        _files.Dispose();
        _folders.Dispose();
        _inputs.Dispose();
        _members.Dispose();
    }

    private void OnInputs(ImmutableArray<CanonicalPath> batch)
    {
        lock (_gate)
        {
            if (_holds > 0)
            {
                _heldInputs.AddRange(batch);
                return;
            }
        }

        Tell(Invalidated(batch));
    }

    private void OnFolder(CanonicalPath path)
    {
        if (Volatile.Read(ref _snapshot) is { } snapshot && MembershipFilter.IsCandidate(snapshot, path))
            _members.Add(path);
    }

    private void OnMembers(ImmutableArray<CanonicalPath> batch)
    {
        lock (_gate)
        {
            if (_holds > 0)
            {
                _heldMembers.AddRange(batch);
                return;
            }
        }

        Tell(Changed(batch));
    }

    /// <summary>Входы из пачки, которые модель ещё не прочла, — по снимку, опубликованному последним.</summary>
    private ImmutableArray<CanonicalPath> Invalidated(IEnumerable<CanonicalPath> batch)
    {
        if (Volatile.Read(ref _snapshot) is not { } snapshot)
            return [];

        var invalidation = snapshot.Invalidate(batch.Where(path => !IsEcho(path)));

        return invalidation.IsEmpty ? [] : invalidation.Causes;
    }

    /// <summary>Пути из пачки, которые меняют состав проектов, — по снимку, опубликованному последним.</summary>
    private ImmutableArray<CanonicalPath> Changed(IEnumerable<CanonicalPath> batch) =>
        Volatile.Read(ref _snapshot) is { } snapshot ? MembershipFilter.Changed(snapshot, batch, _disk) : [];

    /// <summary>
    /// Эхо правки службы: вход лежит таким, каким его прочла модель после правки.
    /// </summary>
    /// <remarks>
    /// Разошедшийся отпечаток забывается сразу: файл трогали после правки, и запомненное больше
    /// ничего не значит — следующая его перемена судится как всякая другая.
    /// </remarks>
    private bool IsEcho(CanonicalPath path)
    {
        string? expected;

        lock (_gate)
        {
            if (!_expected.TryGetValue(path, out expected))
                return false;
        }

        if (_disk.TryFingerprint(path, out var print) && print == expected)
            return true;

        lock (_gate)
        {
            if (_expected.TryGetValue(path, out var still) && still == expected)
                _expected.Remove(path);
        }

        return false;
    }

    /// <summary>
    /// Отпускает придержку; последняя отпущенная сверяет придержанное со снимком, опубликованным
    /// последним.
    /// </summary>
    private void Release(Holding holding)
    {
        List<CanonicalPath> inputs;
        List<CanonicalPath> members;

        lock (_gate)
        {
            if (_disposed)
                return;

            if (holding.IsConfirmed)
            {
                foreach (var (path, print) in holding.Expected)
                    _expected[path] = print;
            }

            if (--_holds > 0)
                return;

            inputs = [.. _heldInputs];
            members = [.. _heldMembers];
            _heldInputs.Clear();
            _heldMembers.Clear();
        }

        Tell([.. Changed(members).Concat(Invalidated(inputs)).Distinct()]);
    }

    private void Tell(ImmutableArray<CanonicalPath> causes)
    {
        if (causes.IsEmpty)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;
        }

        _stale(causes);
    }

    /// <summary>Одна придержка: что правка оставила на диске и прочла ли это модель.</summary>
    /// <param name="watch">Чьё слежение придержано.</param>
    private sealed class Holding(ProjectsWatch watch) : IWatchHold
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<CanonicalPath, string?> _expected = [];
        private int _confirmed;
        private int _released;

        /// <summary>Перечитывание после правки опубликовало снимок.</summary>
        public bool IsConfirmed => Volatile.Read(ref _confirmed) != 0;

        /// <summary>Отпечатки, снятые после правки.</summary>
        public IReadOnlyList<KeyValuePair<CanonicalPath, string?>> Expected
        {
            get
            {
                lock (_gate)
                    return [.. _expected];
            }
        }

        /// <inheritdoc/>
        /// <remarks>Непрочитанный вход не запоминается: его эхо судится как всякая перемена.</remarks>
        public void Expect(IEnumerable<CanonicalPath> inputs)
        {
            ArgumentNullException.ThrowIfNull(inputs);

            foreach (var input in inputs)
            {
                if (!watch._disk.TryFingerprint(input, out var print))
                    continue;

                lock (_gate)
                    _expected[input] = print;
            }
        }

        /// <inheritdoc/>
        public void Confirm() => Volatile.Write(ref _confirmed, 1);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                watch.Release(this);
        }
    }
}
