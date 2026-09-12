using System.Collections.Immutable;
using System.Runtime.Loader;

namespace ArxisStudio.Modules.Projects.Delivery;

/// <summary>
/// Подписчики одного события службы.
/// </summary>
/// <typeparam name="TArgs">Что событие несёт.</typeparam>
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
internal sealed class Subscribers<TArgs>
    where TArgs : EventArgs
{
    private readonly Lock _gate = new();
    private readonly HashSet<AssemblyLoadContext> _contexts = [];
    private ImmutableArray<EventHandler<TArgs>> _handlers = [];

    /// <summary>Подписчики сейчас.</summary>
    public ImmutableArray<EventHandler<TArgs>> Current
    {
        get
        {
            lock (_gate)
                return _handlers;
        }
    }

    /// <summary>Добавляет подписчика.</summary>
    /// <param name="handler">Обработчик; null ничего не делает, как у обычного события.</param>
    public void Add(EventHandler<TArgs>? handler)
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
    }

    /// <summary>Снимает подписчика — последнюю из равных ему подписок, как у обычного события.</summary>
    /// <param name="handler">Обработчик.</param>
    public void Remove(EventHandler<TArgs>? handler)
    {
        if (handler is null)
            return;

        lock (_gate)
        {
            var index = _handlers.LastIndexOf(handler);

            if (index >= 0)
                _handlers = _handlers.RemoveAt(index);
        }
    }

    /// <summary>Зовёт подписчиков по порядку; упавший не мешает следующим.</summary>
    /// <param name="sender">Отправитель события.</param>
    /// <param name="args">Событие.</param>
    /// <param name="failed">Куда девать исключение подписчика.</param>
    public void Invoke(object sender, TArgs args, Action<Exception> failed)
    {
        foreach (var handler in Current)
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed(e);
            }
        }
    }

    private void OnUnloading(AssemblyLoadContext context)
    {
        lock (_gate)
        {
            _contexts.Remove(context);
            _handlers = _handlers.RemoveAll(handler => Contexts(handler).Contains(context));
        }
    }

    /// <summary>Выгружаемые контексты, чей код держит обработчик.</summary>
    private static HashSet<AssemblyLoadContext> Contexts(EventHandler<TArgs> handler)
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
