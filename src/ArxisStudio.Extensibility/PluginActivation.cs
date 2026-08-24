using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Читает события активации манифеста и решает, когда плагин поднимать.
/// </summary>
/// <remarks>
/// Смысл событий в том, чтобы плагин, который сегодня не понадобился, не стоил
/// времени старта: манифест студия читает без загрузки сборки, и меню с
/// панелями строятся по нему.
/// <para>
/// Панель — исключение: показать её, не подняв плагин, нечем, поэтому плагин с
/// <c>onToolWindow:</c> поднимается сразу. Отложенная панель потребовала бы
/// заглушки, которая подменяет себя настоящей, — это отдельная работа, и делать
/// её вслепую незачем.
/// </para>
/// </remarks>
public static class PluginActivation
{
    /// <summary>Плагин поднимается при запуске студии.</summary>
    public const string OnStartup = "onStartup";

    /// <summary>Плагин поднимается перед вызовом команды.</summary>
    public const string OnCommand = "onCommand:";

    /// <summary>Плагин поднимается при открытии файла такого типа.</summary>
    public const string OnFileType = "onFileType:";

    /// <summary>Плагин поднимается, потому что показывает панель.</summary>
    public const string OnToolWindow = "onToolWindow:";

    /// <summary>
    /// Нужно ли поднимать плагин сразу.
    /// </summary>
    /// <param name="manifest">Манифест плагина.</param>
    /// <remarks>
    /// Манифест без событий тоже поднимается сразу: не объявить событие — не то
    /// же самое, что попросить отложить.
    /// </remarks>
    public static bool IsEager(PluginManifest? manifest) =>
        manifest is null ||
        manifest.Activation.Count == 0 ||
        manifest.Activation.Any(activation =>
            Is(activation, OnStartup) ||
            activation.StartsWith(OnToolWindow, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ждёт ли плагин вызова этой команды.</summary>
    /// <param name="manifest">Манифест плагина.</param>
    /// <param name="commandId">Идентификатор команды.</param>
    public static bool WaitsForCommand(PluginManifest? manifest, string commandId) =>
        Waits(manifest, OnCommand, commandId, StringComparer.Ordinal);

    /// <summary>Ждёт ли плагин открытия файла такого типа.</summary>
    /// <param name="manifest">Манифест плагина.</param>
    /// <param name="extension">Расширение с точкой, например <c>.fig</c>.</param>
    public static bool WaitsForFileType(PluginManifest? manifest, string extension) =>
        Waits(manifest, OnFileType, extension, StringComparer.OrdinalIgnoreCase);

    private static bool Waits(PluginManifest? manifest, string prefix, string value, StringComparer comparer)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(value))
            return false;

        return manifest.Activation.Any(activation =>
            activation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            comparer.Equals(activation[prefix.Length..].Trim(), value));
    }

    private static bool Is(string activation, string name) =>
        string.Equals(activation.Trim(), name, StringComparison.OrdinalIgnoreCase);
}
