using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Точка входа консоли: заявляет команды, объявленные в манифесте.
/// </summary>
/// <remarks>
/// Панели у модуля две, и обе просятся вниз — вторая «рядом с первой».
/// Вкладками их делает сам док: шапка группы принадлежит ему, и рисовать свою
/// полосу вкладок внутри панели значило бы поставить вторую поверх той, что
/// студия уже нарисовала.
/// </remarks>
public sealed class ConsoleModule : StudioPlugin
{
    /// <summary>Показать журнал.</summary>
    public const string OpenCommand = "console.open";

    /// <summary>Показать находки.</summary>
    public const string ShowProblemsCommand = "console.showProblems";

    /// <summary>Очистить журнал.</summary>
    public const string ClearCommand = "console.clear";

    /// <summary>Идентификатор панели журнала: он же объявлен в манифесте.</summary>
    public const string LogPanelId = "console.log";

    /// <summary>Идентификатор панели находок: он же объявлен в манифесте.</summary>
    public const string ProblemsPanelId = "console.problems";

    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Console";

    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        context.Commands.Register(OpenCommand, Open);
        context.Commands.Register(ShowProblemsCommand, ShowProblems);
        context.Commands.Register(ClearCommand, Clear);

        context.Log.Write(StudioLogLevel.Info, LogSource, "Модуль поднят");
    }

    /// <summary>
    /// Модуль выключают.
    /// </summary>
    /// <remarks>
    /// Панели отписываются от служб сами — у них для этого есть
    /// <see cref="ToolWindow.Release"/>, и студия зовёт его. Точке входа
    /// тянуться к панели незачем и нечем: её экземпляр создаёт студия.
    /// </remarks>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, LogSource, "Модуль выключен");
        _context = null;
    }

    private void Open()
    {
        Reveal(LogPanelId);
        ConsoleHub.Show(ConsolePanelKind.Log);
    }

    private void ShowProblems()
    {
        Reveal(ProblemsPanelId);
        ConsoleHub.Show(ConsolePanelKind.Problems);
    }

    /// <summary>
    /// Очищает журнал.
    /// </summary>
    /// <remarks>
    /// Единственная команда, идущая мимо хаба: чистится служба студии, а не
    /// панель, и потому очистка работает и до того, как панель построили, и в
    /// студии, собранной без дока. Панель узнает об этом обычным событием
    /// журнала — той же дорогой, что и о новой записи.
    /// </remarks>
    private void Clear() => _context?.GetService<IStudioLogFeed>()?.Clear();

    /// <summary>
    /// Достаёт панель на видное место.
    /// </summary>
    /// <remarks>
    /// Службы может не быть — у студии без дока, — и модуль обязан это
    /// пережить: просьба всё равно уйдёт панели, просто искать её на экране
    /// человеку придётся самому.
    /// </remarks>
    private void Reveal(string panelId) =>
        _context?.GetService<IStudioToolWindows>()?.Show(panelId);
}
