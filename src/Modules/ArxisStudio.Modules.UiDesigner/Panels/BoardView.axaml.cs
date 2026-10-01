using ArxisStudio.Controls;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>Разметка доски форм; склейку с моделью делает <see cref="BoardPanel"/>.</summary>
public sealed partial class BoardView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public BoardView() => InitializeComponent();
}
