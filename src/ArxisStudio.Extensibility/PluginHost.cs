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

    /// <summary>
    /// Поднимает все включённые плагины каталога.
    /// </summary>
    /// <param name="plugins">Что нашёл каталог.</param>
    /// <returns>Результаты по каждому включённому плагину.</returns>
    public IReadOnlyList<LoadedPlugin> LoadAll(IEnumerable<InstalledPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
            _loaded.Add(Load(plugin));

        return _loaded;
    }

    /// <summary>Опускает все поднятые плагины.</summary>
    public void Dispose()
    {
        foreach (var plugin in _loaded)
            plugin.Unload();

        _loaded.Clear();
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
            var studio = _contexts.Create(installed);

            var entries = assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioPlugin).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioPlugin>()
                .ToList();

            var services = assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(StudioService).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<StudioService>()
                .ToList();

            foreach (var plugin in entries)
                plugin.Activate(studio);

            foreach (var service in services)
                service.Start(studio);

            return new LoadedPlugin(installed, context, entries, services, null);
        }
        catch (Exception e) when (e is ReflectionTypeLoadException or BadImageFormatException
            or FileLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException)
        {
            context.Unload();
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
/// <param name="Context">Контекст загрузки; null, если поднять не удалось.</param>
/// <param name="Entries">Точки входа плагина.</param>
/// <param name="Services">Службы плагина.</param>
/// <param name="Error">Почему плагин не поднялся; null, если поднялся.</param>
public sealed record LoadedPlugin(
    InstalledPlugin Installed,
    AssemblyLoadContext? Context,
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
        new(installed, null, [], [], error);

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
