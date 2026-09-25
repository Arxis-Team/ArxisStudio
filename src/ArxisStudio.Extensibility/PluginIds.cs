namespace ArxisStudio.Extensibility;

/// <summary>
/// Как сравнивают идентификаторы плагинов: без регистра.
/// </summary>
/// <remarks>
/// Идентификатор пишут разные руки: автор плагина — в своём манифесте, соседи — в зависимостях,
/// автор языкового пакета — в переводах чужих плагинов. Граф зависимостей сравнивал его без
/// регистра, а хост и переводы пакета — с регистром, и сосед, написавший <c>Arxis.Hello</c> вместо
/// <c>arxis.hello</c>, попадал под одно правило и мимо другого. Без регистра — потому что
/// идентификатор становится именем папки, а папки Windows регистра не различают.
/// </remarks>
public static class PluginIds
{
    /// <summary>Сравнение идентификаторов — для словарей и множеств.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Один ли это плагин.</summary>
    /// <param name="left">Один идентификатор.</param>
    /// <param name="right">Другой.</param>
    public static bool Same(string? left, string? right) => Comparer.Equals(left, right);
}
