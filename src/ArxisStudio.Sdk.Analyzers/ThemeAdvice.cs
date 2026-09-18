using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Подсказка, которой правила о значениях темы отвечают на число.
/// </summary>
/// <remarks>
/// Одна на разметку и на код: правило, которое в двух местах говорит разными
/// словами, выглядит двумя правилами, и автор начинает гадать, какое из них
/// строже.
/// <para>
/// Замечание называет ключ, а не просто запрещает. «8 — это AxSpace» стоит
/// одной правки; «числа нельзя» стоит поиска по словарям темы, которых у автора
/// плагина под рукой нет.
/// </para>
/// </remarks>
internal static class ThemeAdvice
{
    /// <summary>
    /// Меньше двух — не зазор, а поправка на пиксель.
    /// </summary>
    /// <remarks>
    /// Единица под иконкой, сажающая её на строку текста, есть и в самой студии,
    /// и у плагина она так же законна. Ступени меньше двух в шкале нет, и
    /// назвать тут нечего. Отрицательное — та же поправка, только наружу.
    /// </remarks>
    private const double Smallest = 2;

    /// <summary>Окончания роли: у цвета темы их два, у отступа — форма толщины.</summary>
    private static readonly string[] Endings = ["Brush", "Color", "Thickness"];

    /// <summary>Подсказка к числу в <c>Spacing</c>; <c>null</c> — сказать нечего.</summary>
    public static string? Length(double value, ThemeTokens tokens) =>
        value < Smallest ? null : Step(value, tokens.Steps, "ступень шкалы");

    /// <summary>
    /// Подсказка к записи <c>Margin</c>, <c>Padding</c> или <c>new Thickness</c>.
    /// </summary>
    /// <remarks>
    /// Сначала ищется ключ ровно этой формы — <c>0,0,6,0</c> это
    /// <c>AxSpaceSnugTrailingThickness</c>. Если формы в теме нет, называется
    /// ступень каждого числа: сторону плагин соберёт сам, а величину выбирать
    /// ему не нужно.
    /// </remarks>
    public static string? Thickness(double[] sides, ThemeTokens tokens)
    {
        var numbers = sides.Where(side => side >= Smallest).Distinct().ToList();

        if (numbers.Count == 0)
            return null;

        var shaped = tokens.Gaps.FirstOrDefault(gap => gap.Sides.SequenceEqual(sides));

        if (shaped is not null)
            return $"это {shaped.Key}";

        return string.Join("; ", numbers.Select(number => Step(number, tokens.Steps, "ступень шкалы")));
    }

    /// <summary>Подсказка к кеглю; <c>null</c> — сказать нечего.</summary>
    public static string? FontSize(double value, ThemeTokens tokens) =>
        value <= 0 ? null : Step(value, tokens.FontSizes, "кегль темы");

    /// <summary>
    /// Подсказка к цвету; <c>null</c>, если такого цвета в теме нет.
    /// </summary>
    /// <remarks>
    /// Цвет, которого в теме нет, — дело плагина: своя палитра графика или
    /// схема терминала законны. Правилом он становится, когда совпадает с цветом
    /// темы: значит, имелся в виду цвет темы, но записан числом — и при
    /// переключении на светлую тему останется тёмным.
    /// </remarks>
    public static string? Colour(string value, ThemeTokens tokens)
    {
        if (ThemeTokens.Colour(value) is not { } colour ||
            !tokens.BrushesByColour.TryGetValue(colour, out var brushes))
            return null;

        return $"это цвет {Names(brushes)} — кисть переключится вместе с темой, а число останется прежним";
    }

    /// <summary>
    /// Подсказка к ключу из пространства темы, которого в теме нет; <c>null</c> — ключ есть, он
    /// объявлен самим расширением или он не из пространства темы.
    /// </summary>
    /// <param name="key">Ключ, как его назвали.</param>
    /// <param name="tokens">Тема.</param>
    /// <param name="own">Ключи, которые расширение объявило в своей разметке.</param>
    /// <param name="brush">Месту нужна кисть: из похожих называется кисть.</param>
    /// <remarks>
    /// Такая ссылка не валит ни сборку, ни показ: ресурс разрешается в пустоту, кисть молча
    /// прозрачна, зазор — ноль, и заметить промах можно только глазами, в той теме и в том
    /// состоянии экрана, где ключ стоит. Подсказка называет похожий ключ: опечатку в букву или
    /// две и роль без окончания — <c>AxAccent</c> вместо <c>AxAccentBrush</c>.
    /// <para>
    /// Ключ без приставки <c>Ax</c> правило не спрашивает: это свой ресурс расширения, и
    /// объявлен он может быть где угодно — в коде, в словаре чужой сборки, — а приставка —
    /// пространство темы, и своё расширение в нём не заводит.
    /// </para>
    /// </remarks>
    public static string? Absent(string key, ThemeTokens tokens, ISet<string> own, bool brush)
    {
        if (!ThemeTokens.IsSpaced(key) || tokens.Keys.Contains(key) || own.Contains(key))
            return null;

        var near = Near(key, tokens, brush);

        return near.Count == 0
            ? $"{key} — такого ключа в теме нет"
            : $"{key} — такого ключа в теме нет; похоже на {Names(near)}";
    }

    /// <summary>Ключи темы, на которые имя похоже больше всего; пусто — не похоже ни на что.</summary>
    private static List<string> Near(string key, ThemeTokens tokens, bool brush)
    {
        var endings = brush ? new[] { "Brush" } : Endings;
        var completed = endings.Select(ending => key + ending).Where(tokens.Keys.Contains).ToList();

        if (completed.Count > 0)
            return completed;

        // Две правки прощаются всегда, дальше — одна на каждые четыре буквы: в длинном имени
        // слово, написанное с двумя ошибками, всё ещё узнаётся, а в коротком похожим стало бы всё.
        var limit = Math.Max(2, key.Length / 4);
        var scored = tokens.Keys
            .Where(ThemeTokens.IsSpaced)
            .Select(candidate => (Key: candidate, Edits: Edits(key, candidate, limit)))
            .Where(candidate => candidate.Edits <= limit)
            .ToList();

        if (scored.Count == 0)
            return [];

        var fewest = scored.Min(candidate => candidate.Edits);
        var nearest = scored
            .Where(candidate => candidate.Edits == fewest)
            .Select(candidate => candidate.Key)
            .OrderBy(candidate => candidate, StringComparer.Ordinal)
            .ToList();

        if (brush && nearest.Any(IsBrush))
            nearest = nearest.Where(IsBrush).ToList();

        return nearest;
    }

    private static bool IsBrush(string key) => key.EndsWith("Brush", StringComparison.Ordinal);

    /// <summary>
    /// Сколько правок по букве отделяет одно имя от другого: вставка, удаление, замена или
    /// перестановка соседних букв.
    /// </summary>
    /// <remarks>
    /// Имена, длиной разошедшиеся сильнее предела, не сравниваются вовсе: правок у них заведомо
    /// больше, а ключей в теме сотни.
    /// </remarks>
    private static int Edits(string first, string second, int limit)
    {
        if (Math.Abs(first.Length - second.Length) > limit)
            return limit + 1;

        var table = new int[first.Length + 1, second.Length + 1];

        for (var row = 0; row <= first.Length; row++)
            table[row, 0] = row;

        for (var column = 0; column <= second.Length; column++)
            table[0, column] = column;

        for (var row = 1; row <= first.Length; row++)
        {
            for (var column = 1; column <= second.Length; column++)
            {
                var replaced = table[row - 1, column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1);
                var best = Math.Min(replaced, Math.Min(table[row - 1, column], table[row, column - 1]) + 1);

                if (row > 1 && column > 1 && first[row - 1] == second[column - 2] && first[row - 2] == second[column - 1])
                    best = Math.Min(best, table[row - 2, column - 2] + 1);

                table[row, column] = best;
            }
        }

        return table[first.Length, second.Length];
    }

    private static string Step(double value, IReadOnlyList<ThemeTokens.Named> steps, string what)
    {
        var exact = steps.FirstOrDefault(step => step.Value == value);

        if (exact is not null)
            return $"{Number(value)} — это {exact.Key}";

        var below = steps.LastOrDefault(step => step.Value < value);
        var above = steps.FirstOrDefault(step => step.Value > value);
        var near = new[] { below, above }.Where(step => step is not null).Select(step => $"{step!.Key} ({Number(step.Value)})");

        return $"{Number(value)} — не {what}; ближайшие — {string.Join(" и ", near)}";
    }

    private static string Names(IReadOnlyList<string> brushes) =>
        brushes.Count <= 3
            ? string.Join(" или ", brushes)
            : string.Join(", ", brushes.Take(3)) + " и другие";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
