using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Чем кончилась перезагрузка плагина.
/// </summary>
/// <param name="Plugin">Новая копия; null, если перезагрузить не вышло.</param>
/// <param name="Error">Почему не вышло; null, если всё получилось.</param>
/// <param name="Released">
/// Выгрузился ли контекст прежней копии. Нет — значит, на её типы кто-то ещё
/// ссылается: подписка на событие студии, оставленный таймер, работающий поток.
/// Плагин при этом поднят, но старая копия осталась в памяти и продолжает
/// получать то, на что подписалась.
/// </param>
/// <param name="Notes">
/// О чём сказать, не отказывая: изменившийся контракт, который выгрузить
/// нечем. Ради этой строки перезагрузка контракт и перечитывает — потерять
/// её значит оставить автора править типы, которых процесс уже не увидит.
/// </param>
public sealed record PluginReload(
    LoadedPlugin? Plugin, string? Error, bool Released, IReadOnlyList<string> Notes);

/// <summary>
/// Итог каскадной перезагрузки.
/// </summary>
/// <param name="Released">По каждому опущенному: выгрузился ли его контекст.</param>
/// <param name="Raised">Поднятые в порядке подъёма, включая записи с ошибкой.</param>
/// <param name="Skipped">Кого не тронули и почему: не поднят, встроенный.</param>
/// <param name="Notes">О чём сказать, не отказывая: изменившийся контракт.</param>
/// <param name="Lingering">
/// Невыгрузившиеся контексты — слабыми ссылками: застрявшего на миг можно спросить ещё раз, не
/// удержав его этим вопросом.
/// </param>
/// <param name="Restart">
/// Кому новый контракт встанет только после перезапуска студии, и почему: общий контекст контрактов
/// не выгружается.
/// </param>
public sealed record PluginCascade(
    IReadOnlyDictionary<string, bool> Released,
    IReadOnlyList<LoadedPlugin> Raised,
    IReadOnlyDictionary<string, string> Skipped,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, WeakReference> Lingering,
    IReadOnlyDictionary<string, string> Restart);

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
/// Выгружаемый контекст загрузки; null — у встроенного модуля, чьи сборки живут в основном
/// контексте, и у плагина, который не поднялся. Встроенность спрашивают признаком
/// <see cref="InstalledPlugin.IsBuiltIn"/>, а не пустым контекстом.
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
            PluginHost.Quietly(service.Stop);

        foreach (var plugin in Entries)
            PluginHost.Quietly(plugin.Deactivate);

        (Context as PluginLoadContext)?.Release();
    }
}
