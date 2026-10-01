namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Точка доски в мировых координатах холста.</summary>
/// <param name="X">Слева направо.</param>
/// <param name="Y">Сверху вниз.</param>
internal readonly record struct Spot(double X, double Y);

/// <summary>Шаг раскладки: карточка вместе с зазором до соседа.</summary>
/// <param name="X">По горизонтали.</param>
/// <param name="Y">По вертикали.</param>
internal readonly record struct Pitch(double X, double Y);

/// <summary>
/// Где встают карточки, которым место не назначено, и как доска упорядочивается целиком.
/// </summary>
/// <remarks>
/// Сетка почти квадратная — столбцов столько, сколько корень из числа карточек, округлённый вверх: доска
/// на тридцать форм вписывается в окно шестью рядами по шесть, а не лентой в тридцать. Порядок —
/// порядок решения (проект, путь), так что соседи на доске — соседи и в дереве проекта.
/// <para>
/// Новые карточки не двигают тех, кого человек поставил сам: они встают рядами под занятой частью
/// доски, начиная с её левого края. Место у карточки появляется один раз — дальше оно её и хранится.
/// </para>
/// </remarks>
internal static class BoardLayout
{
    /// <summary>Столбцов в сетке на столько карточек.</summary>
    /// <param name="count">Число карточек.</param>
    public static int Columns(int count) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));

    /// <summary>
    /// Места сеткой от точки начала.
    /// </summary>
    /// <param name="count">Сколько мест.</param>
    /// <param name="origin">Левый верхний угол первой карточки.</param>
    /// <param name="pitch">Шаг сетки.</param>
    /// <param name="columns">Столбцов; не задано — по <see cref="Columns"/>.</param>
    public static IReadOnlyList<Spot> Grid(int count, Spot origin, Pitch pitch, int? columns = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var across = Math.Max(1, columns ?? Columns(count));
        var spots = new Spot[count];

        for (var index = 0; index < count; index++)
            spots[index] = new Spot(origin.X + index % across * pitch.X, origin.Y + index / across * pitch.Y);

        return spots;
    }

    /// <summary>
    /// Места для новых карточек — под теми, что уже стоят.
    /// </summary>
    /// <param name="placed">Места поставленных карточек: левые верхние углы.</param>
    /// <param name="count">Сколько новых.</param>
    /// <param name="pitch">Шаг сетки.</param>
    /// <returns>Места новых в порядке их прихода.</returns>
    /// <remarks>
    /// Первый ряд новых — на шаг ниже самой нижней поставленной карточки: шаг отмерен от её верха и
    /// включает и высоту, и зазор. Столбцов у новых — по их собственному числу: десяток новых форм рядом
    /// с одной старой встаёт квадратом, а не лентой ширины старой доски.
    /// </remarks>
    public static IReadOnlyList<Spot> Below(IReadOnlyCollection<Spot> placed, int count, Pitch pitch)
    {
        ArgumentNullException.ThrowIfNull(placed);

        var origin = placed.Count == 0
            ? new Spot(0, 0)
            : new Spot(placed.Min(spot => spot.X), placed.Max(spot => spot.Y) + pitch.Y);

        return Grid(count, origin, pitch);
    }
}
