using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace ArxisStudio.Brand;

/// <summary>
/// Значок приложения: плитка акцентного цвета и знак на ней.
/// </summary>
/// <remarks>
/// Панель задач бывает и тёмной, и светлой, а знак цветом текста темы на
/// прозрачном фоне пропал бы на одной из них. Поэтому значок — плитка, как у
/// Rider и у Unity: носитель у знака свой и от панели задач не зависит.
/// <para>
/// Файл значка не рисуют руками. Его рендерит этот класс из того же
/// <see cref="ArxisMark"/>, что стоит на заставке, и тест сверяет лежащий в
/// репозитории <c>.ico</c> с тем, что выходит сейчас: правка знака, не
/// пересобравшая значок, падает там, а не в панели задач у человека.
/// </para>
/// </remarks>
internal static class BrandIcon
{
    /// <summary>
    /// Размеры, которые Windows спрашивает у значка: панель задач, заголовок,
    /// Проводник в разных видах и плитка при высоком масштабе.
    /// </summary>
    public static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>
    /// Доля плитки, которую занимает знак.
    /// </summary>
    /// <remarks>
    /// Знак шире, чем выше, и по горизонтали упирается в поле; восьми процентов
    /// с каждой стороны хватает, чтобы скругление плитки его не срезало.
    /// </remarks>
    private const double Inset = 0.84;

    /// <summary>Скругление плитки — доля стороны, одинаковая на всех размерах.</summary>
    private const double Rounding = 0.22;

    /// <summary>Картинка значка одного размера, в PNG.</summary>
    public static byte[] Png(int size)
    {
        var tile = Colour("AxAccStrongColor");
        var mark = Colour("AxOnAccColor");

        var surface = new Border
        {
            Width = size,
            Height = size,
            Background = new SolidColorBrush(tile),
            CornerRadius = new CornerRadius(size * Rounding),
            Child = new ArxisMark
            {
                Size = Math.Round(size * Inset),
                Stroke = new SolidColorBrush(mark),

                // Узлы — кольца: их заливка совпадает с плиткой.
                Fill = new SolidColorBrush(tile),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };

        surface.Measure(new Size(size, size));
        surface.Arrange(new Rect(0, 0, size, size));

        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using var stream = new MemoryStream();

        bitmap.Render(surface);
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    /// <summary>
    /// Файл <c>.ico</c> со всеми размерами, картинки внутри — PNG.
    /// </summary>
    /// <remarks>
    /// PNG внутри значка Windows читает с Vista; старый формат с масками
    /// повторял бы каждый размер дважды ради систем, на которых студия не
    /// запускается.
    /// </remarks>
    public static byte[] Ico()
    {
        var images = Sizes.Select(Png).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + 16 * images.Count;

        for (var index = 0; index < images.Count; index++)
        {
            // Размер в записи — байт, и 256 в него не влезает: ноль значит 256.
            var side = (byte)(Sizes[index] >= 256 ? 0 : Sizes[index]);

            writer.Write(side);
            writer.Write(side);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[index].Length);
            writer.Write(offset);

            offset += images[index].Length;
        }

        foreach (var image in images)
            writer.Write(image);

        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// Цвет тёмного варианта темы.
    /// </summary>
    /// <remarks>
    /// Вариант прибит: значок живёт в панели задач, а не в студии, и тема,
    /// выбранная в студии, до него не доходит. Двух значков под две темы
    /// Windows не держит.
    /// </remarks>
    private static Color Colour(string key) =>
        Application.Current is { } app && app.TryGetResource(key, ThemeVariant.Dark, out var value) && value is Color colour
            ? colour
            : throw new InvalidOperationException($"В теме нет цвета {key}: значок рисовать нечем.");
}
