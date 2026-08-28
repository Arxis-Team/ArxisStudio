using System.Collections.ObjectModel;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Журнал студии: то, что показывает панель «Консоль».
/// </summary>
/// <remarks>
/// Журнал один на всю студию: в него пишут и плагины через SDK, и сборка с
/// запуском проекта. Разводить их по разным панелям значило бы заставить
/// человека гадать, в какой смотреть.
/// </remarks>
public sealed class StudioLog : IStudioLog, IStudioLogFeed
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
        _records.Add(new StudioLogRecord(DateTimeOffset.Now, level, source, message));

        // Журнал долгого сеанса иначе растёт без конца; старое уходит первым.
        while (_records.Count > Limit)
            _records.RemoveAt(0);

        Changed?.Invoke(this, EventArgs.Empty);
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
