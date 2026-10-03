using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Профиль одного вида: его держатели, свойства, публикация и оценка открытого.
/// </summary>
/// <remarks>
/// Один на вид и на службу: держатели делят его и движок под ним. Поля, кроме записи, меняет служба
/// под своим замком; запись читается откуда угодно одним атомарным чтением, как и у службы.
/// </remarks>
internal sealed class ProjectProfile
{
    private ProjectsStatus _status = ProjectsStatus.Closed;

    /// <summary>Заводит профиль.</summary>
    /// <param name="kind">Зачем он.</param>
    /// <param name="globalProperties">С какими свойствами он читает и собирает.</param>
    /// <param name="publisher">Доставка его перемен.</param>
    public ProjectProfile(ProjectProfileKind kind, ProjectMetadata globalProperties, Func<ProjectProfile, ChangePublisher> publisher)
    {
        Kind = kind;
        GlobalProperties = globalProperties;
        Publisher = publisher(this);
    }

    /// <summary>Зачем он.</summary>
    public ProjectProfileKind Kind { get; }

    /// <summary>С какими свойствами он читает и собирает.</summary>
    public ProjectMetadata GlobalProperties { get; }

    /// <summary>Доставка его перемен подписчикам.</summary>
    public ChangePublisher Publisher { get; }

    /// <summary>Сколько держателей. Под замком службы.</summary>
    public int Holders { get; set; }

    /// <summary>Что прочесть сверх обычного — всё, что просил хоть один держатель. Под замком службы.</summary>
    public ImmutableArray<string> Properties { get; private set; } = [];

    /// <summary>Оценка текущей сессии службы; null — ничего не открыто или держателей нет. Под замком службы.</summary>
    public ProfileSession? Session { get; set; }

    /// <summary>Номер последней публикации. Под замком службы.</summary>
    public long Sequence { get; set; }

    /// <summary>Опубликованная запись.</summary>
    public ProjectsStatus Status
    {
        get => Volatile.Read(ref _status);
        set => Volatile.Write(ref _status, value);
    }

    /// <summary>Добавляет свойства, которых профиль ещё не читал. Под замком службы.</summary>
    /// <param name="properties">Что просит новый держатель.</param>
    /// <returns>Прибавилось хоть одно: оценку пора перечитать.</returns>
    public bool Widen(ImmutableArray<string> properties)
    {
        var added = properties
            .Where(property => !string.IsNullOrWhiteSpace(property))
            .Where(property => !Properties.Contains(property, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (added.Count == 0)
            return false;

        Properties = [.. Properties, .. added];

        return true;
    }

    /// <summary>Запись профиля из его оценки; без оценки — «закрыто». Под замком службы.</summary>
    public ProjectsStatus Derive() => Session is not { } session
        ? ProjectsStatus.Closed
        : new ProjectsStatus
        {
            Session = session.Owner.Number,
            State = session.Snapshot is not null ? ProjectsState.Ready
                : session.LastLoad is not null ? ProjectsState.Failed
                : ProjectsState.Opening,
            EntryPoint = session.Owner.EntryPoint,
            Configuration = session.Owner.Configuration,
            Snapshot = session.Snapshot,
            LastLoad = session.LastLoad,
            IsLoading = session.Pending is not null || session.Running is not null,
        };
}

/// <summary>
/// Оценка профиля для одной сессии службы: свой движок, свой снимок, своя очередь загрузок.
/// </summary>
/// <remarks>
/// Кончается вместе с сессией службы, которой следует, — или раньше, когда профиль отпустил последний
/// держатель. Движок отпускается очередью службы, после того, что он дочитывает.
/// </remarks>
internal sealed class ProfileSession
{
    private readonly CancellationTokenSource _lifetime = new();
    private SolutionSnapshot? _snapshot;
    private int _retired;

    /// <summary>Заводит оценку.</summary>
    /// <param name="owner">Сессия службы, за которой профиль следует.</param>
    /// <param name="workspace">Свой движок.</param>
    public ProfileSession(ProjectsSession owner, ProjectWorkspace workspace)
    {
        Owner = owner;
        Workspace = workspace;
    }

    /// <summary>Сессия службы, за которой профиль следует.</summary>
    public ProjectsSession Owner { get; }

    /// <summary>Свой движок.</summary>
    public ProjectWorkspace Workspace { get; }

    /// <summary>Отменяется, когда оценка кончилась.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>Оценка кончилась.</summary>
    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    /// <summary>Загрузка, стоящая в очереди и ещё не начатая. Под замком службы.</summary>
    public LoadItem? Pending { get; set; }

    /// <summary>Загрузка, которая идёт. Под замком службы.</summary>
    public LoadItem? Running { get; set; }

    /// <summary>Последняя законченная загрузка. Под замком службы.</summary>
    public ProjectsLoad? LastLoad { get; set; }

    /// <summary>Последний снимок оценки.</summary>
    public SolutionSnapshot? Snapshot
    {
        get => Volatile.Read(ref _snapshot);
        set => Volatile.Write(ref _snapshot, value);
    }

    /// <summary>
    /// Кончает оценку: её загрузки и операции отменяются.
    /// </summary>
    /// <returns><c>false</c> — оценка уже кончилась раньше.</returns>
    /// <remarks>Зовётся вне замка службы: отмена синхронно зовёт обработчики движка.</remarks>
    public bool Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) != 0)
            return false;

        _lifetime.Cancel();

        return true;
    }
}

/// <summary>Операция профиля: над чьей оценкой и с каким запросом.</summary>
/// <param name="Profile">Чей профиль.</param>
/// <param name="Session">Оценка, над которой операция встала.</param>
/// <param name="Request">Запрос со свойствами профиля поверх свойств просившего.</param>
internal sealed record ProfileRun(ProjectProfile Profile, ProfileSession Session, ProjectOperationRequest Request);
