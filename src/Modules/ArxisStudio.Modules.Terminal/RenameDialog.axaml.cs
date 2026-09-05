using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.Input;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Диалог переименования сеанса.
/// </summary>
/// <remarks>
/// Подпись вкладки по умолчанию — имя оболочки, и у трёх PowerShell подряд она
/// одна и та же с номерами. Человек, у которого в одном терминале сборка, а в
/// другом сервер, различает их по имени, которое дал сам, — и это единственное
/// место, где подпись вкладки перестаёт следовать за оболочкой.
/// </remarks>
public partial class RenameDialog : AxDialog
{
    /// <summary>Сколько знаков помещается в подпись вкладки, не выдавливая соседей.</summary>
    public const int MaxLength = 40;

    /// <summary>Собирает диалог из разметки.</summary>
    public RenameDialog()
    {
        InitializeComponent();

        // Пустое имя оставило бы вкладку без подписи — а по ней её и находят.
        // Слушается свойство, а не событие правки: имя ставит и сам диалог,
        // открываясь, и кнопка обязана знать об этом так же, как о наборе.
        Chosen.GetObservable(TextBox.TextProperty)
            .Subscribe(new AnonymousObserver<string?>(text => Save.IsEnabled = Clean(text) is not null));

        Opened += (_, _) =>
        {
            Chosen.Focus();
            Chosen.SelectAll();
        };

        Cancel.Click += (_, _) => Close(null);
        Save.Click += (_, _) => Submit();

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };
    }

    /// <summary>
    /// Спрашивает новое имя; null — человек передумал.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="current">Нынешняя подпись вкладки.</param>
    public static Task<string?> AskAsync(Window owner, string current)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new RenameDialog();

        dialog.Chosen.Text = current;

        return dialog.ShowDialog<string?>(owner);
    }

    /// <summary>Имя без краевых пробелов; null — имени нет.</summary>
    /// <param name="text">Что набрали.</param>
    public static string? Clean(string? text) =>
        text?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, MaxLength)] : null;

    private void Submit()
    {
        if (Clean(Chosen.Text) is { } chosen)
            Close(chosen);
    }
}
