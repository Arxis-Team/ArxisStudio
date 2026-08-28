using Avalonia.Media.Imaging;

namespace ArxisStudio.Services;

/// <summary>
/// Значки плагинов: читает картинку из папки плагина и помнит прочитанное.
/// </summary>
/// <remarks>
/// Это единственное место, где студия открывает картинку с диска, и картинку
/// эту принёс посторонний. Отсюда два правила, которых нет у остальных файлов
/// плагина: предел на размер и декодирование сразу в нужную величину. Иначе
/// «значок» на двадцать тысяч пикселей в стороне занял бы сотни мегабайт
/// памяти на ровном месте — и ничего не нарисовал бы.
/// <para>
/// Прочитанное помнится по пути и времени правки: карточка пересобирается при
/// каждом заходе в менеджер, а файл при этом один и тот же. Не помни мы его,
/// каждый заход заводил бы новые растры, и память утекала бы на глазах.
/// Исправленный файл при этом виден: время правки изменилось — читаем заново.
/// </para>
/// </remarks>
public sealed class PluginIcons
{
    /// <summary>Больше этого значок не читается.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Во столько пикселей по ширине декодируется картинка.</summary>
    /// <remarks>
    /// На карточке значок занимает 24 точки; запас нужен для экранов с
    /// удвоенным масштабом. Больше брать незачем: разница уже не видна, а
    /// память растёт квадратом.
    /// </remarks>
    public const int Width = 128;

    private readonly Dictionary<string, Known> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Общий на студию: карточка пересобирается, а файлы одни и те же.</summary>
    public static PluginIcons Instance { get; } = new();

    /// <summary>
    /// Значок по пути.
    /// </summary>
    /// <param name="path">Путь к картинке; null — значка нет.</param>
    /// <returns>Картинка или null, если её нет, она велика или не читается.</returns>
    public Bitmap? Of(string? path)
    {
        if (path is not { Length: > 0 })
            return null;

        var file = new FileInfo(path);

        if (!file.Exists)
            return null;

        lock (_known)
        {
            // Непрочитанное помнится тоже: битый файл остаётся битым, и
            // ломиться в него на каждой перерисовке незачем.
            if (_known.TryGetValue(path, out var known) && known.Written == file.LastWriteTimeUtc)
                return known.Image;

            var image = Read(file);

            _known[path] = new Known(file.LastWriteTimeUtc, image);
            return image;
        }
    }

    private static Bitmap? Read(FileInfo file)
    {
        if (file.Length > MaxBytes)
            return null;

        try
        {
            using var stream = file.OpenRead();

            return Bitmap.DecodeToWidth(stream, Width);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // Перехват широкий намеренно. Файл принёс посторонний, и чем
            // ответит декодер на мусор — его дело: Skia, например, на
            // нераспознанном файле бросает NullReferenceException, а не
            // что-нибудь про формат. Список исключений здесь означал бы, что
            // студия падает на всём, чего мы не угадали, — на чужой картинке.
            return null;
        }
    }

    private readonly record struct Known(DateTime Written, Bitmap? Image);
}
