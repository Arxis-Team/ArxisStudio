using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Диалог настроек терминала: оболочка по умолчанию, кегль, история, курсор.
/// </summary>
/// <remarks>
/// Свой диалог, а не экран настроек студии: у студии его пока нет, а ждать его
/// терминалу незачем — настройки объявлены в манифесте, и когда экран
/// появится, он покажет их сам, теми же ключами.
/// </remarks>
public static class SettingsDialog
{
    /// <summary>Показывает диалог; true — настройки записаны.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="strings">Строки модуля.</param>
    /// <param name="settings">Настройки модуля.</param>
    /// <param name="shells">Оболочки, из которых выбирать.</param>
    public static async Task<bool> EditAsync(
        Window owner, IStudioStrings strings, IStudioSettings settings, IReadOnlyList<ShellProfile> shells)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shells);

        var current = TerminalSettings.Read(settings);
        var chosen = Math.Max(0, shells.ToList().FindIndex(shell => string.Equals(shell.Id, current.Shell, StringComparison.Ordinal)));

        var shell = new AxComboBox
        {
            ItemsSource = shells.Select(profile => profile.Title).ToList(),
            SelectedIndex = shells.Count > 0 ? chosen : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var font = Field(strings["terminal.settings.fontSize"], current.FontSize.ToString(CultureInfo.InvariantCulture));
        var scrollback = Field(strings["terminal.settings.scrollback"], current.Scrollback.ToString(CultureInfo.InvariantCulture));
        var blink = new AxCheckBox { Content = strings["terminal.settings.cursorBlink"], IsChecked = current.CursorBlink };

        Avalonia.Automation.AutomationProperties.SetName(shell, strings["terminal.settings.shell"]);

        var save = new AxButton { Content = strings["common.save"], MinWidth = 96, Classes = { "accent" } };
        var cancel = new AxButton { Content = strings["common.cancel"], MinWidth = 96 };

        var form = new StackPanel
        {
            Spacing = 10,
            Width = 320,
            Children =
            {
                Labeled(strings["terminal.settings.shell"], shell),
                Labeled(strings["terminal.settings.fontSize"], font),
                Labeled(strings["terminal.settings.scrollback"], scrollback),
                blink,
            },
        };

        var dialog = new AxDialog
        {
            Title = strings["terminal.settings.title"],
            Content = form,
            Buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, save },
            },
        };

        cancel.Click += (_, _) => dialog.Close(false);
        save.Click += (_, _) => Submit();

        form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };

        return await dialog.ShowDialog<bool>(owner);

        void Submit()
        {
            var shellId = shell.SelectedIndex >= 0 && shell.SelectedIndex < shells.Count
                ? shells[shell.SelectedIndex].Id
                : string.Empty;

            Parse(font.Text, scrollback.Text, shellId, blink.IsChecked == true, current).Write(settings);
            dialog.Close(true);
        }
    }

    /// <summary>
    /// Настройки из набранного; что не разобрать — остаётся прежним.
    /// </summary>
    /// <param name="fontSize">Кегль строкой.</param>
    /// <param name="scrollback">История строкой.</param>
    /// <param name="shellId">Имя выбранной оболочки.</param>
    /// <param name="cursorBlink">Мигает ли курсор.</param>
    /// <param name="fallback">Что было до правки.</param>
    public static TerminalSettings Parse(string? fontSize, string? scrollback, string shellId, bool cursorBlink, TerminalSettings fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        var font = double.TryParse(fontSize?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? TerminalSettings.ClampFontSize(size)
            : fallback.FontSize;

        var history = int.TryParse(scrollback?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines)
            ? TerminalSettings.ClampScrollback(lines)
            : fallback.Scrollback;

        return new TerminalSettings(shellId, font, history, cursorBlink);
    }

    private static AxTextBox Field(string name, string text)
    {
        var box = new AxTextBox { Text = text };

        Avalonia.Automation.AutomationProperties.SetName(box, name);

        return box;
    }

    private static StackPanel Labeled(string label, Control field) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label }, field },
    };
}
