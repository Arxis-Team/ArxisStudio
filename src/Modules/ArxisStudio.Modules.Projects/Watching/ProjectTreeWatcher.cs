using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Watching;

/// <summary>
/// Наблюдатель папок проектов: какие файлы и папки появились, пропали или переименованы.
/// </summary>
/// <remarks>
/// <para>
/// Только имена — <see cref="NotifyFilters.FileName"/> и <see cref="NotifyFilters.DirectoryName"/>.
/// Содержимое файлов под масками проекта модель не меняет, а правку того, что её меняет, — файла
/// проекта, импорта, решения — видит наблюдатель входов оценки. Слушать ещё и запись значило бы
/// будить разбор на каждом сохранении исходника.
/// </para>
/// <para>
/// Набор папок не пересоздаётся, если не поменялся: перезагрузка зовёт <see cref="Watch"/> после
/// каждой публикации, и пересоздание открывало бы окно, в котором перемены теряются.
/// </para>
/// <para>
/// Переполнение буфера говорит не «что-то в этом файле», а «перемены потеряны»: честный ответ на
/// него — перечитать всё, и он уходит отдельным вызовом.
/// </para>
/// </remarks>
internal sealed class ProjectTreeWatcher : IDisposable
{
    /// <summary>Столько же, сколько у наблюдателя входов библиотеки: запас дешевле перезагрузки.</summary>
    private const int BufferSize = 64 * 1024;

    private readonly Action<CanonicalPath> _changed;
    private readonly Action<CanonicalPath> _overflowed;
    private readonly Lock _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private ImmutableArray<CanonicalPath> _roots = [];
    private bool _disposed;

    /// <summary>Заводит наблюдателя.</summary>
    /// <param name="changed">
    /// Путь, у которого появилось, пропало или сменилось имя. Зовётся в потоке системы и
    /// одновременно с собой; исключение глотается — в этом потоке оно закончило бы процесс.
    /// </param>
    /// <param name="overflowed">Перемены под этой папкой потеряны.</param>
    public ProjectTreeWatcher(Action<CanonicalPath> changed, Action<CanonicalPath> overflowed)
    {
        _changed = changed;
        _overflowed = overflowed;
    }

    /// <summary>Следит за этими папками и перестаёт следить за прежними.</summary>
    /// <param name="roots">Папки; вложенные следятся вместе с ними.</param>
    public void Watch(ImmutableArray<CanonicalPath> roots)
    {
        lock (_gate)
        {
            if (_disposed || roots.SequenceEqual(_roots))
                return;

            StopAll();

            _roots = roots;

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
        }
    }

    /// <summary>Буфер под папкой переполнился.</summary>
    /// <param name="root">Папка.</param>
    internal void Overflow(CanonicalPath root) => Safely(() => _overflowed(root));

    private void Start(CanonicalPath root)
    {
        FileSystemWatcher? watcher = null;

        try
        {
            watcher = new FileSystemWatcher(root.Value)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };

            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, _) => Overflow(root);

            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Папку убрали между снимком и слежением или читать её нельзя. Загрузку это не
            // проваливает: просили сказать о переменах, а не о том, что диск сдвинулся. Остальные
            // папки следятся по-прежнему.
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

    private void OnEvent(object sender, FileSystemEventArgs e) => Report(e.FullPath);

    /// <summary>Оба конца: ушедшее имя пропало, пришедшее появилось.</summary>
    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Report(e.OldFullPath);
        Report(e.FullPath);
    }

    private void Report(string? fullPath)
    {
        if (CanonicalPath.TryCreate(fullPath, out var path))
            Safely(() => _changed(path));
    }

    private static void Safely(Action call)
    {
        try
        {
            call();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }
}
