using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>
/// Служба файлов: создание, перенос, копия и удаление файлов решения.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный класс, а не ещё одно лицо службы проектов: у <see cref="IStudioProjects"/> уже есть своё
/// событие <c>Changed</c>, и второе одноимённое у одного объекта спорило бы с ним за имя, а тот, кто
/// берёт службу файлов, модели не просил.
/// </para>
/// <para>
/// <b>Порядок.</b> Правка идёт очередью записи на диск — той же, что у локальной истории (почему —
/// в <see cref="FileWorker"/>); удачная перечитывает модель полосой движка и ждёт этого, а потом
/// сообщает <see cref="Changed"/> в поток интерфейса. Если перечитывание уже стоит в очереди — его
/// поставило слежение, увидев правку на диске, — ждётся оно: второе было бы лишним.
/// </para>
/// </remarks>
internal sealed class FilesService : IStudioFiles
{
    private readonly ProjectsHost _host;
    private readonly FileWords _words;
    private readonly IProjectsThread _thread;

    /// <summary>Заводит службу.</summary>
    /// <param name="host">Служба проектов: сессия, снимок, очереди.</param>
    /// <param name="context">Контекст модуля: словари.</param>
    /// <param name="thread">Поток интерфейса.</param>
    public FilesService(ProjectsHost host, IStudioContext context, IProjectsThread thread)
    {
        _host = host;
        _words = new FileWords(context.Strings);
        _thread = thread;
    }

    /// <inheritdoc/>
    public event EventHandler<FilesChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public Task<ProjectOperationResult> CreateAsync(
        IReadOnlyList<FileCreation> items,
        string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0 || items.Any(item => item is null || item.Path.IsEmpty))
            throw new ArgumentException("Создавать нечего: пачка пуста или в ней пустой путь", nameof(items));

        if (items.Any(item => item.IsDirectory && !item.Content.IsEmpty))
            throw new ArgumentException("У каталога нет содержимого", nameof(items));

        // Пачка снимается сейчас: список зовущего может поменяться, пока правка ждёт очереди.
        List<FileCreation> batch = [.. items];
        var text = Label(label);

        return EditAsync((snapshot, store) => FileWorker.Create(batch, text, snapshot, store, _words), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> MoveAsync(
        IReadOnlyList<FileMove> moves,
        string label,
        CancellationToken cancellationToken = default) =>
        RunAsync(new FileWork(FileWorkKind.Move, [.. Pairs(moves)], [], Label(label)), cancellationToken);

    /// <inheritdoc/>
    public Task<ProjectOperationResult> CopyAsync(
        IReadOnlyList<FileMove> copies,
        string label,
        CancellationToken cancellationToken = default) =>
        RunAsync(new FileWork(FileWorkKind.Copy, [.. Pairs(copies)], [], Label(label)), cancellationToken);

    /// <inheritdoc/>
    public Task<ProjectOperationResult> DeleteAsync(
        IReadOnlyList<CanonicalPath> paths,
        string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0 || paths.Any(path => path.IsEmpty))
            throw new ArgumentException("Удалять нечего: пачка пуста или в ней пустой путь", nameof(paths));

        return RunAsync(new FileWork(FileWorkKind.Delete, [], [.. paths], Label(label)), cancellationToken);
    }

    private Task<ProjectOperationResult> RunAsync(FileWork work, CancellationToken cancellationToken) =>
        EditAsync((snapshot, store) => FileWorker.Run(work, snapshot, store, _words), cancellationToken);

    /// <summary>
    /// Правка диска очередью записи: служба файлов и отмена из истории ходят одной дорогой.
    /// </summary>
    /// <param name="work">Правка — в очереди записи, над снимком открытого и историей (null — не ведётся).</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <remarks>
    /// Удачная правка, которая могла поменять модель, перечитывает её раньше, чем вернуться, а о том,
    /// что переехало и что удалено, сообщает <see cref="Changed"/> — кто бы правку ни сделал.
    /// </remarks>
    internal async Task<ProjectOperationResult> EditAsync(
        Func<SolutionSnapshot, LocalHistoryStore?, FileWorkResult> work,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_host.IsStopped, this);

        if (_host.Session is not { Snapshot: not null } session)
        {
            return Refusals.Of(ProjectsDiagnosticCodes.NothingOpen, _words.NothingOpen);
        }

        var outcome = new TaskCompletionSource<FileWorkResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queued = _host.History.Enqueue(() =>
        {
            // Отмена слышна, пока правка не началась: начатую обрывать нельзя.
            if (cancellationToken.IsCancellationRequested)
            {
                outcome.TrySetCanceled(cancellationToken);
                return Task.CompletedTask;
            }

            if (session.IsRetired || session.Snapshot is not { } snapshot)
            {
                outcome.TrySetResult(Refusals.Work(ProjectsDiagnosticCodes.NothingOpen, _words.NothingOpen));

                return Task.CompletedTask;
            }

            try
            {
                outcome.TrySetResult(work(snapshot, _host.History.Store));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                outcome.TrySetException(e);
            }

            return Task.CompletedTask;
        });

        ObjectDisposedException.ThrowIf(!queued, this);

        var done = await outcome.Task.ConfigureAwait(false);

        if (done.Rereads)
            await _host.RereadAsync(session, ProjectsLoadReason.Files).ConfigureAwait(false);

        if (done.Change is { } change)
            _thread.Post(() => Changed?.Invoke(this, change));

        return done.Result;
    }

    private static IEnumerable<FileMove> Pairs(IReadOnlyList<FileMove> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0 || pairs.Any(pair => pair is null || pair.From.IsEmpty || pair.To.IsEmpty))
            throw new ArgumentException("Переносить нечего: пачка пуста или в ней пустой путь", nameof(pairs));

        return pairs;
    }

    private static string Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return label;
    }
}
