using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

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
public static class RenameDialog
{
    /// <summary>Сколько знаков помещается в подпись вкладки, не выдавливая соседей.</summary>
    public const int MaxLength = 40;

    /// <summary>
    /// Спрашивает новое имя; null — человек передумал.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="strings">Строки модуля.</param>
    /// <param name="current">Нынешняя подпись вкладки.</param>
    public static async Task<string?> AskAsync(Window owner, IStudioStrings strings, string current)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);

        var name = new AxTextBox { Text = current, MaxLength = MaxLength, Width = 320 };

        Avalonia.Automation.AutomationProperties.SetName(name, strings["terminal.rename.name"]);

        var rename = new AxButton { Content = strings["common.save"], MinWidth = 96, Classes = { "accent" } };
        var cancel = new AxButton { Content = strings["common.cancel"], MinWidth = 96 };

        var form = new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = strings["terminal.rename.name"] }, name },
        };

        var dialog = new AxDialog
        {
            Title = strings["terminal.rename.title"],
            Content = form,
            Buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, rename },
            },
        };

        // Пустое имя оставило бы вкладку без подписи — а по ней её и находят.
        name.TextChanged += (_, _) => rename.IsEnabled = Clean(name.Text) is not null;

        dialog.Opened += (_, _) =>
        {
            name.Focus();
            name.SelectAll();
        };

        cancel.Click += (_, _) => dialog.Close(null);
        rename.Click += (_, _) => Submit();

        form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };

        return await dialog.ShowDialog<string?>(owner);

        void Submit()
        {
            if (Clean(name.Text) is { } chosen)
                dialog.Close(chosen);
        }
    }

    /// <summary>Имя без краевых пробелов; null — имени нет.</summary>
    /// <param name="text">Что набрали.</param>
    public static string? Clean(string? text) =>
        text?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, MaxLength)] : null;
}
