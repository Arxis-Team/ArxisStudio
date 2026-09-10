using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Состояние службы проектов.</summary>
public enum ProjectsState
{
    /// <summary>Ничего не открыто.</summary>
    Closed,

    /// <summary>Открытие идёт, а читать ещё нечего: первая загрузка не закончилась.</summary>
    Opening,

    /// <summary>
    /// Снимок есть.
    /// </summary>
    /// <remarks>
    /// Последняя загрузка при этом могла провалиться — снимок тогда прежний, а провал лежит в
    /// <see cref="ProjectsStatus.LastLoad"/>.
    /// </remarks>
    Ready,

    /// <summary>Открыто, но ни одна загрузка этой сессии снимка не дала.</summary>
    Failed,
}

/// <summary>
/// Состояние службы проектов одной неизменяемой записью.
/// </summary>
/// <remarks>
/// Поля лежат вместе, потому что и меняются вместе. Снимок, состояние и итог загрузки из одной
/// записи друг другу не противоречат, а три отдельных чтения свойств могли бы попасть на три
/// разные публикации.
/// </remarks>
public sealed record ProjectsStatus
{
    /// <summary>Ничего не открыто и ещё не открывалось — так служба начинает.</summary>
    public static ProjectsStatus Closed { get; } = new();

    /// <summary>
    /// Номер публикации: растёт с каждой переменой и не убывает.
    /// </summary>
    /// <remarks>Две записи одной службы с одним номером — одна и та же запись.</remarks>
    public long Sequence { get; init; }

    /// <summary>
    /// Номер сессии: растёт с каждым открытием нового пути; 0 — ничего не открыто.
    /// </summary>
    /// <remarks>
    /// Пока номер прежний, прежние и идентичности проектов: перезагрузки их сохраняют. Сменился —
    /// всё, что было записано по <see cref="ProjectIdentity"/>, пора выбросить.
    /// </remarks>
    public long Session { get; init; }

    /// <summary>Состояние.</summary>
    public ProjectsState State { get; init; }

    /// <summary>Открытый файл; пусто — ничего не открыто.</summary>
    public CanonicalPath EntryPoint { get; init; }

    /// <summary>Активная конфигурация; null — та, которую выбирает сам проект.</summary>
    public string? Configuration { get; init; }

    /// <summary>Снимок решения; null — читать нечего.</summary>
    public SolutionSnapshot? Snapshot { get; init; }

    /// <summary>Последняя законченная загрузка этой сессии, удачная или нет; null — ни одной.</summary>
    public ProjectsLoad? LastLoad { get; init; }

    /// <summary>Загрузка стоит в очереди или идёт.</summary>
    public bool IsLoading { get; init; }
}
