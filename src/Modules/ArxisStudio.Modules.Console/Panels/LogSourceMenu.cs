using ArxisStudio.Controls;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Что меню умеет делать с отбором по источнику.
/// </summary>
/// <remarks>
/// Одна вещь вместо трёх доводов: список источников нужен обоим меню, нынешний отбор — обоим, и
/// ответ уходит туда же. Меню при этом ничего не держит: и список, и отбор спрашиваются в тот миг,
/// когда меню собирают, — источник мог появиться, пока меню было закрыто.
/// </remarks>
/// <param name="Known">Кто писал в журнал к этому мигу, уже упорядоченные.</param>
/// <param name="Chosen">Нынешний отбор.</param>
/// <param name="Pick">Что сделать с выбранным отбором.</param>
internal sealed record LogSourcePicker(
    Func<IReadOnlyList<string>> Known,
    Func<LogSources> Chosen,
    Action<LogSources> Pick);

/// <summary>
/// Список источников для меню: кто писал в этот журнал и чьи записи показывать.
/// </summary>
/// <remarks>
/// Список собирается при каждом открытии, а не держится: источник — просто строка, которую
/// называет пишущий, и появиться новый может в любой миг.
/// <para>
/// Пункты — флажки, а не переключатели: смотреть студию и плагин сразу, отложив шум запуска, —
/// обычная работа, и отбор, где выбор одного снимает выбор другого, её не описывает. Меню при
/// этом остаётся открытым (<c>StaysOpenOnClick</c>): отбор из трёх источников, закрывающий меню
/// на каждом щелчке, стоил бы трёх открытий подряд. Так собран отбор по источнику в консоли
/// браузера и в журналах Rider.
/// </para>
/// <para>
/// «Все источники» — не пункт списка, а его начало: отдельная строка, возвращающая отбор к
/// пустому. Снимать семь флажков руками, чтобы вернуться к тому, с чего панель начала, человек не
/// должен.
/// </para>
/// </remarks>
internal static class LogSourceMenu
{
    /// <summary>Показывает список у кнопки полосы.</summary>
    /// <param name="anchor">Кнопка, у которой меню встаёт.</param>
    /// <param name="sources">Отбор по источнику: что спросить и куда ответить.</param>
    /// <param name="strings">Словарь студии: подписи пунктов.</param>
    /// <exception cref="ArgumentNullException">Любой из доводов равен <c>null</c>.</exception>
    public static void ShowAt(Control anchor, LogSourcePicker sources, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        foreach (var item in Items(sources, strings))
            flyout.Items.Add(item);

        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Собирает пункты списка — их же показывает подменю в меню строки.
    /// </summary>
    /// <param name="sources">Отбор по источнику: что спросить и куда ответить.</param>
    /// <param name="strings">Словарь студии: подписи пунктов.</param>
    /// <returns>«Все источники» и по флажку на источник, в порядке списка.</returns>
    /// <exception cref="ArgumentNullException">Любой из доводов равен <c>null</c>.</exception>
    /// <remarks>
    /// Состояние флажков ставится не один раз при сборке, а после каждого щелчка, и берётся оно у
    /// самого отбора. Пункт меню переворачивает свой флажок сам, и правдой этот переворот бывает
    /// не всегда: щелчок по уже отмеченным «всем источникам» ничего не меняет, а щелчок по
    /// последнему показанному меняет больше, чем один флажок. Спрашивать надо отбор, а не пункт.
    /// </remarks>
    public static IReadOnlyList<AxMenuItem> Items(LogSourcePicker sources, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(strings);

        var known = sources.Known();
        var items = new List<AxMenuItem>(known.Count + 1);

        items.Add(Item(strings["console.source.all"], _ => LogSources.All));

        foreach (var source in known)
        {
            var name = source;

            items.Add(Item(name, chosen => chosen.Toggle(name)));
        }

        Sync();

        return items;

        AxMenuItem Item(string header, Func<LogSources, LogSources> next)
        {
            var item = new AxMenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.CheckBox,
                StaysOpenOnClick = true,
            };

            item.Click += (_, _) =>
            {
                sources.Pick(next(sources.Chosen()));
                Sync();
            };

            return item;
        }

        void Sync()
        {
            var chosen = sources.Chosen();

            items[0].IsChecked = chosen.ShowsAll;

            for (var index = 0; index < known.Count; index++)
                items[index + 1].IsChecked = chosen.Shows(known[index]);
        }
    }
}
