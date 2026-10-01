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

    /// <summary>Настройка: сетка на холсте доски.</summary>
    public const string GridKey = "ui-designer.grid";

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
