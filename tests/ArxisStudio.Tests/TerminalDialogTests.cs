using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Shells;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Диалоги терминала: переименование, SSH, настройки.
/// </summary>
/// <remarks>
/// Раньше проверять в них было нечего, кроме разбора набранного: остальное
/// жило в куске кода, собиравшем окно. Разметка вынесла вид наружу, а то, что
/// осталось в коде — когда кнопка доступна и что уходит наружу при
/// подтверждении, — стало обычными объектами, которые можно собрать и
/// потрогать без окна.
/// </remarks>
public class TerminalDialogTests
{
    /// <summary>
    /// Пустое имя закрывает дорогу «Сохранить».
    /// </summary>
    /// <remarks>
    /// Имя — единственное, по чему вкладку находят; оставить её без подписи
    /// значило бы оставить человека без способа отличить одну оболочку от
    /// другой.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_name_closes_the_way_to_save()
    {
        var dialog = new RenameDialog();
        var name = Part<AxTextBox>(dialog, "Chosen");
        var save = Part<AxButton>(dialog, "Save");

        name.Text = "сборка";
        Assert.True(save.IsEnabled);

        name.Text = "   ";
        Assert.False(save.IsEnabled);
    }

    /// <summary>
    /// Enter в форме подтверждает, как и кнопка, — и только когда есть что.
    /// </summary>
    /// <remarks>
    /// Клавиша и кнопка ведут в одно место: человек, набравший имя, жмёт
    /// Enter не глядя, и диалог, промолчавший в ответ, выглядел бы сломанным.
    /// </remarks>
    [AvaloniaFact]
    public void Enter_submits_the_rename_when_there_is_something_to_submit()
    {
        var dialog = new RenameDialog();
        var name = Part<AxTextBox>(dialog, "Chosen");
        var closed = 0;

        dialog.Closed += (_, _) => closed++;

        name.Text = "   ";
        Enter(dialog);

        Assert.Equal(0, closed);

        name.Text = "  сервер  ";
        Enter(dialog);

        Assert.Equal(1, closed);
    }

    /// <summary>
    /// Без хоста подключаться некуда, и кнопка об этом говорит сразу.
    /// </summary>
    [AvaloniaFact]
    public void Without_a_host_there_is_nowhere_to_connect()
    {
        var dialog = new SshDialog();
        var host = Part<AxTextBox>(dialog, "HostBox");
        var connect = Part<AxButton>(dialog, "Connect");

        Assert.False(connect.IsEnabled);

        host.Text = "arxis.dev";
        Assert.True(connect.IsEnabled);

        host.Text = " ";
        Assert.False(connect.IsEnabled);
    }

    /// <summary>Порт по умолчанию стоит в поле, а не подразумевается.</summary>
    /// <remarks>
    /// Человек, которому нужен другой порт, правит видимое число; пустое поле
    /// заставляло бы гадать, чем его молчание обернётся.
    /// </remarks>
    [AvaloniaFact]
    public void The_default_port_is_written_in_the_field() =>
        Assert.Equal(
            ShellCatalog.DefaultSshPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Part<AxTextBox>(new SshDialog(), "PortBox").Text);

    /// <summary>Нажимает Enter в форме диалога.</summary>
    private static void Enter(AxDialog dialog) =>
        Part<StackPanel>(dialog, "Form").RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });

    /// <summary>
    /// Названная часть разметки диалога.
    /// </summary>
    /// <remarks>
    /// Кнопки ищутся отдельно: они стоят в свойстве <c>Buttons</c>, а не в
    /// содержимом, и в дерево попадают только из шаблона — то есть у окна,
    /// которое показали.
    /// </remarks>
    private static T Part<T>(AxDialog dialog, string name) where T : Control =>
        Assert.IsType<T>(Parts(dialog).Single(control => control.Name == name));

    private static IEnumerable<Control> Parts(AxDialog dialog) =>
        dialog.GetLogicalDescendants()
            .Concat(dialog.Buttons is Control buttons ? buttons.GetSelfAndLogicalDescendants() : [])
            .OfType<Control>();
}
