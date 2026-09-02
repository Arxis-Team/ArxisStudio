using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Shell;

/// <summary>
/// Лента одного места полосы: элементы в порядке показа и разделители между
/// вкладами разных хозяев.
/// </summary>
/// <remarks>
/// Порядок — не её забота: кто за кем стоит, решает реестр, а лента лишь
/// раскладывает то, что ей дали. Поэтому и проверяется она отдельно от
/// манифестов.
/// <para>
/// Разделители никто не объявляет — лента выводит их сама, там, где меняется
/// хозяин. Объявленный разделитель пережил бы выгрузку своего плагина и остался
/// бы висеть двойным; выведенный исчезает вместе с вкладом.
/// </para>
/// </remarks>
public sealed class ToolBarStrip : StackPanel
{
    /// <summary>Заводит пустую ленту.</summary>
    public ToolBarStrip()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 4;
    }

    /// <summary>
    /// Раскладывает элементы заново.
    /// </summary>
    /// <param name="ordered">Элементы в порядке показа, каждый — со своим хозяином.</param>
    /// <remarks>
    /// Всегда с чистого листа: перекладывать соседей при каждом добавлении или
    /// снятии значило бы держать в ленте своё представление о порядке, а оно
    /// уже есть у реестра.
    /// </remarks>
    public void Place(IReadOnlyList<(string Owner, Control View)> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        Children.Clear();

        string? previous = null;

        foreach (var (owner, view) in ordered)
        {
            if (previous is not null && !string.Equals(previous, owner, StringComparison.Ordinal))
                Children.Add(new AxDivider { Orientation = Orientation.Vertical, Classes = { "toolbar" } });

            Children.Add(view);
            previous = owner;
        }
    }
}
