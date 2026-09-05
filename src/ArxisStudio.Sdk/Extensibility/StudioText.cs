using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Sdk;

/// <summary>
/// Строка расширения в разметке: <c>Content="{Text command.hello}"</c>.
/// </summary>
/// <remarks>
/// Возвращает привязку из <see cref="IStudioStrings.Text"/>, а не текст на
/// сейчас: смена языка в студии обязана перерисовать и панель расширения — так
/// же, как перерисовывает свою.
/// <para>
/// Чей словарь брать, расширение узнаёт по сборке, из которой пришла разметка.
/// Иначе никак: у каждого расширения словарь свой, ключ <c>panel.main</c>
/// придуман дважды в двух плагинах — и каждому обязан достаться его
/// собственный. Сборку даёт корень разметки, а связь «сборка → словарь»
/// кладёт хозяин, поднявший расширение.
/// </para>
/// </remarks>
public sealed class TextExtension
{
    /// <summary>Создаёт расширение без ключа — ключ задаётся свойством.</summary>
    public TextExtension()
    {
    }

    /// <summary>Создаёт расширение с ключом строки.</summary>
    /// <param name="key">Ключ в словарях расширения.</param>
    public TextExtension(string key) => Key = key;

    /// <summary>Ключ в словарях расширения.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Возвращает привязку к строке текущего языка.
    /// </summary>
    /// <param name="serviceProvider">Контекст разметки: по нему находится её корень.</param>
    /// <returns>Привязка к строке, а если словаря нет — <c>!ключ!</c>.</returns>
    /// <remarks>
    /// Словаря может не быть: разметку открыл предпросмотр, или её пишет не
    /// расширение. Тогда возвращается сам ключ в восклицательных знаках —
    /// пропуск виден и не притворяется текстом, ровно как это делает
    /// <see cref="IStudioStrings"/> для ключа без перевода.
    /// </remarks>
    public object ProvideValue(IServiceProvider serviceProvider)
    {
        var root = (serviceProvider?.GetService(typeof(IRootObjectProvider)) as IRootObjectProvider)?.RootObject;
        var strings = StudioText.Of(root?.GetType().Assembly);

        return strings is null ? $"!{Key}!" : strings.Text(Key);
    }
}

/// <summary>
/// Связь «сборка расширения → его словарь строк».
/// </summary>
/// <remarks>
/// Шов между разметкой и хозяином: разметка знает только свою сборку, а какой
/// словарь ей отвечает — знает тот, кто поднял расширение. В контракте плагина
/// этого шва не видно: автору достаётся одно <c>{Text ключ}</c>.
/// <para>
/// Таблица слабая по ключу: словарь живёт ровно столько, сколько сборка. Иначе
/// выгруженный плагин остался бы держаться за свой контекст загрузки, а
/// выгрузка ради выгрузки и делалась.
/// </para>
/// </remarks>
internal static class StudioText
{
    private static readonly ConditionalWeakTable<Assembly, IStudioStrings> Known = [];

    /// <summary>Запоминает, какой словарь отвечает за сборку.</summary>
    /// <param name="assembly">Сборка расширения.</param>
    /// <param name="strings">Его словарь.</param>
    public static void Remember(Assembly assembly, IStudioStrings strings)
    {
        if (assembly is null || strings is null)
            return;

        Known.Remove(assembly);
        Known.Add(assembly, strings);
    }

    /// <summary>Словарь сборки, если он известен.</summary>
    /// <param name="assembly">Сборка, из которой пришла разметка.</param>
    /// <returns>Словарь или <c>null</c>.</returns>
    public static IStudioStrings? Of(Assembly? assembly) =>
        assembly is not null && Known.TryGetValue(assembly, out var strings) ? strings : null;
}
