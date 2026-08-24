using System.Reflection;
using System.Text.Json;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Читает манифест встроенного модуля из его сборки.
/// </summary>
/// <remarks>
/// У модуля нет своей папки в каталоге плагинов — он приезжает со студией,
/// поэтому его <c>module.json</c> лежит внутри сборки встроенным ресурсом.
/// Формат манифеста при этом общий с плагинами: один разбор на оба случая.
/// </remarks>
public static class ModuleManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Читает манифест из сборки модуля.</summary>
    /// <param name="assembly">Сборка со встроенным <c>module.json</c>.</param>
    /// <returns>Манифест или сообщение, почему прочитать не удалось.</returns>
    public static (PluginManifest? Manifest, string? Error) Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(candidate => candidate.EndsWith("module.json", StringComparison.OrdinalIgnoreCase));

        if (name is null)
            return (null, $"В сборке {assembly.GetName().Name} нет встроенного module.json");

        try
        {
            using var stream = assembly.GetManifestResourceStream(name)!;

            return (JsonSerializer.Deserialize<PluginManifest>(stream, Options), null);
        }
        catch (JsonException e)
        {
            return (null, $"module.json не разобрался: {e.Message}");
        }
    }
}
