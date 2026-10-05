using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Холст доски: элемент коллекции — форма решения (<see cref="FormCard"/>), контейнер — карточка формы
/// дизайнера (<see cref="UiDesignerFormItem"/>).
/// </summary>
/// <remarks>
/// Корень формы контейнеру ставит доска, а не привязка: показ формы встаёт, когда она на виду, и уходит,
/// когда её не видно, а держит его сессия формы, а не элемент коллекции. Поэтому тип контейнера назван
/// здесь, а не выведен из <see cref="UiDesignerView.ItemRootBinding"/>, — как у демо дизайнера Surface.
/// Своей темы у холста нет: он носит тему дизайнера.
/// </remarks>
internal sealed class FormsSheet : UiDesignerView
{
    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(UiDesignerView);

    /// <inheritdoc/>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new UiDesignerFormItem();
}
