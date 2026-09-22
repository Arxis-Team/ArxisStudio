using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Model;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>Что несут мышью по окну проекта: значок и имя первого и число остальных.</summary>
/// <param name="Node">Первое несомое — его значок и цвет.</param>
/// <param name="Label">Подпись: имя первого, а у нескольких — и сколько ещё.</param>
internal sealed record Carried(Node Node, string Label);

/// <summary>Подсказка у курсора, пока по окну проекта несут файлы; ведёт её <see cref="FileDrag"/>.</summary>
public sealed partial class DragGhost : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public DragGhost() => InitializeComponent();
}
