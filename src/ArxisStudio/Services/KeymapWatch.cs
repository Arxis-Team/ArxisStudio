using ArxisStudio.Shell;

namespace ArxisStudio.Services;

/// <summary>
/// Следит за <c>keymap.json</c> и говорит, когда файл сохранили, завели или убрали.
/// </summary>
/// <remarks>
/// Файл правят руками в стороннем редакторе, и ответ на правку нужен там, где её сделали, — сразу,
/// а не после перезапуска студии. Редактор сохраняет файл не одной записью: обнуляет, пишет частями,
/// пишет во временный и переименовывает. Поэтому о перемене говорится один раз, когда файл затих, а
/// не на каждую запись: иначе студия раздавала бы сочетания по полупустому файлу.
/// <para>
/// Слушается папка, а не сам файл: файла может ещё не быть, и заведённый потом должен быть услышан
/// так же, как изменённый.
/// </para>
/// </remarks>
internal sealed class KeymapWatch : IDisposable
{
    /// <summary>Сколько файл должен молчать, чтобы правку сочли законченной.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly Action _changed;
    private readonly Timer _timer;
    private readonly FileSystemWatcher? _watcher;
    private int _disposed;

    /// <summary>Начинает следить.</summary>
    /// <param name="path">Путь к <c>keymap.json</c>.</param>
    /// <param name="changed">
    /// Файл затих после перемены. Зовётся в потоке пула, а не интерфейса; исключение глотается —
    /// в этом потоке оно закончило бы процесс.
    /// </param>
    public KeymapWatch(string path, Action changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(changed);

        _changed = changed;
        _timer = new Timer(_ => Settle(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;

            Directory.CreateDirectory(folder);

            _watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += OnEvent;
            _watcher.Error += OnError;

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Папку не завести или не прочитать — следить не за чем. Студия от этого не страдает:
            // файл прочитан при запуске, а правка без слежения вступит в силу со следующим запуском.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Слежение идёт; <c>false</c> — папку не удалось ни завести, ни слушать.</summary>
    public bool Watching => _watcher is not null;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _watcher?.Dispose();
        _timer.Dispose();
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Poke();

    /// <summary>Буфер наблюдателя переполнился: что именно менялось, потеряно, — перечитать всё.</summary>
    private void OnError(object sender, ErrorEventArgs e) => Poke();

    private void Poke()
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        try
        {
            _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Settle()
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        try
        {
            _changed();
        }
        catch (Exception e) when (Faults.Survivable(e))
        {
            // Поток пула: исключение отсюда закончило бы процесс. Тот, кто применяет файл, говорит о
            // своих бедах сам.
        }
    }
}
