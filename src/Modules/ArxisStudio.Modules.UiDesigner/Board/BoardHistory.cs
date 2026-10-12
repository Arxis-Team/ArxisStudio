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
/// <para>
/// <b>Один жест — один шаг.</b> Жест задевает сразу несколько: карточку, взятую за левый край, двигает доска, а
/// ширину пишет её документ; тяга по двум формам правит два документа; Delete убирает элемент одной формы и
/// другую форму целиком. Шаги такого жеста собираются (<see cref="Gather"/>) и ложатся одним: иначе Ctrl+Z
/// отменял половину жеста, и карточка оставалась сдвинутой (запись 417).
/// </para>
/// </remarks>
internal sealed class BoardHistory
{
    private readonly Stack<ISurfaceChange> _undo = new();
    private readonly Stack<ISurfaceChange> _redo = new();
    private List<ISurfaceChange>? _gathered;
    private int _gathering;

    /// <summary>Состав истории сменился: сделано, отменено, возвращено или забыто.</summary>
    public event EventHandler? Changed;

    /// <summary>Есть ли что отменить.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Есть ли что вернуть.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Кладёт сделанное; вернуть отменённое после этого нельзя.</summary>
    /// <param name="change">Уже сделанное изменение.</param>
    /// <remarks>Пока идёт сбор, сделанное ложится в него, а шагом станет, когда сбор кончится.</remarks>
    public void Push(ISurfaceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (_gathered is { } gathered)
        {
            gathered.Add(change);
            return;
        }

        _undo.Push(change);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Собирает сделанное одним жестом в один шаг: он ляжет, когда отпустят последний сбор.</summary>
    /// <returns>Сбор; отпущенный повторно ничего не делает.</returns>
    public IDisposable Gather()
    {
        if (_gathering++ == 0)
            _gathered = [];

        return new Gathering(this);
    }

    /// <summary>Отпускает сбор; последний кладёт собранное шагом.</summary>
    private void Release()
    {
        if (--_gathering > 0 || _gathered is not { } gathered)
            return;

        _gathered = null;

        if (gathered.Count > 0)
            Push(gathered.Count == 1 ? gathered[0] : new BoardSteps(gathered));
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
        _undo.Concat(_redo).Concat(_gathered ?? []).SelectMany(BoardSteps.Flatten)
            .Any(change => change is DocumentStep step && ReferenceEquals(step.Session, session));

    /// <summary>Сбор одного жеста.</summary>
    private sealed class Gathering(BoardHistory history) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            history.Release();
        }
    }
}

/// <summary>Шаги одного жеста — одним шагом истории доски.</summary>
/// <param name="Steps">Шаги в том порядке, в каком их сделали.</param>
/// <remarks>Отмена идёт с конца, как отменялись бы сами шаги; возврат — с начала.</remarks>
internal sealed record BoardSteps(IReadOnlyList<ISurfaceChange> Steps) : ISurfaceChange
{
    /// <inheritdoc/>
    public void Revert()
    {
        for (var at = Steps.Count - 1; at >= 0; at--)
            Steps[at].Revert();
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        foreach (var step in Steps)
            step.Reapply();
    }

    /// <summary>Шаг и всё, что в нём собрано.</summary>
    /// <param name="change">Шаг истории.</param>
    public static IEnumerable<ISurfaceChange> Flatten(ISurfaceChange change) =>
        change is BoardSteps steps ? steps.Steps.SelectMany(Flatten) : [change];
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
