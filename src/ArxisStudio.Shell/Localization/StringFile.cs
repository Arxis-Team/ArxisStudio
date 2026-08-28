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
/// </remarks>
public static class StringFile
{
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
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
