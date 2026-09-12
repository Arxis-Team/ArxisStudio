using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>
/// Одна операция над открытым: что делают, над чем и в какой конфигурации.
/// </summary>
/// <remarks>
/// Запись неизменяемая и описывает операцию целиком, поэтому событие о её конце говорит о том же,
/// о чём говорило событие о начале, — даже если к тому времени открыли другое решение.
/// </remarks>
public sealed record ProjectOperation
{
    /// <summary>Номер операции: растёт с каждой и не повторяется.</summary>
    /// <remarks>По нему начало сходится с концом: событий у операции два, а номер один.</remarks>
    public required long Id { get; init; }

    /// <summary>Что делают.</summary>
    public required ProjectOperationKind Kind { get; init; }

    /// <summary>Над чем: открытое решение или проект.</summary>
    public required CanonicalPath EntryPoint { get; init; }

    /// <summary>Названные проекты; пусто — открытое целиком.</summary>
    public ImmutableArray<ProjectIdentity> Projects
    {
        get => field.IsDefault ? [] : field;
        init;
    }

    /// <summary>Конфигурация, с которой операция началась; null — та, которую выбирает сам проект.</summary>
    public string? Configuration { get; init; }
}

/// <summary>
/// Начало или конец операции.
/// </summary>
/// <remarks>
/// Конструктор публичный намеренно: плагин, проверяющий свой обработчик, должен уметь собрать
/// событие сам, не поднимая ни студии, ни MSBuild.
/// </remarks>
public sealed class ProjectOperationEventArgs : EventArgs
{
    /// <summary>Собирает событие.</summary>
    /// <param name="operation">Какая операция.</param>
    /// <param name="result">Итог; null — операция только началась или была отменена.</param>
    /// <param name="isCancelled">Операцию отменили.</param>
    public ProjectOperationEventArgs(ProjectOperation operation, ProjectOperationResult? result = null, bool isCancelled = false)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Operation = operation;
        Result = result;
        IsCancelled = isCancelled;
    }

    /// <summary>Операция.</summary>
    public ProjectOperation Operation { get; }

    /// <summary>Итог; null — у начала и у отменённой.</summary>
    public ProjectOperationResult? Result { get; }

    /// <summary>Операцию отменили.</summary>
    public bool IsCancelled { get; }
}
