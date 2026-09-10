using Avalonia.Threading;

namespace ArxisStudio.Modules.Projects.Delivery;

/// <summary>
/// Поток интерфейса глазами службы: проверить, отдать дело, начать работу.
/// </summary>
/// <remarks>
/// Шов, а не прямой вызов диспетчера, по одной причине: тесту нужен поток, который он прокачивает
/// сам и простоя которого может дождаться, а не приложение Avalonia. Порядок у обоих один —
/// отданное раньше выполняется раньше.
/// </remarks>
internal interface IProjectsThread
{
    /// <summary>Идёт ли вызов в этом потоке.</summary>
    bool CheckAccess();

    /// <summary>Отдаёт дело в поток, не дожидаясь его.</summary>
    /// <param name="action">Дело.</param>
    void Post(Action action);

    /// <summary>
    /// Начинает асинхронную работу в потоке и отдаёт её итог.
    /// </summary>
    /// <param name="work">Работа.</param>
    /// <remarks>
    /// Нужна задачам студии: их список не потокобезопасен, и начинаться задача обязана в потоке
    /// интерфейса — там же, где её потом снимут.
    /// </remarks>
    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>Поток интерфейса студии.</summary>
internal sealed class AvaloniaProjectsThread : IProjectsThread
{
    /// <summary>Единственный экземпляр: поток интерфейса у приложения один.</summary>
    public static AvaloniaProjectsThread Instance { get; } = new();

    /// <inheritdoc/>
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc/>
    /// <remarks>
    /// Фоновым приоритетом, как перестроение консоли: доставка не встаёт впереди ввода и
    /// отрисовки — человек, тянущий границу панели, важнее дерева, узнающего о перезагрузке.
    /// </remarks>
    public void Post(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);

    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => Dispatcher.UIThread.InvokeAsync(work);
}
