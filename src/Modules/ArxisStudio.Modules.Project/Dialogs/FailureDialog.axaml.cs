using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>Правка файлов не удалась — и почему.</summary>
public partial class FailureDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public FailureDialog()
    {
        InitializeComponent();

        Dismiss.Click += (_, _) => Close();
        Opened += (_, _) => Dismiss.Focus(NavigationMethod.Tab);
    }

    /// <summary>Говорит, что не получилось.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="message">Причина.</param>
    public static Task ShowAsync(Window owner, string message)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new FailureDialog();

        dialog.Message.Text = message;

        return dialog.ShowDialog(owner);
    }
}
