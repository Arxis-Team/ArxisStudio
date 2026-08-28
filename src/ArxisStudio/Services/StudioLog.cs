using System.Collections.ObjectModel;
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
/// Панели, которая показывала бы журнал, в студии сейчас нет, и без такого
/// отражения он виден только сам себе: студия пишет о сбое плагина, а прочесть
/// это негде. Поток — не замена панели, а канал для того, кто запускает студию
/// из терминала: разработчика студии и автора плагина.
/// </para>
/// </remarks>
/// <param name="echo">
/// Куда отражать записи; null — никуда. Решает это приложение: библиотеке не
/// положено считать, что у процесса есть консоль.
/// </param>
public sealed class StudioLog(TextWriter? echo = null) : IStudioLog, IStudioLogFeed
{
    private const int Limit = 2000;

    // Коллекция наблюдаемая: панель консоли — модуль и получает её службой,
    // так что подписаться на изменения она может, а перестроить список по
    // событию — уже нет, там нет ни одного её объекта.
    private readonly ObservableCollection<StudioLogRecord> _records = [];

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<StudioLogRecord> Records => _records;

    /// <inheritdoc/>
    public void Write(StudioLogLevel level, string source, string message)
    {
        var record = new StudioLogRecord(DateTimeOffset.Now, level, source, message);

        _records.Add(record);
        Echo(record);

        // Журнал долгого сеанса иначе растёт без конца; старое уходит первым.
        while (_records.Count > Limit)
            _records.RemoveAt(0);

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
        _records.Clear();
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
            return false;

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
