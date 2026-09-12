using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Delivery;

/// <summary>
/// Доставка событий операций: в поток интерфейса, по порядку, без склейки.
/// </summary>
/// <remarks>
/// <para>
/// <b>Склейки нет, и это не упущение.</b> У начала и конца операции разный смысл: пропущенное
/// начало оставило бы окно сборки показывать законченное, а пропущенный конец — идущее. Операций
/// одновременно всё равно не бывает: полоса движка пропускает их по одной.
/// </para>
/// <para>
/// <b>Порядок с переменами модели.</b> И перемены, и эти события отдаются одному потоку одной
/// очередью, поэтому перезагрузка после восстановления доходит до подписчика раньше, чем конец
/// операции, которая её вызвала.
/// </para>
/// </remarks>
/// <param name="sender">Кто отправитель событий — сама служба.</param>
/// <param name="thread">Поток интерфейса.</param>
/// <param name="first">Своя реакция модуля на конец операции: зовётся раньше подписчиков.</param>
/// <param name="failed">Куда девать исключение подписчика.</param>
internal sealed class OperationPublisher(
    object sender,
    IProjectsThread thread,
    Action<ProjectOperationEventArgs> first,
    Action<Exception> failed)
{
    private readonly Subscribers<ProjectOperationEventArgs> _started = new();
    private readonly Subscribers<ProjectOperationEventArgs> _completed = new();

    /// <summary>Подписывает на начало операций.</summary>
    /// <param name="handler">Обработчик.</param>
    public void SubscribeStarted(EventHandler<ProjectOperationEventArgs>? handler) => _started.Add(handler);

    /// <summary>Снимает подписку на начало операций.</summary>
    /// <param name="handler">Обработчик.</param>
    public void UnsubscribeStarted(EventHandler<ProjectOperationEventArgs>? handler) => _started.Remove(handler);

    /// <summary>Подписывает на конец операций.</summary>
    /// <param name="handler">Обработчик.</param>
    public void SubscribeCompleted(EventHandler<ProjectOperationEventArgs>? handler) => _completed.Add(handler);

    /// <summary>Снимает подписку на конец операций.</summary>
    /// <param name="handler">Обработчик.</param>
    public void UnsubscribeCompleted(EventHandler<ProjectOperationEventArgs>? handler) => _completed.Remove(handler);

    /// <summary>Говорит, что операция началась.</summary>
    /// <param name="operation">Операция.</param>
    public void Start(ProjectOperation operation)
    {
        var change = new ProjectOperationEventArgs(operation);

        thread.Post(() => _started.Invoke(sender, change, failed));
    }

    /// <summary>Говорит, что операция кончилась.</summary>
    /// <param name="operation">Операция.</param>
    /// <param name="result">Итог; null — операцию отменили.</param>
    /// <param name="isCancelled">Операцию отменили.</param>
    public void Complete(ProjectOperation operation, ProjectOperationResult? result, bool isCancelled)
    {
        var change = new ProjectOperationEventArgs(operation, result, isCancelled);

        thread.Post(() =>
        {
            try
            {
                first(change);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed(e);
            }

            _completed.Invoke(sender, change, failed);
        });
    }
}
