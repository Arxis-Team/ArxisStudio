using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner;

/// <summary>
/// Точка входа дизайнера интерфейса: заявляет команду показа доски форм.
/// </summary>
/// <remarks>
/// Всё остальное делает панель: решение ей отдаёт служба проектов, а подписывается на неё панель при
/// постройке — в <c>Activate</c> модуля служба может быть ещё не поднята.
/// </remarks>
public sealed class UiDesignerModule : StudioPlugin
{
    /// <summary>Показать дизайнер и отдать ему клавиатуру.</summary>
    public const string ShowCommand = "ui-designer.show";

    /// <summary>Идентификатор панели: он же объявлен в манифесте.</summary>
    public const string PanelId = "ui-designer.board";

    /// <summary>Иерархия формы впереди.</summary>
    public const string HierarchyId = "ui-designer.hierarchy";

    /// <summary>Инспектор выбранного на форме.</summary>
    public const string InspectorId = "ui-designer.inspector";

    /// <summary>Палитра контролов.</summary>
    public const string ToolboxId = "ui-designer.toolbox";

    /// <summary>Настройка: сетка на холсте доски и вкладок формы.</summary>
    public const string GridKey = "ui-designer.grid";

    /// <summary>
    /// Настройка: режим дизайнера — каждая форма в своей вкладке, а не все на одной доске.
    /// </summary>
    /// <remarks>
    /// Режим решает, кто открывает форму: во вкладках — редактор документов модуля
    /// (<see cref="Documents.FormEditor"/>), на доске — тот, кто открывал её до него, просмотрщик
    /// разметки. Доска при этом остаётся обзором в обоих режимах.
    /// </remarks>
    public const string TabsKey = "ui-designer.tabs";

    /// <summary>
    /// Настройка: живая вкладка формы сохраняет сама — при уходе из окна студии, при закрытии вкладки и
    /// после паузы в правках.
    /// </summary>
    public const string AutoSaveKey = "ui-designer.autoSave";

    /// <summary>
    /// Настройка: что показывает новая живая вкладка формы — <c>design</c>, <c>xaml</c> или <c>split</c>.
    /// </summary>
    /// <remarks>Её пишет выбор вида на полосе вкладки: следующая открывается так, как работали в прошлой.</remarks>
    public const string ViewKey = "ui-designer.view";

    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        context.Commands.Register(ShowCommand, Show);
    }

    /// <inheritdoc/>
    public override void Deactivate() => _context = null;

    private void Show()
    {
        if (_context is null)
            return;

        _context.GetService<IStudioToolWindows>()?.Show(PanelId);
        _context.GetService<IStudioFocus>()?.Focus(PanelId);
    }
}
