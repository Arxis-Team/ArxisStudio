using ArxisStudio.ProjectSystem;
using ArxisStudio.Surface;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>Форма, которую несут на доску: её путь, карточка для заготовки и стоит ли она уже на доске.</summary>
/// <param name="Path">Путь формы.</param>
/// <param name="Card">
/// Карточка: стоящая на доске — она сама, убранная — та, что ушла, а незнакомая доске — новая, только для
/// заготовки.
/// </param>
/// <param name="OnBoard">Карточка уже на доске: отпускание её передвинет, а не вернёт.</param>
internal sealed record Landing(CanonicalPath Path, FormCard Card, bool OnBoard);

/// <summary>
/// Формы, поставленные на доску перетаскиванием, — одной записью истории холста.
/// </summary>
/// <remarks>
/// Убранные возвращаются, стоящие передвигаются, и всё это одно действие человека: Ctrl+Z сперва
/// уводит карточки обратно, потом снимает вернувшиеся — порядок, обратный сделанному. Вернувшаяся
/// карточка встаёт сначала на своё прежнее место и уже оттуда едет в точку отпускания: отмена, вернув
/// её туда, запомнит прежнее место, а не точку броска.
/// </remarks>
/// <param name="returned">Вернувшиеся на доску; пусто — вернувшихся нет.</param>
/// <param name="moves">Сдвиги к точке отпускания; пусто — никто не сдвинулся.</param>
internal sealed class BoardLanded(BoardPresence? returned, BoardMoves? moves) : ISurfaceChange
{
    /// <summary>Вернувшиеся на доску — тестам.</summary>
    public BoardPresence? Returned => returned;

    /// <summary>Сдвиги — тестам.</summary>
    public BoardMoves? Moves => moves;

    /// <inheritdoc/>
    public void Revert()
    {
        moves?.Revert();
        returned?.Revert();
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        returned?.Reapply();
        moves?.Reapply();
    }
}
