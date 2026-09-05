using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal.Shells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.Input;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Диалог нового сеанса SSH: хост, пользователь, порт.
/// </summary>
/// <remarks>
/// Три поля, а не строка подключения: строку человек набрал бы в самом
/// терминале. Диалог нужен тем, кто адрес помнит, а синтаксис <c>ssh</c> — нет.
/// Всё остальное — ключи, агент, известные хосты — остаётся у системного клиента.
/// </remarks>
public partial class SshDialog : AxDialog
{
    /// <summary>Собирает диалог из разметки.</summary>
    public SshDialog()
    {
        InitializeComponent();

        PortBox.Text = ShellCatalog.DefaultSshPort.ToString(CultureInfo.InvariantCulture);

        // Слушается свойство, а не событие правки: так кнопка знает и о том,
        // что поле заполнили не с клавиатуры.
        HostBox.GetObservable(TextBox.TextProperty)
            .Subscribe(new AnonymousObserver<string?>(host => Connect.IsEnabled = !string.IsNullOrWhiteSpace(host)));

        Opened += (_, _) => HostBox.Focus();
        Cancel.Click += (_, _) => Close(null);
        Connect.Click += (_, _) => Submit();

        Form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };
    }

    /// <summary>Спрашивает адрес; null — человек передумал.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    public static Task<ShellProfile?> AskAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return new SshDialog().ShowDialog<ShellProfile?>(owner);
    }

    /// <summary>Порт из поля; пустое и мусор — порт по умолчанию.</summary>
    /// <param name="text">Что набрали.</param>
    public static int Port(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : ShellCatalog.DefaultSshPort;

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
            return;

        Close(ShellCatalog.Ssh(HostBox.Text, UserBox.Text, Port(PortBox.Text), ShellCatalog.CurrentPlatform));
    }
}
