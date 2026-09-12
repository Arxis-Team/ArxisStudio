using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Delivery;

/// <summary>
/// Один шаг модели: каких проектов он коснулся и тронул ли само решение.
/// </summary>
/// <remarks>
/// Считается там, где снимок сменился, — в очереди работы, а не в потоке интерфейса: сравнение
/// содержимого большого решения стоит заметного времени, а доставка обязана быть дешёвой.
/// </remarks>
/// <param name="Touched">Проекты, добавленные, снятые или изменившиеся на этом шаге.</param>
/// <param name="SolutionChanged">Поменялось ли описание самого решения.</param>
internal readonly record struct SnapshotStep(ImmutableArray<ProjectIdentity> Touched, bool SolutionChanged)
{
    /// <summary>Шаг без перемен в модели: сменилось только состояние службы.</summary>
    public static SnapshotStep None { get; } = new([], false);

    /// <summary>Шаг ничего не тронул.</summary>
    public bool IsEmpty => Touched.IsDefaultOrEmpty && !SolutionChanged;
}

/// <summary>
/// Доставка перемен службы подписчикам: в поток интерфейса, по порядку, со склейкой.
/// </summary>
/// <remarks>
/// <para>
/// <b>Склейка.</b> Публикация не зовёт подписчиков, а кладёт запись и один раз просит поток
/// интерфейса о доставке. Пока он не дошёл, новые публикации заменяют запись и копят задетые
/// проекты, поэтому доставка видит последнее состояние и разность от прошлого доставленного — и
/// поток интерфейса не разгребает очередь состояний, устаревших ещё до того, как их показали.
/// </para>
/// <para>
/// <b>Повторный вход.</b> Обработчик, прокачавший поток изнутри доставки — модальным окном,
/// например, — вложенного события не получит: перемена дождётся конца текущей доставки. Иначе
/// второй обработчик увидел бы новое событие раньше старого.
/// </para>
/// <para>
/// <b>Сбой подписчика.</b> Упавший обработчик соседям не мешает. Исключение уходит дальше — в
/// продукте брошенным заново в потоке интерфейса, где студия припишет его по стеку тому, чей код
/// бросил, а не этому модулю, в чьём цикле оно случилось.
/// </para>
/// <para>
/// <b>Подписчиков держит <see cref="Subscribers{TArgs}"/></b> — там же и страховка от забытой
/// подписки.
/// </para>
/// </remarks>
internal sealed class ChangePublisher
{
    private readonly object _sender;
    private readonly IProjectsThread _thread;
    private readonly Action<Exception> _failed;

    private readonly Subscribers<ProjectsChangedEventArgs> _subscribers = new();
    private readonly Lock _gate = new();
    private ProjectsStatus _pending = ProjectsStatus.Closed;
    private ProjectsStatus _delivered = ProjectsStatus.Closed;
    private HashSet<ProjectIdentity> _touched = [];
    private bool _solutionTouched;
    private bool _scheduled;
    private bool _delivering;
    private bool _again;

    /// <summary>Собирает доставку.</summary>
    /// <param name="sender">Кто отправитель событий — сама служба.</param>
    /// <param name="thread">Поток интерфейса.</param>
    /// <param name="failed">Куда девать исключение подписчика; null — бросить заново в потоке.</param>
    public ChangePublisher(
        object sender,
        IProjectsThread thread,
        Action<Exception>? failed)
    {
        _sender = sender;
        _thread = thread;
        _failed = failed ?? Rethrow;
    }

    /// <summary>Добавляет подписчика.</summary>
    /// <param name="handler">Обработчик; null ничего не делает, как у обычного события.</param>
    public void Subscribe(EventHandler<ProjectsChangedEventArgs>? handler) => _subscribers.Add(handler);

    /// <summary>Снимает подписчика — последнюю из равных ему подписок, как у обычного события.</summary>
    /// <param name="handler">Обработчик.</param>
    public void Unsubscribe(EventHandler<ProjectsChangedEventArgs>? handler) => _subscribers.Remove(handler);

    /// <summary>
    /// Кладёт новое состояние и просит поток интерфейса о доставке.
    /// </summary>
    /// <param name="status">Новое состояние; номер у него больше прежнего.</param>
    /// <param name="step">Что шаг сделал с моделью.</param>
    /// <remarks>
    /// Зовётся под замком службы, из любого потока, и потому ничего чужого не зовёт: подписчики
    /// узнают о перемене позже, в своём потоке.
    /// </remarks>
    public void Publish(ProjectsStatus status, SnapshotStep step)
    {
        lock (_gate)
        {
            _pending = status;

            if (!step.Touched.IsDefaultOrEmpty)
                _touched.UnionWith(step.Touched);

            _solutionTouched |= step.SolutionChanged;

            if (_scheduled)
                return;

            _scheduled = true;
        }

        _thread.Post(Deliver);
    }

    /// <summary>Разность двух состояний с проектами, задетыми между ними.</summary>
    /// <param name="previous">Прежнее доставленное состояние.</param>
    /// <param name="current">Новое.</param>
    /// <param name="touched">Проекты, задетые шагами между ними.</param>
    /// <param name="solutionTouched">Трогал ли какой-то шаг само решение.</param>
    internal static ProjectsChangedEventArgs Compose(
        ProjectsStatus previous,
        ProjectsStatus current,
        IReadOnlySet<ProjectIdentity> touched,
        bool solutionTouched)
    {
        var before = previous.Snapshot;
        var after = current.Snapshot;

        if (ReferenceEquals(before, after))
            return new ProjectsChangedEventArgs(previous, current, [], [], [], solutionChanged: false);

        var was = before is null ? [] : before.Projects.Select(project => project.Identity).ToHashSet();
        var now = after is null ? [] : after.Projects.Select(project => project.Identity).ToHashSet();

        ImmutableArray<ProjectSnapshot> added = after is null
            ? []
            : [.. after.Projects.Where(project => !was.Contains(project.Identity))];

        ImmutableArray<ProjectSnapshot> removed = before is null
            ? []
            : [.. before.Projects.Where(project => !now.Contains(project.Identity))];

        ImmutableArray<ProjectSnapshot> modified = after is null
            ? []
            : [.. after.Projects.Where(project => was.Contains(project.Identity) && touched.Contains(project.Identity))];

        // Другой снимок другой сессии — это другое решение целиком, даже если шаги ничего не
        // сказали: шагов между сессиями нет, есть только закрытие и открытие.
        var solutionChanged = solutionTouched
            || before is null
            || after is null
            || before.Workspace != after.Workspace;

        return new ProjectsChangedEventArgs(previous, current, added, removed, modified, solutionChanged);
    }

    private void Deliver()
    {
        ProjectsChangedEventArgs change;

        lock (_gate)
        {
            _scheduled = false;

            if (_delivering)
            {
                _again = true;
                return;
            }

            if (_pending.Sequence == _delivered.Sequence)
                return;

            var touched = _touched;
            _touched = [];

            change = Compose(_delivered, _pending, touched, _solutionTouched);

            _solutionTouched = false;
            _delivered = _pending;
            _delivering = true;
        }

        try
        {
            _subscribers.Invoke(_sender, change, _failed);
        }
        finally
        {
            bool again;

            lock (_gate)
            {
                _delivering = false;
                again = _again && !_scheduled;
                _again = false;

                if (again)
                    _scheduled = true;
            }

            if (again)
                _thread.Post(Deliver);
        }
    }

    /// <summary>
    /// Бросает исключение подписчика заново — в потоке интерфейса и с его собственным стеком.
    /// </summary>
    /// <remarks>
    /// Стек сохраняется захватом, поэтому студия находит в нём кадр того, кто бросил, и приписывает
    /// сбой ему. Отложено, а не брошено на месте: остальные подписчики своё событие уже получили.
    /// </remarks>
    private void Rethrow(Exception error)
    {
        var captured = ExceptionDispatchInfo.Capture(error);

        _thread.Post(captured.Throw);
    }
}
