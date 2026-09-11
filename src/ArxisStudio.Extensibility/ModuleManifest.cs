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

    /// <summary>
    /// Папка встроенного модуля — там, где лежит его сборка.
    /// </summary>
    /// <param name="assembly">Сборка модуля.</param>
    /// <returns>Папка сборки; у сборки без файла — папка приложения.</returns>
    /// <remarks>
    /// Не корень студии: сборка студии кладёт модули в свою папку <c>Modules</c>, а
    /// тесты — рядом с собой, и объявленный модулем контракт лежит там же, где сам
    /// модуль. Спрашивать папку у сборки значит не знать о раскладке ничего — и не
    /// разойтись с ней ни в одной из них. Сборка, собранная в памяти, файла не имеет:
    /// ей остаётся папка приложения, как было до папки модулей.
    /// </remarks>
    public static string FolderOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.Location is { Length: > 0 } location && Path.GetDirectoryName(location) is { Length: > 0 } folder
            ? folder
            : AppContext.BaseDirectory;
    }
}
