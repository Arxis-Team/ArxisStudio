using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>Разметка окна проекта; поведение — у <see cref="ProjectPanel"/>.</summary>
public sealed partial class ProjectPanelView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public ProjectPanelView() => InitializeComponent();
}
