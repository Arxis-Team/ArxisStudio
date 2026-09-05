using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Диалог настроек терминала: оболочка по умолчанию, кегль, история, курсор.
/// </summary>
/// <remarks>
/// Свой диалог, а не экран настроек студии: у студии его пока нет, а ждать его
/// терминалу незачем — настройки объявлены в манифесте, и когда экран
/// появится, он покажет их сам, теми же ключами.
/// </remarks>
public partial class SettingsDialog : AxDialog
{
    private IStudioSettings? _settings;
    private IReadOnlyList<ShellProfile> _shells = [];
    private TerminalSettings _current = TerminalSettings.Default;

    /// <summary>Собирает диалог из разметки.</summary>
    public SettingsDialog()
    {
        InitializeComponent();

        Cancel.Click += (_, _) => Close(false);
        Save.Click += (_, _) => Submit();

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };
    }

    /// <summary>Показывает диалог; true — настройки записаны.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="settings">Настройки модуля.</param>
    /// <param name="shells">Оболочки, из которых выбирать.</param>
    public static Task<bool> EditAsync(Window owner, IStudioSettings settings, IReadOnlyList<ShellProfile> shells)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shells);

        var dialog = new SettingsDialog();

        dialog.Fill(settings, shells);

        return dialog.ShowDialog<bool>(owner);
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

    /// <summary>Ставит в поля то, что записано сейчас.</summary>
    private void Fill(IStudioSettings settings, IReadOnlyList<ShellProfile> shells)
    {
        _settings = settings;
        _shells = shells;
        _current = TerminalSettings.Read(settings);

        var chosen = Math.Max(0, shells.ToList().FindIndex(
            shell => string.Equals(shell.Id, _current.Shell, StringComparison.Ordinal)));

        ShellBox.ItemsSource = shells.Select(profile => profile.Title).ToList();
        ShellBox.SelectedIndex = shells.Count > 0 ? chosen : -1;
        FontSizeBox.Text = _current.FontSize.ToString(CultureInfo.InvariantCulture);
        ScrollbackBox.Text = _current.Scrollback.ToString(CultureInfo.InvariantCulture);
        BlinkBox.IsChecked = _current.CursorBlink;
    }

    private void Submit()
    {
        if (_settings is null)
            return;

        var shellId = ShellBox.SelectedIndex >= 0 && ShellBox.SelectedIndex < _shells.Count
            ? _shells[ShellBox.SelectedIndex].Id
            : string.Empty;

        Parse(FontSizeBox.Text, ScrollbackBox.Text, shellId, BlinkBox.IsChecked == true, _current).Write(_settings);
        Close(true);
    }
}
