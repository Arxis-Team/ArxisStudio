using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Лестница размеров плитки: силуэт от малой ступени до крупной шагом темы, а перед ними — список.
/// </summary>
/// <remarks>
/// Числа лестницы — ключи темы, и окно их не придумывает: малая, обычная и крупная ступени, шаг и
/// ширина плитки на обычной и крупной. Между ними и ниже ширина идёт по той же прямой — подпись
/// под силуэтом растёт вместе с ним. Подложка предмета модели не крупнее обычной ступени: значок на
/// ней размера набора, и крупнее она стояла бы пустым квадратом.
/// <para>
/// Положение ползунка — номер: ноль — список, дальше ступени от малой. В настройках лежит не номер,
/// а размер силуэта в точках: номер съезжал бы с каждой правкой лестницы в теме — новая ступень
/// снизу сдвинула бы выбор каждого, — а размер встаёт на ближайшую ступень, какой бы лестница ни
/// стала. Прежние номера ступеней, 1 и 2, читаются как были — обычные и крупные плитки.
/// </para>
/// </remarks>
internal sealed class TileLadder
{
    /// <summary>Прежний номер ступени «плитки»: до лестницы в настройке лежал номер.</summary>
    private const double FormerTiles = 1;

    /// <summary>Прежний номер ступени «крупные плитки».</summary>
    private const double FormerLargeTiles = 2;

    private readonly double _large;
    private readonly double _width;
    private readonly double _largeWidth;

    private TileLadder(double small, double normal, double large, double step, double width, double largeWidth)
    {
        var glyphs = new List<double>();

        for (var glyph = small; glyph <= large; glyph += step)
            glyphs.Add(glyph);

        Glyphs = glyphs;
        Normal = normal;
        _large = large;
        _width = width;
        _largeWidth = largeWidth;
    }

    /// <summary>Размеры силуэта по ступеням, от малой к крупной.</summary>
    public IReadOnlyList<double> Glyphs { get; }

    /// <summary>Обычная ступень: с неё окно начинает, и крупнее неё подложка не растёт.</summary>
    public double Normal { get; }

    /// <summary>Последнее положение ползунка: положений на одно больше, чем ступеней, — ещё список.</summary>
    public int Last => Glyphs.Count;

    /// <summary>Лестница темы, как её видит контрол.</summary>
    /// <param name="host">Контрол, у которого спрашиваются ключи; не найденное им спрашивается у приложения.</param>
    public static TileLadder Of(IResourceHost host) => new(
        Size(host, "AxTileGlyphSizeSmall"),
        Size(host, "AxTileGlyphSize"),
        Size(host, "AxTileGlyphSizeLarge"),
        Size(host, "AxTileGlyphSizeStep"),
        Size(host, "AxTileWidth"),
        Size(host, "AxTileWidthLarge"));

    /// <summary>Положение ползунка для размера из настроек: ноль — список, дальше ступени от малой.</summary>
    /// <param name="size">Размер силуэта в точках, ноль — список; пусто — обычная ступень.</param>
    public int Position(double? size) => size switch
    {
        null => Nearest(Normal),
        <= 0 => 0,
        FormerTiles => Nearest(Normal),
        FormerLargeTiles => Nearest(_large),
        { } glyph => Nearest(glyph),
    };

    /// <summary>Размер для настроек в этом положении ползунка: ноль — список, иначе силуэт ступени.</summary>
    /// <param name="position">Положение; лишнее зажимается в лестницу.</param>
    public double Size(int position) => position <= 0 ? ProjectSettings.List : Glyphs[Math.Min(position, Last) - 1];

    /// <summary>Ширина плитки при этом силуэте: по прямой через обычную и крупную ступени темы.</summary>
    /// <param name="glyph">Размер силуэта.</param>
    public double Width(double glyph) => _width + ((glyph - Normal) * (_largeWidth - _width) / (_large - Normal));

    /// <summary>Подложка предмета модели при этом силуэте: не крупнее обычной ступени.</summary>
    /// <param name="glyph">Размер силуэта.</param>
    public double Plate(double glyph) => Math.Min(glyph, Normal);

    /// <summary>Сколько строк у подписи при этом силуэте: мельче обычной ступени — одна.</summary>
    /// <param name="glyph">Размер силуэта.</param>
    /// <remarks>
    /// Плитка мельче обычной уже подписи в две строки: у малой ступени на строку приходится шесть-восемь
    /// букв, и почти каждое имя ломалось посреди слова — «Зависимо» и «сти», «Program.c» и «s». Строка
    /// с многоточием читается чище, а полное имя остаётся в подсказке и в строке под колонкой, как у
    /// мелких плиток Unity.
    /// </remarks>
    public int Lines(double glyph) => glyph < Normal ? 1 : 2;

    /// <summary>Положение ступени, ближайшей к размеру; из двух равноудалённых — меньшей.</summary>
    private int Nearest(double glyph)
    {
        var best = 0;

        for (var at = 1; at < Glyphs.Count; at++)
        {
            if (Math.Abs(Glyphs[at] - glyph) < Math.Abs(Glyphs[best] - glyph))
                best = at;
        }

        return best + 1;
    }

    private static double Size(IResourceHost host, string key)
    {
        if ((host.TryFindResource(key, out var value) || Application.Current?.TryFindResource(key, out value) == true)
            && value is double size)
        {
            return size;
        }

        throw new InvalidOperationException($"В теме нет ключа {key}: без него у плиток нет лестницы размеров.");
    }
}
