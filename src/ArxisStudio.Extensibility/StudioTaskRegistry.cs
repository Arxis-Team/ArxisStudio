using System.Collections.ObjectModel;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Идущая фоновая задача — то, что о ней знает студия.
/// </summary>
/// <remarks>
/// Задача принадлежит плагину, и это не подробность учёта: пока она работает,
/// её поток держит типы плагина, а через них — его контекст загрузки. Перед
/// выгрузкой такие задачи надо отменить и дождаться, иначе плагин «выгрузится»
/// только на словах.
/// </remarks>
public sealed class StudioTask : IStudioProgress
{
    private readonly CancellationTokenSource _cancellation = new();

    internal StudioTask(string pluginId, string title)
    {
        PluginId = pluginId;
        Title = title;
    }

    /// <summary>Задача изменилась: сообщение, доля или завершение.</summary>
    public event EventHandler? Changed;

    /// <summary>Чья задача.</summary>
    public string PluginId { get; }

    /// <summary>Имя задачи для человека.</summary>
    public string Title { get; }

    /// <summary>Чем задача занята сейчас; пусто — ничем не сообщила.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Доля сделанного; null — доля неизвестна.</summary>
    public double? Fraction { get; private set; }

    /// <summary>Задачу попросили остановиться.</summary>
    public bool IsCancelling => _cancellation.IsCancellationRequested;

    /// <summary>Токен, по которому работа узнаёт об отмене.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <inheritdoc/>
    public void Report(string message)
    {
        Message = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Report(double fraction, string? message = null)
    {
        Fraction = Math.Clamp(fraction, 0, 1);

        if (message is not null)
            Message = message;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Просит задачу остановиться.</summary>
    /// <remarks>
    /// Просит, а не останавливает: отмена в .NET кооперативная, и работа
    /// прекратится, когда сама заглянет в токен. Работа, которая в него не
    /// смотрит, доработает до конца — это её беда, а не студии.
    /// </remarks>
    public void Cancel()
    {
        _cancellation.Cancel();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Finish() => _cancellation.Dispose();
}

/// <summary>
/// Все идущие задачи студии.
/// </summary>
/// <remarks>
/// Список один на студию: строка состояния показывает по нему, что происходит,
/// а хост плагинов — спрашивает, чьи задачи ещё живы, когда плагин собираются
/// выгрузить.
/// </remarks>
public sealed class StudioTaskRegistry
{
    private readonly ObservableCollection<StudioTask> _running = [];

    /// <summary>Список идущих задач изменился.</summary>
    public event EventHandler? Changed;

    /// <summary>Идущие задачи, свежая последней.</summary>
    public IReadOnlyList<StudioTask> Running => _running;

    /// <summary>Заводит задачу и объявляет о ней.</summary>
    /// <param name="pluginId">Чья задача.</param>
    /// <param name="title">Имя для человека.</param>
    public StudioTask Start(string pluginId, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        var task = new StudioTask(pluginId, title);

        task.Changed += Announce;
        _running.Add(task);
        Announce(task, EventArgs.Empty);

        return task;
    }

    /// <summary>Убирает задачу из списка.</summary>
    /// <param name="task">Кончившаяся задача.</param>
    public void Finish(StudioTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.Changed -= Announce;
        _running.Remove(task);
        task.Finish();

        Announce(task, EventArgs.Empty);
    }

    /// <summary>
    /// Просит задачи плагина остановиться и ждёт, пока они уйдут.
    /// </summary>
    /// <param name="pluginId">Чьи задачи.</param>
    /// <param name="timeout">Сколько ждать; по истечении — перестать ждать.</param>
    /// <returns><c>true</c>, если задач не осталось.</returns>
    /// <remarks>
    /// Ждём не из вежливости: работающая задача держит типы плагина, и
    /// выгрузить его, пока она жива, нельзя. Ждём с пределом: работа, не
    /// смотрящая в токен, иначе заморозила бы студию насмерть — а так студия
    /// скажет человеку, что прежняя копия осталась в памяти.
    /// </remarks>
    public async Task<bool> StopAsync(string pluginId, TimeSpan timeout)
    {
        foreach (var task in _running.Where(task => task.PluginId == pluginId).ToList())
            task.Cancel();

        var deadline = DateTimeOffset.UtcNow + timeout;

        while (_running.Any(task => task.PluginId == pluginId) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(25);

        return !_running.Any(task => task.PluginId == pluginId);
    }

    private void Announce(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
