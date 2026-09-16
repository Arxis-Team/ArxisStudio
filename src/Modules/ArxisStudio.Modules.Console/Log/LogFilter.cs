using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console.Log;

/// <summary>
/// Отбор записей: какие уровни показывать, чей источник и что искать.
/// </summary>
/// <remarks>
/// Значимая структура, а не класс: отбор сравнивают целиком — «изменился ли он
/// с прошлого перестроения», — и равенство по значению здесь и есть ответ.
/// <para>
/// Отбор живёт только в панели и сбрасывается вместе с сеансом. В настройки
/// студии он не идёт намеренно: отбор, переживший перезапуск, прятал бы
/// сегодняшние записи молча, и человек искал бы поломку там, где её нет.
/// </para>
/// </remarks>
/// <param name="Debug">Показывать подробности для отладки.</param>
/// <param name="Info">Показывать обычные сообщения.</param>
/// <param name="Warning">Показывать предупреждения.</param>
/// <param name="Error">Показывать ошибки.</param>
/// <param name="Sources">Кого из пишущих не показывать.</param>
/// <param name="Query">Что искать в источнике и сообщении; пусто — всё.</param>
public readonly record struct LogFilter(
    bool Debug,
    bool Info,
    bool Warning,
    bool Error,
    LogSources Sources,
    string Query)
{
    /// <summary>Отбор, пропускающий всё, — с него панель начинает.</summary>
    public static LogFilter Everything { get; } = new(true, true, true, true, LogSources.All, string.Empty);

    /// <summary>Подходит ли запись.</summary>
    /// <param name="record">Запись журнала.</param>
    public bool Matches(StudioLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!Allows(record.Level))
            return false;

        if (!Sources.Shows(record.Source))
            return false;

        if (Query is not { Length: > 0 } query)
            return true;

        return record.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Message.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool Allows(StudioLogLevel level) => level switch
    {
        StudioLogLevel.Debug => Debug,
        StudioLogLevel.Warning => Warning,
        StudioLogLevel.Error => Error,
        _ => Info,
    };
}
