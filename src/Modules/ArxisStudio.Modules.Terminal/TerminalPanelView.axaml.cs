using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Вид панели терминала: полоса сеансов, кнопки и место под экран.
/// </summary>
/// <remarks>
/// Только вид: что происходит по нажатиям и какие сеансы открыты, знает
/// <see cref="TerminalPanel"/>. Он же и связывает части — они объявлены
/// разметкой и видны ему по именам.
/// </remarks>
public partial class TerminalPanelView : AxUserControl
{
    /// <summary>Собирает вид из разметки.</summary>
    public TerminalPanelView() => InitializeComponent();
}
