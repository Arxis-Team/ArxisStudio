using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Brand;

/// <summary>
/// Знак ArxisStudio — одна фигура в трёх прорисовках под размер.
/// </summary>
/// <remarks>
/// Знак нарисован штрихом с лесенкой толщин (1.15 буква и остров, 1.0 рельс и
/// скамья, 0.9 перемычка и тяги, 0.85 узлы), и эта лесенка верна ровно в одном
/// размере — 48. Растяни её, как растягивает <c>Viewbox</c>, и на 16 пикселях
/// штрих станет волосом, а на 256 — брусом. Поэтому знак не масштабируется, а
/// перерисовывается: так устроены наборы JetBrains и SF Symbols.
/// <list type="bullet">
/// <item>48 и выше — полный знак: буква, остров, рельс, скамья, семь узлов.</item>
/// <item>От 32 до 48 — сокращённый: скамья и тяги сняты, узлов три, пропорции
/// перерисованы на сетке 32, иначе просвет рельса схлопнулся бы в полпикселя.</item>
/// <item>Меньше 32 — заливочный: сплошная буква и сплошной рельс, штриха нет
/// вовсе. Штрих на шестнадцати пикселях задачу не решает в принципе.</item>
/// </list>
/// <para>
/// Не иконка набора: у знака пять фигур с разной толщиной и непрозрачные узлы, а
/// <c>AxIcon</c> рисует один контур одним пером. Цвета — из темы: линии цветом
/// текста, заливка узлов цветом носителя, поэтому знак верен в обеих темах без
/// второй копии.
/// </para>
/// </remarks>
public sealed class ArxisMark : Control
{
    /// <summary>Сторона квадрата, в который вписан знак.</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ArxisMark, double>(nameof(Size), 48);

    /// <summary>Кисть линий и заливки.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ArxisMark, IBrush?>(nameof(Stroke));

    /// <summary>Кисть узлов — цвет носителя, на котором стоит знак.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ArxisMark, IBrush?>(nameof(Fill));

    /// <summary>Контур буквы полного знака на сетке 48: вершина срезана, пяты — по кромке рельса.</summary>
    internal const string LetterData = "M21.84 6H26.16L38.76 31.95H33.51L24 10.2L14.49 31.95H9.24Z";

    /// <summary>Остров полного знака: отдельная фигура внутри буквы, а не вырез в ней.</summary>
    internal const string IslandData = "M24 18.6L28.82 31H19.18Z";

    private const string RailData = "M4 31.95H44V35.4H4Z";
    private const string BenchData = "M7.15 35.4L4 41.95H15.23L17.21 36.39H30.79L32.77 41.95H44L40.85 35.4Z";
    private const string TiesData = "M21.5 33.67H26.5M12.27 35.4L11.71 37.5M35.73 35.4L36.29 37.5";

    private static readonly Rect ApexRect = new(23.05, 9.25, 1.9, 1.9);

    private static readonly Drawing Full = new(
        48,
        [
            new Line(LetterData, 1.15),
            new Line(IslandData, 1.15),
            new Line(RailData, 1),
            new Line(BenchData, 1),
            new Line(TiesData, 0.9),
        ],
        [
            ApexRect,
            new Rect(3, 32.67, 2, 2),
            new Rect(19.5, 32.67, 2, 2),
            new Rect(26.5, 32.67, 2, 2),
            new Rect(43, 32.67, 2, 2),
            new Rect(10.28, 37.36, 2, 2),
            new Rect(35.72, 37.36, 2, 2),
        ],
        0.85,
        []);

    /// <summary>
    /// Сокращённый знак на сетке 32.
    /// </summary>
    /// <remarks>
    /// Не уменьшенная копия полного: рельс здесь относительно выше (3.5 против
    /// 3.45 на сетке 48), а полоса буквы у пят шире — иначе просветы, на которых
    /// знак держится, при штрихе в пиксель схлопнулись бы. Остались три узла —
    /// вершина и торцы рельса: они повторяют треугольник буквы и читаются, а
    /// перемычка центральных узлов на этом размере становится пятном.
    /// </remarks>
    private static readonly Drawing Reduced = new(
        32,
        [
            new Line("M14.6 5.5H17.4L25.8 22.5H21.6L16 9.2L10.4 22.5H6.2Z", 1),
            new Line("M16 13.7L19.2 22H12.8Z", 1),
            new Line("M2.5 22.5H29.5V26H2.5Z", 0.9),
        ],
        [
            new Rect(15.1, 9.7, 1.8, 1.8),
            new Rect(1.5, 23.25, 2, 2),
            new Rect(28.5, 23.25, 2, 2),
        ],
        0.8,
        []);

    /// <summary>
    /// Заливочный знак на сетке 16: полоса буквы и рельс, выступающий за пяты.
    /// </summary>
    /// <remarks>
    /// Просвет внутри буквы — не вырез: контур полосы и так его обходит. Остров
    /// и узлы сняты: на шестнадцати пикселях они не фигуры, а шум. Силуэт держат
    /// три вещи, по которым знак и узнают: срезанная вершина, раскрытые стойки и
    /// рельс шире буквы.
    /// </remarks>
    private static readonly Drawing Solid = new(
        16,
        [],
        [],
        0,
        ["M6.9 2H9.1L14.1 11.9H11.3L8 4.9L4.7 11.9H1.9Z", "M0.9 11.9H15.1V14H0.9Z"]);

    static ArxisMark()
    {
        AffectsRender<ArxisMark>(SizeProperty, StrokeProperty, FillProperty);
        AffectsMeasure<ArxisMark>(SizeProperty);
    }

    /// <summary>Собирает знак с цветами темы.</summary>
    public ArxisMark()
    {
        this.Bind(StrokeProperty, this.GetResourceObservable("AxTextPrimaryBrush"));
        this.Bind(FillProperty, this.GetResourceObservable("AxSurfaceBaseBrush"));
    }

    /// <summary>Какая прорисовка выбирается под размер.</summary>
    internal enum Cut
    {
        /// <summary>Сплошная буква и рельс, без штриха.</summary>
        Solid,

        /// <summary>Буква, остров, рельс и три узла.</summary>
        Reduced,

        /// <summary>Знак целиком.</summary>
        Full,
    }

    /// <summary>Сторона квадрата, в который вписан знак.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Кисть линий и заливки.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Кисть узлов.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Буква полного знака — для водяного знака заставки.</summary>
    /// <remarks>
    /// Каждый раз новая геометрия: одну на два контрола делить незачем, а число
    /// у фигуры остаётся одно — здесь. Водяной знак прежде держал свою копию
    /// этих чисел, и совпадали они только потому, что их никто не трогал.
    /// </remarks>
    public static Geometry Letter => Geometry.Parse(LetterData);

    /// <summary>Остров полного знака.</summary>
    public static Geometry Island => Geometry.Parse(IslandData);

    /// <summary>Узел на вершине полного знака.</summary>
    public static Geometry Apex => new EllipseGeometry(ApexRect);

    /// <summary>Прорисовка под размер: меньше 32 — сплошная, до 48 — сокращённая, дальше — полная.</summary>
    internal static Cut Choose(double size) => size < 32 ? Cut.Solid : size < 48 ? Cut.Reduced : Cut.Full;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);

        if (side <= 0 || Stroke is not { } stroke)
            return;

        var drawing = Choose(side) switch
        {
            Cut.Solid => Solid,
            Cut.Reduced => Reduced,
            _ => Full,
        };

        var scale = side / drawing.Grid;

        // Толщина задана для своей сетки и на экране обязана остаться собой: под
        // масштабом перо растянулось бы вместе с фигурой, поэтому оно делится на
        // масштаб. Полный знак крупнее 48 штрих всё же прибавляет — корнем от
        // масштаба, а не масштабом: на 256 пикселях полуторный штрих стал бы
        // волосом, а впятеро толще — брусом.
        var weight = drawing == Full && scale > 1 ? Math.Sqrt(scale) : 1;

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            foreach (var filled in drawing.Filled)
                context.DrawGeometry(stroke, null, Geometry.Parse(filled));

            foreach (var line in drawing.Lines)
                context.DrawGeometry(null, Pen(stroke, line.Thickness * weight / scale), Geometry.Parse(line.Data));

            var node = Pen(stroke, drawing.NodeThickness * weight / scale);

            foreach (var rect in drawing.Nodes)
                context.DrawEllipse(Fill, node, rect);
        }
    }

    private static Pen Pen(IBrush brush, double thickness) =>
        new(brush, thickness, lineJoin: PenLineJoin.Miter);

    private sealed record Line(string Data, double Thickness);

    private sealed record Drawing(double Grid, Line[] Lines, Rect[] Nodes, double NodeThickness, string[] Filled);
}
