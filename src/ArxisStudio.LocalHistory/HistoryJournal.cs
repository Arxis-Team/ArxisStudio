using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Журнал действий: файл на день, строка на действие, запись только дописыванием.
/// </summary>
/// <remarks>
/// <para>
/// Файл на день — ради срока хранения: старый день уходит удалением одного файла, а не переписыванием
/// журнала, которое оборвалось бы на середине вместе с процессом.
/// </para>
/// <para>
/// Строка пишется одним вызовом и сбрасывается на диск сразу: действие, о котором студия уже
/// сказала человеку, не должно пропасть оттого, что следом упал процесс. Оборванная последняя
/// строка — след такого падения — при чтении пропускается: одно недописанное действие стоит
/// меньше, чем журнал, который не читается целиком.
/// </para>
/// </remarks>
internal sealed class HistoryJournal
{
    private const string Extension = ".jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root;

    /// <summary>Заводит журнал в этой папке.</summary>
    /// <param name="root">Папка журнала.</param>
    public HistoryJournal(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>Дописывает действие в файл его дня.</summary>
    /// <param name="action">Действие.</param>
    /// <remarks>
    /// Файл, оборванный падением посреди строки, кончается не переводом строки. Дописанное прямо
    /// за обрывком слилось бы с ним в одну испорченную строку, и вместе с недописанным пропало бы
    /// и новое действие, — поэтому обрывок сперва закрывается.
    /// </remarks>
    public void Append(HistoryAction action)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Entry.Of(action), Json) + "\n");

        using var stream = new FileStream(PathOf(DayOf(action.Time)), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);

            if (stream.ReadByte() != '\n')
                stream.WriteByte((byte)'\n');
        }

        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Дни, за которые есть записи, от старого к новому.</summary>
    public IReadOnlyList<DateOnly> Days() =>
        [.. Directory.EnumerateFiles(_root, "*" + Extension)
            .Select(file => DateOnly.TryParseExact(
                Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : (DateOnly?)null)
            .OfType<DateOnly>()
            .Order()];

    /// <summary>Действия одного дня в порядке записи; испорченные строки пропускаются.</summary>
    /// <param name="day">День.</param>
    public IEnumerable<HistoryAction> Read(DateOnly day)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(PathOf(day), Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            HistoryAction? action;

            try
            {
                action = JsonSerializer.Deserialize<Entry>(line, Json)?.ToAction();
            }
            catch (JsonException)
            {
                action = null;
            }

            if (action is not null)
                yield return action;
        }
    }

    /// <summary>Снимает день целиком.</summary>
    /// <param name="day">День.</param>
    public void Drop(DateOnly day)
    {
        try
        {
            File.Delete(PathOf(day));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Держит чужой процесс — снимем в следующий раз.
        }
    }

    /// <summary>Сколько занимает журнал, в байтах.</summary>
    public long Size => new DirectoryInfo(_root).EnumerateFiles("*" + Extension).Sum(file => file.Length);

    /// <summary>День действия — по UTC: иначе смена часового пояса делила бы один день на два.</summary>
    /// <param name="time">Время действия.</param>
    public static DateOnly DayOf(DateTimeOffset time) => DateOnly.FromDateTime(time.UtcDateTime);

    private string PathOf(DateOnly day) =>
        Path.Combine(_root, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension);

    /// <summary>Строка журнала: действие, как оно лежит на диске.</summary>
    private sealed class Entry
    {
        public long Id { get; set; }

        public DateTimeOffset Time { get; set; }

        public string? Label { get; set; }

        public HistoryOrigin Origin { get; set; }

        public List<Line>? Changes { get; set; }

        public static Entry Of(HistoryAction action) => new()
        {
            Id = action.Id,
            Time = action.Time,
            Label = action.Label,
            Origin = action.Origin,
            Changes = [.. action.Changes.Select(Line.Of)],
        };

        public HistoryAction? ToAction()
        {
            if (Id <= 0 || Label is null || Changes is not { Count: > 0 })
                return null;

            var changes = Changes.Select(line => line.ToChange()).ToList();

            if (changes.Contains(null))
                return null;

            return new HistoryAction
            {
                Id = Id,
                Time = Time,
                Label = Label,
                Origin = Origin,
                Changes = [.. changes.OfType<HistoryChange>()],
            };
        }
    }

    /// <summary>Правка, как она лежит на диске.</summary>
    private sealed class Line
    {
        public HistoryChangeKind Kind { get; set; }

        public string? Path { get; set; }

        public string? From { get; set; }

        public string? Before { get; set; }

        public string? After { get; set; }

        public bool Dir { get; set; }

        public bool Large { get; set; }

        public static Line Of(HistoryChange change) => new()
        {
            Kind = change.Kind,
            Path = change.Path,
            From = change.From,
            Before = change.Before?.Value,
            After = change.After?.Value,
            Dir = change.IsDirectory,
            Large = change.TooLarge,
        };

        public HistoryChange? ToChange()
        {
            if (string.IsNullOrEmpty(Path) || !Enum.IsDefined(Kind))
                return null;

            return new HistoryChange
            {
                Kind = Kind,
                Path = Path,
                From = From,
                Before = Address(Before),
                After = Address(After),
                IsDirectory = Dir,
                TooLarge = Large,
            };
        }

        private static ContentId? Address(string? text) => ContentId.TryParse(text, out var id) ? id : null;
    }
}

/// <summary>Действия журнала, прочитанные один раз.</summary>
internal static class HistoryJournalExtensions
{
    /// <summary>Все действия всех дней, по номеру.</summary>
    /// <param name="journal">Журнал.</param>
    public static ImmutableArray<HistoryAction> ReadAll(this HistoryJournal journal) =>
        [.. journal.Days().SelectMany(journal.Read).OrderBy(action => action.Id)];
}
