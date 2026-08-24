using System.Collections.ObjectModel;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>Строка журнала студии.</summary>
/// <param name="Time">Когда записано.</param>
/// <param name="Level">Уровень записи.</param>
/// <param name="Source">Кто написал.</param>
/// <param name="Message">Сообщение.</param>
public sealed record StudioLogEntry(DateTimeOffset Time, StudioLogLevel Level, string Source, string Message)
{
    /// <summary>Время в том виде, в каком его показывает панель.</summary>
    public string Stamp => Time.ToString("HH:mm:ss");

    /// <summary>Обычное сообщение.</summary>
    public bool IsInfo => Level == StudioLogLevel.Info;

    /// <summary>Предупреждение.</summary>
    public bool IsWarning => Level == StudioLogLevel.Warning;

    /// <summary>Ошибка.</summary>
    public bool IsError => Level == StudioLogLevel.Error;

    /// <summary>Уровень словом — так его печатает панель.</summary>
    public string LevelName => Level switch
    {
        StudioLogLevel.Debug => "DEBUG",
        StudioLogLevel.Warning => "WARN",
        StudioLogLevel.Error => "ERROR",
        _ => "INFO",
    };
}

/// <summary>
/// Журнал студии: то, что показывает панель «Консоль».
/// </summary>
/// <remarks>
/// Журнал один на всю студию: в него пишут и плагины через SDK, и сборка с
/// запуском проекта. Разводить их по разным панелям значило бы заставить
/// человека гадать, в какой смотреть.
/// </remarks>
public sealed class StudioLog : IStudioLog
{
    private const int Limit = 2000;

    /// <summary>Записи журнала, от старых к новым.</summary>
    public ObservableCollection<StudioLogEntry> Entries { get; } = [];

    /// <inheritdoc/>
    public void Write(StudioLogLevel level, string source, string message)
    {
        Entries.Add(new StudioLogEntry(DateTimeOffset.Now, level, source, message));

        // Журнал долгого сеанса иначе растёт без конца; старое уходит первым.
        while (Entries.Count > Limit)
            Entries.RemoveAt(0);
    }

    /// <summary>Очищает журнал.</summary>
    public void Clear() => Entries.Clear();
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
