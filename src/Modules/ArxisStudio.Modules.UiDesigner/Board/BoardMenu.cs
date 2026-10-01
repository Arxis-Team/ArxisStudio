using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>Что умеет доска — пунктам меню и клавишам одной дорогой.</summary>
/// <param name="Open">Открыть карточки в редакторе.</param>
/// <param name="Frame">Показать карточки целиком; пусто — всю доску.</param>
/// <param name="Arrange">Разложить доску заново.</param>
internal sealed record BoardActions(
    Action<IReadOnlyList<FormCard>> Open,
    Action<IReadOnlyList<FormCard>> Frame,
    Action Arrange);

/// <summary>
/// Контекстное меню доски на контролах студии.
/// </summary>
/// <remarks>
/// Ядро Surface решает, о чём меню: о карточке под указателем, о выборе или о пустом холсте, — и
/// спрашивает хоста событием <c>ContextMenuRequesting</c>. Показывает меню доска сама: у ядра своё
/// меню на голом <c>ContextMenu</c> Avalonia, а в студии всплывающее одевает тема — <see cref="AxMenuFlyout"/>.
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

    /// <summary>
    /// Пункты меню.
    /// </summary>
    /// <param name="cards">О каких карточках меню; пусто — о холсте.</param>
    /// <returns>Пункты и черты между группами.</returns>
    public IReadOnlyList<Control> Items(IReadOnlyList<FormCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (cards.Count == 0)
        {
            return
            [
                Item("board.fit", AxIcons.FitToScreen, FrameKey, () => actions.Frame([])),
                Item("board.arrange", AxIcons.Grid, null, actions.Arrange),
            ];
        }

        return
        [
            Item("board.open", AxIcons.DocumentCode, OpenKey, () => actions.Open(cards)),
            new AxSeparator(),
            Item("board.frame", AxIcons.FitToScreen, FrameKey, () => actions.Frame(cards)),
        ];
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

    private AxMenuItem Item(string key, Avalonia.Media.Geometry glyph, KeyGesture? gesture, Action act)
    {
        var item = new AxMenuItem
        {
            Header = strings[key],
            Icon = new AxIcon { Data = glyph },
            InputGesture = gesture,
        };

        item.Click += (_, _) => act();

        return item;
    }
}
