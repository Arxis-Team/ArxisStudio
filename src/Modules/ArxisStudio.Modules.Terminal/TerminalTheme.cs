using Avalonia.Media;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace ArxisStudio.Modules.Terminal;

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
    /// <returns>Пара <c>0xRRGGBB</c>: текст и фон.</returns>
    /// <remarks>
    /// Инверсия меняет местами уже разрешённые цвета, а не индексы: инверсия
    /// «по умолчанию на по умолчанию» должна дать светлый фон с тёмным текстом,
    /// а не два индекса, из которых ни один не цвет.
    /// </remarks>
    public static (int Foreground, int Background) Resolve(in AttributeData attributes, ColorPalette colors, bool boldIsBright)
    {
        ArgumentNullException.ThrowIfNull(colors);

        var foreground = Foreground(attributes, colors, boldIsBright);
        var background = Background(attributes, colors);

        return attributes.IsInverse() ? (background, foreground) : (foreground, background);
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
