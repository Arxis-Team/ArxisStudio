using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Поднимает включённые плагины и держит их, пока студия работает.
/// </summary>
/// <remarks>
/// Каждый внешний плагин живёт в своём выгружаемом контексте загрузки: иначе
/// выключить плагин можно было бы только перезапуском студии. Контекст
/// разрешает сборки плагина рядом с его entry-сборкой, а сборки самой студии —
/// SDK, контролы, Avalonia — берёт из основного контекста: два экземпляра одного
/// типа не были бы одним типом, и панель плагина не встала бы в интерфейс.
/// <para>
/// Сбой плагина не должен уронить студию, поэтому загрузка и активация каждого
/// плагина обёрнуты: упавший плагин попадает в список с ошибкой, остальные
/// продолжают работать.
/// </para>
/// </remarks>
public sealed class PluginHost : IDisposable
{
    private readonly List<LoadedPlugin> _loaded = [];
    private readonly List<InstalledPlugin> _deferred = [];
    private readonly IStudioContextFactory _contexts;

    /// <summary>Создаёт хост.</summary>
    /// <param name="contexts">Чем выдавать плагину его контекст.</param>
    public PluginHost(IStudioContextFactory contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        _contexts = contexts;
    }

    /// <summary>Поднятые плагины.</summary>
    public IReadOnlyList<LoadedPlugin> Loaded => _loaded;

    /// <summary>Плагины, ждущие своего события.</summary>
    public IReadOnlyList<InstalledPlugin> Deferred => _deferred;

    /// <summary>
    /// Находит плагин, чей код есть в стеке исключения.
    /// </summary>
    /// <param name="error">Исключение, пришедшее без спроса.</param>
    /// <returns>Плагин или null, если в стеке только код студии.</returns>
    /// <remarks>
    /// Так приписываются падения, пришедшие мимо шва: необработанное
    /// исключение потока интерфейса и задача, чьё исключение никто не забрал.
    /// Там некому назвать плагин — его приходится узнавать по стеку.
    /// <para>
    /// Ищется первый кадр чужого кода: студия зовёт плагин, плагин зовёт
    /// студию, и снизу стека может оказаться и то и другое. Виноват тот, чей
    /// код бросил, — он ближе к вершине.
    /// </para>
    /// <para>
    /// Сборку сверяем и по списку плагина, и по его контексту загрузки: список
    /// знает entry-сборку и то, что нашлось рядом, а приватную зависимость
    /// плагина, подгруженную по требованию, знает только контекст.
    /// </para>
    /// </remarks>
    public LoadedPlugin? Blame(Exception? error) => Blame(error, _loaded);

    /// <summary>
    /// Находит среди перечисленных плагинов того, чей код есть в стеке.
    /// </summary>
    /// <param name="error">Исключение, пришедшее без спроса.</param>
    /// <param name="loaded">Где искать.</param>
    /// <returns>Плагин или null, если в стеке только код студии.</returns>
    public static LoadedPlugin? Blame(Exception? error, IReadOnlyCollection<LoadedPlugin> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        for (var current = error; current is not null; current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                if (frame.GetMethod()?.DeclaringType?.Assembly is not { } assembly)
                    continue;

                if (Owner(assembly, loaded) is { } plugin)
                    return plugin;
            }
        }

        return null;
    }

    private static LoadedPlugin? Owner(Assembly assembly, IEnumerable<LoadedPlugin> loaded) =>
        loaded.FirstOrDefault(plugin =>
            plugin.Assemblies.Contains(assembly) ||
            (plugin.Context is not null && AssemblyLoadContext.GetLoadContext(assembly) == plugin.Context));

    /// <summary>
    /// Принимает каталог и поднимает то, что просит подняться сразу.
    /// </summary>
    /// <param name="plugins">Что нашёл каталог.</param>
    /// <returns>Результаты по поднятым плагинам.</returns>
    /// <remarks>
    /// Остальные остаются в списке ждущих: их меню и панели студия покажет по
    /// манифесту, а сборка поднимется, когда придёт объявленное событие.
    /// </remarks>
    public IReadOnlyList<LoadedPlugin> LoadStartup(IEnumerable<InstalledPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var enabled = plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }).ToList();
        var raised = new List<LoadedPlugin>();

        foreach (var plugin in enabled)
        {
            if (PluginActivation.IsEager(plugin.Manifest))
                raised.Add(Add(plugin));
            else
                _deferred.Add(plugin);
        }

        return raised;
    }

    /// <summary>
    /// Поднимает ждущий плагин.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <returns>
    /// Поднятый плагин или null, если такого среди ждущих нет — он либо уже
    /// поднят, либо выключен.
    /// </returns>
    public LoadedPlugin? Activate(string pluginId)
    {
        var waiting = _deferred.FirstOrDefault(plugin => plugin.Id == pluginId);

        if (waiting is null)
            return null;

        _deferred.Remove(waiting);
        return Add(waiting);
    }

    private LoadedPlugin Add(InstalledPlugin installed)
    {
        var loaded = Load(installed);

        _loaded.Add(loaded);
        return loaded;
    }

    /// <summary>Опускает все поднятые плагины.</summary>
    public void Dispose()
    {
        foreach (var plugin in _loaded)
            plugin.Unload();

        _loaded.Clear();
        _deferred.Clear();
    }

    private LoadedPlugin Load(InstalledPlugin installed)
    {
        if (installed.Manifest?.Entry is not { Length: > 0 } entry)
            return LoadedPlugin.Failed(installed, "В манифесте не указана entry-сборка");

        var assemblyPath = Path.Combine(installed.Directory, entry);

        if (!File.Exists(assemblyPath))
            return LoadedPlugin.Failed(installed, $"Сборка плагина не найдена: {entry}");

        var context = new PluginLoadContext(installed.Id, assemblyPath);

        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            return Raise(installed, context, [assembly], _contexts.Create(installed));
        }
        catch (Exception e) when (e is ReflectionTypeLoadException or BadImageFormatException
            or FileLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException)
        {
            context.Unload();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    /// <summary>
    /// Поднимает встроенный модуль: те же точки входа и тот же контракт, но в
    /// основном контексте загрузки и без выгрузки.
    /// </summary>
    /// <param name="assembly">Сборка модуля со встроенным <c>module.json</c>.</param>
    /// <returns>Результат — как у обычного плагина.</returns>
    /// <remarks>
    /// Модуль отличается от плагина только способом доставки: путь подъёма
    /// один, поэтому код между режимами переносим. Сбой модуля точно так же
    /// остаётся записью, а не падением студии.
    /// </remarks>
    public LoadedPlugin LoadBuiltIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var (manifest, error) = ModuleManifest.Load(assembly);

        var installed = new InstalledPlugin(
            AppContext.BaseDirectory,
            manifest,
            error,
            IsEnabled: true);

        var loaded = manifest is null
            ? LoadedPlugin.Failed(installed, error ?? "Манифест модуля не разобрался")
            : Raise(installed, context: null, [assembly], _contexts.Create(installed));

        _loaded.Add(loaded);
        return loaded;
    }

    private static LoadedPlugin Raise(
        InstalledPlugin installed,
        PluginLoadContext? context,
        IReadOnlyList<Assembly> assemblies,
        IStudioContext studio)
    {
        try
        {
            var entries = assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioPlugin).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioPlugin>()
                .ToList();

            var services = assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioService).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioService>()
                .ToList();

            foreach (var plugin in entries)
                plugin.Activate(studio);

            foreach (var service in services)
                service.Start(studio);

            return new LoadedPlugin(installed, context, assemblies, studio, entries, services, null);
        }
        catch (Exception e) when (e is ReflectionTypeLoadException or BadImageFormatException
            or FileLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException)
        {
            context?.Unload();
            return LoadedPlugin.Failed(installed, Describe(e));
        }
    }

    private static string Describe(Exception error) =>
        error is TargetInvocationException { InnerException: { } inner } ? inner.Message : error.Message;
}

/// <summary>Кто выдаёт плагину его контекст.</summary>
public interface IStudioContextFactory
{
    /// <summary>Создаёт контекст для плагина.</summary>
    /// <param name="plugin">Плагин, которому он предназначен.</param>
    IStudioContext Create(InstalledPlugin plugin);
}

/// <summary>Поднятый плагин или причина, почему он не поднялся.</summary>
/// <param name="Installed">Плагин каталога.</param>
/// <param name="Context">
/// Выгружаемый контекст загрузки; null у встроенного модуля — его сборки живут
/// в основном контексте и не выгружаются.
/// </param>
/// <param name="Assemblies">Сборки плагина.</param>
/// <param name="Studio">Контекст, выданный плагину при подъёме.</param>
/// <param name="Entries">Точки входа плагина.</param>
/// <param name="Services">Службы плагина.</param>
/// <param name="Error">Почему плагин не поднялся; null, если поднялся.</param>
public sealed record LoadedPlugin(
    InstalledPlugin Installed,
    AssemblyLoadContext? Context,
    IReadOnlyList<Assembly> Assemblies,
    IStudioContext? Studio,
    IReadOnlyList<StudioPlugin> Entries,
    IReadOnlyList<StudioService> Services,
    string? Error)
{
    /// <summary>Плагин работает.</summary>
    public bool IsLoaded => Error is null;

    /// <summary>Собирает запись о плагине, который поднять не удалось.</summary>
    /// <param name="installed">Плагин каталога.</param>
    /// <param name="error">Почему не удалось.</param>
    public static LoadedPlugin Failed(InstalledPlugin installed, string error) =>
        new(installed, null, [], null, [], [], error);

    /// <summary>Останавливает плагин и выгружает его сборки.</summary>
    public void Unload()
    {
        foreach (var service in Services)
            Safely(service.Stop);

        foreach (var plugin in Entries)
            Safely(plugin.Deactivate);

        (Context as PluginLoadContext)?.Unload();

        // Плагин, упавший на прощание, уже никому не мешает: студия его
        // отпускает, и держаться за исключение незачем.
        static void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
            {
            }
        }
    }
}

/// <summary>
/// Контекст загрузки одного плагина.
/// </summary>
/// <remarks>
/// Сборки студии сюда не тянутся: общий тип должен быть один на всех, иначе
/// <c>StudioPlugin</c> плагина и <c>StudioPlugin</c> студии окажутся разными
/// типами. Поэтому разрешаются только те сборки, что лежат рядом с плагином, а
/// всё остальное отдаётся основному контексту.
/// </remarks>
internal sealed class PluginLoadContext(string name, string entryPath)
    : AssemblyLoadContext($"arxis-plugin:{name}", isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);

    protected override Assembly? Load(AssemblyName assemblyName) =>
        _resolver.ResolveAssemblyToPath(assemblyName) is { } path && !IsShared(assemblyName)
            ? LoadFromAssemblyPath(path)
            : null;

    private static bool IsShared(AssemblyName assemblyName) =>
        assemblyName.Name is { } name &&
        (name.StartsWith("Avalonia", StringComparison.Ordinal) ||
         name.StartsWith("ArxisStudio.Sdk", StringComparison.Ordinal) ||
         name.StartsWith("ArxisStudio.Controls", StringComparison.Ordinal));
}
