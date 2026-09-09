using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Вид панели журнала: полоса отбора, список записей и подробности.
/// </summary>
/// <remarks>
/// Только вид: что происходит по нажатиям и что показано в списке, знает
/// <see cref="LogPanel"/>. Он же и связывает части — они объявлены разметкой и
/// видны ему по именам.
/// </remarks>
public partial class LogPanelView : AxUserControl
{
    /// <summary>Собирает вид из разметки.</summary>
    public LogPanelView() => InitializeComponent();
}
