using ArxisStudio.Controls;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>Разметка иерархии формы; склейку с холстом делает <see cref="HierarchyPanel"/>.</summary>
public sealed partial class HierarchyView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public HierarchyView() => InitializeComponent();
}
