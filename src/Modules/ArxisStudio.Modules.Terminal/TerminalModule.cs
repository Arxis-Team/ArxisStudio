using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Точка входа терминала: заявляет команды, объявленные в манифесте.
/// </summary>
/// <remarks>
/// Команды сами ничего не открывают: у них нет ни окна, ни экрана. Они
/// достают панель на видное место службой студии и просят её через
/// <see cref="TerminalHub"/> — панель открывает сеансы и показывает диалоги,
/// потому что окно есть у неё.
/// </remarks>
public sealed class TerminalModule : StudioPlugin
{
    /// <summary>Показать терминал; сеанс откроется, если ни одного нет. Кнопка в полосе зовёт её же.</summary>
    public const string OpenCommand = "terminal.open";

    /// <summary>Открыть ещё один сеанс оболочки по умолчанию.</summary>
    public const string NewCommand = "terminal.new";

    /// <summary>Открыть сеанс SSH, спросив адрес.</summary>
    public const string NewSshCommand = "terminal.newSsh";

    /// <summary>Показать настройки терминала.</summary>
    public const string SettingsCommand = "terminal.settings";

    /// <summary>Идентификатор панели: он же объявлен в манифесте.</summary>
    public const string PanelId = "terminal.panel";

    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Terminal";

    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        context.Commands.Register(OpenCommand, Open);
        context.Commands.Register(NewCommand, New);
        context.Commands.Register(NewSshCommand, NewSsh);
        context.Commands.Register(SettingsCommand, Settings);
        context.Log.Write(StudioLogLevel.Info, LogSource, "Модуль поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, LogSource, "Модуль выключен");
        _context = null;
    }

    /// <summary>Оболочка по умолчанию с учётом выбора человека.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ShellProfile DefaultProfile(IStudioSettings settings) =>
        ShellCatalog.Default(ShellCatalog.Available(), TerminalSettings.Read(settings).Shell);

    private void Open()
    {
        Reveal();
        TerminalHub.Open(new TerminalRequest(TerminalRequestKind.Open));
    }

    private void New()
    {
        if (_context is null)
            return;

        Reveal();
        TerminalHub.Open(new TerminalRequest(TerminalRequestKind.NewSession, DefaultProfile(_context.Settings)));
    }

    private void NewSsh()
    {
        Reveal();
        TerminalHub.Open(new TerminalRequest(TerminalRequestKind.NewSsh));
    }

    private void Settings() => TerminalHub.Open(new TerminalRequest(TerminalRequestKind.Settings));

    /// <summary>
    /// Достаёт панель на видное место.
    /// </summary>
    /// <remarks>
    /// Службы может не быть — у студии без дока, — и модуль обязан это
    /// пережить: просьба всё равно уйдёт панели, просто искать её на экране
    /// человеку придётся самому.
    /// </remarks>
    private void Reveal() => _context?.GetService<IStudioToolWindows>()?.Show(PanelId);
}
