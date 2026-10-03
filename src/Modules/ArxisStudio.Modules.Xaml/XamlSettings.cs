using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Xaml;

/// <summary>Настройки службы XAML и их ключи.</summary>
internal static class XamlSettings
{
    /// <summary>Пауза перед сборкой дизайна после сохранения кода, в миллисекундах.</summary>
    public const string BuildDelayKey = "xaml.buildDelay";

    /// <summary>Пауза по умолчанию: дольше, чем «сохранить всё» у IDE, короче, чем человек заметит.</summary>
    public const int DefaultBuildDelay = 400;

    /// <summary>Ключи настроек модуля — те, что объявлены в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [BuildDelayKey];

    /// <summary>Пауза перед сборкой дизайна.</summary>
    /// <param name="settings">Настройки модуля.</param>
    /// <returns>Пауза: целые миллисекунды от нуля до десяти секунд.</returns>
    /// <remarks>
    /// Читается при подъёме поколения: хост спрашивает паузу один раз, и новая вступает в силу со
    /// следующим решением или после простоя.
    /// </remarks>
    public static TimeSpan BuildDelay(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var value = settings.Get<double?>(BuildDelayKey);
        var milliseconds = value is { } number && double.IsFinite(number)
            ? Math.Clamp(Math.Round(number), 0, 10_000)
            : DefaultBuildDelay;

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
