using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Одна операция в очереди: над чем она, кто её ждёт и чем кончилась.
/// </summary>
/// <remarks>
/// Операции не склеиваются, в отличие от загрузок: две просьбы собрать — это две сборки, и каждая
/// пройдёт полосу в свой черёд. Ждущий у операции один — тот, кто её попросил, — и его отмена
/// останавливает саму операцию, а не только ожидание: собирать для ушедшего незачем.
/// <para>
/// Восстановления при открытии не ждёт никто: его никто и не просил, и отменит его только конец
/// сессии.
/// </para>
/// </remarks>
internal sealed class OperationItem
{
    private readonly TaskCompletionSource<ProjectOperationResult> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _abandoned = new();
    private readonly IProgress<ProjectOperationProgress>? _asked;
    private CancellationTokenRegistration _ending;

    /// <summary>Заводит операцию сессии.</summary>
    /// <param name="session">Чья операция.</param>
    /// <param name="operation">Что делать.</param>
    /// <param name="progress">Куда говорить о ходе просившему; null — некуда.</param>
    public OperationItem(ProjectsSession session, ProjectOperation operation, IProgress<ProjectOperationProgress>? progress)
    {
        Session = session;
        Operation = operation;
        _asked = progress;

        _ending = session.Lifetime.UnsafeRegister(
            static state => ((OperationItem)state!)._result.TrySetCanceled(), this);

        // Незабранный сбой: итога ждут не всегда — восстановления при открытии не ждёт никто, — а
        // исключение уже приписано модулю задачей студии.
        _result.Task.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Чья операция.</summary>
    public ProjectsSession Session { get; }

    /// <summary>Что делают.</summary>
    public ProjectOperation Operation { get; }

    /// <summary>Итог.</summary>
    public Task<ProjectOperationResult> Result => _result.Task;

    /// <summary>Просивший ушёл: операцию пора останавливать.</summary>
    public CancellationToken Abandoned => _abandoned.Token;

    /// <summary>Куда говорить о ходе: и задаче студии, и просившему.</summary>
    /// <param name="task">Задача студии.</param>
    /// <param name="failed">Куда девать сбой чужого приёмника.</param>
    public IProgress<ProjectOperationProgress> Progress(IStudioProgress task, Action<Exception> failed) =>
        new Both(task, _asked, failed);

    /// <summary>
    /// Итог для просившего.
    /// </summary>
    /// <param name="cancellationToken">Его отмена.</param>
    /// <returns>Итог; отменённое ожидание останавливает и саму операцию.</returns>
    /// <remarks>Зовётся вне замка службы: отмена синхронно зовёт обработчики движка.</remarks>
    public Task<ProjectOperationResult> Wait(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? WaitAsync(cancellationToken) : Result;

    /// <summary>Операция кончилась итогом.</summary>
    /// <param name="result">Итог.</param>
    public void Complete(ProjectOperationResult result)
    {
        _result.TrySetResult(result);
        _ending.Dispose();
    }

    /// <summary>Операция отменена.</summary>
    public void Cancel()
    {
        _result.TrySetCanceled();
        _ending.Dispose();
    }

    /// <summary>Операция упала ошибкой самой службы.</summary>
    /// <param name="error">Ошибка.</param>
    public void Fail(Exception error)
    {
        _result.TrySetException(error);
        _ending.Dispose();
    }

    /// <summary>Отказ: идущая операция остановится, стоящая в очереди не начнётся.</summary>
    public void Abandon()
    {
        _abandoned.Cancel();
        Cancel();
    }

    private async Task<ProjectOperationResult> WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Result.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !Result.IsCompleted)
        {
            Abandon();
            throw;
        }
    }

    /// <summary>Ход операции — задаче студии и просившему; упавший приёмник операцию не роняет.</summary>
    private sealed class Both(IStudioProgress task, IProgress<ProjectOperationProgress>? asked, Action<Exception> failed)
        : IProgress<ProjectOperationProgress>
    {
        public void Report(ProjectOperationProgress value)
        {
            task.Report(value.Message);

            if (asked is null)
                return;

            try
            {
                asked.Report(value);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed(e);
            }
        }
    }
}
