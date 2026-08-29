using System.Runtime.Loader;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Реестр опубликованных реализаций: тип — реализация и её хозяин.
/// </summary>
/// <remarks>
/// Хозяин помнится не для учёта: при выгрузке плагина его публикации
/// снимаются — как команды. Оставь мы их, сосед взял бы объект из
/// выгруженного контекста, и тот жил бы мертвецом: получал вызовы, держал
/// типы, не давал контексту умереть.
/// <para>
/// Занятый тип второму поставщику не отдаётся — то же правило, что у
/// рисовальщиков свойств: два хозяина на одно место — это не выбор, а гонка,
/// и выиграл бы тот, кого раньше загрузили. О попытке говорит событие.
/// </para>
/// </remarks>
public sealed class StudioExportRegistry
{
    private readonly Dictionary<Type, Export> _exports = [];

    // Под замком: плагину разрешено — и прямо советовано — брать экспорт
    // заново из фоновой работы, а она идёт не в потоке интерфейса. Чтение,
    // попавшее на чужую перестройку словаря, вернуло бы мусор или закрутилось
    // бы в цепочке корзины.
    private readonly Lock _gate = new();

    /// <summary>Кто-то попытался занять уже занятый тип.</summary>
    public event EventHandler<string>? Conflict;

    /// <summary>
    /// Публикует реализацию от имени плагина.
    /// </summary>
    /// <param name="contract">Контрактный тип.</param>
    /// <param name="implementation">Реализация.</param>
    /// <param name="ownerId">Чья публикация.</param>
    /// <param name="ownerName">Как звать хозяина в сообщениях.</param>
    /// <returns><c>false</c> — тип занят другим плагином.</returns>
    public bool Publish(Type contract, object implementation, string ownerId, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        // Реализация обязана быть тем, за что её выдают: без проверки в
        // ячейку ложится мусор, сосед получает null на попадании в словарь —
        // неотличимо от «никто не публиковал», — а настоящему хозяину потом
        // отказывают, называя виновником самозванца.
        if (!contract.IsInstanceOfType(implementation))
        {
            Conflict?.Invoke(this, $"{ownerName}: {implementation.GetType().Name} не является {contract.Name}");
            return false;
        }

        // Тип обязан жить в общем контексте, то есть в контрактной сборке.
        // Иначе его никто, кроме самого публикующего, не назовёт — сосед
        // спрашивает свой тип и получает null без единого слова, — а сама
        // запись держит контекст плагина и не даёт ему выгрузиться. Чаще
        // всего так выходит нечаянно: Publish(реализация) выводит T из
        // аргумента, и вместо интерфейса публикуется собственный класс.
        if (AssemblyLoadContext.GetLoadContext(contract.Assembly) != AssemblyLoadContext.Default)
        {
            Conflict?.Invoke(
                this,
                $"{ownerName}: {contract.Name} живёт в сборке плагина, а не в контрактной. " +
                "Вынесите интерфейсы в отдельную сборку и объявите её в provides.contracts. " +
                "Саму сборку плагина контрактом не сделать: она обязана выгружаться при " +
                "перезагрузке, а контракт обязан не выгружаться");

            return false;
        }

        lock (_gate)
        {
            if (_exports.TryGetValue(contract, out var taken))
            {
                // Повторная публикация своего же типа — не гонка, а обновление:
                // так поступает плагин, публикующий заново после перезагрузки.
                if (!string.Equals(taken.OwnerId, ownerId, StringComparison.Ordinal))
                {
                    Conflict?.Invoke(this, $"{ownerName}: {contract.Name} уже опубликован плагином {taken.OwnerId}");
                    return false;
                }
            }

            _exports[contract] = new Export(implementation, ownerId);
            return true;
        }
    }

    /// <summary>Опубликованная реализация типа; null — никто не публиковал.</summary>
    /// <param name="contract">Контрактный тип.</param>
    public object? Get(Type contract) => Published(contract)?.Implementation;

    /// <summary>Реализация вместе с именем хозяина; null — никто не публиковал.</summary>
    /// <param name="contract">Контрактный тип.</param>
    /// <remarks>
    /// Хозяин нужен берущему: сосед, чьей версии он не удовлетворён, для него
    /// всё равно что отсутствует — так же, как отвечает <c>IsActive</c>.
    /// </remarks>
    public (object Implementation, string OwnerId)? Published(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        lock (_gate)
        {
            return _exports.TryGetValue(contract, out var export)
                ? (export.Implementation, export.OwnerId)
                : null;
        }
    }

    /// <summary>Снимает все публикации плагина — при его выгрузке.</summary>
    /// <param name="pluginId">Чьи публикации снять.</param>
    public void RemoveOwnedBy(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        lock (_gate)
        {
            foreach (var contract in _exports
                         .Where(pair => string.Equals(pair.Value.OwnerId, pluginId, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _exports.Remove(contract);
            }
        }
    }

    private readonly record struct Export(object Implementation, string OwnerId);
}

/// <summary>
/// Экспорты глазами одного плагина.
/// </summary>
/// <remarks>
/// Реестр общий, публикующий свой — по образцу команд: публикация обязана
/// знать хозяина, иначе её не снять при выгрузке и не назвать в конфликте.
/// </remarks>
/// <param name="registry">Общий реестр.</param>
/// <param name="owner">Чьими глазами смотрим.</param>
/// <param name="neighbours">Служба соседей — ею и меряется, кто для владельца есть.</param>
public sealed class PluginExports(
    StudioExportRegistry registry, InstalledPlugin owner, IStudioPlugins? neighbours = null) : IStudioExports
{
    /// <inheritdoc/>
    public bool Publish<T>(T implementation) where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);

        return registry.Publish(typeof(T), implementation, owner.Id, owner.DisplayName);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ответ согласован со службой соседей: если та говорит, что соседа для
    /// этого владельца нет — а так она отвечает, когда объявленная им нижняя
    /// граница версии не выдержана, — то и экспортов его здесь не видно.
    /// Иначе плагин, написанный под второй версии, получал бы реализацию
    /// первой и падал с MissingMethodException внутри собственного кадра: гвард
    /// записал бы сбой на него, и через три раза студия отключила бы невиновного.
    /// </remarks>
    public T? Get<T>() where T : class
    {
        if (registry.Published(typeof(T)) is not { } published)
            return null;

        // Свои экспорты видны всегда: сам себе владелец зависимости не объявляет.
        if (neighbours is not null &&
            !string.Equals(published.OwnerId, owner.Id, StringComparison.Ordinal) &&
            !neighbours.IsActive(published.OwnerId))
        {
            return null;
        }

        return published.Implementation as T;
    }
}
