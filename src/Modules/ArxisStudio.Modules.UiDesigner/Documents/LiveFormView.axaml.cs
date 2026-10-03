using ArxisStudio.Controls;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Разметка живой вкладки формы; склейку со службой XAML делает <see cref="LiveFormDocument"/>.</summary>
public sealed partial class LiveFormView : AxUserControl
{
    /// <summary>Строит разметку.</summary>
    public LiveFormView() => InitializeComponent();
}
