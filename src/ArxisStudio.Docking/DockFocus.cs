using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Docking;

/// <summary>
/// Фокус внутри панели: кто его держал и кому вернуть.
/// </summary>
/// <remarks>
/// Движку нельзя знать, что такое панель плагина, и это правило сборки, а не
/// договорённость: раскладка переживает выключение плагина и уезжает в файл, а
/// узел, удержавший чужой объект, навсегда оставил бы в памяти его контекст
/// загрузки. Поэтому здесь нет ни интерфейса, ни делегата — только
/// присоединённое свойство на контроле, который панели и так принадлежит. Оно
/// живёт ровно столько, сколько живёт сам контрол, и умирает вместе с ним:
/// <see cref="DockItems.RemoveOwnedBy"/> роняет обоих одним движением.
/// <para>
/// Механика здесь, политика — у владельца дерева. Движок умеет запомнить и
/// вернуть; кому и когда возвращать, решает студия — так же, как с самим
/// деревом, где вид сообщает о жесте, а правит хозяин.
/// </para>
/// </remarks>
public static class DockFocus
{
    /// <summary>Кто держал фокус внутри этой панели в последний раз.</summary>
    public static readonly AttachedProperty<Control?> KeeperProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, Control?>("Keeper");

    /// <summary>Называет хранителя фокуса панели.</summary>
    /// <param name="panel">Панель.</param>
    /// <param name="keeper">Кто держал фокус; <c>null</c> — никто.</param>
    public static void SetKeeper(Control panel, Control? keeper)
    {
        ArgumentNullException.ThrowIfNull(panel);

        panel.SetValue(KeeperProperty, keeper);
    }

    /// <summary>Кто держал фокус внутри панели.</summary>
    /// <param name="panel">Панель.</param>
    public static Control? GetKeeper(Control panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        return panel.GetValue(KeeperProperty);
    }

    /// <summary>Место, с которого в панели начинают работу: к нему каретка идёт, когда помнить некого.</summary>
    public static readonly AttachedProperty<Control?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, Control?>("Target");

    /// <summary>Называет цель каретки панели.</summary>
    /// <param name="panel">Панель.</param>
    /// <param name="target">Цель; <c>null</c> — первый, кто возьмёт.</param>
    public static void SetTarget(Control panel, Control? target)
    {
        ArgumentNullException.ThrowIfNull(panel);

        panel.SetValue(TargetProperty, target);
    }

    /// <summary>Цель каретки панели.</summary>
    /// <param name="panel">Панель.</param>
    public static Control? GetTarget(Control panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        return panel.GetValue(TargetProperty);
    }

    /// <summary>Фокус сейчас внутри этого контрола.</summary>
    /// <param name="control">Панель или группа.</param>
    public static bool Holds(Control control) => Focused(control) is not null;

    /// <summary>
    /// Запоминает того, кто держит фокус внутри панели сейчас.
    /// </summary>
    /// <param name="panel">Панель.</param>
    /// <remarks>
    /// Спрашивать надо, пока панель на месте: отцепленный контрол фокуса уже не
    /// держит, и к мигу подмены содержимого узнать об этом будет негде.
    /// </remarks>
    public static void Remember(Control panel)
    {
        if (Focused(panel) is { } keeper)
            SetKeeper(panel, keeper);
    }

    /// <summary>
    /// Отдаёт фокус внутрь панели: хранителю, иначе цели панели, иначе первому, кто его возьмёт.
    /// </summary>
    /// <param name="panel">Панель.</param>
    /// <param name="method">
    /// Как пришли: с клавиатуры — и каретка показывает кольцо, как после Tab; мышью или просьбой
    /// программы — без него.
    /// </param>
    /// <returns>Нашлось ли кому.</returns>
    /// <remarks>
    /// Хранителя может не быть вовсе — панель показывают впервые, — или он мог
    /// уйти из дерева: список перестроился, и строка, на которой стоял фокус,
    /// теперь другой объект; закрыли сеанс терминала, в котором печатали. Тогда
    /// каретка идёт к цели — месту, которое панель назвала сама, — и цель не
    /// теряется от того, что хранитель однажды был: прежде она служила только
    /// первым хранителем, и первое же запоминание стирало её насовсем. Нет и цели —
    /// берёт первый, кто может; не может никто — значит панель нечем управлять с
    /// клавиатуры, и врать об этом не надо.
    /// <para>
    /// Ответу верят: обход панелей по F6 идёт дальше, услышав «некому». Поэтому каретка, которую
    /// взяли и передали внутрь себя, — удача на каждом шаге, а не только у хранителя: такой контрол
    /// отвечает «не взял», хотя она уже в панели, и обход прежде уводил её к соседней панели.
    /// </para>
    /// </remarks>
    public static bool Restore(Control panel, NavigationMethod method = NavigationMethod.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(panel);

        foreach (var keeper in new[] { GetKeeper(panel), GetTarget(panel) })
        {
            if (keeper is not null && Inside(keeper, panel) && Give(keeper, method))
                return true;
        }

        return First(panel) is { } first && (first.Focus(method) || Holds(panel));
    }

    /// <summary>
    /// Есть ли внутри панели кому взять каретку.
    /// </summary>
    /// <param name="panel">Панель.</param>
    /// <remarks>
    /// Спрашивают, не трогая каретку, — обход по F6, решая, идти ли в панель: попытка отдать её
    /// показывает панель, будит её оторванное окно и объявляет её выбранной, и всё это досталось бы
    /// панели, мимо которой прошли.
    /// </remarks>
    public static bool CanHold(Control panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        return First(panel) is not null;
    }

    /// <summary>
    /// Отдаёт каретку хранителю, а если сам он её не берёт — тому внутри него, кто может.
    /// </summary>
    /// <remarks>
    /// Хранитель бывает местом, а не контролом. Список в Avalonia 12 каретку сам не берёт — её
    /// держат строки, — и цель панели «вот мой список» не срабатывала вовсе: каретка уходила к
    /// первому попавшемуся, к кнопке над списком. Теперь она идёт к выбранной строке, прокрутив к
    /// ней, а без выбора — к первому внутри хранителя. Хранитель, который взял каретку и сам
    /// передал её внутрь себя, отвечает «не взял», хотя она у него, — это тоже удача.
    /// </remarks>
    private static bool Give(Control keeper, NavigationMethod method)
    {
        if (keeper.Focus(method) || Holds(keeper))
            return true;

        return (Chosen(keeper) ?? First(keeper)) is { } inside && !ReferenceEquals(inside, keeper)
            && (inside.Focus(method) || Holds(inside));
    }

    /// <summary>Строка, выбранная в списке, — рождённая, даже если она за краем окна.</summary>
    private static Control? Chosen(Control keeper)
    {
        if (keeper is not SelectingItemsControl { SelectedIndex: >= 0 and var index } list)
            return null;

        list.ScrollIntoView(index);

        return list.ContainerFromIndex(index);
    }

    /// <summary>Кто держит фокус внутри контрола; <c>null</c> — фокус не здесь.</summary>
    private static Control? Focused(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement() is not Control focused)
            return null;

        return Inside(focused, control) ? focused : null;
    }

    private static bool Inside(Control element, Control root) =>
        ReferenceEquals(element, root) || element.GetVisualAncestors().Contains(root);

    /// <summary>Первый, кто может взять фокус, — начиная с самой панели.</summary>
    /// <remarks>
    /// Сама панель в счёт: у плагина окно инструментов бывает одним контролом — полем, списком, — и
    /// поиск среди потомков его пропускал, а каретка не возвращалась никуда.
    /// </remarks>
    private static Control? First(Control panel) =>
        panel.GetSelfAndVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(candidate =>
                candidate is { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true });
}
