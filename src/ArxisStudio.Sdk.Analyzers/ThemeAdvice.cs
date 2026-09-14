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
