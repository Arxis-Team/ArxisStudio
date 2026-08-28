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
/// <param name="IsBuiltIn">
/// Модуль, приехавший со студией: своей папки у него нет, и словарём ему служат
/// словари самой студии.
/// </param>
public sealed record InstalledPlugin(
    string Directory,
    PluginManifest? Manifest,
    string? Error,
    bool IsEnabled,
    bool IsBuiltIn = false)
{
    /// <summary>Идентификатор: из манифеста, а при ошибке — имя папки.</summary>
    public string Id => Manifest?.Id is { Length: > 0 } id ? id : Path.GetFileName(Directory);

    /// <summary>
    /// Словари плагина: по ним разворачиваются <c>%ключи%</c> из манифеста.
    /// </summary>
    /// <remarks>
    /// Набор общий на папку плагина, а не свой у каждой записи: словари
    /// спрашивают и список плагинов, и меню, и панели, а перечитывать файл
    /// заставляет только смена языка или перезагрузка плагина.
    /// </remarks>
    public PluginStrings Strings => PluginStrings.For(IsBuiltIn ? null : Directory, Id);

    /// <summary>
    /// Отображаемое имя: из манифеста, а при ошибке — имя папки.
    /// </summary>
    /// <remarks>
    /// <c>%ключ%</c> разворачивается здесь, а не у каждого, кто показывает имя:
    /// имя плагина попадает и в список, и в меню, и в журнал, и ключ, забытый
    /// в одном из этих мест, был бы виден человеку сырым.
    /// </remarks>
    public string DisplayName => Manifest?.Name is { Length: > 0 } name
        ? Strings.Resolve(name)
        : Path.GetFileName(Directory);

    /// <summary>Описание для списка плагинов; пусто, если его не объявляли.</summary>
    public string Description => Strings.Resolve(Manifest?.Description);

    /// <summary>
    /// Полнота переводов, которые несёт этот пакет; пусто у обычного плагина.
    /// </summary>
    /// <remarks>
    /// Считается на месте, а не хранится: список ключей студии растёт с
    /// каждым её релизом, и вчерашнее число сегодня было бы неправдой.
    /// </remarks>
    public IReadOnlyList<LanguageCoverage> Coverage =>
        Manifest?.Contributions.Languages
            .Select(declared => LanguageCoverage.Of(Directory, declared))
            .OfType<LanguageCoverage>()
            .ToList() ?? [];

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
