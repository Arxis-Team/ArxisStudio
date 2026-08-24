using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Строки интерфейса. Словари лежат ресурсами сборки
/// (<c>Localization/Strings.&lt;код&gt;.json</c>); смена языка обновляет уже
/// показанный интерфейс — привязки следят за индексатором.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>Локаль, на которую студия опирается, если строки нет в выбранной.</summary>
    public const string FallbackLanguage = "ru";

    private FrozenDictionary<string, string> _strings = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _fallback = FrozenDictionary<string, string>.Empty;

    /// <summary>Общий экземпляр, к которому привязан интерфейс студии.</summary>
    public static Localizer Instance { get; } = new();

    private Localizer()
    {
        _fallback = LoadLanguage(FallbackLanguage);
        _strings = _fallback;
        Language = FallbackLanguage;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Текущий код языка.</summary>
    public string Language { get; private set; }

    /// <summary>
    /// Строка по ключу. Отсутствующий ключ возвращается как <c>!ключ!</c> —
    /// пропуск виден в интерфейсе и не притворяется текстом.
    /// </summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var back) ? back
        : $"!{key}!";

    /// <summary>Переключает язык интерфейса.</summary>
    /// <param name="language">Код культуры, например <c>ru</c> или <c>en</c>.</param>
    public void SetLanguage(string language)
    {
        if (string.Equals(language, Language, StringComparison.OrdinalIgnoreCase))
            return;

        _strings = LoadLanguage(language);
        Language = language;

        // Пустое имя свойства — «перечитайте всё», в том числе индексатор.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static FrozenDictionary<string, string> LoadLanguage(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = $"{typeof(Localizer).Namespace}.Strings.{language}.json";

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return FrozenDictionary<string, string>.Empty;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return parsed?.ToFrozenDictionary(StringComparer.Ordinal)
                   ?? FrozenDictionary<string, string>.Empty;
        }
        catch (JsonException)
        {
            return FrozenDictionary<string, string>.Empty;
        }
    }
}
