using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Следит за папками проектов: папка появилась, пропала или сменила имя — пора спросить диск заново.
/// </summary>
/// <remarks>
/// Пустую папку дерево берёт с диска, а не из снимка, и служба проектов о ней не скажет: состав
/// проекта от неё не меняется, перечитывать модель незачем, и служба нарочно молчит. Без своего
/// слежения окно показывало бы папку, которую уже убрали в проводнике, и не показывало бы новую.
/// Слушаются только имена папок (<see cref="NotifyFilters.DirectoryName"/>): файлы меняют состав, и о
/// них говорит служба.
/// <para>
/// Выход сборки и служебные папки отбрасываются правилом, по которому их не показывает дерево, —
/// <see cref="ItemFilter.ShowsFolder"/> ближайшего проекта, — иначе каждая сборка, раскладывающая
/// <c>obj</c>, перестраивала бы дерево. Перемены склеиваются по тишине: распакованный архив — сотни
/// папок и один вопрос. Переполнение буфера значит «перемены потеряны», и честный ответ на него —
/// спросить заново.
/// </para>
/// </remarks>
internal sealed class FolderWatch : IDisposable
{
    /// <summary>Сколько ждать тишины после последней перемены.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private readonly Action _changed;
    private readonly TimeSpan _quiet;
    private readonly Lock _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private ImmutableArray<CanonicalPath> _roots = [];
    private (CanonicalPath Directory, ItemFilter Filter)[] _projects = [];
    private bool _disposed;

    /// <summary>Заводит слежение.</summary>
    /// <param name="changed">
    /// Папки сменились. Зовётся в потоке системы, один раз на пачку перемен; исключение глотается — в
    /// этом потоке оно закончило бы процесс.
    /// </param>
    /// <param name="quiet">Сколько ждать тишины; пусто — <see cref="Quiet"/>.</param>
    public FolderWatch(Action changed, TimeSpan? quiet = null)
    {
        _changed = changed;
        _quiet = quiet ?? Quiet;
        _timer = new Timer(_ => Fire());
    }

    /// <summary>Следит за папками проектов снимка и перестаёт следить за прежними.</summary>
    /// <param name="snapshot">Снимок; пусто — решение закрыто, следить не за чем.</param>
    /// <remarks>
    /// Проект, лежащий в папке другого, отдельного наблюдателя не получает, а тот же набор папок не
    /// пересоздаётся: новый снимок приходит на каждую перезагрузку, и пересоздание открывало бы окно,
    /// в котором перемены теряются.
    /// </remarks>
    public void Follow(SolutionSnapshot? snapshot)
    {
        (CanonicalPath Directory, ItemFilter Filter)[] projects = snapshot is null
            ? []
            : [.. snapshot.Projects
                .Where(project => !project.ProjectDirectory.IsEmpty)
                .OrderByDescending(project => project.ProjectDirectory.Value.Length)
                .Select(project => (project.ProjectDirectory, new ItemFilter(project)))];

        var roots = new List<CanonicalPath>();

        foreach (var directory in projects.Select(project => project.Directory).Distinct().OrderBy(directory => directory.Value.Length))
        {
            if (!roots.Exists(root => directory.StartsWith(root)))
                roots.Add(directory);
        }

        lock (_gate)
        {
            if (_disposed)
                return;

            Volatile.Write(ref _projects, projects);

            if (roots.SequenceEqual(_roots))
                return;

            StopAll();

            _roots = [.. roots];

            foreach (var root in roots)
                Start(root);
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
            _roots = [];

            StopAll();
            _timer.Dispose();
        }
    }

    /// <summary>
    /// Стоит ли папка вопроса: её показало бы дерево ближайшего проекта, — не выход сборки и не
    /// служебная.
    /// </summary>
    /// <param name="path">Папка из события.</param>
    internal bool Matters(CanonicalPath path)
    {
        foreach (var (directory, filter) in Volatile.Read(ref _projects))
        {
            if (path.StartsWith(directory))
                return filter.ShowsFolder(path, out _);
        }

        return false;
    }

    private void Start(CanonicalPath root)
    {
        FileSystemWatcher? watcher = null;

        try
        {
            watcher = new FileSystemWatcher(root.Value)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName,
            };

            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, _) => Nudge();

            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Папки уже нет или читать её нельзя: остальные следятся по-прежнему, а дерево покажет
            // то, что ответит диск при следующем снимке.
            watcher?.Dispose();
        }
    }

    private void StopAll()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Note(e.FullPath);

    /// <summary>Оба конца: ушедшее имя пропало, пришедшее появилось.</summary>
    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Note(e.OldFullPath);
        Note(e.FullPath);
    }

    private void Note(string? fullPath)
    {
        if (CanonicalPath.TryCreate(fullPath, out var path) && Matters(path))
            Nudge();
    }

    /// <summary>Откладывает вопрос до тишины.</summary>
    private void Nudge()
    {
        lock (_gate)
        {
            if (!_disposed)
                _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        try
        {
            _changed();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }
}
