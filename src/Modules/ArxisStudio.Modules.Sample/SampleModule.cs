using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Точка входа примера: заявляет команду, объявленную в манифесте.
/// </summary>
/// <remarks>
/// Встроенный модуль отличается от внешнего плагина только доставкой: он
/// приезжает вместе со студией, его манифест лежит в сборке ресурсом, а сборка
/// живёт в основном контексте загрузки — выключать и выгружать её отдельно
/// нечем. Всё остальное — и точка входа, и панели, и команды — устроено ровно
/// так же, и код модуля переносится во внешний плагин без единой правки.
/// </remarks>
public sealed class SampleModule : StudioPlugin
{
    /// <summary>Идентификатор команды: он же объявлен в манифесте.</summary>
    public const string AboutCommand = "sample.about";

    /// <summary>Команда-переключатель: подробный журнал. Кнопка в полосе зовёт её же.</summary>
    public const string VerboseCommand = "sample.verbose";

    private IStudioContext? _context;
    private bool _verbose;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _context = context;

        context.Commands.Register(AboutCommand, About);
        context.Commands.Register(VerboseCommand, ToggleVerbose);
        context.Log.Write(StudioLogLevel.Info, "Пример", "Модуль поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "Пример", "Модуль выключен");
        _context = null;
    }

    private void About()
    {
        if (_context is null)
            return;

        _context.Log.Write(
            StudioLogLevel.Info,
            "Пример",
            _context.ProjectPath is { Length: > 0 } path
                ? $"Встроенный модуль, открыт проект {Path.GetFileName(path)}"
                : "Встроенный модуль, проект не открыт");

        if (_verbose)
            _context.Log.Write(StudioLogLevel.Debug, "Пример", $"Папка модуля: {_context.PluginDirectory}");
    }

    /// <summary>
    /// Переключает подробный журнал — и говорит полосе, включён ли он.
    /// </summary>
    /// <remarks>
    /// Кнопка в полосе становится переключателем не сама: студия ничего у
    /// модуля не спрашивает, а помнит то, что он сказал. Обе службы могут
    /// отсутствовать — у студии без полосы или без строки состояния, — и модуль
    /// обязан это пережить.
    /// <para>
    /// О переключении сказано и в строке состояния: сам журнал уходит в
    /// стандартный вывод, панели под него в студии нет, и без этого нажатие
    /// оставалось бы без единого видимого следа.
    /// </para>
    /// </remarks>
    private void ToggleVerbose()
    {
        if (_context is null)
            return;

        _verbose = !_verbose;

        var said = _verbose
            ? _context.Strings["module.verbose.on"]
            : _context.Strings["module.verbose.off"];

        _context.GetService<IStudioToolBar>()?.Update(VerboseCommand, isChecked: _verbose);
        _context.GetService<IStudioStatus>()?.Show(said);
        _context.Log.Write(StudioLogLevel.Info, "Пример", said);
    }
}
