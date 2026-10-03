using ArxisStudio.Controls;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Разметка вкладки формы; склейку с моделью делает <see cref="FormDocument"/>.</summary>
public sealed partial class FormView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public FormView() => InitializeComponent();
}
