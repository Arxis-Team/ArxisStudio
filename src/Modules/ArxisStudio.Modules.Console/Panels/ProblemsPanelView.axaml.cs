using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Вид панели находок: полоса отбора, таблица и подробности.
/// </summary>
/// <remarks>
/// Только вид: что показано и что делает открытие файла, знает
/// <see cref="ProblemsPanel"/>. Он же и связывает части по именам.
/// </remarks>
public partial class ProblemsPanelView : AxUserControl
{
    /// <summary>Собирает вид из разметки.</summary>
    public ProblemsPanelView() => InitializeComponent();
}
