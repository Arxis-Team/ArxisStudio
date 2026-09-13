using Avalonia;
using Avalonia.Controls;
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
    /// Отдаёт фокус внутрь панели: хранителю, а иначе первому, кто его возьмёт.
    /// </summary>
    /// <param name="panel">Панель.</param>
    /// <returns>Нашлось ли кому.</returns>
    /// <remarks>
    /// Хранителя может не быть вовсе — панель показывают впервые, — или он мог
    /// уйти из дерева: список перестроился, и строка, на которой стоял фокус,
    /// теперь другой объект. Тогда берёт первый, кто может; не может никто —
    /// значит панель нечем управлять с клавиатуры, и врать об этом не надо.
    /// </remarks>
    public static bool Restore(Control panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (GetKeeper(panel) is { } keeper && Inside(keeper, panel) && keeper.Focus())
            return true;

        return First(panel) is { } first && first.Focus();
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

    private static Control? First(Control panel) =>
        panel.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(candidate =>
                candidate is { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true });
}
