using System.Text.Json;
using System.Text.Json.Nodes;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>
/// Места карточек доски на диске: <c>.arxis/ui-designer/board.json</c> рядом с решением.
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
/// </remarks>
internal static class BoardFile
{
    /// <summary>Номер формата: растёт, когда старый читатель перестаёт понимать новый файл.</summary>
    public const int Version = 1;

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
    /// Читает места карточек.
    /// </summary>
    /// <param name="file">Файл доски.</param>
    /// <param name="folder">Папка решения.</param>
    /// <returns>Места по путям форм; файла нет или он испорчен — пусто.</returns>
    public static Dictionary<CanonicalPath, Spot> Read(string file, CanonicalPath folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        var spots = new Dictionary<CanonicalPath, Spot>();

        JsonNode? root;

        try
        {
            if (!File.Exists(file))
                return spots;

            root = JsonNode.Parse(File.ReadAllText(file), documentOptions: Reading);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return spots;
        }

        if (root?["forms"] is not JsonObject forms)
            return spots;

        foreach (var (key, value) in forms)
        {
            if (value is not JsonObject place || Number(place["x"]) is not { } x || Number(place["y"]) is not { } y)
                continue;

            try
            {
                spots[CanonicalPath.Create(folder, key)] = new Spot(x, y);
            }
            catch (ArgumentException)
            {
                // Ключ, который не путь, — чужая правка руками; доска его пропускает, а не падает.
            }
        }

        return spots;
    }

    /// <summary>
    /// Записывает места целиком: во временный файл рядом, потом подменой.
    /// </summary>
    /// <param name="file">Файл доски.</param>
    /// <param name="folder">Папка решения.</param>
    /// <param name="spots">Места по путям форм.</param>
    /// <remarks>
    /// Подмена, а не запись поверх: оборванная запись оставила бы половину JSON, и доска открылась бы
    /// нерасставленной. Ошибки диска летят вызывающему — сказать о них в журнал может только он.
    /// </remarks>
    public static void Write(string file, CanonicalPath folder, IReadOnlyDictionary<CanonicalPath, Spot> spots)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(spots);

        var forms = new JsonObject();

        foreach (var (key, spot) in spots
                     .Select(pair => (Key(folder, pair.Key), pair.Value))
                     .OrderBy(pair => pair.Item1, StringComparer.Ordinal))
        {
            forms[key] = new JsonObject { ["x"] = Round(spot.X), ["y"] = Round(spot.Y) };
        }

        var text = new JsonObject { ["version"] = Version, ["forms"] = forms }.ToJsonString(Options);
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

    private static double? Number(JsonNode? node) =>
        node is JsonValue value
        && value.TryGetValue<double>(out var number)
        && double.IsFinite(number)
            ? number
            : null;

    /// <summary>Место пишется с точностью до сотой: дробь тяги в файле — шум разницы, а не точность.</summary>
    private static double Round(double value) => Math.Round(value, 2);
}
