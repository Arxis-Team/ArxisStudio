using ArxisStudio.Extensibility;
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

/// <summary>
/// Журнал глазами одного расширения.
/// </summary>
/// <remarks>
/// Журнал один на студию, и обработчики его события зовёт тот, кто пишет, — из любого потока и посреди
/// своей работы. Обработчик расширения, позванный напрямую, бросал бы в пишущего: в фоновую задачу
/// соседа, в доклад шва о чужом сбое, — и соседи после него события не слышали. Забытая подписка держала
/// бы контекст загрузки ушедшего расширения, а запись звала бы его код. Обёртка зовёт обработчик через
/// шов на счёт хозяина и помнит свои подписки, чтобы снять их с его уходом.
/// </remarks>
/// <param name="feed">Общий журнал.</param>
/// <param name="guard">Шов вызовов расширений.</param>
/// <param name="pluginId">Чьи подписки ставит эта обёртка.</param>
internal sealed class PluginLogFeed(IStudioLogFeed feed, PluginGuard guard, string pluginId) : IStudioLogFeed
{
    private readonly Lock _gate = new();
    private readonly List<(EventHandler Handler, EventHandler Guarded)> _subscriptions = [];

    /// <inheritdoc/>
    public IReadOnlyList<StudioLogRecord> Records => feed.Records;

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add
        {
            if (value is null)
                return;

            EventHandler guarded = (sender, args) =>
                guard.Run(pluginId, "обработчик перемены журнала", () => value(sender, args));

            lock (_gate)
                _subscriptions.Add((value, guarded));

            feed.Changed += guarded;
        }
        remove
        {
            if (value is null)
                return;

            EventHandler guarded;

            // Последнюю из равных, как у событий .NET.
            lock (_gate)
            {
                var index = _subscriptions.FindLastIndex(subscription => subscription.Handler == value);

                if (index < 0)
                    return;

                guarded = _subscriptions[index].Guarded;
                _subscriptions.RemoveAt(index);
            }

            feed.Changed -= guarded;
        }
    }

    /// <inheritdoc/>
    public void Clear() => feed.Clear();

    /// <summary>Снимает подписки, которые расширение оставило за собой.</summary>
    public void Release()
    {
        EventHandler[] guarded;

        lock (_gate)
        {
            guarded = [.. _subscriptions.Select(subscription => subscription.Guarded)];
            _subscriptions.Clear();
        }

        foreach (var handler in guarded)
            feed.Changed -= handler;
    }
}
