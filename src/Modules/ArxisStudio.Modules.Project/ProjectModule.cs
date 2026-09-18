using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Точка входа окна проекта: заявляет команду показа.
/// </summary>
/// <remarks>
/// Всё остальное делает панель: решение ей отдаёт служба проектов, а подписывается на неё панель
/// при постройке — в <c>Activate</c> модуля служба может быть ещё не поднята, если модуль стоит в
/// списке раньше неё.
/// </remarks>
public sealed class ProjectModule : StudioPlugin
{
    /// <summary>Показать окно проекта и отдать ему клавиатуру.</summary>
    public const string ShowCommand = "project.show";

    /// <summary>Идентификатор панели: он же объявлен в манифесте.</summary>
    public const string PanelId = "project.window";

    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Project window";

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

    /// <summary>
    /// Показывает окно и отдаёт ему клавиатуру — как Alt+1 в Rider.
    /// </summary>
    /// <remarks>
    /// Панель, которую человек закрыл, служба показа не открывает: это право меню «Панели». Здесь
    /// окно выводится вперёд и получает фокус, если оно на месте.
    /// </remarks>
    private void Show()
    {
        if (_context is null)
            return;

        _context.GetService<IStudioToolWindows>()?.Show(PanelId);
        _context.GetService<IStudioFocus>()?.Focus(PanelId);
    }
}
