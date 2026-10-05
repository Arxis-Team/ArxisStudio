using System.Runtime.CompilerServices;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Доска модуля, когда она построена: её спрашивает редактор документов, чтобы показать форму на доске, а не
/// вкладкой.
/// </summary>
/// <remarks>
/// Одна на контекст модуля — панель у студии одна. Строит доску студия, когда её показывают, и тот, кто
/// показал, ждёт постройки (<see cref="ShownAsync"/>): между просьбой показать и панелью проходит
/// раскладка окна. Панель приходит сюда сама, построившись, и уходит, прощаясь.
/// </remarks>
internal sealed class Boards
{
    private static readonly ConditionalWeakTable<IStudioContext, Boards> Table = [];

    private BoardPanel? _board;
    private TaskCompletionSource<BoardPanel> _built = Waiting();

    /// <summary>Доска контекста модуля.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static Boards Of(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Table.GetValue(context, static _ => new Boards());
    }

    /// <summary>Доска построилась.</summary>
    /// <param name="board">Доска.</param>
    public void Built(BoardPanel board)
    {
        ArgumentNullException.ThrowIfNull(board);

        _board = board;
        _built.TrySetResult(board);
    }

    /// <summary>Доска попрощалась: следующую будут ждать заново.</summary>
    /// <param name="board">Доска.</param>
    public void Released(BoardPanel board)
    {
        if (!ReferenceEquals(_board, board))
            return;

        _board = null;
        _built = Waiting();
    }

    /// <summary>Доска, когда она построена; не построилась за <paramref name="wait"/> — null.</summary>
    /// <param name="wait">Сколько ждать постройки.</param>
    public async Task<BoardPanel?> ShownAsync(TimeSpan wait)
    {
        if (_board is { } board)
            return board;

        var built = _built.Task;

        return await Task.WhenAny(built, Task.Delay(wait)) == built ? await built : null;
    }

    private static TaskCompletionSource<BoardPanel> Waiting() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
