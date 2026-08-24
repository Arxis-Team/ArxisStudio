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

    /// <summary>Каталог установленных плагинов: одна папка на плагин.</summary>
    public static string Plugins => Path.Combine(UserData, "plugins");

    /// <summary>Создаёт каталог пользовательских данных, если его ещё нет.</summary>
    public static void EnsureUserData() => Directory.CreateDirectory(UserData);
}
