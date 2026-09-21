using ArxisStudio.LocalHistory;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>
/// Один взгляд на диск: чем файлы разошлись с тем, что история помнит, — и запись этого одним
/// действием.
/// </summary>
/// <remarks>
/// <para>
/// Пишется только расхождение. Тот же размер и то же время записи — файл не читается вовсе; то же
/// содержимое при сдвинутом времени — запоминается новое время, но правки нет. Поэтому собственные
/// действия студии наблюдатель видит и не повторяет: их состояние история уже знает.
/// </para>
/// <para>
/// Переименование мимо студии приходит пропажей и появлением. Пропавший и появившийся с одним и тем
/// же содержимым — это переезд, и он записывается переездом: иначе история переименованного файла
/// начиналась бы с «появился», а удалённое под старым именем висело бы в ней вечно.
/// </para>
/// <para>
/// Файл больше предела пишется только появлением, пропажей и переездом: его содержимое не хранится,
/// а правка без содержимого — шум, который ничего не вернёт.
/// </para>
/// </remarks>
/// <param name="store">История.</param>
internal sealed class HistorySight(LocalHistoryStore store)
{
    /// <summary>
    /// Пути, на которые уже посмотрели: наблюдатель папок сообщает и о папке, и о каждом её файле, и
    /// файл, увиденный дважды за одну пачку, записался бы дважды.
    /// </summary>
    private readonly HashSet<string> _looked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, HistoryFileState State)> _created = [];
    private readonly List<(string Path, HistoryFileState State)> _learned = [];
    private readonly List<(string Path, HistoryFileState Before, HistoryFileState After)> _modified = [];
    private readonly List<(string Path, HistoryFileState State)> _gone = [];

    /// <summary>
    /// Смотрит на файл на диске.
    /// </summary>
    /// <param name="path">Полный путь.</param>
    /// <param name="quietly">
    /// Первый взгляд опорного снимка: файл, которого история не знала, запоминается без правки —
    /// он не появился, его просто впервые увидели.
    /// </param>
    public void Look(string path, bool quietly = false)
    {
        if (!_looked.Add(path))
            return;

        var known = store.Known(path);
        FileInfo info;

        try
        {
            info = new FileInfo(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        if (!info.Exists)
        {
            if (known is not null)
                _gone.Add((path, known));

            return;
        }

        if (known is not null && known.Looks(info.Length, info.LastWriteTimeUtc))
            return;

        // Не прочиталось — файл держит тот, кто его пишет. Следующее событие о нём придёт, когда
        // запись кончится.
        if (store.Capture(path) is not { } state)
            return;

        if (known is null)
        {
            if (quietly)
                _learned.Add((path, state));
            else
                _created.Add((path, state));

            return;
        }

        if (known.Content == state.Content || (known.TooLarge && state.TooLarge))
        {
            store.Learn(path, state);
            return;
        }

        _modified.Add((path, known, state));
    }

    /// <summary>Файл пропал.</summary>
    /// <param name="path">Полный путь.</param>
    /// <param name="state">Каким история его помнила.</param>
    public void Gone(string path, HistoryFileState state)
    {
        if (_looked.Add(path))
            _gone.Add((path, state));
    }

    /// <summary>
    /// Записывает увиденное одним действием и переносит его в известное состояние.
    /// </summary>
    /// <param name="label">Метка действия.</param>
    /// <param name="origin">Кто сделал.</param>
    /// <param name="firstSeen">
    /// Файлы, впервые увиденные опорным снимком, — содержимое и путь; общие для всех кусков одного
    /// обхода. Кусок пишет сразу, чтобы пачки правок, вставшие между кусками, не читали
    /// устаревшего, — а переезд, чьё прежнее имя пропало, а новое попалось в другом куске, сводится
    /// через эту карту.
    /// </param>
    /// <returns>Записанное действие; null — расхождений не было.</returns>
    public HistoryAction? Commit(string label, HistoryOrigin origin, Dictionary<ContentId, string>? firstSeen = null)
    {
        var changes = new List<HistoryChange>();
        var arrived = new List<(string Path, HistoryFileState State)>(_created);
        var seen = new List<(string Path, HistoryFileState State)>(_learned);

        foreach (var (path, state) in seen)
        {
            store.Learn(path, state);

            if (firstSeen is not null && state.Content is { } content)
                firstSeen.TryAdd(content, path);
        }

        foreach (var (path, before) in _gone)
        {
            store.Forget(path);

            if (Twin(arrived, before) is { } moved)
            {
                store.Learn(moved.Path, moved.State);
                changes.Add(Moved(path, moved.Path, before, moved.State));

                continue;
            }

            if (before.Content is { } content
                && firstSeen is not null
                && firstSeen.Remove(content, out var arrival)
                && store.Known(arrival) is { } now)
            {
                changes.Add(Moved(path, arrival, before, now));

                continue;
            }

            changes.Add(new HistoryChange
            {
                Kind = HistoryChangeKind.Deleted,
                Path = path,
                Before = before.Content,
                TooLarge = before.TooLarge,
            });
        }

        foreach (var (path, state) in arrived)
        {
            store.Learn(path, state);
            changes.Add(new HistoryChange
            {
                Kind = HistoryChangeKind.Created,
                Path = path,
                After = state.Content,
                TooLarge = state.TooLarge,
            });
        }

        foreach (var (path, before, after) in _modified)
        {
            store.Learn(path, after);

            if (after.TooLarge && !before.TooLarge)
            {
                // Вырос за предел: прежнее содержимое ещё есть, нового не будет.
                changes.Add(new HistoryChange
                {
                    Kind = HistoryChangeKind.Modified,
                    Path = path,
                    Before = before.Content,
                    TooLarge = true,
                });

                continue;
            }

            changes.Add(new HistoryChange
            {
                Kind = HistoryChangeKind.Modified,
                Path = path,
                Before = before.Content,
                After = after.Content,
                TooLarge = after.TooLarge,
            });
        }

        _looked.Clear();
        _created.Clear();
        _learned.Clear();
        _modified.Clear();
        _gone.Clear();

        return changes.Count == 0 ? null : store.Record(label, origin, changes);
    }

    private static HistoryChange Moved(string from, string to, HistoryFileState before, HistoryFileState after) => new()
    {
        Kind = HistoryChangeKind.Moved,
        Path = to,
        From = from,
        Before = before.Content,
        After = after.Content,
        TooLarge = after.TooLarge,
    };

    /// <summary>Появившийся с тем же содержимым, что у пропавшего, — и вынимает его из списка.</summary>
    private static (string Path, HistoryFileState State)? Twin(
        List<(string Path, HistoryFileState State)> arrived,
        HistoryFileState before)
    {
        if (before.Content is not { } content)
            return null;

        var at = arrived.FindIndex(item => item.State.Content == content);

        if (at < 0)
            return null;

        var twin = arrived[at];

        arrived.RemoveAt(at);

        return twin;
    }
}
