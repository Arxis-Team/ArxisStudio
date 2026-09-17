using System.Text.Json;

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
    /// <summary>Настройки разбора манифеста.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
