using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Reactive;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>Текст метки локальной истории.</summary>
public partial class LabelDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public LabelDialog()
    {
        InitializeComponent();

        Chosen.GetObservable(TextBox.TextProperty).Subscribe(new AnonymousObserver<string?>(text =>
            Confirm.IsEnabled = !string.IsNullOrWhiteSpace(text)));

        Opened += (_, _) => Chosen.Focus();
        Cancel.Click += (_, _) => Close(null);
        Confirm.Click += (_, _) => Submit();

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
            {
                Submit();
                key.Handled = true;
            }
        };
    }

    /// <summary>Спрашивает текст метки.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <returns>Текст; null — человек передумал.</returns>
    public static Task<string?> AskAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return new LabelDialog().ShowDialog<string?>(owner);
    }

    private void Submit()
    {
        if (Chosen.Text is { } text && !string.IsNullOrWhiteSpace(text))
            Close(text.Trim());
    }
}
