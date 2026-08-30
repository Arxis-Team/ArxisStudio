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
        var along = side is DockSide.Top or DockSide.Bottom
            ? DockOrientation.Vertical
            : DockOrientation.Horizontal;
        var first = side is DockSide.Left or DockSide.Top;

        // Если группа уже стоит в делении того же направления — встаём ей
        // соседом, а не заворачиваем её в новое деление внутри старого. Три
        // области в ряд — это один узел с тремя детьми: так тянется любая
        // граница, а не только соседняя, и так же устроено дерево у Unity.
        return Sibling(root, groupId, fresh, along, first)
            ?? Rewrite(root, groupId, group => new DockSplit
            {
                Orientation = along,
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
    /// <param name="keep">Группы, которые остаются, даже опустев.</param>
    public static DockNode Remove(DockNode root, string item, IReadOnlySet<string>? keep = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Prune(Filter(root, id => !string.Equals(id, item, StringComparison.Ordinal)), keep);
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
    /// <param name="keep">Группы, которые остаются, даже опустев.</param>
    public static DockNode Keep(DockNode root, IReadOnlySet<string> known, IReadOnlySet<string>? keep = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(known);

        return Prune(Filter(root, id => known.Contains(id)), keep);
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
    /// <param name="keep">
    /// Группы, которые остаются, даже опустев, — например та, куда открываются
    /// документы: пустое место под них человек видит и узнаёт, а исчезни оно —
    /// следующий документ появился бы неизвестно где.
    /// </param>
    public static DockNode Prune(DockNode root, IReadOnlySet<string>? keep = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root is not DockSplit split)
            return root;

        // Место ребёнка помнится вместе с ним: прибирание подменяет сам узел
        // (деление свернулось в группу), и по ссылке прежнюю долю уже не найти.
        // Брать же доли по порядку уцелевших нельзя — тогда при уходе среднего
        // ребёнка последний тихо унаследовал бы чужой размер.
        var shares = Shares(split);
        var kept = split.Children
            .Select((child, at) => (Node: Prune(child, keep), At: at))
            .Where(child => child.Node is not DockGroup { Items.Count: 0 } empty
                || keep?.Contains(empty.Id) == true)
            .ToList();

        if (kept.Count == 0)
            return new DockGroup { Id = split.Children.OfType<DockGroup>().FirstOrDefault()?.Id ?? "root" };

        if (kept.Count == 1)
            return kept[0].Node;

        return new DockSplit
        {
            Orientation = split.Orientation,
            Children = [.. kept.Select(child => child.Node)],
            Weights = Normalize([.. kept.Select(child => shares[child.At])]),
        };
    }

    /// <summary>
    /// Меняет доли деления, найденного по пути от корня.
    /// </summary>
    /// <param name="root">Корень дерева.</param>
    /// <param name="path">Номера детей сверху вниз; пустой путь — сам корень.</param>
    /// <param name="weights">Новые доли, по одной на ребёнка.</param>
    /// <returns>Новое дерево; прежнее, если по пути деления нет.</returns>
    /// <remarks>
    /// Долей должно быть ровно столько же, сколько детей, и меньшее число не
    /// дополняется. Список другой длины означает, что мерили уже не то дерево:
    /// принять его значило бы молча переставить границы, которых человек не
    /// трогал, — а оставить прежние доли всего лишь не двинет ту, что он потянул.
    /// </remarks>
    public static DockNode Resize(DockNode root, IReadOnlyList<int> path, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(weights);

        if (root is not DockSplit split)
            return root;

        if (path.Count == 0)
        {
            return weights.Count == split.Children.Count
                ? new DockSplit
                {
                    Orientation = split.Orientation,
                    Children = split.Children,
                    Weights = Normalize(weights),
                }
                : root;
        }

        var at = path[0];

        if (at < 0 || at >= split.Children.Count)
            return root;

        var child = Resize(split.Children[at], [.. path.Skip(1)], weights);

        return ReferenceEquals(child, split.Children[at])
            ? root
            : new DockSplit
            {
                Orientation = split.Orientation,
                Children = [.. split.Children.Select((node, number) => number == at ? child : node)],
                Weights = split.Weights,
            };
    }

    /// <summary>
    /// Доли деления по числу детей: чего нет — поровну, сумма приводится к единице.
    /// </summary>
    /// <param name="split">Деление.</param>
    /// <remarks>
    /// Долей может быть меньше, чем детей: файл раскладки правят руками, а
    /// прежние версии формата могли считать иначе. Оставлять такое дереву
    /// нельзя — ребёнок без доли встал бы шириной в ноль.
    /// </remarks>
    public static IReadOnlyList<double> Shares(DockSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);

        return Normalize(
            [.. Enumerable.Range(0, split.Children.Count)
                .Select(at => at < split.Weights.Count ? split.Weights[at] : 1d / split.Children.Count)]);
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

    /// <summary>
    /// Ставит новую группу соседом той, что уже стоит в делении нужного направления.
    /// </summary>
    /// <returns>Новое дерево; null — подходящего деления по дороге не нашлось.</returns>
    /// <remarks>
    /// Место новичку отдаёт сосед, а не все поровну: человек делил надвое ту
    /// область, на которую смотрел, и переставлять из-за этого границы на
    /// другом конце окна ему никто не обещал.
    /// </remarks>
    private static DockNode? Sibling(
        DockNode node,
        string groupId,
        DockGroup fresh,
        DockOrientation along,
        bool first)
    {
        if (node is not DockSplit split)
            return null;

        var at = -1;

        for (var number = 0; number < split.Children.Count; number++)
        {
            if (split.Children[number] is DockGroup group
                && string.Equals(group.Id, groupId, StringComparison.Ordinal))
            {
                at = number;
                break;
            }
        }

        if (at >= 0 && split.Orientation == along)
        {
            var shares = Shares(split).ToList();
            var children = split.Children.ToList();
            var half = shares[at] / 2;

            shares[at] = half;
            children.Insert(first ? at : at + 1, fresh);
            shares.Insert(first ? at : at + 1, half);

            return new DockSplit { Orientation = along, Children = children, Weights = Normalize(shares) };
        }

        for (var number = 0; number < split.Children.Count; number++)
        {
            if (Sibling(split.Children[number], groupId, fresh, along, first) is not { } grown)
                continue;

            var children = split.Children.ToList();
            children[number] = grown;

            return new DockSplit
            {
                Orientation = split.Orientation,
                Children = children,
                Weights = split.Weights,
            };
        }

        return null;
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
