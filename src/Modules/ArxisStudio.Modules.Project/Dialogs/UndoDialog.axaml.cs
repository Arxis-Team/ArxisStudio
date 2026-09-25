using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>
/// Вопрос перед отменой последнего действия над файлами.
/// </summary>
/// <remarks>Что отменится, называет метка действия — та же, под которой оно лежит в истории.</remarks>
public partial class UndoDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public UndoDialog()
    {
        InitializeComponent();

        Keep.Click += (_, _) => Close(false);
        Confirm.Click += (_, _) => Close(true);
        Opened += (_, _) => Confirm.Focus(NavigationMethod.Tab);
    }

    /// <summary>Спрашивает, отменять ли.</summary>
    /// <param name="owner">Окно, которому принадлежит вопрос.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="label">Метка действия.</param>
    /// <returns><c>true</c> — отменить; Esc и «Не отменять» — <c>false</c>.</returns>
    public static async Task<bool> AskAsync(Window owner, IStudioStrings strings, string label)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);

        var dialog = new UndoDialog();

        dialog.Question.Text = strings.Format("project.undo.question", label);

        return await dialog.ShowDialog<bool?>(owner) == true;
    }
}
