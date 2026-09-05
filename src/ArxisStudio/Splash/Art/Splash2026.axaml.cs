using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Splash.Art;

/// <summary>
/// Оформление заставки релиза 2026.1.
/// </summary>
/// <remarks>
/// Отдельно от окна заставки нарочно: к новой версии переписывают картинку, а
/// не раму. Окно даёт полосу хода, подвал и скруглённую подложку и обещает
/// модель заставки в <c>DataContext</c>; отсюда можно взять любое её свойство и
/// нельзя тронуть ни одного этапа запуска.
/// <para>
/// Новый релиз — новый такой файл рядом и один изменённый элемент в разметке
/// окна. Прежний остаётся в истории, а не в сборке.
/// </para>
/// </remarks>
public partial class Splash2026 : UserControl
{
    /// <summary>Собирает оформление.</summary>
    public Splash2026() => AvaloniaXamlLoader.Load(this);
}
