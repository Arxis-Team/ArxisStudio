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
/// Команды студии: то, что плагины заявляют и вызывают друг у друга.
/// </summary>
/// <remarks>
/// Обработчик команды — чужой код, и зовётся он отсюда: и когда человек выбрал
/// пункт меню, и когда одна команда вызывает другую. Поэтому вызов идёт через
/// шов, а хозяин команды запоминается при заявке — по стеку упавшего
/// обработчика плагина уже не назвать.
/// </remarks>
/// <param name="guard">Шов вызовов плагинов; null — звать напрямую.</param>
public sealed class StudioCommands(PluginGuard? guard = null) : IStudioCommands
{
    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Идентификаторы заявленных команд.</summary>
    public IReadOnlyCollection<string> Registered => _handlers.Keys;

    /// <summary>
    /// Будильник: зовётся, когда у команды не нашлось обработчика.
    /// </summary>
    /// <remarks>
    /// Хозяин команды может ещё спать — ждать своего <c>onCommand:</c>.
    /// Реестр о хосте плагинов не знает и знать не должен (ссылка сюда пришла
    /// бы кольцом), поэтому пробуждение выставляет окно студии. Без
    /// будильника поведение прежнее: не нашлось — false.
    /// </remarks>
    public Action<string>? Awaken { get; set; }

    /// <inheritdoc/>
    public void Register(string id, Action handler) => Register(id, handler, owner: null);

    /// <summary>
    /// Заявляет команду от имени плагина.
    /// </summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <param name="handler">Что делать по вызову.</param>
    /// <param name="owner">Чей это обработчик; null — самой студии.</param>
    public void Register(string id, Action handler, string? owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[id] = new Handler(handler, owner);
    }

    /// <inheritdoc/>
    public bool Invoke(string id)
    {
        if (!_handlers.TryGetValue(id, out var handler))
        {
            // Будим и спрашиваем ещё раз: хозяин, ждавший этой команды,
            // зарегистрирует обработчик при подъёме. Рекурсия конечна —
            // хост убирает плагин из ждущих до подъёма, и второй звонок по
            // той же команде никого не найдёт.
            Awaken?.Invoke(id);

            if (!_handlers.TryGetValue(id, out handler))
                return false;
        }

        if (guard is null || handler.Owner is not { } owner)
        {
            handler.Run();
            return true;
        }

        return guard.Run(owner, $"команда {id}", handler.Run);
    }

    /// <summary>Убирает команды, заявленные выгружаемым плагином.</summary>
    /// <param name="ids">Идентификаторы, которые перестают действовать.</param>
    public void Remove(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _handlers.Remove(id);
    }

    /// <summary>
    /// Убирает все команды одного владельца.
    /// </summary>
    /// <param name="pluginId">Чьи обработчики снять.</param>
    /// <remarks>
    /// По владельцу, а не по манифесту: манифест при перезагрузке уже
    /// свежий, и команда, убранная новой версией, осталась бы висеть с
    /// обработчиком из выгруженного контекста. Владельца реестр помнит с
    /// рождения записи — он и есть правда о том, чьё это.
    /// </remarks>
    public void RemoveOwnedBy(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        foreach (var id in _handlers
                     .Where(pair => string.Equals(pair.Value.Owner, pluginId, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _handlers.Remove(id);
        }
    }

    private readonly record struct Handler(Action Run, string? Owner);
}

/// <summary>
/// Команды глазами одного плагина.
/// </summary>
/// <remarks>
/// Реестр один на студию, а имя заявителя у каждого своё: контракт SDK о
/// хозяине команды не говорит, и подставить его может только тот, кто выдаёт
/// плагину контекст.
/// </remarks>
/// <param name="commands">Общий реестр команд.</param>
/// <param name="pluginId">Чьи заявки идут через эту обёртку.</param>
public sealed class PluginCommands(StudioCommands commands, string pluginId) : IStudioCommands
{
    /// <inheritdoc/>
    public void Register(string id, Action handler) => commands.Register(id, handler, pluginId);

    /// <inheritdoc/>
    public bool Invoke(string id) => commands.Invoke(id);
}
