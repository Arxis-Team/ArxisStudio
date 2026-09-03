using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Диалог нового сеанса SSH: хост, пользователь, порт.
/// </summary>
/// <remarks>
/// Три поля, а не строка подключения: строку человек набрал бы в самом
/// терминале. Диалог нужен тем, кто адрес помнит, а синтаксис <c>ssh</c> — нет.
/// Всё остальное — ключи, агент, известные хосты — остаётся у системного клиента.
/// </remarks>
public static class SshDialog
{
    /// <summary>Спрашивает адрес; null — человек передумал.</summary>
    /// <param name="owner">Окно, которому принадлежит диалог.</param>
    /// <param name="strings">Строки модуля.</param>
    public static async Task<ShellProfile?> AskAsync(Window owner, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(strings);

        var host = Field(strings["terminal.ssh.host"]);
        var user = Field(strings["terminal.ssh.user"]);
        var port = Field(strings["terminal.ssh.port"]);

        port.Text = ShellCatalog.DefaultSshPort.ToString(CultureInfo.InvariantCulture);

        var connect = new AxButton
        {
            Content = strings["terminal.ssh.connect"],
            MinWidth = 96,
            Classes = { "accent" },
            IsEnabled = false,
        };

        var cancel = new AxButton { Content = strings["common.cancel"], MinWidth = 96 };

        var form = new StackPanel
        {
            Spacing = 10,
            Width = 320,
            Children =
            {
                Labeled(strings["terminal.ssh.host"], host),
                Labeled(strings["terminal.ssh.user"], user),
                Labeled(strings["terminal.ssh.port"], port),
            },
        };

        var dialog = new AxDialog
        {
            Title = strings["terminal.ssh.title"],
            Content = form,
            Buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, connect },
            },
        };

        // Без хоста подключаться некуда — кнопка об этом говорит сразу.
        host.TextChanged += (_, _) => connect.IsEnabled = !string.IsNullOrWhiteSpace(host.Text);

        dialog.Opened += (_, _) => host.Focus();
        cancel.Click += (_, _) => dialog.Close(null);
        connect.Click += (_, _) => Submit();

        form.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                Submit();
        };

        return await dialog.ShowDialog<ShellProfile?>(owner);

        void Submit()
        {
            if (string.IsNullOrWhiteSpace(host.Text))
                return;

            dialog.Close(ShellCatalog.Ssh(host.Text, user.Text, Port(port.Text), ShellCatalog.CurrentPlatform));
        }
    }

    /// <summary>Порт из поля; пустое и мусор — порт по умолчанию.</summary>
    /// <param name="text">Что набрали.</param>
    public static int Port(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : ShellCatalog.DefaultSshPort;

    /// <summary>
    /// Поле с именем для средств доступности.
    /// </summary>
    /// <remarks>
    /// Подсказки внутри поля нет: подпись стоит прямо над ним, и повторённая в
    /// поле она только сбивает — человек читает её как уже введённое значение.
    /// </remarks>
    private static AxTextBox Field(string name)
    {
        var box = new AxTextBox();

        Avalonia.Automation.AutomationProperties.SetName(box, name);

        return box;
    }

    private static StackPanel Labeled(string label, Control field) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label }, field },
    };
}
