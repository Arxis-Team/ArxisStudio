namespace ArxisStudio.Sdk;

/// <summary>
/// Документы студии: открыть файл во вкладке.
/// </summary>
/// <remarks>
/// Кто откроет файл, решает не тот, кто просит: оболочка спрашивает
/// зарегистрированные редакторы и ставит вкладку сама. Панели поэтому просят
/// «открой это», а не «покажи мне вот такой редактор».
/// </remarks>
public interface IStudioDocuments
{
    /// <summary>Открывает файл во вкладке.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    Task OpenAsync(string filePath);
}

/// <summary>Запись журнала студии.</summary>
/// <param name="Time">Когда записано.</param>
/// <param name="Level">Уровень записи.</param>
/// <param name="Source">Кто написал.</param>
/// <param name="Message">Сообщение.</param>
public sealed record StudioLogRecord(DateTimeOffset Time, StudioLogLevel Level, string Source, string Message)
{
    /// <summary>Время в том виде, в каком его показывает панель.</summary>
    public string Stamp => Time.ToString("HH:mm:ss");

    /// <summary>Уровень словом — так его печатает панель.</summary>
    public string LevelName => Level switch
    {
        StudioLogLevel.Debug => "DEBUG",
        StudioLogLevel.Warning => "WARN",
        StudioLogLevel.Error => "ERROR",
        _ => "INFO",
    };

    /// <summary>Обычное сообщение.</summary>
    public bool IsInfo => Level == StudioLogLevel.Info;

    /// <summary>Предупреждение.</summary>
    public bool IsWarning => Level == StudioLogLevel.Warning;

    /// <summary>Ошибка.</summary>
    public bool IsError => Level == StudioLogLevel.Error;
}

/// <summary>
/// Журнал с той стороны, с которой его читают.
/// </summary>
/// <remarks>
/// Писать в журнал умеет каждый через <see cref="IStudioLog"/>, а показывать
/// его — тот, кто взялся за панель «Консоль». Разделены они потому, что это
/// разные права: право сказать и право видеть всё сказанное.
/// </remarks>
public interface IStudioLogFeed
{
    /// <summary>Записи журнала, от старых к новым.</summary>
    IReadOnlyList<StudioLogRecord> Records { get; }

    /// <summary>Записей стало больше или их очистили.</summary>
    event EventHandler? Changed;

    /// <summary>Очищает журнал.</summary>
    void Clear();
}
