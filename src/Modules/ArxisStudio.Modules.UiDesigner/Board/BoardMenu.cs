using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>Что умеет доска — пунктам меню и клавишам одной дорогой.</summary>
/// <param name="Open">Открыть карточки в редакторе.</param>
/// <param name="Frame">Показать карточки целиком; пусто — всю доску.</param>
/// <param name="Arrange">Разложить доску заново.</param>
/// <param name="Remove">Убрать карточки с доски.</param>
/// <param name="Return">Вернуть убранные формы на доску.</param>
/// <param name="Removed">Убранные формы, которые можно вернуть, — спрашиваются при показе меню.</param>
/// <param name="Where">Где форма лежит — папкой от решения: ею одноимённые формы различаются в меню.</param>
internal sealed record BoardActions(
    Action<IReadOnlyList<FormCard>> Open,
    Action<IReadOnlyList<FormCard>> Frame,
    Action Arrange,
    Action<IReadOnlyList<FormCard>> Remove,
    Action<IReadOnlyList<CanonicalPath>> Return,
    Func<IReadOnlyList<FoundForm>> Removed,
    Func<FoundForm, string> Where);

/// <summary>
/// Контекстное меню доски на контролах студии.
/// </summary>
/// <remarks>
/// Ядро Surface решает, о чём меню: о карточке под указателем, о выборе или о пустом холсте, — и
/// спрашивает хоста событием <c>ContextMenuRequesting</c>. Показывает меню доска сама: у ядра своё
/// меню на голом <c>ContextMenu</c> Avalonia, а в студии всплывающее одевает тема — <see cref="AxMenuFlyout"/>.
/// <para>
/// «Убрать с доски», а не «Удалить»: файл формы остаётся на месте, уходит только карточка, и слово
/// «удалить» рядом с файлом обещало бы то, чего не будет. Пункт стоит последним и отделён чертой, как
/// уборка в меню Rider. Вернуть убранное можно отменой и из меню холста — списком по одной и всё разом.
/// </para>
/// <para>
/// Пункты собираются отдельно от показа: попап — отдельное окно, которого у безголового прогона нет, и
/// проверять пункты честнее там, где они рождаются.
/// </para>
/// </remarks>
/// <param name="strings">Словари модуля.</param>
/// <param name="actions">Действия доски.</param>
internal sealed class BoardMenu(IStudioStrings strings, BoardActions actions)
{
    /// <summary>Открыть выбранное.</summary>
    public static KeyGesture OpenKey { get; } = new(Key.Enter);

    /// <summary>Показать выбранное или всю доску.</summary>
    public static KeyGesture FrameKey { get; } = new(Key.F);

    /// <summary>Убрать выбранное с доски: клавиша ядра, ею же подписан пункт.</summary>
    public static KeyGesture RemoveKey { get; } = new(Key.Delete);

    /// <summary>
    /// Пункты меню.
    /// </summary>
    /// <param name="cards">О каких карточках меню; пусто — о холсте.</param>
    /// <returns>Пункты и черты между группами.</returns>
    public IReadOnlyList<Control> Items(IReadOnlyList<FormCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (cards.Count > 0)
        {
            return
            [
                Item("board.open", AxIcons.DocumentCode, OpenKey, () => actions.Open(cards)),
                new AxSeparator(),
                Item("board.frame", AxIcons.FitToScreen, FrameKey, () => actions.Frame(cards)),
                new AxSeparator(),
                Item("board.remove", AxIcons.Minus, RemoveKey, () => actions.Remove(cards)),
            ];
        }

        var items = new List<Control>
        {
            Item("board.fit", AxIcons.FitToScreen, FrameKey, () => actions.Frame([])),
            Item("board.arrange", AxIcons.Grid, null, actions.Arrange),
        };

        if (actions.Removed() is { Count: > 0 } removed)
        {
            items.Add(new AxSeparator());
            items.Add(Returning(removed));
        }

        return items;
    }

    /// <summary>Показывает меню.</summary>
    /// <param name="anchor">К чему привязать.</param>
    /// <param name="cards">О каких карточках меню.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем.</param>
    public void Show(Control anchor, IReadOnlyList<FormCard> cards, bool atPointer)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var flyout = new AxMenuFlyout();

        foreach (var item in Items(cards))
            flyout.Items.Add(item);

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>
    /// «Вернуть на доску ▸»: убранные формы по одной и «Вернуть все».
    /// </summary>
    /// <remarks>
    /// Форма названа файлом и папкой от решения: одноимённые окна — в двух проектах или в двух папках
    /// одного — иначе были бы одной строкой.
    /// </remarks>
    private AxMenuItem Returning(IReadOnlyList<FoundForm> removed)
    {
        var menu = Item("board.return", AxIcons.Plus, null, null);

        foreach (var form in removed)
        {
            var path = form.File.Path;

            menu.Items.Add(Row(
                $"{path.FileName} · {actions.Where(form)}",
                FormCard.GlyphOf(form.Root.Kind),
                () => actions.Return([path])));
        }

        menu.Items.Add(new AxSeparator());
        menu.Items.Add(Item(
            "board.return.all", null, null, () => actions.Return([.. removed.Select(form => form.File.Path)])));

        return menu;
    }

    private AxMenuItem Item(string key, Geometry? glyph, KeyGesture? gesture, Action? act) =>
        Row(strings[key], glyph, act, gesture);

    private static AxMenuItem Row(string header, Geometry? glyph, Action? act, KeyGesture? gesture = null)
    {
        var item = new AxMenuItem
        {
            Header = header,
            Icon = glyph is null ? null : new AxIcon { Data = glyph },
            InputGesture = gesture,
        };

        if (act is not null)
            item.Click += (_, _) => act();

        return item;
    }
}
