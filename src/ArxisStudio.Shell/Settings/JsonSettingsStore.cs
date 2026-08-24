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

    /// <summary>Происходит после сохранения настроек.</summary>
    event EventHandler? Saved;

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
    public event EventHandler? Saved;

    /// <inheritdoc/>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options));
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private static StudioSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(path), Options)
                       ?? new StudioSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Настройки — не то, ради чего стоит не запускать студию.
        }

        return new StudioSettings();
    }
}
