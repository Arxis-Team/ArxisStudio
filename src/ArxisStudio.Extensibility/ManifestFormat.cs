using System.Text.Json;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Как читается манифест — и <c>plugin.json</c>, и <c>module.json</c>.
/// </summary>
/// <remarks>
/// Формат у плагина и модуля один, и разбор обязан быть одним: настройки чтения лежали копией у
/// каталога и у модулей, и первая же правка одной из них сделала бы манифест, который разбирается
/// плагином и не разбирается модулем. Пишет манифест человек, поэтому комментарии и запятая после
/// последнего элемента — не ошибка, а регистр имён полей не важен.
/// </remarks>
internal static class ManifestFormat
{
    /// <summary>Имя манифеста в папке внешнего плагина.</summary>
    public const string Plugin = "plugin.json";

    /// <summary>Имя манифеста в папке встроенного модуля.</summary>
    public const string Module = "module.json";

    /// <summary>Настройки разбора манифеста.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Читает манифест из файла; null — тело из одного <c>null</c>.</summary>
    /// <param name="path">Путь к манифесту.</param>
    /// <remarks>
    /// Исключения чтения и разбора — <see cref="JsonException"/>, <see cref="IOException"/>,
    /// <see cref="UnauthorizedAccessException"/> — уходят зовущему: каталог и модули говорят о них
    /// своими словами.
    /// </remarks>
    public static PluginManifest? Read(string path) =>
        JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Options);
}
