using System.Collections.Immutable;

namespace ArxisStudio.LocalHistory;

/// <summary>Что сняла очистка.</summary>
/// <param name="Days">Сколько дней журнала снято.</param>
/// <param name="Objects">Сколько объектов содержимого убрано.</param>
public readonly record struct LocalHistoryPruning(int Days, int Objects);

/// <summary>
/// Локальная история в одной папке: содержимое по адресу, журнал действий, известное состояние и
/// срок хранения.
/// </summary>
/// <remarks>
/// <para>
/// <b>Раскладка.</b> <c>objects/</c> — содержимое (<see cref="ContentStore"/>), <c>journal/</c> —
/// действия по дням (<see cref="HistoryJournal"/>), <c>state.json</c> — известное состояние
/// (<see cref="KnownStates"/>), <c>.lock</c> — замок процесса.
/// </para>
/// <para>
/// <b>Один процесс.</b> Папку держит тот, кто открыл её первым: замок — файл, открытый без права
/// делиться. Вторая студия над той же папкой получает <see cref="LocalHistoryBusyException"/> и
/// историю не пишет — иначе обе дописывали бы один журнал и переписывали одно состояние.
/// </para>
/// <para>
/// <b>Потоки.</b> Журнал, действия и состояние меняются под одним замком. Снятие содержимого идёт
/// мимо него: объект кладётся идемпотентно, а хэшировать и сжимать большой файл под замком значило бы
/// держать им запись правки, которой до файла нет дела.
/// </para>
/// <para>
/// <b>Что решает хозяин.</b> Какие пути — файлы проекта, когда снимать и какой меткой называть
/// действие, библиотека не знает. Она хранит то, что ей дали, и отвечает, что было.
/// </para>
/// </remarks>
public sealed class LocalHistoryStore : IDisposable
{
    private readonly Lock _gate = new();
    private readonly FileStream _hold;
    private readonly ContentStore _content;
    private readonly HistoryJournal _journal;
    private readonly KnownStates _known;
    private readonly List<HistoryAction> _actions;
    private LocalHistoryOptions _options;
    private long _next;
    private bool _disposed;

    private LocalHistoryStore(string root, FileStream hold, LocalHistoryOptions options)
    {
        Root = root;
        _hold = hold;
        _options = options;
        _content = new ContentStore(Path.Combine(root, "objects"));
        _journal = new HistoryJournal(Path.Combine(root, "journal"));
        _known = KnownStates.Load(Path.Combine(root, "state.json"));
        _actions = [.. _journal.ReadAll()];
        _next = _actions.Count == 0 ? 1 : _actions[^1].Id + 1;
    }

    /// <summary>Папка истории.</summary>
    public string Root { get; }

    /// <summary>Срок и пределы; меняются на ходу вместе с настройками.</summary>
    public LocalHistoryOptions Options
    {
        get
        {
            lock (_gate)
                return _options;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_gate)
                _options = value;
        }
    }

    /// <summary>Действия, которые хранятся, от старого к новому.</summary>
    public ImmutableArray<HistoryAction> Actions
    {
        get
        {
            lock (_gate)
                return [.. _actions];
        }
    }

    /// <summary>Открывает историю в папке, заводя её, если нужно.</summary>
    /// <param name="root">Папка истории.</param>
    /// <param name="options">Срок и пределы; null — умолчания.</param>
    /// <exception cref="LocalHistoryBusyException">Папку держит другой процесс.</exception>
    public static LocalHistoryStore Open(string root, LocalHistoryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        Directory.CreateDirectory(root);

        FileStream hold;

        try
        {
            hold = new FileStream(Path.Combine(root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            throw new LocalHistoryBusyException($"Папку истории {root} держит другой процесс", e);
        }

        try
        {
            return new LocalHistoryStore(root, hold, options ?? LocalHistoryOptions.Default);
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }

    /// <summary>Каким история видела файл в последний раз; null — не видела.</summary>
    /// <param name="path">Полный путь.</param>
    public HistoryFileState? Known(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        lock (_gate)
            return _known.Get(path);
    }

    /// <summary>Известные файлы под папкой, на любой глубине.</summary>
    /// <param name="folder">Полный путь папки.</param>
    public IReadOnlyList<KeyValuePair<string, HistoryFileState>> KnownUnder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        lock (_gate)
            return _known.Under(folder);
    }

    /// <summary>
    /// Снимает файл с диска: кладёт содержимое в хранилище и возвращает состояние.
    /// </summary>
    /// <param name="path">Полный путь.</param>
    /// <returns>Состояние; null — файла нет или его не прочесть (держит другой процесс).</returns>
    /// <remarks>
    /// Ни журнала, ни известного состояния снятие не трогает: что это — появление, правка или просто
    /// первый взгляд, — решает тот, кто снимал. Файл больше предела не читается вовсе, и состояние
    /// приходит без адреса содержимого.
    /// </remarks>
    public HistoryFileState? Capture(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var limit = Options.MaxFileBytes;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
                return null;

            if (info.Length > limit)
                return new HistoryFileState(info.Length, info.LastWriteTimeUtc, null);

            var bytes = File.ReadAllBytes(path);

            // Время — после чтения: файл, переписанный посреди него, при следующем взгляде разойдётся
            // с состоянием по времени и будет снят заново.
            var written = File.GetLastWriteTimeUtc(path);

            return bytes.LongLength > limit
                ? new HistoryFileState(bytes.LongLength, written, null)
                : new HistoryFileState(bytes.LongLength, written, _content.Put(bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Запоминает, каким файл стал.</summary>
    /// <param name="path">Полный путь.</param>
    /// <param name="state">Состояние.</param>
    public void Learn(string path, HistoryFileState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
            _known.Set(path, state);
    }

    /// <summary>Забывает файл: его не стало или он больше не в счёт.</summary>
    /// <param name="path">Полный путь.</param>
    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        lock (_gate)
            _known.Remove(path);
    }

    /// <summary>Записывает действие.</summary>
    /// <param name="label">Метка для человека.</param>
    /// <param name="origin">Кто сделал.</param>
    /// <param name="changes">Правки по порядку.</param>
    /// <returns>Записанное действие — с номером и временем.</returns>
    /// <exception cref="ArgumentException">Метка пуста или правок нет.</exception>
    public HistoryAction Record(string label, HistoryOrigin origin, IEnumerable<HistoryChange> changes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(changes);

        ImmutableArray<HistoryChange> list = [.. changes];

        if (list.IsEmpty)
            throw new ArgumentException("Действие без правок записывать незачем", nameof(changes));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var action = new HistoryAction
            {
                Id = _next++,
                Time = _options.Time.GetUtcNow(),
                Label = label,
                Origin = origin,
                Changes = list,
            };

            _journal.Append(action);
            _actions.Add(action);

            return action;
        }
    }

    /// <summary>
    /// Правки пути от новой к старой — сквозь переименования и переезды.
    /// </summary>
    /// <param name="path">Нынешний полный путь.</param>
    /// <remarks>
    /// Идём назад по действиям и помним, как путь звался до каждого: переезд файла или папки, в
    /// которой он лежал, меняет имя, под которым ищутся правки постарше. Иначе история
    /// переименованного файла начиналась бы с переименования.
    /// </remarks>
    public ImmutableArray<HistoryRevision> Revisions(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        HistoryAction[] actions;

        lock (_gate)
            actions = [.. _actions];

        var found = ImmutableArray.CreateBuilder<HistoryRevision>();
        var current = path;

        for (var at = actions.Length - 1; at >= 0; at--)
        {
            var action = actions[at];
            string? earlier = null;

            foreach (var change in action.Changes)
            {
                if (Same(change.Path, current))
                {
                    found.Add(new HistoryRevision(action, change));

                    if (change is { Kind: HistoryChangeKind.Moved, From: { } from })
                        earlier = from;
                }
                else if (change is { IsDirectory: true, Kind: HistoryChangeKind.Moved, From: { } folder }
                         && Inside(current, change.Path))
                {
                    found.Add(new HistoryRevision(action, change));
                    earlier = folder + current[change.Path.Length..];
                }
            }

            if (earlier is not null)
                current = earlier;
        }

        return found.ToImmutable();
    }

    /// <summary>Читает содержимое по адресу; null — его нет или оно испорчено.</summary>
    /// <param name="id">Адрес.</param>
    public byte[]? Read(ContentId id) => _content.Read(id);

    /// <summary>Пишет известное состояние на диск, если оно менялось.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_disposed)
                _known.Save();
        }
    }

    /// <summary>
    /// Снимает то, что пережило срок, и то, что не влезает в предел, и убирает осиротевшее содержимое.
    /// </summary>
    /// <returns>Что снято.</returns>
    /// <remarks>
    /// Срок — в календарных днях по UTC, сегодняшний включительно. Предел объёма снимает самые старые
    /// дни по одному, пока история не уложится; сегодняшний не снимается никогда — действие, которое
    /// человек только что сделал, обязано откатываться.
    /// </remarks>
    public LocalHistoryPruning Prune()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var now = _options.Time.GetUtcNow();
            var today = HistoryJournal.DayOf(now);
            var oldest = today.AddDays(-(Math.Max(1, _options.Days) - 1));
            var days = 0;

            foreach (var day in _journal.Days().Where(day => day < oldest))
            {
                Drop(day);
                days++;
            }

            var objects = Sweep();

            while (_journal.Size + _content.Size > _options.MaxTotalBytes
                   && _journal.Days().Where(day => day < today).Select(day => (DateOnly?)day).FirstOrDefault() is { } first)
            {
                Drop(first);
                days++;
                objects += Sweep();
            }

            _known.Save();

            return new LocalHistoryPruning(days, objects);
        }
    }

    /// <summary>Пишет состояние и отпускает папку.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _known.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Не записалось — при следующем открытии расхождение найдётся по диску.
            }
        }

        _hold.Dispose();
    }

    private void Drop(DateOnly day)
    {
        _journal.Drop(day);
        _actions.RemoveAll(action => HistoryJournal.DayOf(action.Time) == day);
    }

    private int Sweep()
    {
        var alive = new HashSet<ContentId>(_known.Contents());

        foreach (var change in _actions.SelectMany(action => action.Changes))
        {
            if (change.Before is { } before)
                alive.Add(before);

            if (change.After is { } after)
                alive.Add(after);
        }

        return _content.Sweep(alive);
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Inside(string path, string folder) =>
        path.Length > folder.Length
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (path[folder.Length] == Path.DirectorySeparatorChar || path[folder.Length] == Path.AltDirectorySeparatorChar);
}
