using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Ядро службы соседей: одно на студию, знает хост и каталог.
/// </summary>
/// <remarks>
/// Заводится раньше хоста — фабрика контекстов получает его параметром, а
/// хост появляется позже, — поэтому хост подключается отдельным шагом. До
/// подключения служба честно отвечает «никто не поднят»: спрашивать её в это
/// время некому, контексты раздаются только при подъёме.
/// </remarks>
public sealed class StudioPluginRoster
{
    private PluginHost? _host;
    private Func<IReadOnlyList<InstalledPlugin>>? _installed;

    /// <summary>Состав поднятых изменился.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Подключает ядро к хосту и каталогу.
    /// </summary>
    /// <param name="host">Хост плагинов.</param>
    /// <param name="installed">Откуда брать установленных; список живой.</param>
    public void Attach(PluginHost host, Func<IReadOnlyList<InstalledPlugin>> installed)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(installed);

        _host = host;
        _installed = installed;

        host.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Поднят ли плагин и работает ли.</summary>
    /// <param name="pluginId">Идентификатор.</param>
    public bool IsActive(string pluginId) =>
        _host?.Loaded.Any(loaded =>
            loaded.IsLoaded &&
            string.Equals(loaded.Installed.Id, pluginId, StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>Версия из манифеста; null — нет ни среди установленных, ни среди поднятых.</summary>
    /// <param name="pluginId">Идентификатор.</param>
    /// <remarks>
    /// Каталог знает установленных, но не модули: те приезжают со студией и в
    /// папке плагинов не лежат. Их версия берётся у поднятого — иначе сосед,
    /// объявивший нижнюю границу на модуль, видел бы его отсутствующим и не
    /// получал его экспортов, хотя граф зависимостей ту же зависимость принимает.
    /// </remarks>
    public string? Version(string pluginId) =>
        (_installed?.Invoke().FirstOrDefault(plugin => Named(plugin, pluginId))
            ?? _host?.Loaded.Select(loaded => loaded.Installed).FirstOrDefault(plugin => Named(plugin, pluginId)))
        ?.Manifest?.Version;

    private static bool Named(InstalledPlugin plugin, string pluginId) =>
        string.Equals(plugin.Id, pluginId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Служба соседей глазами одного плагина.
/// </summary>
/// <remarks>
/// Ядро общее, спрашивающий свой — по образцу команд: ответ зависит от того,
/// кто спрашивает. Сосед, объявленный владельцем в зависимостях с нижней
/// границей и оказавшийся старее, считается отсутствующим: спрашивающий
/// написан под возможности, которых в старой версии нет, и «да» означало бы
/// падение на первом обращении. Проверка версий живёт здесь, одна на всех, —
/// авторам не приходится сравнивать номера руками.
/// </remarks>
/// <param name="roster">Общее ядро.</param>
/// <param name="owner">Чьими глазами смотрим.</param>
public sealed class PluginNeighbours(StudioPluginRoster roster, InstalledPlugin owner) : IStudioPlugins
{
    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => roster.Changed += value;
        remove => roster.Changed -= value;
    }

    /// <inheritdoc/>
    public bool IsActive(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        if (!roster.IsActive(pluginId))
            return false;

        var declared = (owner.Manifest?.Dependencies ?? []).FirstOrDefault(dependency =>
            string.Equals(dependency.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        return declared is null || PluginGraph.Satisfies(roster.Version(pluginId), declared.Min);
    }

    /// <inheritdoc/>
    public string? Version(string pluginId)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);

        return roster.Version(pluginId);
    }
}
