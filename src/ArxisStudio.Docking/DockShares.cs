namespace ArxisStudio.Docking;

/// <summary>
/// Доли деления на экране и в дереве: как показать доли, когда не все дети видны, и как вернуть
/// в дерево то, что намерила граница.
/// </summary>
/// <remarks>
/// Чистая арифметика над долями, без сеток и контролов. Вынесена из <see cref="DockView"/>: у вида
/// своя работа — строить экран и слушать руки, — а эти правила проверяются парой и должны
/// читаться вместе.
/// </remarks>
internal static class DockShares
{
    /// <summary>
    /// Раскладывает померенные доли по местам, не трогая спрятанных.
    /// </summary>
    /// <param name="all">Доли всех детей деления.</param>
    /// <param name="visible">Номера тех, кто попал на экран.</param>
    /// <param name="measured">Доли, снятые с полос сетки, — по числу показанных.</param>
    /// <param name="floor">Номер ребёнка, несущего пол, в дереве; -1 — никто.</param>
    /// <returns>Доли всех детей для дерева.</returns>
    /// <remarks>
    /// На экране могли стоять не все дети: у соседа выключили плагин, и его
    /// группа ничего не показывает. Отдать в дерево доли одних лишь видимых
    /// значило бы отобрать место у спрятанного — и панель, вернувшись, встала
    /// бы шириной в ноль. Поэтому видимые делят между собой ровно то место,
    /// которое им и принадлежало.
    /// </remarks>
    public static IReadOnlyList<double> Spread(
        IReadOnlyList<double> all,
        IReadOnlyList<int> visible,
        IReadOnlyList<double> measured,
        int floor)
    {
        if (measured.Count != visible.Count)
            return all;

        var next = all.ToList();
        var mine = Seat(visible, floor);

        // Некому было отдавать место — значит и снимать нечего: видимые делят
        // между собой ровно то, что им принадлежало.
        if (mine < 0)
        {
            var room = visible.Sum(at => all[at]);

            for (var number = 0; number < visible.Count; number++)
                next[visible[number]] = measured[number] * room;

            return DockTree.Normalize(next);
        }

        // Пол показан шире своей доли ровно на то, что причитается
        // отсутствующим. Снимаем добавку — иначе первое же перетаскивание
        // съело бы их доли, и панель, вернувшись, встала бы шириной в ноль.
        var slack = 1 - visible.Sum(at => all[at]);

        // Уже того места, что пол держит за отсутствующих, его не утянуть:
        // такому экрану нет соответствия в дереве. Доля пола вышла бы
        // отрицательной, Normalize молча обратила бы её в ноль — и стоило
        // вернуть скрытую панель, как область документов раскладывалась бы
        // шириной в ноль. Отказываем: граница отскакивает к пределу, и человек
        // видит, что дальше некуда, — так же ведёт себя и минимальный размер.
        if (measured[mine] <= slack)
            return all;

        for (var number = 0; number < visible.Count; number++)
            next[visible[number]] = measured[number];

        next[visible[mine]] = measured[mine] - slack;

        return DockTree.Normalize(next);
    }

    /// <summary>
    /// Доли для показа: пол рабочей области берёт место отсутствующих.
    /// </summary>
    /// <param name="all">Доли всех детей деления.</param>
    /// <param name="visible">Номера тех, кто попал на экран.</param>
    /// <param name="floor">Номер ребёнка, несущего пол, в дереве; -1 — никто.</param>
    /// <returns>Доли по числу показанных, в сумме единица.</returns>
    /// <remarks>
    /// Обратна <see cref="Spread"/>, и это не совпадение: что показали, то
    /// перетаскивание и обязано вернуть в дерево. Проверяется парой напрямую —
    /// тянем границу и сверяем записанное с показанным.
    /// </remarks>
    public static IReadOnlyList<double> Shown(
        IReadOnlyList<double> all,
        IReadOnlyList<int> visible,
        int floor)
    {
        var room = visible.Select(at => all[at]).ToList();
        var mine = Seat(visible, floor);

        if (mine < 0)
            return DockTree.Normalize(room);

        room[mine] += 1 - room.Sum();

        return DockTree.Normalize(room);
    }

    /// <summary>
    /// Какое место среди показанных занимает названный ребёнок дерева.
    /// </summary>
    /// <param name="visible">Номера показанных детей, по порядку на экране.</param>
    /// <param name="floor">Номер ребёнка в дереве; -1 — никакой.</param>
    /// <returns>Место в списке показанных; -1 — его там нет.</returns>
    private static int Seat(IReadOnlyList<int> visible, int floor)
    {
        if (floor < 0)
            return -1;

        for (var number = 0; number < visible.Count; number++)
        {
            if (visible[number] == floor)
                return number;
        }

        return -1;
    }
}
