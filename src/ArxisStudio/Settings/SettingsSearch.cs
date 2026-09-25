namespace ArxisStudio.Settings;

/// <summary>
/// Правило поиска окна настроек: каким запрос ищут и что он находит.
/// </summary>
/// <remarks>
/// Поиск находит страницу, сужает её строки и отбирает плагины в менеджере — и обязан везде
/// находить одно и то же. Правило жило в каждой странице своей копией, и страница, нашедшая
/// строку, могла не показать её, будь у соседней копии другой регистр или другие края запроса.
/// </remarks>
internal static class SettingsSearch
{
    /// <summary>Запрос, каким его ищут: без пробелов по краям; пустой — поиска нет.</summary>
    /// <param name="query">Что набрано в поле поиска.</param>
    public static string? Normalize(string? query) => string.IsNullOrWhiteSpace(query) ? null : query.Trim();

    /// <summary>Находит ли запрос текст: вхождение без оглядки на регистр, по правилам языка человека.</summary>
    /// <param name="text">Где ищут.</param>
    /// <param name="query">Что ищут — уже нормализованное.</param>
    public static bool Matches(string text, string query) =>
        text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Находит ли запрос хоть один из текстов.</summary>
    /// <param name="texts">Где ищут.</param>
    /// <param name="query">Что ищут — уже нормализованное.</param>
    public static bool Matches(IEnumerable<string> texts, string query) => texts.Any(text => Matches(text, query));
}
