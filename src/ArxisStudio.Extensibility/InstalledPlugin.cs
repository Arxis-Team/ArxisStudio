using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Плагин, найденный в каталоге. Плагин с испорченным манифестом тоже попадает
/// сюда — со своей ошибкой: молча пропасть из списка хуже, чем показаться
/// сломанным, потому что второе объясняет, почему он не работает.
/// </summary>
/// <param name="Directory">Папка плагина.</param>
/// <param name="Manifest">Разобранный манифест или null, если разбор не удался.</param>
/// <param name="Error">Сообщение об ошибке разбора или null.</param>
/// <param name="IsEnabled">Включён ли плагин в настройках студии.</param>
public sealed record InstalledPlugin(
    string Directory,
    PluginManifest? Manifest,
    string? Error,
    bool IsEnabled)
{
    /// <summary>Идентификатор: из манифеста, а при ошибке — имя папки.</summary>
    public string Id => Manifest?.Id is { Length: > 0 } id ? id : Path.GetFileName(Directory);

    /// <summary>Отображаемое имя: из манифеста, а при ошибке — имя папки.</summary>
    public string DisplayName => Manifest?.Name is { Length: > 0 } name ? name : Path.GetFileName(Directory);

    /// <summary>Разобрался ли манифест.</summary>
    public bool IsValid => Manifest is not null;

    /// <summary>Абсолютный путь к иконке плагина, если она объявлена и существует.</summary>
    public string? IconPath
    {
        get
        {
            if (Manifest?.Icon is not { Length: > 0 } icon)
                return null;

            var path = Path.Combine(Directory, icon);
            return File.Exists(path) ? path : null;
        }
    }
}
