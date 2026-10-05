namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Точка доски в мировых координатах холста.</summary>
/// <param name="X">Слева направо.</param>
/// <param name="Y">Сверху вниз.</param>
internal readonly record struct Spot(double X, double Y);

/// <summary>Форма для раскладки: её размер и место над ней — под заголовок окна.</summary>
/// <param name="Width">Ширина формы.</param>
/// <param name="Height">Высота формы.</param>
/// <param name="Above">Сколько над формой занимает её хром: у окна — заголовок, у остальных — ничего.</param>
internal readonly record struct Box(double Width, double Height, double Above);

/// <summary>
/// Где встают формы, которым место не назначено, и как доска упорядочивается целиком.
/// </summary>
/// <remarks>
/// <para>
/// Формы разного размера встают рядами, слева направо: следующая — через зазор от предыдущей, а не
/// поместилась в ряд — в новый, через зазор под самой высокой формой ряда. Ширина ряда — столько самых
/// широких форм, сколько корень из их числа, округлённый вверх: в ряд встаёт не меньше форм, чем столбцов,
/// и доска на тридцать форм вписывается в окно шестью рядами, а не лентой. Порядок — порядок решения
/// (проект, путь), так что соседи на доске — соседи и в дереве проекта.
/// </para>
/// <para>
/// Место формы — её левый верхний угол; заголовок окна стоит над ним, и ряд отводит под него место:
/// заголовок не ложится на форму ряда выше.
/// </para>
/// <para>
/// Новые формы не двигают тех, кого человек поставил сам: они встают рядами под занятой частью доски,
/// начиная с её левого края. Место у формы появляется один раз — дальше оно её и хранится.
/// </para>
/// </remarks>
internal static class BoardLayout
{
    /// <summary>Сколько форм в ряду на столько форм.</summary>
    /// <param name="count">Число форм.</param>
    public static int Columns(int count) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));

    /// <summary>
    /// Места рядами от точки начала.
    /// </summary>
    /// <param name="boxes">Формы в порядке раскладки.</param>
    /// <param name="origin">Левый верхний угол первого ряда — вместе с заголовком окна, если первая форма окно.</param>
    /// <param name="gap">Зазор между формами и рядами.</param>
    /// <returns>Левые верхние углы форм — без заголовка окна.</returns>
    public static IReadOnlyList<Spot> Rows(IReadOnlyList<Box> boxes, Spot origin, double gap)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        var spots = new Spot[boxes.Count];

        if (boxes.Count == 0)
            return spots;

        var limit = Columns(boxes.Count) * (boxes.Max(box => box.Width) + gap) - gap;
        var x = origin.X;
        var top = origin.Y;
        var height = 0.0;

        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];

            if (x > origin.X && x + box.Width > origin.X + limit)
            {
                top += height + gap;
                x = origin.X;
                height = 0;
            }

            spots[index] = new Spot(x, top + box.Above);
            x += box.Width + gap;
            height = Math.Max(height, box.Above + box.Height);
        }

        return spots;
    }

    /// <summary>
    /// Места для новых форм — под теми, что уже стоят.
    /// </summary>
    /// <param name="placed">Поставленные формы: место и размер.</param>
    /// <param name="fresh">Новые формы в порядке их прихода.</param>
    /// <param name="gap">Зазор между формами и рядами.</param>
    /// <returns>Места новых в порядке их прихода.</returns>
    /// <remarks>
    /// Первый ряд новых — через зазор под самым низом поставленных. Ширина рядов — по числу новых: десяток
    /// новых форм рядом с одной старой встаёт квадратом, а не лентой ширины старой доски.
    /// </remarks>
    public static IReadOnlyList<Spot> Below(IReadOnlyCollection<(Spot Spot, Box Box)> placed, IReadOnlyList<Box> fresh, double gap)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(fresh);

        var origin = placed.Count == 0
            ? new Spot(0, 0)
            : new Spot(placed.Min(form => form.Spot.X), placed.Max(form => form.Spot.Y + form.Box.Height) + gap);

        return Rows(fresh, origin, gap);
    }
}
