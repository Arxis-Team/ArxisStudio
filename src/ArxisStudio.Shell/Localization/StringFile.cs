using System.Text.Json;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Файл словаря: плоский JSON вида «ключ — строка».
/// </summary>
/// <remarks>
/// Правило одно на всех, и потому записано один раз: словари читают и студия,
/// и плагины, и языковые пакеты. Испорченный или отсутствующий файл — это
/// пустой словарь, а не отказ: файл правит человек, запятая не на месте —
/// обычное дело, и студия, онемевшая из-за неё, была бы наказанием,
/// несоразмерным поводу. Пропуск при этом виден: ключ покажется как
/// <c>!ключ!</c>.
/// <para>
/// Комментарии и висячие запятые — не порча: так студия читает манифест,
/// <c>keymap.json</c> и настройки расширений, и словарь рядом с манифестом
/// выпадал из этого ряда. Прежде одна строка комментария выбрасывала весь файл —
/// молча, со всеми строками разом.
/// </para>
/// </remarks>
public static class StringFile
{
    /// <summary>
    /// Как читается словарь: с комментариями и висячими запятыми.
    /// </summary>
    /// <remarks>
    /// Встроенные словари студии читаются с теми же настройками: правило одно, и
    /// поставляемый словарь не должен разбираться иначе, чем положенный поверх него.
    /// </remarks>
    internal static JsonSerializerOptions Options { get; } = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Читает словарь.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns>Строки или пустой словарь, если файла нет или он испорчен.</returns>
    public static Dictionary<string, string> Read(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
