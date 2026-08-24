using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Контекст, который студия выдаёт плагину.
/// </summary>
/// <remarks>
/// Журнал и команды общие, а путь проекта и папка — свои у каждого плагина:
/// плагин должен знать, где лежат его ресурсы, и не должен знать, где лежат
/// чужие.
/// </remarks>
/// <param name="Log">Журнал студии.</param>
/// <param name="Commands">Команды студии.</param>
/// <param name="ProjectPath">Путь к открытому проекту или null.</param>
/// <param name="PluginDirectory">Папка плагина.</param>
public sealed record StudioContext(
    IStudioLog Log,
    IStudioCommands Commands,
    string? ProjectPath,
    string PluginDirectory) : IStudioContext;

/// <summary>Выдаёт контекст каждому поднимаемому плагину.</summary>
/// <param name="log">Журнал студии.</param>
/// <param name="commands">Команды студии.</param>
/// <param name="projectPath">Путь к открытому проекту или null.</param>
public sealed class StudioContextFactory(IStudioLog log, IStudioCommands commands, string? projectPath)
    : IStudioContextFactory
{
    /// <inheritdoc/>
    public IStudioContext Create(InstalledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return new StudioContext(log, commands, projectPath, plugin.Directory);
    }
}
