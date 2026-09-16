using System.Reflection;
using System.Text.Json;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Читает манифест встроенного модуля из его папки.
/// </summary>
/// <remarks>
/// У модуля есть своя папка, и устроена она как папка установленного плагина: <c>module.json</c> в
/// корне, сборки в <c>bin</c>. Формат манифеста тот же, что у плагина, — один разбор на оба случая,
/// — и теперь то же самое место: манифест читается файлом, а не ресурсом внутри сборки. Двух форм
/// у него нет нарочно; разойдясь, они дали бы расхождение, которое некому поймать.
/// </remarks>
public static class ModuleManifest
{
    /// <summary>Имя файла манифеста в папке модуля.</summary>
    private const string FileName = "module.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Читает манифест модуля.
    /// </summary>
    /// <param name="assembly">Сборка модуля; манифест ищется в её папке.</param>
    /// <returns>Манифест или сообщение, почему прочитать не удалось.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Манифест <c>null</c> и сообщение <c>null</c> вместе не возвращаются никогда: не прочиталось —
    /// сказано, почему. На этом стоит разбор у зовущих, и потому же здесь ловится не одна
    /// <see cref="JsonException"/>: манифест теперь файл, а файл бывает и занят, и закрыт правами.
    /// Каждое сообщение называет дорогу — вопрос «где искали» возникает первым.
    /// </remarks>
    public static (PluginManifest? Manifest, string? Error) Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var path = Path.Combine(FolderOf(assembly), FileName);

        if (!File.Exists(path))
            return (null, $"Рядом с модулем нет манифеста: ждали {path}");

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Options);

            // Тело из одного null разбирается без исключения и даёт null: без этой проверки
            // зовущий получил бы пустой ответ без единого слова о том, что случилось.
            return manifest is null ? (null, $"{path}: манифест пуст") : (manifest, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, $"{path} не прочитался: {e.Message}");
        }
    }

    /// <summary>
    /// Папка встроенного модуля — та, в которой лежит его манифест.
    /// </summary>
    /// <param name="assembly">Сборка модуля.</param>
    /// <returns>Папка модуля; у сборки без файла — папка приложения.</returns>
    /// <remarks>
    /// Сборки модуля лежат в его <c>bin</c>, поэтому от папки сборки делается шаг вверх — ровно
    /// так же считается папка установленного плагина, у которого <c>entry</c> указывает в
    /// <c>bin</c>. Шаг делается по имени папки, а не по догадке: <c>bin</c> — часть формы, и папка
    /// с этим именем внутри папки модуля значит только это.
    /// <para>
    /// Всё прочее возвращается как есть, вместе с хвостовым разделителем, если он был: сборка,
    /// собранная в памяти, файла не имеет и получает папку приложения, а сравнивать дороги умеет
    /// тот, кто их сравнивает.
    /// </para>
    /// </remarks>
    public static string FolderOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (assembly.Location is not { Length: > 0 } location || Path.GetDirectoryName(location) is not { Length: > 0 } folder)
            return AppContext.BaseDirectory;

        return string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), "bin", StringComparison.OrdinalIgnoreCase)
               && Path.GetDirectoryName(folder) is { Length: > 0 } above
            ? above
            : folder;
    }
}
