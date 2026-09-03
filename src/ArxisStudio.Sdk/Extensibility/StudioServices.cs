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

/// <summary>
/// Панели плагина на экране: достать свою панель на видное место.
/// </summary>
/// <remarks>
/// Достаётся через <see cref="IStudioContext.GetService{T}"/>. Нужна командам:
/// кнопка «Терминал» в полосе, за которой панель не появляется, — сломанная
/// кнопка, а сама панель о своём месте в доке ничего не знает. Чужие панели
/// отсюда недостижимы: служба выдаётся каждому плагину своя и знает, чья она.
/// <para>
/// Звать можно из любого потока: студия сама перенесёт показ в поток
/// интерфейса.
/// </para>
/// </remarks>
public interface IStudioToolWindows
{
    /// <summary>Достаёт панель на видное место: выбирает её вкладку и поднимает окно, если она в своём.</summary>
    /// <param name="toolWindowId">Идентификатор панели из манифеста — без имени плагина.</param>
    void Show(string toolWindowId);
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
