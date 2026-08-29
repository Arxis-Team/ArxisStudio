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

    /// <summary>Опубликованная реализация типа; null — никто не публиковал.</summary>
    /// <param name="contract">Контрактный тип.</param>
    public object? Get(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return _exports.TryGetValue(contract, out var export) ? export.Implementation : null;
    }

    /// <summary>Снимает все публикации плагина — при его выгрузке.</summary>
    /// <param name="pluginId">Чьи публикации снять.</param>
    public void RemoveOwnedBy(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        foreach (var contract in _exports
                     .Where(pair => string.Equals(pair.Value.OwnerId, pluginId, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _exports.Remove(contract);
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
public sealed class PluginExports(StudioExportRegistry registry, InstalledPlugin owner) : IStudioExports
{
    /// <inheritdoc/>
    public bool Publish<T>(T implementation) where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);

        return registry.Publish(typeof(T), implementation, owner.Id, owner.DisplayName);
    }

    /// <inheritdoc/>
    public T? Get<T>() where T : class => registry.Get(typeof(T)) as T;
}
