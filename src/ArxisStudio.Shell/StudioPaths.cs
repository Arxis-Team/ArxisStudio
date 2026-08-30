namespace ArxisStudio.Shell;

/// <summary>
/// Места, где студия хранит своё: настройки, список недавних проектов, плагины.
/// Один источник правды — иначе каждый сервис изобретал бы свой путь, и на
/// Linux с macOS они разъезжались бы по-разному.
/// </summary>
public static class StudioPaths
{
    /// <summary>Корень пользовательских данных студии.</summary>
    public static string UserData { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create),
        "ArxisStudio");

    /// <summary>Файл настроек студии.</summary>
    public static string SettingsFile => Path.Combine(UserData, "settings.json");

    /// <summary>Файл со списком недавних проектов.</summary>
    public static string RecentProjectsFile => Path.Combine(UserData, "recent-projects.json");

    /// <summary>
    /// Файл раскладки доков.
    /// </summary>
    /// <remarks>
    /// Отдельный от настроек, и это не мелочь. Раскладка пишется часто — на
    /// каждую потянутую границу и смену вкладки, — а тема с языком меняются
    /// раз в полгода. Держи их в одном файле, и каждый ресайз переписывал бы
    /// весь: одна неудачная запись уносила бы вместе с раскладкой ещё и язык.
    /// </remarks>
    public static string LayoutFile => Path.Combine(UserData, "layout.json");

    /// <summary>Каталог установленных плагинов: одна папка на плагин.</summary>
    public static string Plugins => Path.Combine(UserData, "plugins");

    /// <summary>
    /// Каталог словарей пользователя: <c>lang/&lt;код&gt;.json</c>.
    /// </summary>
    /// <remarks>
    /// Словарь, положенный сюда, сильнее поставляемого со студией: правят
    /// его, а в папку установки на общей машине может быть и не записать.
    /// </remarks>
    public static string Languages => Path.Combine(UserData, "lang");

    /// <summary>Создаёт каталог пользовательских данных, если его ещё нет.</summary>
    public static void EnsureUserData() => Directory.CreateDirectory(UserData);
}
