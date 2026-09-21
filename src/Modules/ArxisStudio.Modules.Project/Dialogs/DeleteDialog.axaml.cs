using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>
/// Вопрос перед удалением файлов и папок.
/// </summary>
/// <remarks>
/// Текст вопроса собирает спрашивающий: он знает, что выбрано, — диалог только показывает и
/// возвращает ответ.
/// </remarks>
public partial class DeleteDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public DeleteDialog()
    {
        InitializeComponent();

        Cancel.Click += (_, _) => Close(false);
        Confirm.Click += (_, _) => Close(true);
        Opened += (_, _) => Confirm.Focus(NavigationMethod.Tab);
    }

    /// <summary>Спрашивает, удалять ли.</summary>
    /// <param name="owner">Окно, которому принадлежит вопрос.</param>
    /// <param name="question">Что уйдёт.</param>
    /// <returns><c>true</c> — человек согласился; Esc и «Отмена» — <c>false</c>.</returns>
    public static async Task<bool> AskAsync(Window owner, string question)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new DeleteDialog();

        dialog.Question.Text = question;

        return await dialog.ShowDialog<bool?>(owner) == true;
    }
}
