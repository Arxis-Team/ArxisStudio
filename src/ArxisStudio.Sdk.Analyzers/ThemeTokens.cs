using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Значения темы, которые расширение обязано называть по имени.
/// </summary>
/// <remarks>
/// Читаются из самих словарей темы, вшитых в сборку анализатора при её
/// сборке, — а не из списка, переписанного сюда руками. Список разошёлся бы с
/// темой на первой же правке шкалы, и правило называло бы ключи, которых нет.
/// Словари едут вместе с SDK, поэтому плагин получает ровно ту тему, против
/// которой собирается.
/// <para>
/// Семейств три, и расширению открыто одно. Ступени шкал палитры
/// (<c>AxGray1</c>, <c>AxBlue6</c>) — внутренность темы: из них собраны
/// смысловые цвета, и переименовать ступень тема вправе. Цвета
/// (<c>AxAccColor</c>) — значения типа <c>Color</c>, их берут там, где нужен
/// цвет, а не кисть. Кисти (<c>AxAccBrush</c>) — то, что называет разметка.
/// </para>
/// </remarks>
internal sealed class ThemeTokens
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Lazy<ThemeTokens> Shared = new(Load);

    private ThemeTokens(
        IReadOnlyList<Named> steps,
        IReadOnlyList<Shaped> gaps,
        IReadOnlyList<Named> fontSizes,
        IReadOnlyDictionary<string, List<string>> brushesByColour,
        IReadOnlyDictionary<string, string> brushByColourKey,
        IReadOnlyDictionary<string, string> scale)
    {
        Steps = steps;
        Gaps = gaps;
        FontSizes = fontSizes;
        BrushesByColour = brushesByColour;
        BrushByColourKey = brushByColourKey;
        Scale = scale;
    }

    /// <summary>Таблица темы, разобранная один раз на загрузку анализатора.</summary>
    public static ThemeTokens Instance => Shared.Value;

    /// <summary>Ступени шкалы расстояний снизу вверх: <c>AxSpace</c> — 8.</summary>
    public IReadOnlyList<Named> Steps { get; }

    /// <summary>Зазоры формой <c>Thickness</c>: равносторонние, направленные и парные.</summary>
    public IReadOnlyList<Shaped> Gaps { get; }

    /// <summary>Кегли темы снизу вверх.</summary>
    public IReadOnlyList<Named> FontSizes { get; }

    /// <summary>Цвет в записи <c>#AARRGGBB</c> — кисти, которые его несут хотя бы в одном варианте темы.</summary>
    public IReadOnlyDictionary<string, List<string>> BrushesByColour { get; }

    /// <summary>Смысловой цвет — его кисть: <c>AxAccColor</c> — <c>AxAccBrush</c>.</summary>
    public IReadOnlyDictionary<string, string> BrushByColourKey { get; }

    /// <summary>Ступень шкалы палитры — её цвет в тёмном варианте, по которому ищется смысловая кисть.</summary>
    public IReadOnlyDictionary<string, string> Scale { get; }

    /// <summary>
    /// Приводит запись цвета к <c>#AARRGGBB</c> прописными; не цвет — <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Разметка принимает и <c>#RGB</c>, и <c>#RRGGBB</c>, и строчные буквы, и
    /// сверка по записи как она есть пропустила бы <c>#3574f0</c> мимо
    /// <c>#3574F0</c>. Имена цветов (<c>Red</c>) сюда не доходят: в теме их нет.
    /// </remarks>
    public static string? Colour(string value)
    {
        var text = value.Trim();

        if (!text.StartsWith("#", StringComparison.Ordinal))
            return null;

        var digits = text.Substring(1);

        if (!Regex.IsMatch(digits, "^[0-9A-Fa-f]+$"))
            return null;

        digits = digits.Length switch
        {
            3 => "FF" + string.Concat(digits.Select(digit => new string(digit, 2))),
            4 => string.Concat(digits.Select(digit => new string(digit, 2))),
            6 => "FF" + digits,
            8 => digits,
            _ => string.Empty,
        };

        return digits.Length == 8 ? "#" + digits.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Разбирает запись <c>Thickness</c>: одно, два или четыре числа; иначе <c>null</c>.
    /// </summary>
    public static double[]? Sides(string value)
    {
        var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var numbers = new double[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]))
                return null;
        }

        return numbers.Length switch
        {
            1 => new[] { numbers[0], numbers[0], numbers[0], numbers[0] },
            2 => new[] { numbers[0], numbers[1], numbers[0], numbers[1] },
            4 => numbers,
            _ => null,
        };
    }

    private static ThemeTokens Load()
    {
        var spacing = Read("Spacing.axaml");
        var typography = Read("Typography.axaml");
        var palette = Read("Palette.axaml");

        var steps = Doubles(spacing, key => key.StartsWith("AxSpace", StringComparison.Ordinal));
        var gaps = spacing.Descendants(XName.Get("Thickness", Avalonia))
            .Select(element => (Key: Key(element), Sides: Sides(element.Value)))
            .Where(gap => gap.Key is not null && gap.Sides is not null)
            .Select(gap => new Shaped(gap.Key!, gap.Sides!))
            .ToList();
        var fontSizes = Doubles(typography, key => key.StartsWith("AxFontSize", StringComparison.Ordinal));

        var brushKeys = new HashSet<string>(
            palette.Descendants(XName.Get("SolidColorBrush", Avalonia)).Select(Key).OfType<string>(),
            StringComparer.Ordinal);

        var brushesByColour = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var brushByColourKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var scale = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in palette.Descendants(XName.Get("Color", Avalonia)))
        {
            if (Key(element) is not { } key || Colour(element.Value) is not { } colour)
                continue;

            if (!key.EndsWith("Color", StringComparison.Ordinal))
            {
                // Ступень шкалы объявлена в обоих вариантах; запоминается первый —
                // тёмный, он в словаре идёт раньше. Смысловую кисть по ступени
                // ищут только ради подсказки, и одного варианта для неё хватает.
                if (!scale.ContainsKey(key))
                    scale[key] = colour;

                continue;
            }

            var brush = key.Substring(0, key.Length - "Color".Length) + "Brush";

            if (!brushKeys.Contains(brush))
                continue;

            brushByColourKey[key] = brush;

            if (!brushesByColour.TryGetValue(colour, out var brushes))
                brushesByColour[colour] = brushes = [];

            if (!brushes.Contains(brush))
                brushes.Add(brush);
        }

        foreach (var brushes in brushesByColour.Values)
            brushes.Sort(StringComparer.Ordinal);

        return new ThemeTokens(steps, gaps, fontSizes, brushesByColour, brushByColourKey, scale);
    }

    private static List<Named> Doubles(XDocument document, Func<string, bool> wanted) =>
        document.Descendants(XName.Get("Double", Xaml))
            .Select(element => (Key: Key(element), Text: element.Value))
            .Where(entry => entry.Key is not null && wanted(entry.Key))
            .Select(entry => (entry.Key, Ok: double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number), Number: number))
            .Where(entry => entry.Ok)
            .Select(entry => new Named(entry.Key!, entry.Number))
            .OrderBy(entry => entry.Value)
            .ToList();

    private static string? Key(XElement element) => element.Attribute(XName.Get("Key", Xaml))?.Value;

    private static XDocument Read(string file)
    {
        var assembly = typeof(ThemeTokens).GetTypeInfo().Assembly;

        using var stream = assembly.GetManifestResourceStream("theme/" + file)
            ?? throw new InvalidOperationException($"В сборку анализатора не вшит словарь темы {file}.");
        using var reader = new StreamReader(stream);

        return XDocument.Parse(reader.ReadToEnd());
    }

    /// <summary>Ключ темы с одним числом.</summary>
    internal sealed class Named(string key, double value)
    {
        /// <summary>Имя ключа.</summary>
        public string Key { get; } = key;

        /// <summary>Величина.</summary>
        public double Value { get; } = value;
    }

    /// <summary>Ключ темы с четырьмя сторонами.</summary>
    internal sealed class Shaped(string key, double[] sides)
    {
        /// <summary>Имя ключа.</summary>
        public string Key { get; } = key;

        /// <summary>Левая, верхняя, правая и нижняя стороны.</summary>
        public double[] Sides { get; } = sides;
    }
}
