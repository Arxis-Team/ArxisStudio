using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Что панель примера знает о студии.
/// </summary>
/// <param name="context">Что студия даёт модулю.</param>
/// <remarks>
/// Модель отделена от представления не ради обряда, а ради двух вещей:
/// разметка собирается без студии (иначе её не открыть предпросмотром), а
/// связь с контекстом остаётся в одном месте и проверяется без окна.
/// </remarks>
public sealed class SamplePanelModel(IStudioContext context)
{
    /// <summary>Открытый проект — или сообщение о том, что его нет.</summary>
    public string Project => context.ProjectPath is { Length: > 0 } path
        ? $"Проект: {Path.GetFileName(path)}"
        : "Проект не открыт";

    /// <summary>
    /// Просит студию исполнить команду модуля.
    /// </summary>
    /// <remarks>
    /// Через команду, а не напрямую: та же дорога, что у пункта меню и у
    /// кнопки в полосе, — и одно место, где написано, что делать.
    /// </remarks>
    public void Log() => context.Commands.Invoke(SampleModule.AboutCommand);
}
