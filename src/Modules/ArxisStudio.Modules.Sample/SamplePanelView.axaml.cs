using ArxisStudio.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Представление панели примера: разметка и то, что за ней стоит.
/// </summary>
/// <remarks>
/// Конструктор без доводов — намеренно: представление обязано собираться и
/// само по себе, иначе его не откроет ни предпросмотр, ни дизайнер. Всё, что
/// оно знает о студии, приходит моделью в <c>DataContext</c>.
/// </remarks>
public partial class SamplePanelView : AxUserControl
{
    /// <summary>Собирает представление из разметки.</summary>
    public SamplePanelView() => InitializeComponent();

    private void OnLogClick(object? sender, RoutedEventArgs e) =>
        (DataContext as SamplePanelModel)?.Log();
}
