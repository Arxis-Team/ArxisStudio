using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Inspector;

/// <summary>
/// Служба показа: стопка целей, последняя живая показана, замок держит показанную.
/// </summary>
/// <remarks>
/// <para>
/// Цель приходит из чужого кода — дизайнера, плагина, — и держится ручкой (<see cref="IStudioContributions.Guard{T}"/>):
/// её код зовётся через шов на счёт хозяина, а с выгрузкой хозяина ручка отпускает её, и цель уходит из стопки
/// раньше, чем выгрузят её сборку. Закрытую цель (<see cref="InspectorTarget.IsClosed"/>) стопка снимает сама.
/// </para>
/// <para>
/// Под замком новые цели встают в стопку, но показана остаётся прежняя; снятый замок показывает последнюю живую.
/// Если закреплённую закрыли, показанной нет вовсе — панель говорит, что закреплённое закрыто, и предлагает
/// снять замок.
/// </para>
/// </remarks>
internal sealed class StudioInspector : IStudioInspector
{
    private readonly IStudioContributions? _contributions;
    private readonly IStudioStrings? _strings;
    private readonly List<Shown> _shown = [];
    private Shown? _current;
    private bool _locked;

    /// <summary>Заводит службу.</summary>
    /// <param name="contributions">Ручки к чужим объектам; пусто — цели зовутся напрямую.</param>
    /// <param name="catalog">Каталог вкладов — его берёт панель: панель строит студия, и модуля она не видит.</param>
    /// <param name="strings">Словарь модуля — для целей-объектов; пусто — без пояснений.</param>
    public StudioInspector(IStudioContributions? contributions, InspectorCatalog? catalog = null, IStudioStrings? strings = null)
    {
        _contributions = contributions;
        _strings = strings;
        Catalog = catalog;
    }

    /// <summary>Каталог вкладов.</summary>
    internal InspectorCatalog? Catalog { get; }

    /// <inheritdoc/>
    public InspectorTarget? Current => _current?.Target;

    /// <inheritdoc/>
    public bool IsLocked => _locked;

    /// <inheritdoc/>
    public event EventHandler? CurrentChanged;

    /// <summary>Ручка к показанной цели: через неё панель зовёт её код.</summary>
    internal IStudioGuarded<InspectorTarget>? CurrentGuard => _current?.Guard;

    /// <summary>Сколько целей в стопке — с теми, что ждут под замком.</summary>
    internal int Count => _shown.Count;

    /// <inheritdoc/>
    public IDisposable Show(InspectorTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsClosed)
            return Nothing.Instance;

        var guard = _contributions?.Guard(target) ?? new DirectGuard<InspectorTarget>(target);
        var shown = new Shown(this, target, guard);

        _shown.Add(shown);

        if (!_locked)
            Pick();

        return shown;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Объект держится ручкой его хозяина (<see cref="IStudioContributions.Guard{T}"/>): его код зовётся на счёт
    /// хозяина, а с его выгрузкой цель закрывается сама. Отпущенная ручка показа закрывает цель и отписывается от
    /// объекта.
    /// </remarks>
    public IDisposable Inspect(object instance, string title)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(title);

        var guard = _contributions?.Guard(instance) ?? new DirectGuard<object>(instance);
        var target = new ObjectTarget(guard, title, _strings);

        return new Inspected(Show(target), target);
    }

    /// <summary>Ставит или снимает замок.</summary>
    /// <param name="locked">Закрепить ли показанную.</param>
    internal void Lock(bool locked)
    {
        if (_locked == locked)
            return;

        _locked = locked;

        if (locked || !Pick())
            CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Снимает цель со стопки: ручку отпустили, цель закрыли или выгрузили её хозяина.</summary>
    private void Drop(Shown shown)
    {
        if (!_shown.Remove(shown))
            return;

        shown.Detach();

        if (!ReferenceEquals(_current, shown))
            return;

        if (_locked)
        {
            // Закреплённое закрыто: показанной нет, пока не снимут замок.
            _current = null;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Pick();
    }

    /// <summary>Показывает последнюю живую цель.</summary>
    /// <returns>Сменилась ли показанная.</returns>
    private bool Pick()
    {
        var next = _shown.Count > 0 ? _shown[^1] : null;

        if (ReferenceEquals(next, _current))
            return false;

        _current = next;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Цель в стопке: её ручка, подписки и снятие.</summary>
    private sealed class Shown : IDisposable
    {
        private readonly StudioInspector _owner;

        public Shown(StudioInspector owner, InspectorTarget target, IStudioGuarded<InspectorTarget> guard)
        {
            _owner = owner;
            Target = target;
            Guard = guard;
            guard.Released += OnReleased;
            target.Changed += OnChanged;
        }

        public InspectorTarget Target { get; }

        public IStudioGuarded<InspectorTarget> Guard { get; }

        public void Dispose() => _owner.Drop(this);

        public void Detach()
        {
            Guard.Released -= OnReleased;
            Target.Changed -= OnChanged;
        }

        private void OnReleased(object? sender, EventArgs e) => _owner.Drop(this);

        private void OnChanged(object? sender, InspectorChangedEventArgs e)
        {
            if (e.Changes.HasFlag(InspectorChanges.Closed))
                _owner.Drop(this);
        }
    }

    /// <summary>Ручка без шва: студия не дала ручек — объект зовётся напрямую, а упавший вызов просто не прошёл.</summary>
    internal sealed class DirectGuard<T>(T value) : IStudioGuarded<T>
        where T : class
    {
        public string OwnerId => string.Empty;

        public bool IsReleased => false;

        public event EventHandler? Released
        {
            add { }
            remove { }
        }

        public bool Run(string what, Action<T> call)
        {
            try
            {
                call(value);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }

        public bool TryGet<TResult>(string what, Func<T, TResult> call, out TResult? result)
        {
            try
            {
                result = call(value);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                result = default;
                return false;
            }
        }

        public async Task<bool> RunAsync(string what, Func<T, Task> call)
        {
            try
            {
                await call(value).ConfigureAwait(true);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }

        public bool Farewell(string what, Action<T> call) => Run(what, call);

        public IStudioGuarded<TOther> Guard<TOther>(TOther other)
            where TOther : class =>
            new DirectGuard<TOther>(other);

        public Avalonia.Controls.Control Host(Avalonia.Controls.Control view) => view;
    }

    /// <summary>Ручка, которой нечего снимать: закрытую цель не показывают.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    /// <summary>Ручка показанного объекта: снимает цель и закрывает её — подписка на объект уходит вместе с ней.</summary>
    private sealed class Inspected(IDisposable shown, ObjectTarget target) : IDisposable
    {
        public void Dispose()
        {
            shown.Dispose();
            target.Shut();
        }
    }
}
