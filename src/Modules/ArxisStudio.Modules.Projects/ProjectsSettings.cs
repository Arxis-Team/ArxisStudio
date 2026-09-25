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
/// <para>
/// Локальная история выключаема и настраиваема сроком и пределами: она занимает место на диске
/// человека, и сколько — решает он. Умолчания — пять дней, файл до пяти мегабайт, вся история до
/// гигабайта, как у IntelliJ. Числа вне разумного зажимаются: срок меньше дня хранил бы только
/// сегодняшнее, а нулевой предел файла не хранил бы ничего, оставаясь включённым.
/// </para>
/// </remarks>
/// <param name="WatchFiles">Перечитывать ли модель, когда её файлы меняются на диске.</param>
/// <param name="RestoreOnOpen">Восстанавливать ли пакеты, когда открытое их ждёт.</param>
/// <param name="History">Вести ли локальную историю файлов проекта.</param>
/// <param name="HistoryDays">Сколько дней хранить историю.</param>
/// <param name="HistoryMaxFileMb">Самый большой файл, чьё содержимое история хранит, в мегабайтах.</param>
/// <param name="HistoryMaxTotalMb">Сколько история может занимать всего, в мегабайтах.</param>
public sealed record ProjectsSettings(
    bool WatchFiles,
    bool RestoreOnOpen,
    bool History = true,
    int HistoryDays = 5,
    int HistoryMaxFileMb = 5,
    int HistoryMaxTotalMb = 1024)
{
    /// <summary>Ключ настройки слежения за диском.</summary>
    public const string WatchFilesKey = "projects.watchFiles";

    /// <summary>Ключ настройки восстановления при открытии.</summary>
    public const string RestoreOnOpenKey = "projects.restoreOnOpen";

    /// <summary>Ключ настройки локальной истории.</summary>
    public const string HistoryKey = "projects.history";

    /// <summary>Ключ срока хранения истории, в днях.</summary>
    public const string HistoryDaysKey = "projects.history.days";

    /// <summary>Ключ предела файла истории, в мегабайтах.</summary>
    public const string HistoryMaxFileMbKey = "projects.history.maxFileMb";

    /// <summary>Ключ предела всей истории, в мегабайтах.</summary>
    public const string HistoryMaxTotalMbKey = "projects.history.maxTotalMb";

    /// <summary>Предел файла истории, в байтах: так его ждёт хранилище.</summary>
    public long HistoryMaxFileBytes => HistoryMaxFileMb * Megabyte;

    /// <summary>Предел всей истории, в байтах.</summary>
    public long HistoryMaxTotalBytes => HistoryMaxTotalMb * Megabyte;

    private const long Megabyte = 1024L * 1024;

    /// <summary>Настройки, пока человек ничего не менял.</summary>
    public static ProjectsSettings Default { get; } = new(true, true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        [WatchFilesKey, RestoreOnOpenKey, HistoryKey, HistoryDaysKey, HistoryMaxFileMbKey, HistoryMaxTotalMbKey];

    /// <summary>Ключи локальной истории: на их смену служба перенастраивает историю.</summary>
    public static IReadOnlyList<string> HistoryKeys { get; } =
        [HistoryKey, HistoryDaysKey, HistoryMaxFileMbKey, HistoryMaxTotalMbKey];

    /// <summary>Читает настройки из студии, подставляя умолчания.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ProjectsSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ProjectsSettings(
            settings.Get<bool?>(WatchFilesKey) ?? Default.WatchFiles,
            settings.Get<bool?>(RestoreOnOpenKey) ?? Default.RestoreOnOpen,
            settings.Get<bool?>(HistoryKey) ?? Default.History,
            Whole(settings.Get<double?>(HistoryDaysKey), Default.HistoryDays, 1, 365),
            Whole(settings.Get<double?>(HistoryMaxFileMbKey), Default.HistoryMaxFileMb, 1, 1024),
            Whole(settings.Get<double?>(HistoryMaxTotalMbKey), Default.HistoryMaxTotalMb, 16, 1024 * 1024));
    }

    private static int Whole(double? value, int fallback, int least, int most) =>
        value is { } number && double.IsFinite(number) ? (int)Math.Clamp(Math.Round(number), least, most) : fallback;
}
