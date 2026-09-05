using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Brand;

/// <summary>
/// Знак ArxisStudio в натуральную величину — 48 × 48.
/// </summary>
/// <remarks>
/// Не иконка: у знака пять фигур с разной толщиной штриха и непрозрачные узлы,
/// а <c>AxIcon</c> рисует один контур одним пером. Поэтому знак живёт разметкой
/// рядом со студией, а не в наборе иконок.
/// <para>
/// Цвета берутся из темы, поэтому знак верен и в тёмной, и в светлой без второй
/// копии: линии — цветом текста, заливка узлов — цветом носителя.
/// </para>
/// </remarks>
public partial class ArxisMark : UserControl
{
    /// <summary>Собирает знак.</summary>
    public ArxisMark() => AvaloniaXamlLoader.Load(this);
}
