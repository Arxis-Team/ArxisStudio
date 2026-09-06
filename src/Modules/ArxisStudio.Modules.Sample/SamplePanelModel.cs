using System.Globalization;
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
    /// <summary>
    /// Открытый проект — или сообщение о том, что его нет.
    /// </summary>
    /// <remarks>
    /// Текст из словаря, а не строкой в коде: модуль — образец, и зашитая
    /// строка в нём означала бы, что так и надо. Словарь у встроенного модуля
    /// не свой — его строки написала студия, — но берутся они той же дорогой,
    /// что и у плагина: через <see cref="IStudioContext.Strings"/>.
    /// </remarks>
    public string Project => context.ProjectPath is { Length: > 0 } path
        ? string.Format(CultureInfo.CurrentCulture, context.Strings["module.sample.project"], Path.GetFileName(path))
        : context.Strings["module.sample.noproject"];

    /// <summary>
    /// Просит студию исполнить команду модуля.
    /// </summary>
    /// <remarks>
    /// Через команду, а не напрямую: та же дорога, что у пункта меню и у
    /// кнопки в полосе, — и одно место, где написано, что делать.
    /// </remarks>
    public void Log() => context.Commands.Invoke(SampleModule.AboutCommand);
}
