using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Годится ли перевод строки-шаблона: подставляет ли он то, что подставляет исходная строка.
/// </summary>
/// <remarks>
/// Шаблоны собирают <c>string.Format</c>, часто в обработчике, и перевод с опечаткой в подстановке — <c>{0</c> или
/// <c>{1}</c> там, где аргумент один, — ронял его <c>FormatException</c>, а сбой приписывался тому, чей обработчик
/// (запись 397). Такой перевод уступает исходной строке. Исходная строка без подстановок — не шаблон, и скобки в её
/// переводе законны.
/// </remarks>
public static partial class StringPatterns
{
    /// <summary>Годится ли перевод вместо исходной строки.</summary>
    /// <param name="translated">Перевод.</param>
    /// <param name="source">Исходная строка — на языке, на котором писали.</param>
    public static bool Fits(string translated, string source)
    {
        ArgumentNullException.ThrowIfNull(translated);
        ArgumentNullException.ThrowIfNull(source);

        var places = Places(source);

        if (places == 0)
            return true;

        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, translated, new object?[places]);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Переводы, которые годятся вместо исходных строк.</summary>
    /// <param name="translated">Переводы по ключу.</param>
    /// <param name="sources">Исходные строки по ключу; перевод ключа, которого там нет, остаётся.</param>
    public static FrozenDictionary<string, string> Sound(
        IReadOnlyDictionary<string, string> translated,
        IReadOnlyDictionary<string, string> sources)
    {
        ArgumentNullException.ThrowIfNull(translated);
        ArgumentNullException.ThrowIfNull(sources);

        return translated
            .Where(pair => !sources.TryGetValue(pair.Key, out var source) || Fits(pair.Value, source))
            .ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Сколько аргументов ждёт строка: наибольший номер подстановки плюс один; 0 — не шаблон.</summary>
    private static int Places(string text)
    {
        var places = 0;

        foreach (Match match in Placeholder().Matches(text))
        {
            if (int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                places = Math.Max(places, index + 1);
        }

        return places;
    }

    // Подстановка — {номер}, с выравниванием и форматом или без; {{ — это скобка, а не подстановка.
    [GeneratedRegex(@"(?<!\{)\{(\d+)[^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
