using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Настройки службы проектов.
/// </summary>
/// <remarks>
/// <para>
/// Слежение за диском выключаемо, и это не удобство, а аварийный выключатель: на большом
/// репозитории переключение ветки меняет тысячи файлов, и человек, у которого студия занята
/// перечитыванием, должен иметь способ это остановить, не закрывая решение.
/// </para>
/// <para>
/// Восстановление при открытии выключаемо по другой причине. Оно исполняет то, что написано в
/// чужих проектах, — сразу, как только папку открыли, — и человеку, который эту папку ещё не
/// читал, такое обещание студия давать не вправе. Спрос доверия к папке придёт вместе с Welcome,
/// а пока это переключатель.
/// </para>
/// </remarks>
/// <param name="WatchFiles">Перечитывать ли модель, когда её файлы меняются на диске.</param>
/// <param name="RestoreOnOpen">Восстанавливать ли пакеты, когда открытое их ждёт.</param>
public sealed record ProjectsSettings(bool WatchFiles, bool RestoreOnOpen)
{
    /// <summary>Ключ настройки слежения за диском.</summary>
    public const string WatchFilesKey = "projects.watchFiles";

    /// <summary>Ключ настройки восстановления при открытии.</summary>
    public const string RestoreOnOpenKey = "projects.restoreOnOpen";

    /// <summary>Настройки, пока человек ничего не менял.</summary>
    public static ProjectsSettings Default { get; } = new(true, true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [WatchFilesKey, RestoreOnOpenKey];

    /// <summary>Читает настройки из студии, подставляя умолчания.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ProjectsSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ProjectsSettings(
            settings.Get<bool?>(WatchFilesKey) ?? Default.WatchFiles,
            settings.Get<bool?>(RestoreOnOpenKey) ?? Default.RestoreOnOpen);
    }
}
