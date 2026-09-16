using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Меню источников: кто писал в этот журнал и чьи записи показывать.
/// </summary>
/// <remarks>
/// Список собирается при каждом открытии, а не держится: источник — просто строка, которую
/// называет пишущий, и появиться новый может в любой миг. Это то же, что «Show output from» у
/// Visual Studio, только имена приходят не из перечня каналов, а из самих записей.
/// <para>
/// Выбранный пункт отмечен точкой: меню, которое не показывает, что в нём уже выбрано, заставляет
/// человека помнить свой же отбор — а отбор по источнику переживает десятки записей.
/// </para>
/// </remarks>
internal static class LogSourceMenu
{
    /// <summary>Показывает меню у кнопки.</summary>
    /// <param name="anchor">Кнопка, у которой меню встаёт.</param>
    /// <param name="sources">Источники журнала, уже упорядоченные.</param>
    /// <param name="chosen">Выбранный источник; <c>null</c> — показаны все.</param>
    /// <param name="strings">Словарь студии: подписи пунктов.</param>
    /// <param name="pick">Что сделать с выбором.</param>
    /// <exception cref="ArgumentNullException">Любой из доводов равен <c>null</c>.</exception>
    public static void ShowAt(
        Control anchor,
        IEnumerable<string> sources,
        string? chosen,
        IStudioStrings strings,
        Action<string?> pick)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(pick);

        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        flyout.Items.Add(Item(strings["console.source.all"], null));

        foreach (var source in sources)
            flyout.Items.Add(Item(source, source));

        flyout.ShowAt(anchor);

        AxMenuItem Item(string header, string? source)
        {
            var item = new AxMenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(source, chosen, StringComparison.Ordinal),
            };

            item.Click += (_, _) => pick(source);

            return item;
        }
    }
}
