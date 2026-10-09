using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>Разметка инспектора; склейку со службой и целью делает <see cref="InspectorPanel"/>.</summary>
public sealed partial class InspectorView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public InspectorView() => InitializeComponent();
}
