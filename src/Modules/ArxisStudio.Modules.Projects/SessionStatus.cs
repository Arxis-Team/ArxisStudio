using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Projects;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Состояние службы, каким его видит подписчик, — из сессии и в сравнении с прежним.
/// </summary>
/// <remarks>
/// <see cref="ProjectsStatus"/> — тип контракта, и его поверхность записана: вывод состояния из
/// сессии и сравнение живут здесь, рядом с ним, а не среди очередей и замков хоста.
/// </remarks>
internal static class SessionStatus
{
    /// <summary>Состояние службы при этой сессии; без сессии — «закрыто».</summary>
    /// <param name="session">Сессия; null — ничего не открыто.</param>
    public static ProjectsStatus Of(ProjectsSession? session) => session is null
        ? ProjectsStatus.Closed
        : new ProjectsStatus
        {
            Session = session.Number,
            State = session.Snapshot is not null ? ProjectsState.Ready
                : session.LastLoad is not null ? ProjectsState.Failed
                : ProjectsState.Opening,
            EntryPoint = session.EntryPoint,
            Configuration = session.Configuration,
            Snapshot = session.Snapshot,
            LastLoad = session.LastLoad,
            IsLoading = session.Pending is not null || session.Running is not null,
        };

    /// <summary>
    /// Нечего сказать подписчику: всё, что он видит, прежнее, — номер перемены не в счёт.
    /// </summary>
    /// <param name="a">Прежнее состояние.</param>
    /// <param name="b">Новое.</param>
    public static bool Same(ProjectsStatus a, ProjectsStatus b) =>
        a.Session == b.Session
        && a.State == b.State
        && a.EntryPoint == b.EntryPoint
        && string.Equals(a.Configuration, b.Configuration, StringComparison.Ordinal)
        && ReferenceEquals(a.Snapshot, b.Snapshot)
        && ReferenceEquals(a.LastLoad, b.LastLoad)
        && a.IsLoading == b.IsLoading;
}
