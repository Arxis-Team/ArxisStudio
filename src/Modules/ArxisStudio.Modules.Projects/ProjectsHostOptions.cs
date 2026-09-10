using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Watching;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Шов службы проектов: из чего её собрать, если не из продуктовых частей.
/// </summary>
/// <remarks>
/// Модуль поднимает студия, конструктором без аргументов, поэтому подменить части можно только
/// через контекст: служба спрашивает этот тип у <see cref="IStudioContext.GetService{T}"/>, а тест
/// кладёт его в словарь служб. Тип внутренний: плагин его не назовёт, студия не регистрирует, и в
/// продукте ответ всегда null — берутся умолчания.
/// </remarks>
internal sealed class ProjectsHostOptions
{
    /// <summary>Продуктовая сборка службы.</summary>
    public static ProjectsHostOptions Default { get; } = new();

    /// <summary>
    /// Заводит движок новой сессии.
    /// </summary>
    /// <remarks>
    /// Провайдер MSBuild не называет ни одного типа Microsoft.Build, поэтому сборка этой лямбды
    /// движок не грузит: он поднимется при первой загрузке, после локатора.
    /// </remarks>
    public Func<ProjectWorkspace> Workspace { get; init; } =
        static () => new ProjectWorkspace(new MSBuildProjectProvider());

    /// <summary>Поток интерфейса; null — диспетчер Avalonia.</summary>
    public IProjectsThread? Thread { get; init; }

    /// <summary>Заводит слежение за диском; null в ответе — не следить.</summary>
    public Func<Action<ImmutableArray<CanonicalPath>>, IProjectsWatch?> Watch { get; init; } =
        static stale => new ProjectsWatch(stale, FileChangeCoalescingOptions.Default);

    /// <summary>
    /// Куда девать исключение подписчика; null — бросить заново в потоке интерфейса.
    /// </summary>
    /// <remarks>
    /// В продукте исключение уходит студии необработанным, и она приписывает его тому, чей код
    /// бросил. Тесту нужен сам сбой, а не упавший поток.
    /// </remarks>
    public Action<Exception>? SubscriberFailed { get; init; }
}
