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
/// <param name="Settings">Настройки этого плагина.</param>
/// <param name="Tasks">Фоновые задачи этого плагина.</param>
/// <param name="Strings">Словари этого плагина.</param>
/// <param name="ProjectPath">Путь к открытому проекту или null.</param>
/// <param name="PluginDirectory">Папка плагина.</param>
/// <param name="Services">Службы студии по типу; null — служб нет.</param>
public sealed record StudioContext(
    IStudioLog Log,
    IStudioCommands Commands,
    IStudioSettings Settings,
    IStudioTasks Tasks,
    IStudioStrings Strings,
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
/// <param name="settings">
/// Хранилище настроек плагинов; null — завести своё, по стандартным путям.
/// </param>
/// <param name="tasks">Список фоновых задач; null — завести свой.</param>
/// <param name="guard">Шов вызовов плагина; null — завести свой.</param>
/// <param name="plugins">Ядро службы соседей; null — службы нет.</param>
/// <param name="exports">Реестр экспортов; null — обмена реализациями нет.</param>
/// <param name="toolbar">Полоса студии; null — состояние элементов менять негде.</param>
/// <param name="dock">Док студии; null — панели на экран доставать нечем.</param>
public sealed class StudioContextFactory(
    IStudioLog log,
    IStudioCommands commands,
    string? projectPath,
    IReadOnlyDictionary<Type, object>? services = null,
    PluginSettingsStore? settings = null,
    StudioTaskRegistry? tasks = null,
    PluginGuard? guard = null,
    StudioPluginRoster? plugins = null,
    StudioExportRegistry? exports = null,
    StudioToolBar? toolbar = null,
    StudioDock? dock = null)
    : IStudioContextFactory
{
    private readonly StudioTaskRegistry _tasks = tasks ?? new StudioTaskRegistry();
    private readonly PluginGuard _guard = guard ?? new PluginGuard();

    /// <summary>Задачи, о которых знает студия.</summary>
    public StudioTaskRegistry Tasks => _tasks;

    private readonly PluginSettingsStore _settings = settings ?? new PluginSettingsStore(projectPath);

    /// <summary>Настройки, которые фабрика раздаёт плагинам.</summary>
    public PluginSettingsStore Settings => _settings;

    /// <summary>Настройки, выданные плагинам, — по идентификатору плагина.</summary>
    /// <remarks>
    /// Нужны студии, чтобы сказать плагину об изменении, сделанном мимо него:
    /// человек правит настройки в окне студии, а не через плагин.
    /// </remarks>
    public IReadOnlyDictionary<string, PluginSettings> Issued => _issued;

    private readonly Dictionary<string, PluginSettings> _issued = new(StringComparer.Ordinal);

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

        var settings = new PluginSettings(
            plugin.Id,
            plugin.Manifest?.Contributions.Settings ?? [],
            _settings,
            log);

        _issued[plugin.Id] = settings;

        var tasks = new PluginTasks(plugin.Id, _tasks, _guard, log);

        // Службы соседей и экспортов выдаются своими на каждого: ответ
        // IsActive зависит от того, кто спрашивает, а публикация обязана
        // знать хозяина.
        var granted = services;

        if (plugins is not null || exports is not null || toolbar is not null || dock is not null)
        {
            var extended = services is null
                ? new Dictionary<Type, object>()
                : new Dictionary<Type, object>(services.ToDictionary(pair => pair.Key, pair => pair.Value));

            var neighbours = plugins is null ? null : new PluginNeighbours(plugins, plugin);

            if (neighbours is not null)
                extended[typeof(IStudioPlugins)] = neighbours;

            // Экспортам отдаётся та же служба соседей: два ответа об одном
            // соседе обязаны сходиться. Разойдись они — плагин узнавал бы от
            // одной «соседа нет», а от другой получал его реализацию.
            if (exports is not null)
                extended[typeof(IStudioExports)] = new PluginExports(exports, plugin, neighbours);

            // Полоса — тоже именной фасад: плагин меняет состояние только своих
            // элементов, и чей это вызов, знает лишь тот, кто выдал контекст.
            if (toolbar is not null)
                extended[typeof(IStudioToolBar)] = new PluginToolBar(toolbar, plugin.Id);

            // Док — тем более именной: имя панели в нём начинается с имени
            // плагина, и подставить его может только выдавший контекст.
            if (dock is not null)
                extended[typeof(IStudioToolWindows)] = new PluginToolWindows(dock, plugin.Id);

            granted = extended;
        }

        return new StudioContext(log, own, settings, tasks, plugin.Strings, projectPath, plugin.Directory, granted);
    }
}
