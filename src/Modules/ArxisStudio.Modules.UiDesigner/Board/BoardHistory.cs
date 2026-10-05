using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Surface;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// История доски: всё, что сделано на ней, по порядку — места и состав форм и правки их текста.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ctrl+Z отменяет последнее, где бы его ни сделали.</b> На доске правят несколько форм и раскладку
/// разом, и человек помнит порядок своих действий, а не то, какому файлу какое принадлежит, — так
/// устроена история холста в Figma. Места, уборка и бросок — изменения самой доски; правка формы — шаг её
/// документа (<see cref="DocumentStep"/>), и отмена его — отмена в истории документа.
/// </para>
/// <para>
/// Историю холста ядра (<c>SurfaceHistory</c>) доска не берёт: та сама кладёт в себя каждый жест холста, а
/// жест внутри формы — не правка доски, а правка документа, и вернуть его геометрией холста нельзя: живое
/// дерево к этому времени перестроено из текста.
/// </para>
/// </remarks>
internal sealed class BoardHistory
{
    private readonly Stack<ISurfaceChange> _undo = new();
    private readonly Stack<ISurfaceChange> _redo = new();

    /// <summary>Состав истории сменился: сделано, отменено, возвращено или забыто.</summary>
    public event EventHandler? Changed;

    /// <summary>Есть ли что отменить.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Есть ли что вернуть.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Кладёт сделанное; вернуть отменённое после этого нельзя.</summary>
    /// <param name="change">Уже сделанное изменение.</param>
    public void Push(ISurfaceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _undo.Push(change);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Отменяет последнее.</summary>
    /// <returns>Было ли что отменить.</returns>
    public bool Undo()
    {
        if (!_undo.TryPop(out var change))
            return false;

        change.Revert();
        _redo.Push(change);
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Возвращает последнее отменённое.</summary>
    /// <returns>Было ли что вернуть.</returns>
    public bool Redo()
    {
        if (!_redo.TryPop(out var change))
            return false;

        change.Reapply();
        _undo.Push(change);
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Забывает всё: доска сменила решение.</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
            return;

        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Есть ли в истории шаги формы: её документ доска держит, пока их можно отменить.</summary>
    /// <param name="session">Сессия формы.</param>
    public bool Holds(FormSession session) =>
        _undo.Concat(_redo).Any(change => change is DocumentStep step && ReferenceEquals(step.Session, session));
}

/// <summary>Правка формы на доске — шаг истории её документа.</summary>
/// <param name="Session">Сессия формы.</param>
/// <remarks>
/// Отмена — отмена в истории документа, а не возврат прежнего текста: документ ведёт историю сам, и шаги,
/// сделанные в нём вкладкой той же формы, идут той же очередью.
/// </remarks>
internal sealed record DocumentStep(FormSession Session) : ISurfaceChange
{
    /// <inheritdoc/>
    public void Revert()
    {
        if (Session.Edits is { } edits)
            _ = edits.StepAsync(back: true);
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        if (Session.Edits is { } edits)
            _ = edits.StepAsync(back: false);
    }
}
