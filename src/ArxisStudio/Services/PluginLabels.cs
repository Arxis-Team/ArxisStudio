using ArxisStudio.Extensibility;
using Avalonia;

namespace ArxisStudio.Services;

/// <summary>
/// Подписи вкладов на экране: ключ словаря — живой привязкой, текст — как есть.
/// </summary>
/// <remarks>
/// Подпись вклада — заголовок панели, текст кнопки полосы, её подсказку и имя для диктора —
/// показывает не автор расширения, а студия, и переводить её при смене языка — её забота. Правило
/// одно, и жило оно в полосе и в раскладке двумя копиями.
/// </remarks>
internal static class PluginLabels
{
    /// <summary>Ставит подпись в свойство контрола.</summary>
    /// <param name="strings">Словари хозяина вклада.</param>
    /// <param name="target">Кому ставить.</param>
    /// <param name="property">Какое свойство.</param>
    /// <param name="text">Ключ вида <c>%panel.main%</c> или готовый текст.</param>
    public static void Label(this PluginStrings strings, AvaloniaObject target, AvaloniaProperty property, string text)
    {
        if (PluginStrings.IsKey(text, out var key))
            target.Bind(property, strings.Text(key));
        else
            target.SetValue(property, text);
    }
}
