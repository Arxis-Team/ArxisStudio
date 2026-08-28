namespace ArxisStudio.Extensibility;

/// <summary>
/// Переводы чужих плагинов, пришедшие из языковых пакетов.
/// </summary>
/// <remarks>
/// Словари плагина живут в его папке, и до этого дня перевести плагин мог
/// только его автор. Пакет — способ перевести чужое: он объявляет, для кого
/// и на какой язык, а студия подкладывает это плагину, когда тот спрашивает
/// строку.
/// </remarks>
public interface IPluginTranslations
{
    /// <summary>
    /// Перевод плагина на язык.
    /// </summary>
    /// <param name="pluginId">Чей плагин переводят.</param>
    /// <param name="language">Код языка.</param>
    /// <returns>Строки или пустой словарь, если такого перевода нет.</returns>
    IReadOnlyDictionary<string, string> Read(string pluginId, string language);
}
