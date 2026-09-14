using System.Text.Json;
using Avalonia.Input;

namespace ArxisStudio.Services;

/// <summary>
/// Сочетания клавиш, назначенные человеком, — <c>keymap.json</c> рядом с настройками.
/// </summary>
/// <param name="Entries">Команды в порядке файла — и сочетания, которые человек им дал.</param>
/// <param name="Complaints">Что в файле не разобралось — строками для журнала.</param>
/// <remarks>
/// Файл пишет человек, и форма у него человеческая: имя команды — сочетание.
/// <code>
/// {
///   "studio.palette": "Ctrl+Alt+P",
///   "studio.close": ["Ctrl+W", "Ctrl+F4"],
///   "hello.greet": null
/// }
/// </code>
/// Строка — одно сочетание, массив — несколько, <c>null</c> или пустая строка — ни одного. Названная
/// команда своё сочетание по умолчанию теряет: человек решил за неё сам. Комментарии и запятая после
/// последнего значения разрешены — файл правят руками. Названная дважды команда берёт последнее
/// значение, но стоит на месте первого: порядок записей решает, кому достаётся сочетание, названное
/// у двух команд.
/// <para>
/// Опечатка не повод терять остальное. Сочетание, которое не разобралось, называется в жалобе и
/// пропускается, а команда, у которой не разобралось ни одно, остаётся при своём по умолчанию:
/// человек хотел другое сочетание, а не никакого. Файл, который не разобрался целиком, не меняет
/// ничего.
/// </para>
/// </remarks>
public sealed record StudioKeymap(IReadOnlyList<KeymapEntry> Entries, IReadOnlyList<string> Complaints)
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Файла нет — сочетания те, что по умолчанию.</summary>
    public static StudioKeymap Empty { get; } = new([], []);

    /// <summary>Читает файл; нет его — пустая раскладка без жалоб.</summary>
    /// <param name="path">Путь к <c>keymap.json</c>.</param>
    public static StudioKeymap Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Empty with { Complaints = [$"keymap.json не прочитан: {e.Message}"] };
        }
    }

    /// <summary>Разбирает текст файла.</summary>
    /// <param name="json">Содержимое <c>keymap.json</c>.</param>
    public static StudioKeymap Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            return Empty with { Complaints = [$"keymap.json не разобран и не применён: {e.Message}"] };
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Empty with { Complaints = ["keymap.json не применён: ждали объект «команда — сочетание»"] };

            var entries = new List<KeymapEntry>();
            var complaints = new List<string>();

            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (Gestures(entry, complaints) is not { } gestures)
                    continue;

                var at = entries.FindIndex(known => string.Equals(known.CommandId, entry.Name, StringComparison.Ordinal));

                if (at < 0)
                {
                    entries.Add(new KeymapEntry(entry.Name, gestures));
                    continue;
                }

                complaints.Add($"keymap.json: команда {entry.Name} названа дважды — взято последнее значение");
                entries[at] = new KeymapEntry(entry.Name, gestures);
            }

            return new StudioKeymap(entries, complaints);
        }
    }

    /// <summary>Сочетания одной команды; <c>null</c> — команда остаётся при своём по умолчанию.</summary>
    private static List<string>? Gestures(JsonProperty entry, List<string> complaints)
    {
        var written = entry.Value.ValueKind switch
        {
            JsonValueKind.Null => [],
            JsonValueKind.String => [entry.Value.GetString()!],
            JsonValueKind.Array => entry.Value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToList(),
            _ => (List<string?>?)null,
        };

        if (written is null || written.Contains(null))
        {
            complaints.Add($"keymap.json: у команды {entry.Name} сочетание должно быть строкой, списком строк или null — оставлено по умолчанию");

            return null;
        }

        var named = written.Where(gesture => !string.IsNullOrWhiteSpace(gesture)).Select(gesture => gesture!.Trim()).ToList();
        var read = named.Where(gesture => Readable(gesture, entry.Name, complaints)).ToList();

        // Ни одно из названных не разобралось: человек хотел другое сочетание, а не никакого.
        return named.Count > 0 && read.Count == 0 ? null : read;
    }

    private static bool Readable(string gesture, string command, List<string> complaints)
    {
        try
        {
            KeyGesture.Parse(gesture);

            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            complaints.Add($"keymap.json: сочетание «{gesture}» у команды {command} не разобралось");

            return false;
        }
    }
}

/// <summary>Одна запись <c>keymap.json</c>.</summary>
/// <param name="CommandId">Имя команды.</param>
/// <param name="Gestures">Сочетания, которые человек ей дал; пустой список — ни одного.</param>
public sealed record KeymapEntry(string CommandId, IReadOnlyList<string> Gestures);
