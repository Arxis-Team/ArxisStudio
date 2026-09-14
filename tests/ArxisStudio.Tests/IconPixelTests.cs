using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ArxisStudio.Icons;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значок студии ложится в пиксели там, где его клетка целая, и размер ему даёт тема.
/// </summary>
/// <remarks>
/// Набор нарисован в клетке 16: оси штрихов на полуклетках, концы на целых. Когда клетка занимает
/// целое число пикселей — значок в 16 точек при 200%, в 32 на обычном экране, — контур может лечь в
/// пиксели без каймы, и сводит его к этому сам <see cref="AxIcon"/>. Размер, названный числом мимо
/// темы, у значка этого не отнимает, но делает редким: 18 и 20 точек, какими были значок плагина
/// без картинки и знак вопроса, дают клетку в 1.125 и 1.25 пикселя, и целой она не становится ни
/// при каком обычном масштабе.
/// </remarks>
public class IconPixelTests
{
    /// <summary>Значок в разметке: имя элемента и его атрибуты.</summary>
    private static readonly Regex MarkupIcons = new(
        """<(?:\w+:)?AxIcon\b([^>]*)>""",
        RegexOptions.Compiled);

    /// <summary>Стиль, чей селектор называет значок, вместе с телом.</summary>
    private static readonly Regex IconStyles = new(
        """<Style\s+Selector="[^"]*\bAxIcon\b[^"]*"\s*>(.*?)</Style>""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Значок, заведённый кодом, вместе с инициализатором.</summary>
    private static readonly Regex CodeIcons = new(
        """new\s+AxIcon\s*(?:\(\s*\))?\s*\{((?:[^{}]|\{[^{}]*\})*)\}""",
        RegexOptions.Compiled);

    /// <summary>Размер числом: атрибутом, сеттером или присваиванием в инициализаторе.</summary>
    private static readonly Regex NumericSizes = new(
        """(?:\b(?:Min|Max)?(?:Width|Height)="\s*[\d.]+\s*")|(?:\bProperty="(?:Min|Max)?(?:Width|Height)"\s+Value="\s*[\d.]+\s*")|(?:\b(?:Min|Max)?(?:Width|Height)\s*=\s*[\d.]+[dDfFmM]?\b)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Клетка в целое число пикселей — и контур без каймы: каждый пиксель чернил закрашен целиком.
    /// </summary>
    /// <remarks>
    /// Плюс — две полосы в клетку шириной и девять длиной, с концами на целых клетках, поэтому при
    /// клетке в <c>c</c> пикселей чернил ровно <c>17·c²</c>: две полосы по <c>9·c²</c> минус
    /// перекрестье. Обводка в заданные 1.2 клетки дала бы кайму в пятую долю пикселя вдоль каждой
    /// кромки, и счёт разошёлся бы в обе стороны.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1d, 32d, 2)]
    [InlineData(2d, 16d, 2)]
    [InlineData(3d, 16d, 3)]
    public void On_whole_pixels_a_cell_the_outline_has_no_fringe(double scaling, double size, int cell)
    {
        var (solid, fringe) = Ink(scaling, size);

        Assert.True(fringe == 0, $"масштаб {scaling}, значок {size}: {fringe} пикселей каймы вокруг контура");
        Assert.True(solid == 17 * cell * cell, $"масштаб {scaling}, значок {size}: чернил {solid} пикселей вместо {17 * cell * cell}");
    }

    /// <summary>Ни одного размера значка числом — ни в разметке, ни в стилях, ни в коде студии.</summary>
    /// <remarks>
    /// Разметка считается вся, с модулями, плагинами и шаблонами; код — студии, тем же набором, что
    /// у счёта отступов. Размер значку дают ключи <c>AxIconSize</c> и <c>AxIconSizeSmall</c> темы, а
    /// в разметке — класс <c>small</c>.
    /// </remarks>
    [Fact]
    public void No_icon_size_is_written_as_a_number()
    {
        var markup = MarkupSources.All()
            .SelectMany(source => MarkupIcons.Matches(source.Text).Select(match => match.Groups[1].Value)
                .Concat(IconStyles.Matches(source.Text).Select(match => match.Groups[1].Value))
                .SelectMany(text => NumericSizes.Matches(text).Select(size => $"{source.Name}: {size.Value}")));

        var code = CodeSources.All()
            .SelectMany(source => CodeIcons.Matches(source.Text)
                .SelectMany(match => NumericSizes.Matches(match.Groups[1].Value).Select(size => $"{source.Name}: {size.Value}")));

        var found = markup.Concat(code).ToList();

        Assert.True(
            found.Count == 0,
            "размер значка написан числом — его дают ключ AxIconSize темы или класс small: " + string.Join(", ", found));
    }

    /// <summary>Счётчик видит каждый значок, заведённый разметкой и кодом.</summary>
    /// <remarks>
    /// Запрет разбирает текст, и форма записи, которой он не знает, прошла бы мимо молча.
    /// </remarks>
    [Fact]
    public void The_counter_sees_every_icon()
    {
        var markup = MarkupSources.All().ToList();
        var code = CodeSources.All().ToList();

        Assert.Contains(markup, source => MarkupIcons.IsMatch(source.Text));
        Assert.Contains(code, source => CodeIcons.IsMatch(source.Text));

        foreach (var (name, text) in markup)
        {
            var named = Regex.Count(text, """<(?:\w+:)?AxIcon\b""");

            Assert.True(
                named == MarkupIcons.Count(text),
                $"{name}: значков {named}, а счётчик разобрал {MarkupIcons.Count(text)}");

            var styled = Regex.Count(text, """Selector="[^"]*\bAxIcon\b""");

            Assert.True(
                styled == IconStyles.Count(text),
                $"{name}: стилей значка {styled}, а счётчик разобрал {IconStyles.Count(text)}");
        }

        foreach (var (name, text) in code)
        {
            var created = Regex.Count(text, """new\s+AxIcon\b""");

            Assert.True(
                created == CodeIcons.Count(text),
                $"{name}: значков {created}, а счётчик разобрал {CodeIcons.Count(text)} — форма записи, которой он не знает");
        }
    }

    /// <summary>
    /// Чернила плюса белым на чёрном: сколько пикселей закрашено целиком и сколько — долей.
    /// </summary>
    private static (int Solid, int Fringe) Ink(double scaling, double size)
    {
        var icon = new AxIcon
        {
            Data = AxIcons.Plus,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        icon.Width = icon.Height = size;

        var window = new Window { Width = 64, Height = 64, Background = Brushes.Black, Content = icon };

        window.Show();
        window.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = frame.Lock();

        var solid = 0;
        var fringe = 0;

        for (var y = 0; y < pixels.Size.Height; y++)
        {
            for (var x = 0; x < pixels.Size.Width; x++)
            {
                // Белое на чёрном: все три канала равны, и порядок их в кадре не важен.
                var value = Marshal.ReadByte(pixels.Address, y * pixels.RowBytes + x * 4 + 1);

                if (value == 255)
                    solid++;
                else if (value > 0)
                    fringe++;
            }
        }

        window.Close();

        return (solid, fringe);
    }
}
