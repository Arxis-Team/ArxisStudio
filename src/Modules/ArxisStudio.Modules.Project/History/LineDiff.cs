namespace ArxisStudio.Modules.Project.History;

/// <summary>Что стало со строкой.</summary>
internal enum DiffKind
{
    /// <summary>Не менялась.</summary>
    Same,

    /// <summary>Была и пропала: есть только слева.</summary>
    Removed,

    /// <summary>Появилась: есть только справа.</summary>
    Added,

    /// <summary>Переписана: слева прежняя, справа новая.</summary>
    Changed,
}

/// <summary>Строка разницы в две колонки: слева — как было, справа — как стало.</summary>
/// <param name="Kind">Что стало со строкой.</param>
/// <param name="Left">Номер строки слева, с единицы; 0 — слева строки нет.</param>
/// <param name="LeftText">Текст слева; пусто — строки нет.</param>
/// <param name="Right">Номер строки справа, с единицы; 0 — справа строки нет.</param>
/// <param name="RightText">Текст справа; пусто — строки нет.</param>
internal sealed record DiffRow(DiffKind Kind, int Left, string? LeftText, int Right, string? RightText);

/// <summary>
/// Разница двух текстов построчно — алгоритмом Майерса, как у <c>git diff</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Почему Майерс.</b> Он находит кратчайшую правку — наименьшее число удалённых и добавленных
/// строк, — и разница читается так, как человек правил: переписанная строка видна переписанной, а не
/// удалённой и вставленной где-то рядом. Время — O((N+M)·D), где D — длина правки: обычная правка
/// большого файла находится за доли миллисекунды.
/// </para>
/// <para>
/// <b>Общие начало и конец</b> снимаются до поиска: правка обычно в середине, и перебирать тысячи
/// одинаковых строк незачем. Строки сравниваются номерами, а не текстом: одинаковый текст получает
/// один номер, и поиск сравнивает числа.
/// </para>
/// <para>
/// <b>Предел.</b> Тексты, разошедшиеся сильнее <see cref="MaxEdit"/> строк правки, — файл,
/// переписанный целиком или сгенерированный заново, — точной разницы не получают: середина
/// показывается одним переписанным куском. Это правда — в нём поменялось всё, — и окно не ждёт
/// минутами ответа, который человеку ничего не скажет.
/// </para>
/// <para>
/// <b>Две колонки.</b> Удалённые и добавленные строки одного места встают парами — «переписано», —
/// а лишние остаются удалёнными или добавленными: переписанная строка стоит напротив прежней.
/// </para>
/// </remarks>
internal static class LineDiff
{
    /// <summary>Самая длинная правка, которую разница ищет точно, — удалённые и добавленные строки вместе.</summary>
    internal const int MaxEdit = 2048;

    /// <summary>Строки текста: переводы строк любые, и последний перевод пустой строки не даёт.</summary>
    /// <param name="text">Текст.</param>
    public static IReadOnlyList<string> Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        var start = 0;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is not ('\n' or '\r'))
                continue;

            lines.Add(text[start..at]);

            if (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n')
                at++;

            start = at + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines;
    }

    /// <summary>Разница в две колонки.</summary>
    /// <param name="before">Строки слева — как было.</param>
    /// <param name="after">Строки справа — как стало.</param>
    public static IReadOnlyList<DiffRow> Rows(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var rows = new List<DiffRow>(Math.Max(before.Count, after.Count));
        var removed = new List<int>();
        var added = new List<int>();

        void Pair()
        {
            var paired = Math.Min(removed.Count, added.Count);

            for (var at = 0; at < paired; at++)
                rows.Add(new DiffRow(DiffKind.Changed, removed[at] + 1, before[removed[at]], added[at] + 1, after[added[at]]));

            foreach (var left in removed.Skip(paired))
                rows.Add(new DiffRow(DiffKind.Removed, left + 1, before[left], 0, null));

            foreach (var right in added.Skip(paired))
                rows.Add(new DiffRow(DiffKind.Added, 0, null, right + 1, after[right]));

            removed.Clear();
            added.Clear();
        }

        foreach (var (kind, left, right) in Script(before, after))
        {
            switch (kind)
            {
                case DiffKind.Removed:
                    removed.Add(left);
                    break;
                case DiffKind.Added:
                    added.Add(right);
                    break;
                default:
                    Pair();
                    rows.Add(new DiffRow(DiffKind.Same, left + 1, before[left], right + 1, after[right]));
                    break;
            }
        }

        Pair();

        return rows;
    }

    /// <summary>
    /// Правка по шагам от начала к концу: общая строка, удалённая слева, добавленная справа.
    /// </summary>
    /// <param name="before">Слева.</param>
    /// <param name="after">Справа.</param>
    /// <returns>
    /// Шаги с номерами строк с нуля. У удалённой в счёт левый номер, у добавленной — правый, у общей —
    /// оба.
    /// </returns>
    internal static List<(DiffKind Kind, int Left, int Right)> Script(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var (a, b) = Numbered(before, after);
        var head = 0;

        while (head < a.Length && head < b.Length && a[head] == b[head])
            head++;

        var tail = 0;

        while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail])
            tail++;

        var steps = new List<(DiffKind Kind, int Left, int Right)>(Math.Max(a.Length, b.Length));

        for (var at = 0; at < head; at++)
            steps.Add((DiffKind.Same, at, at));

        foreach (var (kind, left, right) in Middle(a.AsSpan(head, a.Length - head - tail), b.AsSpan(head, b.Length - head - tail)))
            steps.Add((kind, left + head, right + head));

        for (var at = tail; at > 0; at--)
            steps.Add((DiffKind.Same, a.Length - at, b.Length - at));

        return steps;
    }

    /// <summary>Номер для каждой строки: одинаковый текст — одинаковый номер.</summary>
    private static (int[] Before, int[] After) Numbered(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);

        int Number(string line)
        {
            if (!numbers.TryGetValue(line, out var number))
            {
                number = numbers.Count;
                numbers.Add(line, number);
            }

            return number;
        }

        return ([.. before.Select(Number)], [.. after.Select(Number)]);
    }

    /// <summary>
    /// Кратчайшая правка середины: жадный поиск Майерса по диагоналям, с записью пройденного, чтобы
    /// пройти его назад.
    /// </summary>
    private static List<(DiffKind Kind, int Left, int Right)> Middle(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var n = a.Length;
        var m = b.Length;

        if (n == 0 || m == 0)
            return Coarse(n, m);

        var limit = Math.Min(n + m, MaxEdit);

        // Дальние точки диагоналей: far[offset + k] — докуда дошли по диагонали k = x − y.
        var offset = limit + 1;
        var far = new int[(2 * limit) + 3];
        var trace = new List<int[]>();

        for (var d = 0; d <= limit; d++)
        {
            // Дальние точки перед шагом d, диагонали −d…d: обратный проход читает их отсюда.
            trace.Add(far.AsSpan(offset - d, (2 * d) + 1).ToArray());

            for (var k = -d; k <= d; k += 2)
            {
                var down = k == -d || (k != d && far[offset + k - 1] < far[offset + k + 1]);
                var x = down ? far[offset + k + 1] : far[offset + k - 1] + 1;
                var y = x - k;

                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                far[offset + k] = x;

                if (x >= n && y >= m)
                    return Back(trace, n, m);
            }
        }

        return Coarse(n, m);
    }

    /// <summary>Проходит найденный путь назад — от конца к началу — и отдаёт шаги по порядку.</summary>
    /// <param name="trace">Дальние точки перед каждым шагом: у шага d — диагонали −d…d.</param>
    /// <param name="n">Строк слева.</param>
    /// <param name="m">Строк справа.</param>
    private static List<(DiffKind Kind, int Left, int Right)> Back(List<int[]> trace, int n, int m)
    {
        var steps = new List<(DiffKind Kind, int Left, int Right)>(n + m);
        var x = n;
        var y = m;

        for (var d = trace.Count - 1; d > 0; d--)
        {
            var before = trace[d];
            var k = x - y;
            var down = k == -d || (k != d && before[k - 1 + d] < before[k + 1 + d]);
            var previous = down ? k + 1 : k - 1;
            var previousX = before[previous + d];
            var previousY = previousX - previous;

            // Общие строки после шага: от точки, куда шаг пришёл, до той, где стоим.
            var landed = down ? previousX : previousX + 1;

            while (x > landed)
            {
                x--;
                y--;
                steps.Add((DiffKind.Same, x, y));
            }

            steps.Add(down ? (DiffKind.Added, previousX, previousY) : (DiffKind.Removed, previousX, previousY));
            x = previousX;
            y = previousY;
        }

        // Шаг ноль — одни общие строки от начала.
        while (x > 0 && y > 0)
        {
            x--;
            y--;
            steps.Add((DiffKind.Same, x, y));
        }

        steps.Reverse();

        return steps;
    }

    /// <summary>Середина одним куском: слева удалено всё, справа добавлено всё.</summary>
    private static List<(DiffKind Kind, int Left, int Right)> Coarse(int n, int m) =>
    [
        .. Enumerable.Range(0, n).Select(left => (DiffKind.Removed, left, 0)),
        .. Enumerable.Range(0, m).Select(right => (DiffKind.Added, 0, right)),
    ];
}
