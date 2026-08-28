using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Фоновые задачи одного плагина.
/// </summary>
/// <remarks>
/// Список задач один на студию, а заводятся они от имени плагина: по имени их
/// потом и отменят, когда плагин станут выгружать.
/// <para>
/// Работа уходит в поток из пула. Отмена — не сбой и в журнал не пишется: её
/// попросил человек. Всё остальное, чем работа кончилась плохо, идёт через шов —
/// тем же путём, что и сбой панели: приписывается плагину и считается ему.
/// </para>
/// </remarks>
/// <param name="pluginId">Чьи это задачи.</param>
/// <param name="registry">Общий список задач.</param>
/// <param name="guard">Шов вызовов плагина.</param>
/// <param name="log">Журнал студии.</param>
public sealed class PluginTasks(
    string pluginId,
    StudioTaskRegistry registry,
    PluginGuard guard,
    IStudioLog log) : IStudioTasks
{
    /// <inheritdoc/>
    public async Task<T> RunAsync<T>(string title, Func<IStudioProgress, CancellationToken, Task<T>> work)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(work);

        var task = registry.Start(pluginId, title);

        try
        {
            // Task.Run, а не прямой вызов: работу зовут из потока интерфейса, и
            // начаться она обязана не в нём — иначе всё, что автор сделал до
            // первого await, студия отстоит замороженной.
            return await Task.Run(() => work(task, task.Token), task.Token);
        }
        catch (OperationCanceledException)
        {
            log.Write(StudioLogLevel.Debug, "Tasks", $"{title}: отменено");
            throw;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            guard.Report(pluginId, $"задача «{title}»", e);
            throw;
        }
        finally
        {
            registry.Finish(task);
        }
    }

    /// <inheritdoc/>
    public Task RunAsync(string title, Func<IStudioProgress, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunAsync<object?>(title, async (progress, token) =>
        {
            await work(progress, token);
            return null;
        });
    }
}
