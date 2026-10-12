using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArxisStudio.Shell.Settings;

/// <summary>
/// Хранилище настроек студии в JSON-файле пользователя.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Текущие настройки.</summary>
    StudioSettings Current { get; }

    /// <summary>Сохраняет текущие настройки на диск.</summary>
    void Save();
}

/// <summary>
/// Настройки в <c>settings.json</c> каталога пользователя. Нечитаемый или
/// испорченный файл не роняет запуск: студия стартует со значениями по
/// умолчанию, а файл перезаписывается первым же сохранением.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    /// <summary>Создаёт хранилище над указанным файлом.</summary>
    /// <param name="path">Путь к файлу; по умолчанию — <see cref="StudioPaths.SettingsFile"/>.</param>
    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? StudioPaths.SettingsFile;
        Current = Load(_path);
    }

    /// <inheritdoc/>
    public StudioSettings Current { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Целиком или никак (<see cref="AtomicFile"/>): обрезок прочитался бы испорченным, и следующий
    /// запуск переписал бы умолчаниями все настройки человека.
    /// </remarks>
    public void Save() => AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Current, Options));

    private static StudioSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(path), Options) is { } read)
                return Sound(read);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Настройки — не то, ради чего стоит не запускать студию.
        }

        return new StudioSettings();
    }

    /// <summary>Ставит умолчание полю, чьего значения студия не пишет.</summary>
    /// <remarks>
    /// Разбор перечисления принимает и число, а поле строки — <c>null</c>: <c>"interfaceDensity": 7</c>
    /// доезжало до адреса словаря, которого в теме нет, а язык <c>null</c> ронял этап языка на старте.
    /// Файл правят руками, и одно незнакомое значение стоит умолчания своему полю, а не всем (запись 383).
    /// </remarks>
    private static StudioSettings Sound(StudioSettings read)
    {
        var defaults = new StudioSettings();

        if (!Enum.IsDefined(read.Theme))
            read.Theme = defaults.Theme;

        if (!Enum.IsDefined(read.Density))
            read.Density = defaults.Density;

        if (string.IsNullOrWhiteSpace(read.Language))
            read.Language = defaults.Language;

        return read;
    }
}
