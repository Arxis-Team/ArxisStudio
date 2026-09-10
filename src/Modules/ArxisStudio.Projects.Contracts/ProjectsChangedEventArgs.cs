using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Что поменялось между двумя доставленными состояниями.</summary>
[Flags]
public enum ProjectsChanges
{
    /// <summary>Ничего: запись переиздана без перемен.</summary>
    None = 0,

    /// <summary>Открыт другой путь или открытое закрыто: идентичности проектов сменились.</summary>
    Session = 1,

    /// <summary>Сменилось <see cref="ProjectsStatus.State"/>.</summary>
    State = 2,

    /// <summary>Сменился снимок — пусть даже на такой же по содержимому.</summary>
    Snapshot = 4,

    /// <summary>Сменилась активная конфигурация.</summary>
    Configuration = 8,

    /// <summary>Закончилась загрузка: в <see cref="ProjectsStatus.LastLoad"/> новый итог.</summary>
    LastLoad = 16,

    /// <summary>Загрузка встала в очередь или закончилась: сменилось <see cref="ProjectsStatus.IsLoading"/>.</summary>
    Loading = 32,
}

/// <summary>
/// Перемена в службе проектов: прежнее доставленное состояние, новое и разность снимков.
/// </summary>
/// <remarks>
/// Конструктор открыт намеренно: плагин, проверяющий свою реакцию тестом, собирает событие сам и
/// обходится без службы.
/// </remarks>
public sealed class ProjectsChangedEventArgs : EventArgs
{
    /// <summary>Собирает событие.</summary>
    /// <param name="previous">Прежнее доставленное состояние.</param>
    /// <param name="current">Новое состояние.</param>
    /// <param name="added">Проекты, которых не было, — из снимка <paramref name="current"/>.</param>
    /// <param name="removed">Проекты, которых не стало, — из снимка <paramref name="previous"/>.</param>
    /// <param name="modified">Проекты, которые есть в обоих снимках и менялись, — из <paramref name="current"/>.</param>
    /// <param name="solutionChanged">Поменялось ли описание самого решения.</param>
    public ProjectsChangedEventArgs(
        ProjectsStatus previous,
        ProjectsStatus current,
        ImmutableArray<ProjectSnapshot> added,
        ImmutableArray<ProjectSnapshot> removed,
        ImmutableArray<ProjectSnapshot> modified,
        bool solutionChanged)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        Previous = previous;
        Current = current;
        Added = added.IsDefault ? [] : added;
        Removed = removed.IsDefault ? [] : removed;
        Modified = modified.IsDefault ? [] : modified;
        SolutionChanged = solutionChanged;
        Changes = Compare(previous, current);
    }

    /// <summary>Прежнее доставленное состояние.</summary>
    public ProjectsStatus Previous { get; }

    /// <summary>Новое состояние.</summary>
    public ProjectsStatus Current { get; }

    /// <summary>Какие поля записи поменялись; вычисляется из двух записей.</summary>
    public ProjectsChanges Changes { get; }

    /// <summary>Проекты, которых в прежнем снимке не было.</summary>
    /// <remarks>
    /// Сравниваются идентичности, поэтому со сменой сессии здесь оказываются все проекты нового
    /// снимка, а в <see cref="Removed"/> — все прежнего.
    /// </remarks>
    public ImmutableArray<ProjectSnapshot> Added { get; }

    /// <summary>Проекты, которых в новом снимке не стало; объекты из прежнего снимка.</summary>
    public ImmutableArray<ProjectSnapshot> Removed { get; }

    /// <summary>
    /// Проекты, которые есть в обоих снимках и менялись между ними.
    /// </summary>
    /// <remarks>
    /// Сравнивается содержимое, а не ссылки: каждая загрузка строит проекты заново, и проект,
    /// перечитанный без перемен, сюда не попадает. Склейка при этом концы заново не сравнивает —
    /// проект, поменявшийся и вернувшийся к прежнему между двумя доставками, здесь будет.
    /// </remarks>
    public ImmutableArray<ProjectSnapshot> Modified { get; }

    /// <summary>
    /// Поменялось описание самого решения: имя, папки, конфигурации, платформы, диагностики или
    /// сам открытый файл.
    /// </summary>
    public bool SolutionChanged { get; }

    private static ProjectsChanges Compare(ProjectsStatus previous, ProjectsStatus current)
    {
        var changes = ProjectsChanges.None;

        if (previous.Session != current.Session)
            changes |= ProjectsChanges.Session;

        if (previous.State != current.State)
            changes |= ProjectsChanges.State;

        if (!ReferenceEquals(previous.Snapshot, current.Snapshot))
            changes |= ProjectsChanges.Snapshot;

        if (!string.Equals(previous.Configuration, current.Configuration, StringComparison.Ordinal))
            changes |= ProjectsChanges.Configuration;

        if (!ReferenceEquals(previous.LastLoad, current.LastLoad))
            changes |= ProjectsChanges.LastLoad;

        if (previous.IsLoading != current.IsLoading)
            changes |= ProjectsChanges.Loading;

        return changes;
    }
}
