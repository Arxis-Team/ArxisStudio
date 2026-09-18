using ArxisStudio.Controls;
using Avalonia;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Размеры плитки на текущей ступени, поставленные списку плиток: разметка плитки берёт их у своего
/// списка привязкой.
/// </summary>
/// <remarks>
/// Ступеней столько, сколько даёт тема, и стиль на каждую пришлось бы переписывать с каждой правкой
/// лестницы. Числа считает <see cref="TileLadder"/> из ключей темы, колонка ставит их списку, а
/// плитка только читает.
/// </remarks>
internal static class TileMetrics
{
    /// <summary>Сторона силуэта и места под значок.</summary>
    public static readonly AttachedProperty<double> GlyphProperty =
        AvaloniaProperty.RegisterAttached<AxListBox, double>("Glyph", typeof(TileMetrics));

    /// <summary>Ширина плитки: силуэт и подпись под ним.</summary>
    public static readonly AttachedProperty<double> WidthProperty =
        AvaloniaProperty.RegisterAttached<AxListBox, double>("Width", typeof(TileMetrics));

    /// <summary>Сторона подложки предмета модели.</summary>
    public static readonly AttachedProperty<double> PlateProperty =
        AvaloniaProperty.RegisterAttached<AxListBox, double>("Plate", typeof(TileMetrics));

    /// <summary>Сколько строк у подписи плитки.</summary>
    public static readonly AttachedProperty<int> LinesProperty =
        AvaloniaProperty.RegisterAttached<AxListBox, int>("Lines", typeof(TileMetrics), 2);

    /// <summary>Сторона силуэта списка.</summary>
    /// <param name="list">Список плиток.</param>
    public static double GetGlyph(AxListBox list) => list.GetValue(GlyphProperty);

    /// <summary>Ставит сторону силуэта списку.</summary>
    /// <param name="list">Список плиток.</param>
    /// <param name="value">Сторона.</param>
    public static void SetGlyph(AxListBox list, double value) => list.SetValue(GlyphProperty, value);

    /// <summary>Ширина плитки списка.</summary>
    /// <param name="list">Список плиток.</param>
    public static double GetWidth(AxListBox list) => list.GetValue(WidthProperty);

    /// <summary>Ставит ширину плитки списку.</summary>
    /// <param name="list">Список плиток.</param>
    /// <param name="value">Ширина.</param>
    public static void SetWidth(AxListBox list, double value) => list.SetValue(WidthProperty, value);

    /// <summary>Сторона подложки списка.</summary>
    /// <param name="list">Список плиток.</param>
    public static double GetPlate(AxListBox list) => list.GetValue(PlateProperty);

    /// <summary>Ставит сторону подложки списку.</summary>
    /// <param name="list">Список плиток.</param>
    /// <param name="value">Сторона.</param>
    public static void SetPlate(AxListBox list, double value) => list.SetValue(PlateProperty, value);

    /// <summary>Сколько строк у подписи плитки списка.</summary>
    /// <param name="list">Список плиток.</param>
    public static int GetLines(AxListBox list) => list.GetValue(LinesProperty);

    /// <summary>Ставит списку, сколько строк у подписи плитки.</summary>
    /// <param name="list">Список плиток.</param>
    /// <param name="value">Строк.</param>
    public static void SetLines(AxListBox list, int value) => list.SetValue(LinesProperty, value);

    /// <summary>Ставит списку размеры ступени.</summary>
    /// <param name="list">Список плиток.</param>
    /// <param name="ladder">Лестница темы.</param>
    /// <param name="glyph">Силуэт ступени.</param>
    public static void Apply(AxListBox list, TileLadder ladder, double glyph)
    {
        SetGlyph(list, glyph);
        SetWidth(list, ladder.Width(glyph));
        SetPlate(list, ladder.Plate(glyph));
        SetLines(list, ladder.Lines(glyph));
    }
}
