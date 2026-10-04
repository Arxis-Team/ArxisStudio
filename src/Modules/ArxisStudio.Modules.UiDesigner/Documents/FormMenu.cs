using ArxisStudio.Controls;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Что умеет холст сверх правок строения: выбрать родителя и вписать форму.</summary>
/// <param name="CanSelectParent">Есть ли родитель у выбранного.</param>
/// <param name="SelectParent">Выбрать родителя.</param>
/// <param name="Frame">Показать форму целиком.</param>
internal sealed record CanvasActions(bool CanSelectParent, Action SelectParent, Action Frame);

/// <summary>
/// Контекстное меню холста формы: правки строения выбранного, выбор родителя и «вписать всё».
/// </summary>
/// <remarks>
/// <para>
/// Пункты, которым нечего делать, выключены, а не спрятаны: порядок меню не прыгает от выбора, и рука
/// находит «Вставить» там же, где в прошлый раз, — так устроено меню окна проекта и меню Visual Studio.
/// Сочетания подписаны из той же таблицы, которой их ловят (<see cref="FormKeys"/>).
/// </para>
/// <para>
/// «Обернуть в ▸» — как «Group Into» у Blend: рамки и панели Avalonia. Рамка держит одно содержимое, и
/// для нескольких выбранных она выключена.
/// </para>
/// <para>
/// Пункты собираются отдельно от показа: попап — отдельное окно, которого у безголового прогона нет, и
/// проверять пункты честнее там, где они рождаются.
/// </para>
/// </remarks>
/// <param name="strings">Словарь модуля.</param>
internal sealed class FormMenu(IStudioStrings strings)
{
    /// <summary>Пункты меню.</summary>
    /// <param name="commands">Правки формы.</param>
    /// <param name="owner">Где просили меню: у его окна берётся буфер обмена.</param>
    /// <param name="canvas">Пункты холста.</param>
    /// <returns>Пункты и черты между группами.</returns>
    public IReadOnlyList<Control> Items(FormCommands commands, Visual owner, CanvasActions canvas)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(canvas);

        var take = commands.CanTake;

        return
        [
            Item("form.menu.cut", FormKeys.Cut, take, () => _ = commands.CutAsync(owner)),
            Item("form.menu.copy", FormKeys.Copy, take, () => _ = commands.CopyAsync(owner)),
            Item("form.menu.paste", FormKeys.Paste, commands.CanPaste, () => _ = commands.PasteAsync(owner)),
            Item("form.menu.duplicate", FormKeys.Duplicate, take, () => _ = commands.DuplicateAsync()),
            Item("form.menu.delete", FormKeys.Delete, take, () => _ = commands.DeleteAsync()),
            new AxSeparator(),
            Wrap(commands),
            Item("form.menu.unwrap", null, commands.CanUnwrap, () => _ = commands.UnwrapAsync()),
            new AxSeparator(),
            Item("form.menu.parent", FormKeys.Parent, canvas.CanSelectParent, canvas.SelectParent),
            Item("form.menu.frame", BoardMenu.FrameKey, enabled: true, canvas.Frame),
        ];
    }

    /// <summary>Показывает меню.</summary>
    /// <param name="anchor">К чему привязать.</param>
    /// <param name="items">Пункты.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем.</param>
    /// <param name="at">
    /// Просили клавишей — точка в координатах якоря, где меню встанет левым верхним углом: у выбранного, а
    /// не у края холста. Null — под якорем.
    /// </param>
    public static void Show(Control anchor, IReadOnlyList<Control> items, bool atPointer, Point? at = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return;

        var flyout = new AxMenuFlyout();

        foreach (var item in items)
            flyout.Items.Add(item);

        if (!atPointer && at is { } point)
        {
            flyout.Placement = PlacementMode.AnchorAndGravity;
            flyout.PlacementAnchor = PopupAnchor.TopLeft;
            flyout.PlacementGravity = PopupGravity.BottomRight;
            flyout.HorizontalOffset = point.X;
            flyout.VerticalOffset = point.Y;
        }

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>«Обернуть в ▸»: рамки и панели; выключено, когда выбранное обернуть нельзя ничем.</summary>
    private AxMenuItem Wrap(FormCommands commands)
    {
        var menu = new AxMenuItem { Header = strings["form.menu.wrap"] };

        foreach (var container in FormCommands.Wrappers)
            menu.Items.Add(Row(container, null, commands.CanWrap(container), () => _ = commands.WrapAsync(container)));

        menu.IsEnabled = menu.Items.OfType<AxMenuItem>().Any(static item => item.IsEnabled);

        return menu;
    }

    private AxMenuItem Item(string key, KeyGesture? gesture, bool enabled, Action act) =>
        Row(strings[key], gesture, enabled, act);

    private static AxMenuItem Row(string header, KeyGesture? gesture, bool enabled, Action act)
    {
        var item = new AxMenuItem { Header = header, InputGesture = gesture, IsEnabled = enabled };

        item.Click += (_, _) => act();

        return item;
    }
}
