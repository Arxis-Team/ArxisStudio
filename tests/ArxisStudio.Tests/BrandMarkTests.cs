using System.Runtime.InteropServices;
using ArxisStudio.Brand;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Знак студии: три прорисовки под размер и одна геометрия на всех, кто её рисует.
/// </summary>
/// <remarks>
/// Знак нарисован штрихом, чья лесенка толщин верна в одном размере. Проверяется
/// здесь то, что видно глазом: маленький знак залит, большой обведён. Выбор
/// прорисовки по одному имени перечисления ничего бы не доказал — прорисовка
/// могла бы выбраться и нарисоваться не той.
/// </remarks>
public class BrandMarkTests
{
    /// <summary>Прорисовка идёт за размером, и границы у неё там, где объявлено.</summary>
    [Theory]
    [InlineData(16, "Solid")]
    [InlineData(31.9, "Solid")]
    [InlineData(32, "Reduced")]
    [InlineData(47, "Reduced")]
    [InlineData(48, "Full")]
    [InlineData(256, "Full")]
    public void The_drawing_follows_the_size(double size, string expected)
    {
        Assert.Equal(expected, ArxisMark.Choose(size).ToString());
    }

    /// <summary>
    /// Маленький знак залит, большой обведён.
    /// </summary>
    /// <remarks>
    /// Точка берётся внутри рельса, в стороне от перемычки и тяг, и целиком внутри
    /// просвета: на сетке 48 просвет рельса — от 32.45 до 34.9, строка 34 уже
    /// задевает нижнюю кромку, и сглаживание дало бы серый. На шестнадцати
    /// пикселях рельс сплошной, и точка несёт цвет знака; на сорока восьми рельс
    /// — полый прямоугольник, и та же точка его просвета несёт цвет носителя.
    /// Растяни мы одну прорисовку на все размеры, как это делает Viewbox, — оба
    /// замера дали бы одно и то же.
    /// </remarks>
    [AvaloniaFact]
    public void A_small_mark_is_solid_and_a_large_one_is_drawn_with_a_line()
    {
        Assert.Equal(Colors.White, Pixel(16, x: 8, y: 13));
        Assert.Equal(Colors.Black, Pixel(48, x: 8, y: 33));
    }

    /// <summary>
    /// Водяной знак заставки не держит своей копии чисел знака.
    /// </summary>
    /// <remarks>
    /// Прежде держал: буква, остров и узел вершины были переписаны в разметку
    /// заставки, и совпадали со знаком только потому, что знак никто не трогал.
    /// Первая же правка буквы разошлась бы с водяным знаком молча — а заметить
    /// разницу в семи процентах прозрачности на глаз нельзя.
    /// </remarks>
    [Fact]
    public void The_splash_watermark_carries_no_copy_of_the_mark()
    {
        var art = MarkupSources.All().Single(source => source.Name == "Splash2026.axaml").Text;

        Assert.DoesNotContain("M21.84", art, StringComparison.Ordinal);
        Assert.DoesNotContain("23.05", art, StringComparison.Ordinal);
        Assert.Contains("ArxisMark.Letter", art, StringComparison.Ordinal);
    }

    /// <summary>Цвет точки знака заданного размера: белый знак на чёрном носителе.</summary>
    private static Color Pixel(int size, int x, int y)
    {
        var mark = new ArxisMark { Size = size, Stroke = Brushes.White, Fill = Brushes.Black };
        var surface = new Border { Background = Brushes.Black, Child = mark };

        surface.Measure(new Size(size, size));
        surface.Arrange(new Rect(0, 0, size, size));

        using var rendered = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using var copy = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        rendered.Render(surface);

        using var frame = copy.Lock();

        rendered.CopyPixels(frame);

        var bgra = Marshal.ReadInt32(frame.Address, y * frame.RowBytes + x * 4);

        return Color.FromArgb(
            (byte)(bgra >> 24),
            (byte)(bgra >> 16),
            (byte)(bgra >> 8),
            (byte)bgra);
    }
}
