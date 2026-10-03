using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>
/// Отмена и возврат из панелей модуля: клавиши платформы — Ctrl+Z и Ctrl+Y, а на macOS свои, — ведут
/// историю формы впереди.
/// </summary>
/// <remarks>
/// История у формы одна, и правят её все: холст, иерархия, инспектор, палитра. Ctrl+Z в иерархии,
/// отменяющий то, что сделано в инспекторе, — то же, что делают Visual Studio и Blend.
/// </remarks>
internal static class HistoryKeys
{
    /// <summary>Шаг истории формы по клавише.</summary>
    /// <param name="e">Нажатие.</param>
    /// <param name="view">Панель, в которой нажали: у её окна спрашиваются клавиши платформы.</param>
    /// <param name="bench">Верстак: форма впереди.</param>
    /// <returns>Была ли это отмена или возврат и есть ли что отменять: тогда нажатие обработано.</returns>
    public static bool Step(KeyEventArgs e, Visual view, DesignerWorkbench? bench)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(view);

        if (bench?.Form?.Edits is not { } edits || view.GetPlatformSettings()?.HotkeyConfiguration is not { } keys)
            return false;

        bool back;

        if (keys.Undo.Any(gesture => gesture.Matches(e)))
            back = true;
        else if (keys.Redo.Any(gesture => gesture.Matches(e)))
            back = false;
        else
            return false;

        e.Handled = true;
        _ = edits.StepAsync(back);

        return true;
    }
}
