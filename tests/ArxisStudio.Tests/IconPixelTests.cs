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
    /// <remarks>
    /// Приставка у стиля бывает: в разметке модулей и плагинов адрес по умолчанию — адрес студии, и
    /// Avalonia пишется через <c>a:</c>. Без приставки счётчик видел стиль значка, но не разбирал его.
    /// </remarks>
    private static readonly Regex IconStyles = new(
        """<(?:\w+:)?Style\s+Selector="[^"]*\bAxIcon\b[^"]*"\s*>(.*?)</(?:\w+:)?Style>""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Размер ключом ресурса: атрибутом или сеттером.</summary>
    private static readonly Regex ResourceSizes = new(
        """(?:\b(?:Min|Max)?(?:Width|Height)="(\{[^"]*\})")|(?:\bProperty="(?:Min|Max)?(?:Width|Height)"\s+Value="(\{[^"]*\})")""",
        RegexOptions.Compiled);

    /// <summary>Единственные ключи, которыми значку разрешено задать размер, — ключи силуэтов плиток.</summary>
    private static readonly Regex TileSizes = new(
        """^\{(?:\w+:)?DynamicResource\s+AxTileGlyphSize(?:Large)?\}$""",
        RegexOptions.Compiled);

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

    /// <summary>Запись мелкого размера в разметке.</summary>
    private const string Small = "Size=\"Small\"";

    /// <summary>
    /// Мелкий значок достаётся шеврону, а не полосе инструментов.
    /// </summary>
    /// <remarks>
    /// Размер набора один — шестнадцать точек, — и мелкие двенадцать названы в теме единственным
    /// отступлением: шеврон в тесной строке, где шестнадцать не помещаются. Отступление расползлось
    /// по студии: двенадцатью точками рисовались счётчики консоли, кнопки её полосы, значок уровня
    /// в строке, плюс и «ещё» терминала, кнопка скрытия группы доков, отмена задачи в статус-баре и
    /// значок узла настроек. Глиф в двенадцать точек рядом с подписью в тринадцать читается
    /// огрызком — в Rider, Visual Studio и Unity значок полосы шестнадцать.
    /// <para>
    /// Правило проверяется по имени рисунка, а не по месту: шеврон остаётся шевроном и в полосе, и
    /// в строке дерева, а всё прочее в двенадцати точках — ошибка.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_small_size_belongs_to_the_chevron_alone()
    {
        var found = MarkupSources.All()
            .SelectMany(source => MarkupIcons.Matches(source.Text)
                .Select(match => match.Groups[1].Value)
                .Where(icon => icon.Contains(Small, StringComparison.Ordinal))
                .Where(icon => !icon.Contains("Chevron", StringComparison.Ordinal))
                .Select(icon => $"{source.Name}: {icon.Trim()}"))
            .ToList();

        Assert.True(
            found.Count == 0,
            "мелкий значок не у шеврона — размер набора шестнадцать: " + string.Join("; ", found));
    }

    /// <summary>
    /// Растянуть значок можно только ключом плитки.
    /// </summary>
    /// <remarks>
    /// Плитка окна проекта — второе названное исключение из «размер один»: силуэт в 64 и 128 точек,
    /// где единица сетки занимает целое число пикселей на каждом масштабе. Любой другой ключ — хотя
    /// бы подложка плагина в 32 точки — вернул бы размер мимо правила, только записанный словом, а
    /// не числом, и прошёл бы мимо запрета на числа.
    /// </remarks>
    [Fact]
    public void An_icon_is_sized_by_the_named_tile_keys_alone()
    {
        var found = MarkupSources.All()
            .SelectMany(source => MarkupIcons.Matches(source.Text).Select(match => match.Groups[1].Value)
                .Concat(IconStyles.Matches(source.Text).Select(match => match.Groups[1].Value))
                .SelectMany(text => ResourceSizes.Matches(text))
                .Select(size => size.Groups[1].Success ? size.Groups[1].Value : size.Groups[2].Value)
                .Where(value => !TileSizes.IsMatch(value))
                .Select(value => $"{source.Name}: {value}"))
            .ToList();

        Assert.True(
            found.Count == 0,
            "значок растянут не ключом плитки AxTileGlyphSize[Large]: " + string.Join(", ", found));
    }

    /// <summary>
    /// Силуэт размером плитки ложится в пиксели на каждом масштабе с шагом в четверть.
    /// </summary>
    /// <remarks>
    /// Квадрат остановки — силуэт в семь единиц сетки с краями на целых. В плитку 64 единица занимает
    /// 4 пикселя при 100 %, 5 при 125 %, 6, 7 и 8 дальше; в 128 — вдвое больше. Край заливки тогда
    /// стоит на границе пикселя, и чернил ровно <c>(7 · единица)²</c> без единого пикселя каймы.
    /// Размер берётся у ключа темы: правило держит ключ, а не число, переписанное в тест.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("AxTileGlyphSize", 4)]
    [InlineData("AxTileGlyphSizeLarge", 8)]
    public void A_tile_sized_silhouette_lands_on_whole_pixels_at_every_scale(string key, int unit)
    {
        Assert.True(Avalonia.Application.Current!.TryFindResource(key, out var value), $"в теме нет ключа {key}");

        var size = Assert.IsType<double>(value);

        foreach (var scaling in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        {
            var (solid, fringe) = Ink(AxIcons.Stop, scaling, size);
            var side = 7 * unit * scaling;

            Assert.True(fringe == 0, $"{key} при {scaling * 100} %: {fringe} пикселей каймы по краю силуэта");
            Assert.True(solid == side * side, $"{key} при {scaling * 100} %: чернил {solid} пикселей вместо {side * side}");
        }
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
    private static (int Solid, int Fringe) Ink(double scaling, double size) => Ink(AxIcons.Plus, scaling, size);

    /// <summary>
    /// Чернила значка белым на чёрном: сколько пикселей закрашено целиком и сколько — долей.
    /// </summary>
    private static (int Solid, int Fringe) Ink(Geometry data, double scaling, double size)
    {
        var icon = new AxIcon
        {
            Data = data,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        icon.Width = icon.Height = size;

        // Окно шире значка: крупная плитка в 128 точек иначе обрезалась бы рамкой окна.
        var room = Math.Max(64, size);
        var window = new Window { Width = room, Height = room, Background = Brushes.Black, Content = icon };

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
