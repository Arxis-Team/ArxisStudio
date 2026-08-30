namespace ArxisStudio.Docking;

/// <summary>
/// Живые панели по именам — и по хозяевам.
/// </summary>
/// <remarks>
/// Хозяин для движка — просто строка: чей это он не спрашивает. Но именно из-за
/// неё контрол уходит из памяти вовремя. Раскладка переживает и выключение
/// плагина, и перезапуск студии, поэтому держать контрол в дереве нельзя, а
/// держать его вечно — тем более: сборка, к которой он принадлежит, не
/// выгрузится, пока жив хоть один её объект.
/// <para>
/// Имя панели при этом остаётся в дереве. Выключенный плагин обязан вернуться
/// на своё место, когда его включат обратно, — а место помнит дерево, не этот
/// список.
/// </para>
/// </remarks>
public sealed class DockItems
{
    private readonly Dictionary<string, DockItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    /// <summary>Сколько панелей сейчас живо.</summary>
    public int Count => _items.Count;

    /// <summary>Кладёт панель, вытесняя прежнюю с тем же именем.</summary>
    /// <param name="owner">Чья панель — по нему её потом и снимут.</param>
    /// <param name="item">Сама панель.</param>
    public void Add(string owner, DockItem item)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(item);

        _items[item.Id] = item;
        _owners[item.Id] = owner;
    }

    /// <summary>Ищет панель по имени; null — такой нет.</summary>
    /// <param name="id">Имя панели.</param>
    public DockItem? Find(string? id) =>
        id is not null && _items.TryGetValue(id, out var item) ? item : null;

    /// <summary>Имена всех живых панелей.</summary>
    /// <remarks>
    /// Набор собирается заново на каждый вызов, и звать это в цикле отрисовки
    /// незачем: нужно оно при чтении раскладки, чтобы отсеять панели плагина,
    /// которого больше нет.
    /// </remarks>
    public IReadOnlySet<string> Known() => _items.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Снимает одну панель.</summary>
    /// <param name="id">Имя панели.</param>
    /// <returns>Была ли она здесь.</returns>
    public bool Remove(string id)
    {
        _owners.Remove(id);

        return _items.Remove(id);
    }

    /// <summary>Снимает всё, что положил этот хозяин.</summary>
    /// <param name="owner">Чьи панели убираем.</param>
    /// <returns>Имена снятых панелей — их же надо убрать из дерева.</returns>
    public IReadOnlyList<string> RemoveOwnedBy(string owner)
    {
        var gone = _owners
            .Where(pair => string.Equals(pair.Value, owner, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var id in gone)
        {
            _items.Remove(id);
            _owners.Remove(id);
        }

        return gone;
    }
}
