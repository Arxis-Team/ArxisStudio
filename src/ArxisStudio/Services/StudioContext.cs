using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Контекст, который студия выдаёт плагину.
/// </summary>
/// <remarks>
/// Журнал и команды общие, а путь проекта и папка — свои у каждого плагина:
/// плагин должен знать, где лежат его ресурсы, и не должен знать, где лежат
/// чужие. Службы — то, чем список услуг растёт без слома контракта.
/// </remarks>
/// <param name="Log">Журнал студии.</param>
/// <param name="Commands">Команды студии.</param>
/// <param name="ProjectPath">Путь к открытому проекту или null.</param>
/// <param name="PluginDirectory">Папка плагина.</param>
/// <param name="Services">Службы студии по типу; null — служб нет.</param>
public sealed record StudioContext(
    IStudioLog Log,
    IStudioCommands Commands,
    string? ProjectPath,
    string PluginDirectory,
    IReadOnlyDictionary<Type, object>? Services = null) : IStudioContext
{
    /// <inheritdoc/>
    public T? GetService<T>() where T : class =>
        Services is not null && Services.TryGetValue(typeof(T), out var service) ? service as T : null;
}

/// <summary>Выдаёт контекст каждому поднимаемому плагину.</summary>
/// <param name="log">Журнал студии.</param>
/// <param name="commands">Команды студии.</param>
/// <param name="projectPath">Путь к открытому проекту или null.</param>
/// <param name="services">Службы студии по типу.</param>
public sealed class StudioContextFactory(
    IStudioLog log,
    IStudioCommands commands,
    string? projectPath,
    IReadOnlyDictionary<Type, object>? services = null)
    : IStudioContextFactory
{
    /// <inheritdoc/>
    /// <remarks>
    /// Команды плагин получает своей обёрткой: реестр общий, а заявка должна
    /// знать заявителя — упавший обработчик иначе не приписать никому.
    /// </remarks>
    public IStudioContext Create(InstalledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var own = commands is StudioCommands registry
            ? new PluginCommands(registry, plugin.Id)
            : commands;

        return new StudioContext(log, own, projectPath, plugin.Directory, services);
    }
}
