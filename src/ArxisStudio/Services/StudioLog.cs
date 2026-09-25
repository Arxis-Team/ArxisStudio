using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Журнал студии: то, что показывает панель «Консоль».
/// </summary>
/// <remarks>
/// Журнал один на всю студию: в него пишут и плагины через SDK, и сама
/// оболочка — о подъёме плагинов, их сбоях и отключениях. Разводить это по
/// разным местам значило бы заставить человека гадать, в какое смотреть.
/// <para>
/// Записи можно отражать в поток — обычно это стандартный вывод процесса.
/// Показывает журнал панель «Консоль», и поток ей не замена и не конкурент:
/// он канал для того, кто запускает студию из терминала — разработчика студии
/// и автора плагина, — и остаётся единственным, пока панель ещё не построена.
/// </para>
/// <para>
/// Журнал на студию один, и это важнее, чем кажется: пока их было два — свой
/// у приложения и свой у главного окна, — службой отдавался только второй, и
/// записи запуска панель не увидела бы никогда, хотя в терминале они есть.
/// </para>
/// </remarks>
/// <param name="echo">
/// Куда отражать записи; null — никуда. Решает это приложение: библиотеке не
/// положено считать, что у процесса есть консоль.
/// </param>
public sealed class StudioLog(TextWriter? echo = null) : IStudioLog, IStudioLogFeed
{
    private const int Limit = 2000;

    // Писать в журнал могут из любого потока: фоновая задача плагина
    // отчитывается о своей отмене из пула, а шов сбоев зовёт запись оттуда же.
    // Поэтому список закрыт замком, а наружу уходит снимок.
    private readonly Lock _gate = new();
    private readonly List<StudioLogRecord> _records = [];

    private IReadOnlyList<StudioLogRecord>? _snapshot;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>
    /// Записи журнала, от старых к новым.
    /// </summary>
    /// <remarks>
    /// Отдаётся снимок, а не живой список, и он же запоминается до следующей
    /// записи: панель читает его на каждое изменение, и копировать две тысячи
    /// записей ради каждого чтения незачем.
    /// <para>
    /// Наблюдаемой коллекции здесь нет намеренно — она была, и это оказалось
    /// ошибкой: её <c>CollectionChanged</c> прилетал бы в привязки Avalonia из
    /// потока пула, а трогать дерево контролов оттуда нельзя. Перенос в поток
    /// интерфейса — дело того, кто показывает, а не того, кто пишет: журнал
    /// собирают и там, где никакой Avalonia нет.
    /// </para>
    /// </remarks>
    public IReadOnlyList<StudioLogRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _snapshot ??= [.. _records];
            }
        }
    }

    /// <inheritdoc/>
    public void Write(StudioLogLevel level, string source, string message)
    {
        var record = new StudioLogRecord(DateTimeOffset.Now, level, source, message);

        lock (_gate)
        {
            _records.Add(record);

            // Журнал долгого сеанса иначе растёт без конца; старое уходит первым.
            if (_records.Count > Limit)
                _records.RemoveRange(0, _records.Count - Limit);

            _snapshot = null;
        }

        // Эхо и событие — вне замка: подписчик исполняется своим кодом, и
        // держать на нём наш замок значит однажды получить взаимную блокировку.
        Echo(record);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отражает запись в поток тем же видом, каким её показала бы панель.
    /// </summary>
    /// <remarks>
    /// Время, уровень и источник берутся у самой записи: панель и поток должны
    /// говорить одно и то же, иначе искать по журналу придётся дважды.
    /// <para>
    /// Отсутствие консоли — не ошибка: приложение с графическим интерфейсом
    /// запускают и без терминала, и тогда написанное просто некуда деть. А вот
    /// уронить студию из-за того, что журнал не смог напечатать строку, было бы
    /// нелепо вдвойне.
    /// </para>
    /// </remarks>
    private void Echo(StudioLogRecord record)
    {
        if (echo is null)
            return;

        try
        {
            echo.WriteLine($"{record.Stamp} {record.LevelName,-5} {record.Source,-12} {record.Message}");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
            _snapshot = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
