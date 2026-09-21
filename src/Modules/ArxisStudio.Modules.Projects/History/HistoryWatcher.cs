using System.Collections.Immutable;
using ArxisStudio.LocalHistory;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>
/// Локальная история одной сессии: следит за правками на диске и снимает опорный снимок.
/// </summary>
/// <remarks>
/// <para>
/// <b>Правки.</b> Наблюдатели папок сообщают имена, запись и размер; события склеиваются, как у
/// слежения за составом, и пачка сверяется с тем, что история помнит (<see cref="HistorySight"/>).
/// Сверяется она в очереди записи, а не на потоке наблюдателя: чтение и сжатие файла там держали бы
/// буфер событий, и он переполнялся бы на первом же переключении ветки.
/// </para>
/// <para>
/// <b>Опорный снимок.</b> Каждая новая папка, за которой начали следить, обходится один раз: файлы,
/// которых история не знала, запоминаются без правки, а те, что разошлись с запомненным, пока
/// студия была закрыта, пишутся одним действием. Без этого первая внешняя правка файла не
/// вернулась бы к тому, что было до неё. Обход идёт кусками, и пачки наблюдателей встают между ними,
/// а не ждут конца обхода большого решения.
/// </para>
/// <para>
/// <b>Переполнение.</b> Буфер событий папки переполнился — значит часть правок не дошла, и папка
/// обходится заново тем же опорным снимком: он найдёт разошедшееся по длине и времени записи.
/// </para>
/// </remarks>
internal sealed class HistoryWatcher : IDisposable
{
    /// <summary>Столько файлов опорного снимка идёт одним делом очереди.</summary>
    private const int Chunk = 128;

    /// <summary>Как у наблюдателя состава: запас дешевле обхода заново.</summary>
    private const int BufferSize = 64 * 1024;

    private readonly HistoryRecorder _recorder;
    private readonly FileChangeCoalescer _changes;
    private readonly Dictionary<CanonicalPath, FileSystemWatcher> _watchers = [];
    private readonly HashSet<CanonicalPath> _scanned = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private SolutionSnapshot? _snapshot;
    private bool _disposed;

    /// <summary>Заводит историю сессии.</summary>
    /// <param name="recorder">Служба истории: хранилище и очередь записи.</param>
    /// <param name="coalescing">Сколько ждать тишины и сколько копить самое большее.</param>
    public HistoryWatcher(HistoryRecorder recorder, FileChangeCoalescingOptions coalescing)
    {
        _recorder = recorder;
        _changes = new FileChangeCoalescer(OnBatch, coalescing);
    }

    /// <summary>
    /// Следит по новому снимку: за новыми папками начинает, у ушедших — перестаёт.
    /// </summary>
    /// <param name="snapshot">Снимок.</param>
    public void Follow(SolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            if (_disposed)
                return;

            Volatile.Write(ref _snapshot, snapshot);

            var roots = HistoryFilter.Roots(snapshot);

            foreach (var gone in _watchers.Keys.Where(root => !roots.Any(wanted => wanted.Root == root)).ToList())
            {
                Stop(_watchers[gone]);
                _watchers.Remove(gone);
                _scanned.Remove(gone);
            }

            foreach (var (root, deep) in roots)
            {
                if (!_watchers.ContainsKey(root))
                    Start(root, deep);

                if (_scanned.Add(root))
                    Scan(root, deep, _recorder.Words.Offline);
            }
        }
    }

    /// <summary>Сообщает о пути, который поменялся; наблюдатели зовут его сами, тесты — прямо.</summary>
    /// <param name="fullPath">Полный путь из события.</param>
    internal void Report(string fullPath)
    {
        if (!CanonicalPath.TryCreate(fullPath, out var path) || Volatile.Read(ref _snapshot) is not { } snapshot)
            return;

        if (HistoryFilter.IsTracked(snapshot, path))
            _changes.Add(path);
    }

    /// <summary>Отдаёт накопленное сразу, не дожидаясь тишины.</summary>
    internal void Flush() => _changes.Flush();

    /// <summary>Перестаёт следить; стоящее в очереди доделается впустую.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var watcher in _watchers.Values)
                Stop(watcher);

            _watchers.Clear();
        }

        _stop.Cancel();
        _changes.Dispose();
    }

    private void OnBatch(ImmutableArray<CanonicalPath> batch) => _recorder.Enqueue(() =>
    {
        if (_stop.IsCancellationRequested
            || _recorder.Store is not { } store
            || Volatile.Read(ref _snapshot) is not { } snapshot)
        {
            return Task.CompletedTask;
        }

        var sight = new HistorySight(store);

        foreach (var path in batch.Distinct())
        {
            if (Directory.Exists(path.Value))
            {
                // Папка появилась или приехала: её файлы — новые под новым именем.
                if (!path.FileName.StartsWith('.'))
                {
                    foreach (var file in Files(snapshot, path, deep: true))
                        sight.Look(file.Value);
                }

                continue;
            }

            if (File.Exists(path.Value))
            {
                sight.Look(path.Value);
                continue;
            }

            if (store.Known(path.Value) is { } known)
            {
                sight.Gone(path.Value, known);
                continue;
            }

            // Пропала папка: пропало всё, что история под ней знала.
            foreach (var (file, state) in store.KnownUnder(path.Value))
                sight.Gone(file, state);
        }

        sight.Commit(_recorder.Words.External, HistoryOrigin.External);
        _recorder.Written();

        return Task.CompletedTask;
    });

    /// <summary>Обходит папку опорным снимком — кусками, чтобы пачки правок вставали между ними.</summary>
    private void Scan(CanonicalPath root, bool deep, string label) => _recorder.Enqueue(() =>
    {
        if (_stop.IsCancellationRequested
            || _recorder.Store is not { } store
            || Volatile.Read(ref _snapshot) is not { } snapshot)
        {
            return Task.CompletedTask;
        }

        var files = Files(snapshot, root, deep).Select(file => file.Value).ToList();
        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        // Известное под папкой, чего на диске больше нет, — удалено, пока студия не смотрела. Не в
        // счёт то, что отбор теперь отбрасывает (сменился выход сборки), и то, что у папки решения
        // лежит глубже неё: за тем следят папки проектов.
        var missing = store.KnownUnder(root.Value)
            .Where(pair => !present.Contains(pair.Key)
                && (deep || HistoryFilter.Folder(CanonicalPath.Create(pair.Key)) == root)
                && HistoryFilter.IsTracked(snapshot, CanonicalPath.Create(pair.Key)))
            .ToList();

        // Кусок пишет сразу: пачки правок встают между кусками и читают уже записанное. Переезды,
        // чьи концы попали в разные куски, сводит общая карта впервые увиденного.
        var firstSeen = new Dictionary<ContentId, string>();

        foreach (var chunk in files.Chunk(Chunk))
        {
            _recorder.Enqueue(() =>
            {
                if (_stop.IsCancellationRequested)
                    return Task.CompletedTask;

                var sight = new HistorySight(store);

                foreach (var file in chunk)
                    sight.Look(file, quietly: true);

                sight.Commit(label, HistoryOrigin.External, firstSeen);
                _recorder.Written();

                return Task.CompletedTask;
            });
        }

        _recorder.Enqueue(() =>
        {
            if (_stop.IsCancellationRequested)
                return Task.CompletedTask;

            var sight = new HistorySight(store);

            // Пачка между кусками могла уже записать пропажу: такой файл история забыла, и второй
            // записи ему не нужно.
            foreach (var (file, _) in missing)
            {
                if (!File.Exists(file) && store.Known(file) is { } still)
                    sight.Gone(file, still);
            }

            sight.Commit(label, HistoryOrigin.External, firstSeen);
            _recorder.Written(checkpoint: true);

            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    });

    /// <summary>
    /// Файлы под папкой, которые история пишет; служебные папки, выход и ссылки на другие места не
    /// обходятся вовсе.
    /// </summary>
    private static IEnumerable<CanonicalPath> Files(SolutionSnapshot snapshot, CanonicalPath folder, bool deep)
    {
        var pending = new Stack<string>([folder.Value]);

        while (pending.TryPop(out var current))
        {
            List<string> entries;

            try
            {
                entries = [.. Directory.EnumerateFileSystemEntries(current)];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (!CanonicalPath.TryCreate(entry, out var path))
                    continue;

                FileAttributes attributes;

                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    // Ссылка на другое место — не вглубь: она ведёт из решения или по кругу.
                    if (deep
                        && !attributes.HasFlag(FileAttributes.ReparsePoint)
                        && !path.FileName.StartsWith('.')
                        && HistoryFilter.IsTracked(snapshot, path))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if (HistoryFilter.IsTracked(snapshot, path))
                    yield return path;
            }
        }
    }

    private void Start(CanonicalPath root, bool deep)
    {
        FileSystemWatcher? watcher = null;

        try
        {
            watcher = new FileSystemWatcher(root.Value)
            {
                IncludeSubdirectories = deep,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Created += (_, e) => Report(e.FullPath);
            watcher.Changed += (_, e) => Report(e.FullPath);
            watcher.Deleted += (_, e) => Report(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Report(e.OldFullPath);
                Report(e.FullPath);
            };
            watcher.Error += (_, _) => Scan(root, deep, _recorder.Words.External);

            watcher.EnableRaisingEvents = true;

            _watchers[root] = watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Папку убрали между снимком и слежением или читать её нельзя: следующий снимок попробует
            // снова, а остальные папки следятся по-прежнему.
            watcher?.Dispose();
        }
    }

    private static void Stop(FileSystemWatcher watcher)
    {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }
}
