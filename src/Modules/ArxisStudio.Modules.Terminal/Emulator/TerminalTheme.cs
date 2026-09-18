using Avalonia.Media;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace ArxisStudio.Modules.Terminal.Emulator;

/// <summary>
/// Цвета терминала: палитра Campbell из Windows Terminal на фоне темы студии.
/// </summary>
/// <remarks>
/// Шестнадцать именованных цветов — Campbell: оболочки Windows рисуют под неё,
/// и тёмно-синие подсказки PowerShell читаются на тёмном фоне. Фон, текст и
/// выделение — из темы студии, чтобы панель терминала не была чужим окном
/// среди своих. Остальные 240 индексов и truecolor эмулятор считает сам.
/// </remarks>
public static class TerminalTheme
{
    /// <summary>Индекс, которым эмулятор обозначает «цвет текста по умолчанию».</summary>
    public const int DefaultForeground = Constants.DefaultAttrDataFg;

    /// <summary>Индекс, которым эмулятор обозначает «цвет фона по умолчанию».</summary>
    public const int DefaultBackground = Constants.DefaultAttrDataBg;

    /// <summary>Наименьший контраст текста к фону его ячейки: 4,5:1 — порог WCAG для обычного текста.</summary>
    public const double MinimumContrast = 4.5;

    /// <summary>Сколько пар «текст — фон» помнится поправленными, прежде чем память начнётся заново.</summary>
    /// <remarks>Программа с truecolor рисует тысячами оттенков, и память без предела росла бы вместе с выводом.</remarks>
    private const int Remembered = 4096;

    private static readonly Dictionary<long, int> Adjusted = [];

    /// <summary>Тема эмулятора: Campbell на цветах студии.</summary>
    /// <param name="background">Фон панели.</param>
    /// <param name="foreground">Обычный текст.</param>
    /// <param name="selection">Подложка выделения.</param>
    public static ThemeOptions Campbell(Color background, Color foreground, Color selection) => new()
    {
        Black = "#0C0C0C",
        Red = "#C50F1F",
        Green = "#13A10E",
        Yellow = "#C19C00",
        Blue = "#0037DA",
        Magenta = "#881798",
        Cyan = "#3A96DD",
        White = "#CCCCCC",
        BrightBlack = "#767676",
        BrightRed = "#E74856",
        BrightGreen = "#16C60C",
        BrightYellow = "#F9F1A5",
        BrightBlue = "#3B78FF",
        BrightMagenta = "#B4009E",
        BrightCyan = "#61D6D6",
        BrightWhite = "#F2F2F2",
        Background = Hex(background),
        Foreground = Hex(foreground),
        Cursor = Hex(foreground),
        Selection = Hex(selection),
    };

    /// <summary>Цвет Avalonia из упакованного RGB эмулятора.</summary>
    /// <param name="rgb">Цвет вида <c>0xRRGGBB</c>.</param>
    public static Color ToColor(int rgb) =>
        Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>
    /// Цвета текста и фона ячейки с учётом всего, что на них влияет.
    /// </summary>
    /// <param name="attributes">Атрибуты ячейки.</param>
    /// <param name="colors">Палитра эмулятора: в ней уже учтены OSC 4/10/11 от программ.</param>
    /// <param name="boldIsBright">Жирный текст первых восьми цветов рисовать ярким — как в xterm.</param>
    /// <param name="painted">
    /// Фон, на котором текст на самом деле рисуют, если это не фон ячейки: подложка выделения.
    /// Читаемым текст обязан быть на нём.
    /// </param>
    /// <returns>Пара <c>0xRRGGBB</c>: текст и фон; текст — читаемый на этом фоне (<see cref="Legible"/>).</returns>
    /// <remarks>
    /// Инверсия меняет местами уже разрешённые цвета, а не индексы: инверсия
    /// «по умолчанию на по умолчанию» должна дать светлый фон с тёмным текстом,
    /// а не два индекса, из которых ни один не цвет.
    /// </remarks>
    public static (int Foreground, int Background) Resolve(
        in AttributeData attributes, ColorPalette colors, bool boldIsBright, int? painted = null)
    {
        ArgumentNullException.ThrowIfNull(colors);

        var foreground = Foreground(attributes, colors, boldIsBright);
        var background = Background(attributes, colors);

        if (attributes.IsInverse())
            (foreground, background) = (background, foreground);

        return (Legible(foreground, painted ?? background), background);
    }

    /// <summary>
    /// Цвет текста, читаемый на этом фоне: не меньше <see cref="MinimumContrast"/>; уже читаемый
    /// возвращается как есть.
    /// </summary>
    /// <param name="foreground">Цвет текста, <c>0xRRGGBB</c>.</param>
    /// <param name="background">Фон, на котором его рисуют, <c>0xRRGGBB</c>.</param>
    /// <remarks>
    /// Campbell подобрана под тёмный фон: в светлой теме студии жёлтый, белый и яркие цвета ложились
    /// на светлый фон почти невидимыми — десять из шестнадцати ниже 4,5:1, — а в тёмной темнели
    /// синий и чёрный. Чинить одну палитру мало: программа вправе задать цвет числом, мимо неё.
    /// Поэтому контраст правится на рисовании, как в VS Code (<c>minimumContrastRatio</c>, по
    /// умолчанию тоже 4,5): текст темнеет или светлеет ровно настолько, насколько нужно, смешиваясь с
    /// чёрным или белым, — оттенок остаётся своим.
    /// </remarks>
    public static int Legible(int foreground, int background)
    {
        if (Contrast(foreground, background) >= MinimumContrast)
            return foreground;

        var key = ((long)(foreground & 0xFFFFFF) << 24) | (uint)(background & 0xFFFFFF);

        lock (Adjusted)
        {
            if (Adjusted.TryGetValue(key, out var known))
                return known;
        }

        var darker = Toward(foreground, background, 0x000000);
        var lighter = Toward(foreground, background, 0xFFFFFF);
        var dark = Contrast(darker, background) >= MinimumContrast;
        var light = Contrast(lighter, background) >= MinimumContrast;

        // Годятся оба — берётся тот, что уводит от фона: на светлом текст темнеет, на тёмном светлеет.
        var chosen = (dark, light) switch
        {
            (true, false) => darker,
            (false, true) => lighter,
            (true, true) => Luminance(background) > 0.18 ? darker : lighter,
            _ => Contrast(darker, background) >= Contrast(lighter, background) ? darker : lighter,
        };

        lock (Adjusted)
        {
            if (Adjusted.Count >= Remembered)
                Adjusted.Clear();

            Adjusted[key] = chosen;
        }

        return chosen;
    }

    /// <summary>Контраст двух цветов по WCAG: от 1 до 21.</summary>
    /// <param name="first">Цвет, <c>0xRRGGBB</c>.</param>
    /// <param name="second">Цвет, <c>0xRRGGBB</c>.</param>
    public static double Contrast(int first, int second)
    {
        var a = Luminance(first);
        var b = Luminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Смешивает цвет с чёрным или белым по двадцатой, пока текст не станет читаемым.</summary>
    private static int Toward(int color, int background, int target)
    {
        for (var step = 1; step <= 20; step++)
        {
            var mixed = Mix(color, target, step / 20.0);

            if (Contrast(mixed, background) >= MinimumContrast)
                return mixed;
        }

        return target;
    }

    private static int Mix(int color, int target, double share)
    {
        static int Channel(int from, int to, double share) => (int)Math.Round(from + ((to - from) * share));

        var r = Channel((color >> 16) & 0xFF, (target >> 16) & 0xFF, share);
        var g = Channel((color >> 8) & 0xFF, (target >> 8) & 0xFF, share);
        var b = Channel(color & 0xFF, target & 0xFF, share);

        return (r << 16) | (g << 8) | b;
    }

    /// <summary>Относительная яркость по WCAG.</summary>
    private static double Luminance(int rgb)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;

            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((rgb >> 16) & 0xFF)) + (0.7152 * Channel((rgb >> 8) & 0xFF)) + (0.0722 * Channel(rgb & 0xFF));
    }

    private static int Foreground(in AttributeData attributes, ColorPalette colors, bool boldIsBright)
    {
        if (attributes.GetFgColorMode() == (int)ColorMode.RGB)
            return attributes.GetFgColor();

        var index = attributes.GetFgColor();

        if (index == DefaultForeground || index < 0 || index > 255)
            return colors.Foreground;

        if (boldIsBright && attributes.IsBold() && index < 8)
            index += 8;

        return colors[index];
    }

    private static int Background(in AttributeData attributes, ColorPalette colors)
    {
        if (attributes.GetBgColorMode() == (int)ColorMode.RGB)
            return attributes.GetBgColor();

        var index = attributes.GetBgColor();

        return index == DefaultBackground || index < 0 || index > 255 ? colors.Background : colors[index];
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
