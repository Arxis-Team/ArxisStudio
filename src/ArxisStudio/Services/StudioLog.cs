using System.Collections.ObjectModel;
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
public sealed class StudioCommands : IStudioCommands
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    /// <summary>Идентификаторы заявленных команд.</summary>
    public IReadOnlyCollection<string> Registered => _handlers.Keys;

    /// <inheritdoc/>
    public void Register(string id, Action handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[id] = handler;
    }

    /// <inheritdoc/>
    public bool Invoke(string id)
    {
        if (!_handlers.TryGetValue(id, out var handler))
            return false;

        handler();
        return true;
    }

    /// <summary>Убирает команды, заявленные выгружаемым плагином.</summary>
    /// <param name="ids">Идентификаторы, которые перестают действовать.</param>
    public void Remove(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _handlers.Remove(id);
    }
}
