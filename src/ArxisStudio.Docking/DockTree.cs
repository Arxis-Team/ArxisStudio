namespace ArxisStudio.Docking;

/// <summary>Куда встаёт панель относительно группы.</summary>
public enum DockSide
{
    /// <summary>Ещё одной вкладкой в саму группу.</summary>
    Tab,

    /// <summary>Слева от группы.</summary>
    Left,

    /// <summary>Справа от группы.</summary>
    Right,

    /// <summary>Сверху над группой.</summary>
    Top,

    /// <summary>Снизу под группой.</summary>
    Bottom,
}

/// <summary>
/// Правки дерева: каждая возвращает новое дерево, ничего не меняя на месте.
/// </summary>
/// <remarks>
/// Чистые функции выбраны не из любви к неизменяемости, а ради проверяемости:
/// перекладку доков разбирают тестами без окна, без контролов и без потока
/// интерфейса, а значит быстро и по одному случаю за раз. Показ дерева — забота
/// вида, и он подстраивается под готовый результат.
/// </remarks>
public static class DockTree
{
    /// <summary>
    /// Ставит панель к группе с указанной стороны.
    /// </summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="groupId">К какой группе ставим.</param>
    /// <param name="side">С какой стороны.</param>
    /// <param name="item">Идентификатор панели.</param>
    /// <param name="newGroupId">Имя для новой группы, если сторона не <see cref="DockSide.Tab"/>.</param>
    /// <returns>Новое дерево; прежнее, если такой группы нет.</returns>
    public static DockNode Insert(DockNode root, string groupId, DockSide side, string item, string newGroupId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(item);

        if (side == DockSide.Tab)
        {
            return Rewrite(root, groupId, group => new DockGroup
            {
                Id = group.Id,
                Items = [.. group.Items, item],
                Selected = item,
            });
        }

        var fresh = new DockGroup { Id = newGroupId, Items = [item], Selected = item };
        var vertical = side is DockSide.Top or DockSide.Bottom;
        var first = side is DockSide.Left or DockSide.Top;

        return Rewrite(root, groupId, group => new DockSplit
        {
            Orientation = vertical ? DockOrientation.Vertical : DockOrientation.Horizontal,
            Children = first ? [fresh, group] : [group, fresh],
            Weights = [0.5, 0.5],
        });
    }

    /// <summary>
    /// Убирает панель отовсюду, где она встречается.
    /// </summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="item">Идентификатор панели.</param>
    /// <returns>Новое дерево, уже прибранное.</returns>
    public static DockNode Remove(DockNode root, string item)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Prune(Filter(root, id => !string.Equals(id, item, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Оставляет только известные панели.
    /// </summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="known">Какие идентификаторы считать живыми.</param>
    /// <returns>Новое дерево без неизвестных панелей.</returns>
    /// <remarks>
    /// Нужно при чтении сохранённой раскладки: плагин могли удалить, пока студия не
    /// работала. Само удаление из <b>файла</b> при этом не делается — выключенный
    /// плагин обязан вернуться на своё место, когда его включат обратно.
    /// </remarks>
    public static DockNode Keep(DockNode root, IReadOnlySet<string> known)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(known);

        return Prune(Filter(root, id => known.Contains(id)));
    }

    /// <summary>Ищет группу по имени; null — такой нет.</summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="groupId">Имя группы.</param>
    public static DockGroup? Group(DockNode root, string? groupId)
    {
        ArgumentNullException.ThrowIfNull(root);

        return groupId is null
            ? null
            : root.Groups().FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.Ordinal));
    }

    /// <summary>Ищет группу, в которой лежит панель; null — нигде.</summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="item">Идентификатор панели.</param>
    public static DockGroup? Holder(DockNode root, string item)
    {
        ArgumentNullException.ThrowIfNull(root);

        return root.Groups().FirstOrDefault(group => group.Items.Contains(item, StringComparer.Ordinal));
    }

    /// <summary>Делает вкладку выбранной в её группе.</summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="item">Идентификатор панели.</param>
    /// <returns>Новое дерево; прежнее, если панели нигде нет.</returns>
    public static DockNode Select(DockNode root, string item)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (Holder(root, item) is not { } holder)
            return root;

        return Rewrite(root, holder.Id, group => new DockGroup
        {
            Id = group.Id,
            Items = group.Items,
            Selected = item,
        });
    }

    /// <summary>
    /// Прибирает дерево: пустые группы уходят, деление с одним ребёнком заменяется им.
    /// </summary>
    /// <param name="root">Корень дерева.</param>
    /// <returns>Прибранное дерево.</returns>
    /// <remarks>
    /// Единственная группа остаётся, даже опустев: дерево без единого узла показывать
    /// нечем, а пустое место, куда открываются документы, человек видит и узнаёт.
    /// </remarks>
    public static DockNode Prune(DockNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root is not DockSplit split)
            return root;

        // Место ребёнка помнится вместе с ним: прибирание подменяет сам узел
        // (деление свернулось в группу), и по ссылке прежнюю долю уже не найти.
        // Брать же доли по порядку уцелевших нельзя — тогда при уходе среднего
        // ребёнка последний тихо унаследовал бы чужой размер.
        var kept = split.Children
            .Select((child, at) => (Node: Prune(child), At: at))
            .Where(child => child.Node is not DockGroup { Items.Count: 0 })
            .ToList();

        if (kept.Count == 0)
            return new DockGroup { Id = split.Children.OfType<DockGroup>().FirstOrDefault()?.Id ?? "root" };

        if (kept.Count == 1)
            return kept[0].Node;

        return new DockSplit
        {
            Orientation = split.Orientation,
            Children = [.. kept.Select(child => child.Node)],
            Weights = DockTree.Normalize(
                [.. kept.Select(child => child.At < split.Weights.Count ? split.Weights[child.At] : 1d / kept.Count)]),
        };
    }

    /// <summary>Приводит доли к сумме единица.</summary>
    /// <param name="weights">Исходные доли.</param>
    /// <returns>Доли, в сумме дающие единицу.</returns>
    public static IReadOnlyList<double> Normalize(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Count == 0)
            return [];

        var total = weights.Sum(weight => weight > 0 ? weight : 0);

        return total <= 0
            ? [.. weights.Select(_ => 1d / weights.Count)]
            : [.. weights.Select(weight => (weight > 0 ? weight : 0) / total)];
    }

    /// <summary>Заменяет группу с указанным именем на то, что вернёт правка.</summary>
    private static DockNode Rewrite(DockNode node, string groupId, Func<DockGroup, DockNode> change)
    {
        switch (node)
        {
            case DockGroup group when string.Equals(group.Id, groupId, StringComparison.Ordinal):
                return change(group);

            case DockSplit split:
                var children = split.Children.Select(child => Rewrite(child, groupId, change)).ToList();

                return new DockSplit
                {
                    Orientation = split.Orientation,
                    Children = children,
                    Weights = split.Weights,
                };

            default:
                return node;
        }
    }

    /// <summary>Оставляет в группах только те панели, что прошли отбор.</summary>
    private static DockNode Filter(DockNode node, Func<string, bool> keep)
    {
        switch (node)
        {
            case DockGroup group:
                var items = group.Items.Where(keep).ToList();

                return new DockGroup
                {
                    Id = group.Id,
                    Items = items,
                    // Выбранная могла уйти — тогда выбираем первую оставшуюся, а не
                    // держим имя того, чего в группе больше нет.
                    Selected = group.Selected is { } chosen && items.Contains(chosen, StringComparer.Ordinal)
                        ? chosen
                        : items.FirstOrDefault(),
                };

            case DockSplit split:
                return new DockSplit
                {
                    Orientation = split.Orientation,
                    Children = [.. split.Children.Select(child => Filter(child, keep))],
                    Weights = split.Weights,
                };

            default:
                return node;
        }
    }
}
