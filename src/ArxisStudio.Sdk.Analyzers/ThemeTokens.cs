using System.Globalization;
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
/// Семейств у палитры два. Цвета (<c>AxAccentColor</c>) — значения типа
/// <c>Color</c>, их берут там, где нужен цвет, а не кисть. Кисти
/// (<c>AxAccentBrush</c>) — то, что называет разметка. Ступеней шкал, из которых
/// до SDK 6.0 собирались цвета, в словаре больше нет: роль записана числом, и
/// прежние имена помнит <see cref="ThemeRenames"/>, а не тема.
/// </para>
/// </remarks>
internal sealed class ThemeTokens
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Folder = "theme/";

    private static readonly Lazy<ThemeTokens> Shared = new(Load);

    /// <summary>
    /// Имя из пространства темы: приставка <c>Ax</c>, заглавная буква и дальше буквы и цифры.
    /// </summary>
    /// <remarks>
    /// Тем же образцом ключ темы в коде узнаёт проверка ключей самой студии: имена типов берут
    /// через <c>nameof</c> и <c>typeof</c>, и строка такой формы — ключ.
    /// </remarks>
    private static readonly Regex Spaced = new("^Ax[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

    private ThemeTokens(
        IReadOnlyList<Named> steps,
        IReadOnlyList<Shaped> gaps,
        IReadOnlyList<Named> fontSizes,
        IReadOnlyDictionary<string, List<string>> brushesByColour,
        IReadOnlyDictionary<string, string> brushByColourKey,
        HashSet<string> keys)
    {
        Steps = steps;
        Gaps = gaps;
        FontSizes = fontSizes;
        BrushesByColour = brushesByColour;
        BrushByColourKey = brushByColourKey;
        Keys = keys;
    }

    /// <summary>Таблица темы, разобранная один раз на загрузку анализатора.</summary>
    public static ThemeTokens Instance => Shared.Value;

    /// <summary>
    /// Каждый ключ, который тема объявляет: палитра, шкалы, метрики, строки и именованные шаблоны.
    /// </summary>
    /// <remarks>
    /// Ступени плотности новых ключей не заводят, только переопределяют объявленные, — это держит
    /// проверка плотности в теме, — поэтому список один на все ступени.
    /// </remarks>
    public HashSet<string> Keys { get; }

    /// <summary>Ступени шкалы расстояний снизу вверх: <c>AxSpace</c> — 8.</summary>
    public IReadOnlyList<Named> Steps { get; }

    /// <summary>Зазоры формой <c>Thickness</c>: равносторонние, направленные и парные.</summary>
    public IReadOnlyList<Shaped> Gaps { get; }

    /// <summary>Кегли темы снизу вверх.</summary>
    public IReadOnlyList<Named> FontSizes { get; }

    /// <summary>Цвет в записи <c>#AARRGGBB</c> — кисти, которые его несут хотя бы в одном варианте темы.</summary>
    public IReadOnlyDictionary<string, List<string>> BrushesByColour { get; }

    /// <summary>Цвет роли — её кисть: <c>AxAccentColor</c> — <c>AxAccentBrush</c>.</summary>
    public IReadOnlyDictionary<string, string> BrushByColourKey { get; }

    /// <summary>Похоже ли имя на ключ темы: <c>AxSurfacePanelBrush</c> — да, <c>ChartLine</c> — нет.</summary>
    public static bool IsSpaced(string key) => Spaced.IsMatch(key);

    /// <summary>
    /// Приводит запись цвета к <c>#AARRGGBB</c> прописными; не цвет — <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Разметка принимает и <c>#RGB</c>, и <c>#RRGGBB</c>, и строчные буквы, и
    /// сверка по записи как она есть пропустила бы <c>#5a8ff3</c> мимо
    /// <c>#5A8FF3</c>. Имена цветов (<c>Red</c>) сюда не доходят: в теме их нет.
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

        foreach (var element in palette.Descendants(XName.Get("Color", Avalonia)))
        {
            if (Key(element) is not { } key || Colour(element.Value) is not { } colour ||
                !key.EndsWith("Color", StringComparison.Ordinal))
                continue;

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

        var keys = new HashSet<string>(
            Files().Select(Read).SelectMany(document => document.Descendants()).Select(Key)
                .OfType<string>()
                .Where(key => !key.StartsWith("{", StringComparison.Ordinal)),
            StringComparer.Ordinal);

        return new ThemeTokens(steps, gaps, fontSizes, brushesByColour, brushByColourKey, keys);
    }

    /// <summary>Имена всех словарей темы, вшитых в анализатор.</summary>
    private static IEnumerable<string> Files() =>
        typeof(ThemeTokens).GetTypeInfo().Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Folder, StringComparison.Ordinal))
            .Select(name => name.Substring(Folder.Length));

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

        using var stream = assembly.GetManifestResourceStream(Folder + file)
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
