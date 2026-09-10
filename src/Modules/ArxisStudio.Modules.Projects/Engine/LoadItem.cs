using System.Collections.Immutable;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Одна загрузка в очереди: зачем она, кто её ждёт и чем кончилась.
/// </summary>
/// <remarks>
/// Пока загрузка не началась, к ней присоединяются — вторая просьба перечитать, смена
/// конфигурации, перемена на диске, — и все получат один итог. Поля, кроме итога, меняет служба под
/// своим замком.
/// <para>
/// Итог отменяется сразу, как только кончилась сессия или ушёл последний ждущий, — не дожидаясь,
/// пока очередь дойдёт до загрузки или движок дочитает начатое. Ждущий не обязан сидеть за работой,
/// от которой уже отказались.
/// </para>
/// </remarks>
internal sealed class LoadItem
{
    private readonly TaskCompletionSource<WorkspaceLoadResult> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _abandoned = new();
    private readonly HashSet<CanonicalPath> _seen = [];
    private readonly List<CanonicalPath> _causes = [];
    private CancellationTokenRegistration _ending;

    /// <summary>Заводит загрузку сессии.</summary>
    /// <param name="session">Чья загрузка.</param>
    /// <param name="reason">Зачем она.</param>
    public LoadItem(ProjectsSession session, ProjectsLoadReason reason)
    {
        Session = session;
        Reason = reason;

        _ending = session.Lifetime.UnsafeRegister(
            static state => ((LoadItem)state!)._result.TrySetCanceled(), this);

        // Незабранный сбой: итог ждут не всегда — перемену на диске не ждёт никто, — а исключение
        // уже приписано модулю задачей студии, и «забытая задача» сказала бы о нём второй раз.
        _result.Task.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Чья загрузка.</summary>
    public ProjectsSession Session { get; }

    /// <summary>Самая определённая из просьб, склеенных в загрузку.</summary>
    public ProjectsLoadReason Reason { get; private set; }

    /// <summary>Файлы, перемены в которых её поставили, в порядке прихода.</summary>
    public ImmutableArray<CanonicalPath> Causes => [.. _causes];

    /// <summary>Сколько вызывающих её ждут.</summary>
    public int Waiters { get; set; }

    /// <summary>Её ждёт сама служба — перемену на диске отменить некому.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Итог.</summary>
    public Task<WorkspaceLoadResult> Result => _result.Task;

    /// <summary>Отказались все, кто ждал.</summary>
    public CancellationToken Abandoned => _abandoned.Token;

    /// <summary>Присоединяет просьбу: имя загрузке даёт самая определённая.</summary>
    /// <param name="reason">Причина присоединившейся просьбы.</param>
    public void Strengthen(ProjectsLoadReason reason)
    {
        if (Rank(reason) > Rank(Reason))
            Reason = reason;
    }

    /// <summary>Добавляет причины, не повторяя уже названных.</summary>
    /// <param name="causes">Файлы.</param>
    public void AddCauses(ImmutableArray<CanonicalPath> causes)
    {
        if (causes.IsDefaultOrEmpty)
            return;

        foreach (var cause in causes)
        {
            if (_seen.Add(cause))
                _causes.Add(cause);
        }
    }

    /// <summary>Загрузку ждёт служба: уход вызывающих её не отменит.</summary>
    public void Pin() => IsPinned = true;

    /// <summary>Ещё один ждущий. Под замком службы.</summary>
    public void Join() => Waiters++;

    /// <summary>
    /// Итог для ждущего.
    /// </summary>
    /// <param name="cancellationToken">Его отмена.</param>
    /// <param name="leave">Что сделать, когда он перестанет ждать.</param>
    /// <returns>Итог; отменённое ожидание отменяет только задачу этого ждущего.</returns>
    /// <remarks>
    /// Зовётся вне замка службы, и это условие, а не удобство: токен, отменённый раньше, бросит
    /// прямо здесь, и уход разберётся на месте — под замком этот разбор вошёл бы в него повторно.
    /// </remarks>
    public Task<WorkspaceLoadResult> Wait(CancellationToken cancellationToken, Action<LoadItem> leave) =>
        cancellationToken.CanBeCanceled ? WaitAsync(cancellationToken, leave) : Result;

    /// <summary>Отказ от загрузки: начатая остановится, ждущие узнают сразу.</summary>
    /// <remarks>Зовётся вне замка службы: отмена синхронно зовёт обработчики движка.</remarks>
    public void Abandon()
    {
        _abandoned.Cancel();
        Cancel();
    }

    /// <summary>Загрузка кончилась итогом.</summary>
    /// <param name="result">Итог.</param>
    public void Complete(WorkspaceLoadResult result)
    {
        _result.TrySetResult(result);
        _ending.Dispose();
    }

    /// <summary>Загрузка отменена.</summary>
    public void Cancel()
    {
        _result.TrySetCanceled();
        _ending.Dispose();
    }

    /// <summary>Загрузка упала ошибкой самой службы.</summary>
    /// <param name="error">Ошибка.</param>
    public void Fail(Exception error)
    {
        _result.TrySetException(error);
        _ending.Dispose();
    }

    private async Task<WorkspaceLoadResult> WaitAsync(CancellationToken cancellationToken, Action<LoadItem> leave)
    {
        try
        {
            return await Result.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !Result.IsCompleted)
        {
            leave(this);
            throw;
        }
    }

    private static int Rank(ProjectsLoadReason reason) => reason switch
    {
        ProjectsLoadReason.Open => 3,
        ProjectsLoadReason.Configuration => 2,
        ProjectsLoadReason.Reload => 1,
        _ => 0,
    };
}
