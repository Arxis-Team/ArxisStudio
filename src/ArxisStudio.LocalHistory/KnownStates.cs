using System.Text.Json;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Последнее, что история видела на диске: длина, время записи и адрес содержимого каждого файла.
/// </summary>
/// <remarks>
/// <para>
/// Это не история, а её точка отсчёта. Внешняя правка записывается с содержимым «до» только
/// потому, что история знала файл до неё; удалённое мимо студии возвращается только потому, что его
/// содержимое уже лежало в хранилище. Поэтому объекты, на которые ссылается известное состояние,
/// очистка не трогает, как бы старо ни было действие, их положившее.
/// </para>
/// <para>
/// Файл состояния переписывается целиком через временный, так что оборванная запись оставляет
/// прежнее состояние, а не половину. Отставшее состояние не опасно: при следующем взгляде на диск
/// расхождение найдётся по длине и времени записи.
/// </para>
/// </remarks>
internal sealed class KnownStates
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _file;
    private readonly Dictionary<string, HistoryFileState> _states;
    private bool _dirty;

    private KnownStates(string file, Dictionary<string, HistoryFileState> states)
    {
        _file = file;
        _states = states;
    }

    /// <summary>Сколько файлов известно.</summary>
    public int Count => _states.Count;

    /// <summary>Читает состояние; нет файла или он испорчен — начинаем с пустого.</summary>
    /// <param name="file">Файл состояния.</param>
    public static KnownStates Load(string file)
    {
        var states = new Dictionary<string, HistoryFileState>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(file)
                && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllBytes(file), Json) is { } entries)
            {
                foreach (var (path, entry) in entries)
                {
                    if (entry.Ticks > 0)
                    {
                        states[path] = new HistoryFileState(
                            entry.Size,
                            new DateTime(entry.Ticks, DateTimeKind.Utc),
                            ContentId.TryParse(entry.Content, out var id) ? id : null);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            states.Clear();
        }

        return new KnownStates(file, states);
    }

    /// <summary>Состояние файла; null — история его не знает.</summary>
    /// <param name="path">Полный путь.</param>
    public HistoryFileState? Get(string path) => _states.GetValueOrDefault(path);

    /// <summary>Запоминает состояние файла.</summary>
    /// <param name="path">Полный путь.</param>
    /// <param name="state">Состояние.</param>
    public void Set(string path, HistoryFileState state)
    {
        _states[path] = state;
        _dirty = true;
    }

    /// <summary>Забывает файл.</summary>
    /// <param name="path">Полный путь.</param>
    public void Remove(string path)
    {
        if (_states.Remove(path))
            _dirty = true;
    }

    /// <summary>Известные файлы под папкой, на любой глубине.</summary>
    /// <param name="folder">Полный путь папки.</param>
    public List<KeyValuePair<string, HistoryFileState>> Under(string folder)
    {
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

        return [.. _states.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Адреса, на которые ссылается состояние.</summary>
    public IEnumerable<ContentId> Contents() => _states.Values.Select(state => state.Content).OfType<ContentId>();

    /// <summary>Пишет состояние, если оно менялось с прошлой записи.</summary>
    public void Save()
    {
        if (!_dirty)
            return;

        var entries = _states.ToDictionary(
            pair => pair.Key,
            pair => new Entry { Size = pair.Value.Size, Ticks = pair.Value.Written.Ticks, Content = pair.Value.Content?.Value },
            StringComparer.OrdinalIgnoreCase);

        var temporary = $"{_file}.{Guid.NewGuid():N}.tmp";

        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(entries, Json));
        File.Move(temporary, _file, overwrite: true);

        _dirty = false;
    }

    /// <summary>Состояние файла, как оно лежит на диске.</summary>
    private sealed class Entry
    {
        public long Size { get; set; }

        public long Ticks { get; set; }

        public string? Content { get; set; }
    }
}
