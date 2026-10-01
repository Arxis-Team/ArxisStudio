using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Холст Surface в цветах студии: кисти ядра — токенами палитры Arxis.
/// </summary>
/// <remarks>
/// Ядро рисует свои цвета ключами <c>Surface.*</c> из собственной темы, и у него они свои числа. Значение
/// цвета у студии объявляется в её теме и больше нигде, поэтому здесь чисел нет: ключ ядра получает кисть
/// токена, найденную в теме для варианта, которым сейчас светится холст.
/// <para>
/// Кисть кладётся в ресурсы самого холста плоской записью. Свои записи словарь находит раньше
/// тематических и подключённых (запись 322 плана), поэтому плоская запись видна в любом варианте — и
/// при смене варианта её перекладывают: вид зовёт <see cref="Apply"/> на
/// <c>ActualThemeVariantChanged</c>.
/// </para>
/// <para>
/// Линии сетки — полупрозрачные слои наведения и нажатия, а не обводки: только эта пара токенов даёт
/// две различимые ступени тона на утопленной подложке в обоих вариантах. Тонкая обводка темы на светлой
/// подложке почти сливается с дорожкой, и крупная линия от мелкой не отличалась бы.
/// </para>
/// </remarks>
internal static class BoardLooks
{
    /// <summary>Ключ ядра — токен палитры.</summary>
    public static IReadOnlyList<(string Surface, string Token)> Map { get; } =
    [
        ("Surface.BackgroundBrush", "AxSurfaceSunkenBrush"),
        ("Surface.Grid.BackgroundBrush", "AxSurfaceSunkenBrush"),
        ("Surface.Grid.LineBrush", "AxHoverBrush"),
        ("Surface.Grid.MajorLineBrush", "AxPressedBrush"),
        ("Surface.MarqueeStrokeBrush", "AxAccentBrush"),
        ("Surface.MarqueeFillBrush", "AxHoverBrush"),
        ("SurfaceItem.SelectionBrush", "AxAccentBrush"),
        ("SurfaceItem.HoverBrush", "AxStrokeStrongBrush"),
        ("Surface.Simplified.ItemFill", "AxSurfacePanelBrush"),
        ("Surface.Simplified.ItemStroke", "AxStrokeSubtleBrush"),
    ];

    /// <summary>
    /// Перекладывает кисти ядра под нынешний вариант темы холста.
    /// </summary>
    /// <param name="surface">Холст.</param>
    /// <remarks>
    /// Токен ищется от холста вверх, а не у приложения: вариант может быть задан окну или панели, а не
    /// всему приложению. Токена нет — ключ ядра остаётся своим, и холст рисуется цветами библиотеки.
    /// </remarks>
    public static void Apply(Control surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var variant = surface.ActualThemeVariant;

        foreach (var (key, token) in Map)
        {
            if (surface.TryFindResource(token, variant, out var brush) && brush is not null)
                surface.Resources[key] = brush;
            else
                surface.Resources.Remove(key);
        }
    }
}
