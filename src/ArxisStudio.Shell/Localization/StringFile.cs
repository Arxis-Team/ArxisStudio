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
            if (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options) is { } read)
                return read;

            Tell(path, "в файле null, а не словарь");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Tell(path, e.Message);
        }

        return [];
    }

    /// <summary>
    /// Файл словаря есть, а прочитать его не вышло: студия читает его пустым.
    /// </summary>
    /// <remarks>
    /// Пустой словарь вместо отказа — правило, и оно остаётся. Но молчать о нём
    /// нельзя: все строки хозяина словаря покажутся ключами, а у языкового пакета
    /// и у слоя поверх языка студии нет сборки, которая сказала бы об этом
    /// раньше. Звучит раз на версию файла: словарь перечитывают при смене языка
    /// и при перезагрузке плагина, и один сломанный файл заполнил бы журнал.
    /// Исправленный и снова испорченный — новая версия, о нём скажут снова.
    /// </remarks>
    public static event EventHandler<StringFileProblem>? Unreadable;

    private static readonly Dictionary<string, DateTime> Told = new(StringComparer.OrdinalIgnoreCase);

    private static void Tell(string path, string reason)
    {
        // Некому слушать — и помечать сказанным нечего: первый же слушатель
        // обязан услышать о файле, который сломан сейчас.
        if (Unreadable is not { } listeners)
            return;

        var written = File.GetLastWriteTimeUtc(path);

        lock (Told)
        {
            if (Told.TryGetValue(path, out var before) && before == written)
                return;

            Told[path] = written;
        }

        listeners(null, new StringFileProblem(path, reason));
    }
}

/// <summary>Словарь, который студия не прочла, и почему.</summary>
/// <param name="Path">Путь к файлу.</param>
/// <param name="Reason">Что помешало — словами разборщика.</param>
public sealed record StringFileProblem(string Path, string Reason);
