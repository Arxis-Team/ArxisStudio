using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using Avalonia.Controls;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Сбор вкладов глазами одного расширения: собранное записывается на него и уходит вместе с ним.
/// </summary>
/// <remarks>
/// Реестр общий, собирающий свой — по образцу экспортов: набор обязан знать, кто его собрал, иначе
/// его не снять, когда собравший уйдёт, и вклады в него держали бы чужие контексты загрузки.
/// </remarks>
/// <param name="registry">Общий реестр вкладов.</param>
/// <param name="pluginId">Чьими глазами смотрим.</param>
public sealed class PluginContributionFacade(PluginContributionRegistry registry, string pluginId) : IStudioContributions
{
    /// <inheritdoc/>
    public IStudioContributionSet<T> Collect<T>() where T : StudioContribution => registry.Collect<T>(pluginId);

    /// <inheritdoc/>
    public IStudioGuarded<T> Guard<T>(T foreign) where T : class => registry.Guard(foreign);
}

/// <summary>Поднятое расширение, как его помнит реестр вкладов.</summary>
/// <param name="Id">Идентификатор.</param>
/// <param name="Name">Как его звать в сообщениях.</param>
/// <param name="Assemblies">Его сборки.</param>
/// <param name="Studio">Его контекст; null — расширение, поднятое без студии.</param>
/// <param name="IsBuiltIn">Встроенный модуль.</param>
/// <param name="LoadContext">Его выгружаемый контекст загрузки; null — у модуля.</param>
internal sealed record RaisedExtension(
    string Id,
    string Name,
    IReadOnlyList<Assembly> Assemblies,
    IStudioContext? Studio,
    bool IsBuiltIn,
    AssemblyLoadContext? LoadContext);

/// <summary>Набор вкладов, каким его видит реестр: без типа вклада.</summary>
internal abstract class ContributionSet(string collectorId, string? contractOwner)
{
    /// <summary>Кто собирает.</summary>
    public string CollectorId => collectorId;

    /// <summary>Хозяин контракта, в который собирают; null — тип не из контрактной сборки модуля.</summary>
    public string? ContractOwner => contractOwner;

    /// <summary>Вступает поднятое расширение: его вклады встают в набор.</summary>
    public abstract void Join(RaisedExtension raised, bool announce);

    /// <summary>Расширение уходит: его вклады отпускаются и покидают набор.</summary>
    public abstract void Leave(string ownerId);

    /// <summary>Уходит сам собирающий: отпускается всё.</summary>
    public abstract void Close();
}

/// <summary>Живой набор вкладов одного типа.</summary>
/// <typeparam name="T">Тип вклада.</typeparam>
internal sealed class ContributionSet<T>(PluginContributionRegistry registry, string collectorId, string? contractOwner)
    : ContributionSet(collectorId, contractOwner), IStudioContributionSet<T>
    where T : StudioContribution
{
    private IReadOnlyList<ContributionHandle<T>> _items = [];
    private EventHandler? _changed;

    /// <summary>Пустой набор, который не меняется никогда: так отвечают на тип, набором не годный.</summary>
    public static ContributionSet<T> Empty(PluginContributionRegistry registry, string collectorId) =>
        new(registry, collectorId, null);

    /// <inheritdoc/>
    public IReadOnlyList<IStudioContribution<T>> Items => _items;

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Модули стоят раньше плагинов при любом порядке подъёма: так равный по точности вклад плагина
    /// отличим от студийного, а модуль, поднятый тестом после плагина, не встаёт за ним.
    /// </remarks>
    public override void Join(RaisedExtension raised, bool announce)
    {
        var joined = registry.Build<T>(raised);

        if (joined.Count == 0)
            return;

        _items =
        [
            .. _items.Where(item => item.IsBuiltIn),
            .. joined.Where(item => item.IsBuiltIn),
            .. _items.Where(item => !item.IsBuiltIn),
            .. joined.Where(item => !item.IsBuiltIn),
        ];

        if (announce)
            _changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Набор меняется раньше, чем вклады отпускаются: обработчик <c>Released</c> читает
    /// <see cref="Items"/> и уходящих там уже не находит. <see cref="Changed"/> — последним, когда
    /// отпущено всё.
    /// </remarks>
    public override void Leave(string ownerId)
    {
        var leaving = _items.Where(item => PluginIds.Same(item.OwnerId, ownerId)).ToList();

        if (leaving.Count == 0)
            return;

        _items = [.. _items.Except(leaving)];

        foreach (var item in leaving)
            item.Let(farewell: true);

        _changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public override void Close()
    {
        var all = _items;

        _items = [];
        _changed = null;

        foreach (var item in all)
            item.Let(farewell: true);
    }
}

/// <summary>Ручка, которую реестр отпускает с выгрузкой хозяина, — без типа объекта.</summary>
internal interface IReleasable
{
    /// <summary>Идентификатор хозяина.</summary>
    string OwnerId { get; }

    /// <summary>Отпускает объект и говорит об этом подписчикам.</summary>
    void Let();
}

/// <summary>Ручка к чужому объекту: вызовы через шов на счёт хозяина, отпускание с его выгрузкой.</summary>
/// <typeparam name="T">Тип объекта.</typeparam>
internal class GuardedHandle<T>(PluginContributionRegistry registry, string ownerId, T target)
    : IStudioGuarded<T>, IReleasable
    where T : class
{
    private T? _value = target;
    private EventHandler? _released;

    /// <summary>Реестр, через чей шов идут вызовы.</summary>
    protected PluginContributionRegistry Registry => registry;

    /// <inheritdoc/>
    public string OwnerId => ownerId;

    /// <inheritdoc/>
    public bool IsReleased => Volatile.Read(ref _value) is null;

    /// <inheritdoc/>
    public event EventHandler? Released
    {
        add => _released += value;
        remove => _released -= value;
    }

    /// <summary>Объект, пока ручка держит его; null — отпущен.</summary>
    protected T? Value => Volatile.Read(ref _value);

    /// <inheritdoc/>
    /// <remarks>Объект студии — без хозяина — зовётся напрямую: студия не встаёт между собой и собой.</remarks>
    public bool Run(string what, Action<T> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (Value is not { } held)
            return false;

        if (ownerId.Length == 0)
        {
            call(held);
            return true;
        }

        return registry.Seam.Run(ownerId, what, () => call(held));
    }

    /// <inheritdoc/>
    public bool TryGet<TResult>(string what, Func<T, TResult> call, out TResult? result)
    {
        ArgumentNullException.ThrowIfNull(call);

        TResult? got = default;
        var passed = Run(what, held => got = call(held));

        result = passed ? got : default;

        return passed;
    }

    /// <inheritdoc/>
    public async Task<bool> RunAsync(string what, Func<T, Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (Value is not { } held)
            return false;

        if (ownerId.Length == 0)
        {
            await call(held);
            return true;
        }

        return await registry.Seam.RunAsync(ownerId, what, () => call(held));
    }

    /// <inheritdoc/>
    public bool Farewell(string what, Action<T> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (Value is not { } held)
            return false;

        if (ownerId.Length == 0)
        {
            call(held);
            return true;
        }

        return registry.Seam.Farewell(ownerId, what, () => call(held));
    }

    /// <inheritdoc/>
    public IStudioGuarded<TOther> Guard<TOther>(TOther value) where TOther : class
    {
        ArgumentNullException.ThrowIfNull(value);

        return registry.Adopt(ownerId, value);
    }

    /// <inheritdoc/>
    public Control Host(Control view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (ownerId.Length == 0)
            return view;

        var what = $"раскладка {view.GetType().Name}";

        return new PluginSurface(view, error => registry.Seam.Report(ownerId, what, error)) { IsCompact = true };
    }

    /// <summary>
    /// Отпускает объект и говорит об этом подписчикам — раньше выгрузки хозяина.
    /// </summary>
    /// <remarks>
    /// Подписчики забываются: это обработчики собирающего, и ручка, пережившая хозяина в чьём-то поле, не
    /// должна держать ни их, ни объект.
    /// </remarks>
    public void Let()
    {
        if (Interlocked.Exchange(ref _value, null) is null)
            return;

        Forget();

        var released = _released;

        _released = null;
        released?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Отпускает то, что держит наследник, вместе с объектом.</summary>
    protected virtual void Forget()
    {
    }
}

/// <summary>Вклад в контракт: экземпляр класса расширения за ручкой шва.</summary>
/// <typeparam name="T">Тип вклада.</typeparam>
internal sealed class ContributionHandle<T>(
    PluginContributionRegistry registry, string ownerId, bool builtIn, T contribution)
    : GuardedHandle<T>(registry, ownerId, contribution), IStudioContribution<T>
    where T : StudioContribution
{
    // Тип держится отдельно ради атрибутов: читать их, не зовя код вклада. Отпускается вместе с ним —
    // тип плагина держал бы его контекст загрузки так же, как экземпляр.
    private Type? _type = contribution.GetType();

    /// <inheritdoc/>
    public bool IsBuiltIn => builtIn;

    /// <inheritdoc/>
    /// <remarks>
    /// Атрибут — тоже код: его конструктор исполняется при чтении, и класс атрибута вправе прийти из сборки
    /// расширения. Поэтому чтение — через шов, а упавшее отвечает пустотой.
    /// </remarks>
    public IReadOnlyList<TAttribute> Attributes<TAttribute>() where TAttribute : Attribute
    {
        if (Volatile.Read(ref _type) is not { } type)
            return [];

        return TryGet($"атрибуты {type.Name}", _ => (IReadOnlyList<TAttribute>)[.. type.GetCustomAttributes<TAttribute>(inherit: true)], out var found)
            ? found ?? []
            : [];
    }

    /// <summary>Отпускает вклад; <paramref name="farewell"/> — сперва попрощавшись с ним.</summary>
    /// <param name="farewell">Позвать <see cref="StudioContribution.Release"/> прощальной дорогой шва.</param>
    public void Let(bool farewell)
    {
        if (farewell && Value is { } leaving)
            Farewell($"прощание с {leaving.GetType().Name}", item => item.Release());

        Let();
    }

    /// <inheritdoc/>
    protected override void Forget() => Volatile.Write(ref _type, null);
}
