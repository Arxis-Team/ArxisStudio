using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace ArxisStudio.Modules.Projects.Delivery;

/// <summary>
/// Подписчики одного события службы.
/// </summary>
/// <typeparam name="THandler">Вид обработчика события.</typeparam>
/// <remarks>
/// <para>
/// <b>Забытая подписка.</b> Выгрузка контекста плагина снимает его обработчики сама: иначе делегат
/// держал бы выгруженную сборку в памяти навсегда, а доставка звала бы код мертвеца. Это
/// страховка — отписываться плагин обязан в <c>Deactivate</c>.
/// </para>
/// <para>
/// <b>Сбой подписчика.</b> Упавший обработчик соседям не мешает: исключение уходит хозяину списка,
/// а остальные своё событие получают.
/// </para>
/// <para>
/// Список неизменяемый и забирается целиком: подписка изнутри обработчика идущей доставке не
/// мешает и соседей не теряет.
/// </para>
/// </remarks>
internal sealed class Subscribers<THandler>
    where THandler : Delegate
{
    private readonly Lock _gate = new();
    private readonly HashSet<AssemblyLoadContext> _contexts = [];
    private ImmutableArray<THandler> _handlers = [];

    /// <summary>Подписчики сменились: пришёл, ушёл или снят с выгрузкой своего контекста.</summary>
    /// <remarks>Зовётся вне замка — в потоке того, кто подписался, отписался или выгружает.</remarks>
    public event Action? Changed;

    /// <summary>Подписчики сейчас.</summary>
    public ImmutableArray<THandler> Current
    {
        get
        {
            lock (_gate)
                return _handlers;
        }
    }

    /// <summary>Добавляет подписчика.</summary>
    /// <param name="handler">Обработчик; null ничего не делает, как у обычного события.</param>
    public void Add(THandler? handler)
    {
        if (handler is null)
            return;

        lock (_gate)
        {
            _handlers = _handlers.Add(handler);

            foreach (var context in Contexts(handler))
            {
                if (_contexts.Add(context))
                    context.Unloading += OnUnloading;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Снимает подписчика — последнюю из равных ему подписок, как у обычного события.</summary>
    /// <param name="handler">Обработчик.</param>
    public void Remove(THandler? handler)
    {
        if (handler is null)
            return;

        lock (_gate)
        {
            var index = _handlers.LastIndexOf(handler);

            if (index < 0)
                return;

            _handlers = _handlers.RemoveAt(index);
        }

        Changed?.Invoke();
    }

    /// <summary>Зовёт подписчиков по порядку; упавший не мешает следующим.</summary>
    /// <param name="call">Как позвать одного: с отправителем и событием.</param>
    /// <param name="failed">Куда девать исключение подписчика.</param>
    public void Invoke(Action<THandler> call, Action<Exception> failed)
    {
        foreach (var handler in Current)
        {
            try
            {
                call(handler);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed(e);
            }
        }
    }

    private void OnUnloading(AssemblyLoadContext context)
    {
        bool removed;

        lock (_gate)
        {
            _contexts.Remove(context);

            var before = _handlers.Length;

            _handlers = _handlers.RemoveAll(handler => Contexts(handler).Contains(context));
            removed = _handlers.Length != before;
        }

        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Выгружаемые контексты, чей код держит обработчик.</summary>
    private static HashSet<AssemblyLoadContext> Contexts(THandler handler)
    {
        var found = new HashSet<AssemblyLoadContext>();

        foreach (var single in handler.GetInvocationList())
        {
            // И метод, и объект: лямбда живёт в сборке плагина, а метод общего типа может быть
            // вызван на объекте, созданном плагином, — держит контекст и то и другое.
            if (AssemblyLoadContext.GetLoadContext(single.Method.Module.Assembly) is { IsCollectible: true } method)
                found.Add(method);

            if (single.Target is { } target
                && AssemblyLoadContext.GetLoadContext(target.GetType().Assembly) is { IsCollectible: true } owner)
            {
                found.Add(owner);
            }
        }

        return found;
    }
}

/// <summary>Куда уходит сбой подписчика, если службе не назвали иного.</summary>
internal static class Subscribers
{
    /// <summary>Бросает исключение подписчика заново в потоке интерфейса — отдельной работой.</summary>
    /// <param name="thread">Поток интерфейса.</param>
    /// <remarks>
    /// Стек сохраняется захватом, поэтому студия находит в нём кадр того, кто бросил, и приписывает
    /// сбой ему, а не модулю, в чьём цикле он случился. Отложено, а не брошено на месте: остальные
    /// подписчики своё событие получают.
    /// </remarks>
    public static Action<Exception> Rethrown(IProjectsThread thread) =>
        error => thread.Post(ExceptionDispatchInfo.Capture(error).Throw);
}
