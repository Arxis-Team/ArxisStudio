using ArxisStudio.Surface;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Карточки, убранные с доски или возвращённые на неё, — одной записью истории холста.
/// </summary>
/// <remarks>
/// Состав коллекции холст не меняет (ADR 0001 ядра): удаление по клавише он превращает в запрос, а
/// выполняет его хозяин коллекции и кладёт в ту же историю своим изменением. Поэтому Ctrl+Z после
/// «Убрать с доски» возвращает карточки на места, а Ctrl+Y убирает снова — той же очередью, что тяга.
/// </remarks>
/// <param name="Board">Доска.</param>
/// <param name="Cards">Карточки.</param>
/// <param name="Removing">Уборка — или возврат.</param>
internal sealed record BoardPresence(BoardModel Board, IReadOnlyList<FormCard> Cards, bool Removing) : ISurfaceChange
{
    /// <inheritdoc/>
    public void Revert()
    {
        if (Removing)
            Board.Put(Cards);
        else
            Board.Take(Cards);
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        if (Removing)
            Board.Take(Cards);
        else
            Board.Put(Cards);
    }
}
