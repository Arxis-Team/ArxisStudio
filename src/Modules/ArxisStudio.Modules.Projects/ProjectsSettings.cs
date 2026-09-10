using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Настройки службы проектов.
/// </summary>
/// <remarks>
/// Слежение за диском выключаемо, и это не удобство, а аварийный выключатель: на большом
/// репозитории переключение ветки меняет тысячи файлов, и человек, у которого студия занята
/// перечитыванием, должен иметь способ это остановить, не закрывая решение.
/// </remarks>
/// <param name="WatchFiles">Перечитывать ли модель, когда её файлы меняются на диске.</param>
public sealed record ProjectsSettings(bool WatchFiles)
{
    /// <summary>Ключ настройки слежения за диском.</summary>
    public const string WatchFilesKey = "projects.watchFiles";

    /// <summary>Настройки, пока человек ничего не менял.</summary>
    public static ProjectsSettings Default { get; } = new(true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [WatchFilesKey];

    /// <summary>Читает настройки из студии, подставляя умолчания.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ProjectsSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ProjectsSettings(settings.Get<bool?>(WatchFilesKey) ?? Default.WatchFiles);
    }
}
