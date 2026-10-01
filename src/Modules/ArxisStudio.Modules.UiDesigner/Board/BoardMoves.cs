using ArxisStudio.Surface;
using Avalonia;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>Сдвиг одной карточки.</summary>
/// <param name="Card">Карточка.</param>
/// <param name="From">Где стояла.</param>
/// <param name="To">Где встала.</param>
internal sealed record BoardMove(FormCard Card, Point From, Point To);

/// <summary>
/// Сдвиги карточек, сделанные доской, а не жестом: раскладка целиком.
/// </summary>
/// <remarks>
/// Правки холста — тягу, стрелки — история ядра заворачивает сама. Раскладку холст не делает, её делает
/// доска, и в ту же историю она кладёт её этим изменением (ADR 0001 ядра: структурное — хосту). Места
/// пишутся в карточки, а холст забирает их привязкой, — та же дорога, какой ходит отмена тяги.
/// </remarks>
/// <param name="moves">Сдвиги.</param>
internal sealed class BoardMoves(IReadOnlyList<BoardMove> moves) : ISurfaceChange
{
    /// <summary>Сдвиги — тестам.</summary>
    public IReadOnlyList<BoardMove> Moves => moves;

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var move in moves)
            move.Card.Location = move.From;
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        foreach (var move in moves)
            move.Card.Location = move.To;
    }
}
