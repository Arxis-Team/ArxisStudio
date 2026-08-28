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

    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _context = context;

        context.Commands.Register(AboutCommand, About);
        context.Log.Write(StudioLogLevel.Info, "Пример", "Модуль поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "Пример", "Модуль выключен");
        _context = null;
    }

    private void About() =>
        _context?.Log.Write(
            StudioLogLevel.Info,
            "Пример",
            _context.ProjectPath is { Length: > 0 } path
                ? $"Встроенный модуль, открыт проект {Path.GetFileName(path)}"
                : "Встроенный модуль, проект не открыт");
}
