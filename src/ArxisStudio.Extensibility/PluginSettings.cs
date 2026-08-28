using System.Text.Json;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Настройки одного плагина: то, что он видит через свой контекст.
/// </summary>
/// <remarks>
/// Хранилище одно на студию, а ветка у каждого своя: плагин не может ни
/// прочитать чужую настройку, ни записать её — не потому, что это запрещено, а
/// потому, что чужих ключей он попросту не назовёт.
/// <para>
/// Ключ проверяется по манифесту. Незаявленный ключ отвергается со словом в
/// журнал: по объявлению студия знает, куда класть значение и как показать его
/// в настройках, — без объявления значение легло бы неизвестно куда и не
/// показалось бы никому, включая того, кто его записал.
/// </para>
/// </remarks>
/// <param name="pluginId">Чьи это настройки.</param>
/// <param name="declared">Что плагин объявил в манифесте.</param>
/// <param name="store">Общее хранилище.</param>
/// <param name="log">Куда жаловаться на незаявленный ключ.</param>
public sealed class PluginSettings(
    string pluginId,
    IList<PluginSetting> declared,
    PluginSettingsStore store,
    IStudioLog log) : IStudioSettings
{
    /// <inheritdoc/>
    public event EventHandler<string>? Changed;

    /// <inheritdoc/>
    public T? Get<T>(string key)
    {
        if (Declaration(key) is not { } setting)
            return default;

        var value = store.Read(pluginId, setting);

        if (value is null)
            return default;

        try
        {
            // Значение приходит двумя дорогами: из файла — разобранным узлом,
            // из манифеста — обёрнутым объектом. GetValue понимает первое,
            // Deserialize — второе.
            return value.GetValue<T>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            try
            {
                return value.Deserialize<T>();
            }
            catch (Exception inner) when (inner is JsonException or NotSupportedException)
            {
                log.Write(StudioLogLevel.Warning, "Plugins", $"{pluginId}: настройку {key} не прочитать как {typeof(T).Name}");
                return default;
            }
        }

        // Тип в файле мог разойтись с тем, что просит плагин: файл правят
        // руками. Это не отказ студии — это значение, которого она не поняла.
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            log.Write(StudioLogLevel.Warning, "Plugins", $"{pluginId}: настройку {key} не прочитать как {typeof(T).Name}");
            return default;
        }
    }

    /// <inheritdoc/>
    public void Set(string key, object? value)
    {
        if (Declaration(key) is not { } setting)
            return;

        if (store.Write(pluginId, setting, value) is { } error)
        {
            log.Write(StudioLogLevel.Warning, "Plugins", $"{pluginId}: {error}");
            return;
        }

        Changed?.Invoke(this, key);
    }

    /// <summary>Говорит плагину, что настройку изменили не им.</summary>
    /// <param name="key">Ключ изменившейся настройки.</param>
    public void Announce(string key) => Changed?.Invoke(this, key);

    private PluginSetting? Declaration(string key)
    {
        var found = declared.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal));

        if (found is null)
            log.Write(StudioLogLevel.Warning, "Plugins", $"{pluginId}: настройка {key} не объявлена в манифесте");

        return found;
    }
}
