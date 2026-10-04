using System.Text.Json;
using System.Text.Json.Nodes;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Что доска помнит о решении: места карточек и формы, убранные с доски.</summary>
/// <param name="Spots">Места по путям форм — и у убранных: вернувшись, карточка встаёт туда же.</param>
/// <param name="Removed">Формы, убранные с доски: файл на месте, а карточки нет.</param>
internal sealed record BoardData(Dictionary<CanonicalPath, Spot> Spots, HashSet<CanonicalPath> Removed)
{
    /// <summary>Доска, о которой не помнится ничего.</summary>
    public static BoardData Empty() => new([], []);
}

/// <summary>
/// Доска на диске: <c>.arxis/ui-designer/board.json</c> рядом с решением.
/// </summary>
/// <remarks>
/// Раскладка принадлежит решению, а не человеку: её коммитят вместе с проектом, и у второго
/// разработчика доска стоит так же. Поэтому файл лежит в <c>.arxis</c> — там же, где проектные
/// настройки студии, — а не в папке данных. Точка в имени папки уводит его и от модели проекта, и от
/// локальной истории: слежение за решением такие папки не смотрит, и запись доски не перечитывает
/// модель.
/// <para>
/// Ключ — путь формы от папки решения через прямую косую черту: файл переезжает вместе с решением и
/// одинаков на любой ОС. Ключи пишутся по порядку — у файла под git разница правки в одну карточку
/// — одна строка. Испорченный файл не мешает открыть доску: она откроется с нерасставленными
/// карточками, а файл перепишется при первой правке.
/// </para>
/// <para>
/// Убранные формы — список <c>removed</c>. Номер формата из-за него не растёт: прежний читатель
/// списка не знает и просто показывает такие формы, то есть понимает файл, а не ломается на нём.
/// </para>
/// <para>
/// <b>Вторая версия</b> — места, отмеренные под карточку со снимком формы. Первая отмеряла их под
/// карточку в 120 точек, рядами через 160 (<see cref="FirstRowPitch"/>), и карточка со снимком выше —
/// ряды первой версии налезли бы друг на друга. Поэтому места первой версии при чтении растягиваются по
/// вертикали на нынешний шаг рядов, а файл переписывается не сам, а первой правкой доски: открыть доску
/// не значит править файл, который коммитят вместе с проектом.
/// </para>
/// </remarks>
internal static class BoardFile
{
    /// <summary>
    /// Номер формата: растёт, когда старый читатель перестаёт понимать новый файл — или когда меняется
    /// то, в чём отмерены места.
    /// </summary>
    public const int Version = 2;

    /// <summary>Шаг рядов первой версии: карточка без снимка в 120 точек и зазор в 40.</summary>
    public const double FirstRowPitch = 160;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions Reading = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Где лежит доска решения; null — решения нет.</summary>
    /// <param name="entryPoint">Открытое решение или проект.</param>
    public static string? PathFor(CanonicalPath entryPoint) =>
        FolderOf(entryPoint) is { } folder
            ? Path.Combine(folder.Value, ".arxis", "ui-designer", "board.json")
            : null;

    /// <summary>Папка, от которой отмеряются ключи: папка решения.</summary>
    /// <param name="entryPoint">Открытое решение или проект.</param>
    public static CanonicalPath? FolderOf(CanonicalPath entryPoint) =>
        !entryPoint.IsEmpty
        && Path.GetDirectoryName(entryPoint.Value) is { Length: > 0 } folder
        && CanonicalPath.TryCreate(folder, out var path)
            ? path
            : null;

    /// <summary>
    /// Читает доску.
    /// </summary>
    /// <param name="file">Файл доски.</param>
    /// <param name="folder">Папка решения.</param>
    /// <param name="rowPitch">
    /// Нынешний шаг рядов: на него растягиваются места первой версии. По умолчанию — шаг первой версии,
    /// и места читаются как записаны.
    /// </param>
    /// <returns>Места и убранные формы; файла нет или он испорчен — пусто.</returns>
    public static BoardData Read(string file, CanonicalPath folder, double rowPitch = FirstRowPitch)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        var board = BoardData.Empty();

        JsonNode? root;

        try
        {
            if (!File.Exists(file))
                return board;

            root = JsonNode.Parse(File.ReadAllText(file), documentOptions: Reading);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return board;
        }

        // Номера нет — файл первой версии: она писала его всегда, но ручная правка могла его снять.
        var stretch = Number(root?["version"]) is >= 2 ? 1 : rowPitch / FirstRowPitch;

        if (root?["forms"] is JsonObject forms)
        {
            foreach (var (key, value) in forms)
            {
                if (value is JsonObject place
                    && Number(place["x"]) is { } x
                    && Number(place["y"]) is { } y
                    && PathOf(folder, key) is { } path)
                {
                    board.Spots[path] = new Spot(x, Round(y * stretch));
                }
            }
        }

        if (root?["removed"] is JsonArray removed)
        {
            foreach (var entry in removed)
            {
                if (entry is JsonValue value && value.TryGetValue<string>(out var key) && PathOf(folder, key) is { } path)
                    board.Removed.Add(path);
            }
        }

        return board;
    }

    /// <summary>
    /// Записывает доску целиком: во временный файл рядом, потом подменой.
    /// </summary>
    /// <param name="file">Файл доски.</param>
    /// <param name="folder">Папка решения.</param>
    /// <param name="board">Места и убранные формы.</param>
    /// <remarks>
    /// Подмена, а не запись поверх: оборванная запись оставила бы половину JSON, и доска открылась бы
    /// нерасставленной. Ошибки диска летят вызывающему — сказать о них в журнал может только он. Пустой
    /// список убранных не пишется: файл доски, где ничего не убирали, остаётся таким, каким был.
    /// </remarks>
    public static void Write(string file, CanonicalPath folder, BoardData board)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(board);

        var forms = new JsonObject();

        foreach (var (key, spot) in board.Spots
                     .Select(pair => (Key(folder, pair.Key), pair.Value))
                     .OrderBy(pair => pair.Item1, StringComparer.Ordinal))
        {
            forms[key] = new JsonObject { ["x"] = Round(spot.X), ["y"] = Round(spot.Y) };
        }

        var json = new JsonObject { ["version"] = Version, ["forms"] = forms };

        if (board.Removed.Count > 0)
        {
            json["removed"] = new JsonArray(board.Removed
                .Select(path => Key(folder, path))
                .Order(StringComparer.Ordinal)
                .Select(key => (JsonNode?)JsonValue.Create(key))
                .ToArray());
        }

        var text = json.ToJsonString(Options);
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Ключ формы: путь от папки решения через прямую косую черту.</summary>
    /// <param name="folder">Папка решения.</param>
    /// <param name="form">Путь формы.</param>
    public static string Key(CanonicalPath folder, CanonicalPath form) =>
        Path.GetRelativePath(folder.Value, form.Value).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Путь по ключу; ключ, который не путь, — чужая правка руками, и доска его пропускает.</summary>
    private static CanonicalPath? PathOf(CanonicalPath folder, string key)
    {
        try
        {
            return CanonicalPath.Create(folder, key);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue value
        && value.TryGetValue<double>(out var number)
        && double.IsFinite(number)
            ? number
            : null;

    /// <summary>Место пишется с точностью до сотой: дробь тяги в файле — шум разницы, а не точность.</summary>
    private static double Round(double value) => Math.Round(value, 2);
}
