using System.Globalization;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>Слова панели: строки модуля и откуда значение — тем языком, каким строка говорит в подсказке.</summary>
/// <param name="strings">Словарь модуля.</param>
internal sealed class InspectorWords(IStudioStrings strings)
{
    /// <summary>Строка модуля.</summary>
    /// <param name="key">Ключ.</param>
    public string this[string key] => strings[key];

    /// <summary>Строка модуля с подстановкой.</summary>
    /// <param name="key">Ключ.</param>
    /// <param name="argument">Что подставить.</param>
    public string Format(string key, object argument) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], argument);

    /// <summary>Откуда значение — подсказкой строки: «Стиль Button:pointerover», «Наследуется от Window».</summary>
    /// <param name="value">Значение.</param>
    /// <returns>Текст; пусто — у смешанного: откуда — у каждого своё.</returns>
    public string? Source(InspectorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsMixed)
            return strings["inspector.mixed"];

        var detail = value.SourceDetail;

        return value.Source switch
        {
            InspectorValueSource.Local => strings["inspector.source.local"],
            InspectorValueSource.Design => strings["inspector.source.design"],
            InspectorValueSource.Expression => Format("inspector.source.expression", value.Written ?? value.Text ?? string.Empty),
            InspectorValueSource.Style => detail is { Length: > 0 }
                ? Format("inspector.source.style", detail)
                : strings["inspector.source.styleUnnamed"],
            InspectorValueSource.Template => strings["inspector.source.template"],
            InspectorValueSource.Inherited => detail is { Length: > 0 }
                ? Format("inspector.source.inherited", detail)
                : strings["inspector.source.inheritedUnnamed"],
            InspectorValueSource.Animation => strings["inspector.source.animation"],
            InspectorValueSource.Code => strings["inspector.source.code"],
            _ => strings["inspector.source.default"],
        };
    }
}
