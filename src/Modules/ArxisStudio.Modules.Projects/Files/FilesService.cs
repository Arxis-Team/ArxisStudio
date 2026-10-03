using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Watching;
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
/// сообщает <see cref="Changed"/> в поток интерфейса. Если перечитывание уже стоит в очереди, ждётся
/// оно: второе было бы лишним.
/// </para>
/// <para>
/// <b>Запись и слежение за содержимым.</b> Запись (<see cref="WriteAsync"/>) запоминает отпечаток
/// записанного, и слежение (<see cref="ContentWatch"/>), увидев на конце пачки ровно эти байты, метит
/// перемену студийной. Слежение живёт, пока есть подписчик на <see cref="ContentChanged"/>: первый
/// заводит его у сессии, последний гасит.
/// </para>
/// <para>
/// <b>Одно перечитывание на правку.</b> Слежение видит правку раньше, чем её перечитали, и пачка,
/// разобранная посреди перечитывания, сверилась бы с прежним снимком и попросила бы вторую
/// загрузку — так и было: каждое переименование перечитывало модель дважды. Теперь с первой записи
/// на диск до конца перечитывания приговоры слежения придержаны, а входы оценки запомнены такими,
/// какими правка их оставила; придержанное сверяется со свежим снимком. Перемена снаружи, которую
/// перечитывание не увидело, загрузку по-прежнему просит.
/// </para>
/// </remarks>
internal sealed class FilesService : IStudioFiles
{
    private readonly ProjectsHost _host;
    private readonly FileWords _words;
    private readonly IProjectsThread _thread;
    private readonly ConcurrentDictionary<CanonicalPath, string> _written = new();
    private readonly Lock _contentGate = new();
    private EventHandler<FileContentChangedEventArgs>? _contentChanged;
    private int _contentSubscribers;

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
    public event EventHandler<FileContentChangedEventArgs>? ContentChanged
    {
        add
        {
            if (value is null)
                return;

            bool first;

            lock (_contentGate)
            {
                _contentChanged += value;
                first = ++_contentSubscribers == 1;
            }

            if (first)
                _host.WatchContent();
        }

        remove
        {
            if (value is null)
                return;

            bool last;

            lock (_contentGate)
            {
                var before = _contentChanged;

                _contentChanged -= value;

                if (ReferenceEquals(before, _contentChanged))
                    return;

                last = --_contentSubscribers == 0;
            }

            if (last)
                _host.WatchContent();
        }
    }

    /// <summary>Есть кому слушать перемены содержимого — слежению за ним жить.</summary>
    internal bool IsContentWatched
    {
        get
        {
            lock (_contentGate)
                return _contentSubscribers > 0;
        }
    }

    /// <summary>
    /// Пачка слежения за содержимым: откуда каждая перемена — и в поток интерфейса.
    /// </summary>
    /// <param name="batch">Пачка коалесцера; зовётся в его потоке.</param>
    /// <remarks>
    /// Отпечаток снимается здесь, а не в потоке интерфейса: чтение файла ему не по чину. Записанное
    /// студией узнаётся по байтам на конце пачки; разошлись — файл с тех пор переписали снаружи, и
    /// ожидание больше не нужно.
    /// </remarks>
    internal void OnContent(ImmutableArray<FileChange> batch)
    {
        var changes = ImmutableArray.CreateBuilder<FileContentChange>(batch.Length);

        foreach (var change in batch)
        {
            var origin = FileChangeOrigin.External;

            // Временный файл, подменивший цель, коалесцер обычно склеивает в одно «изменено». Разорви
            // пачка запись посередине — цель придёт переименованием из временного, и её байты те же.
            if (change.Kind is FileChangeKind.Renamed)
                _written.TryRemove(change.OldPath, out _);

            if (change.Kind is FileChangeKind.Changed or FileChangeKind.Created or FileChangeKind.Renamed
                && _written.TryGetValue(change.Path, out var print))
            {
                if (DiskView.Instance.TryFingerprint(change.Path, out var now) && string.Equals(now, print, StringComparison.Ordinal))
                    origin = FileChangeOrigin.Studio;
                else
                    _written.TryRemove(KeyValuePair.Create(change.Path, print));
            }
            else if (change.Kind is FileChangeKind.Deleted)
            {
                _written.TryRemove(change.Path, out _);
            }

            changes.Add(new FileContentChange(change, origin));
        }

        var args = new FileContentChangedEventArgs(changes.MoveToImmutable());

        _thread.Post(() =>
        {
            EventHandler<FileContentChangedEventArgs>? handlers;

            lock (_contentGate)
                handlers = _contentChanged;

            handlers?.Invoke(this, args);
        });
    }

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

    /// <inheritdoc/>
    public Task<ProjectOperationResult> WriteAsync(
        IReadOnlyList<FileWrite> writes,
        string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0 || writes.Any(write => write is null || write.Path.IsEmpty))
            throw new ArgumentException("Записывать нечего: пачка пуста или в ней пустой путь", nameof(writes));

        if (writes.Select(write => write.Path).Distinct().Count() != writes.Count)
            throw new ArgumentException("Один файл назван в пачке дважды", nameof(writes));

        // Пачка снимается сейчас: список и байты зовущего могут поменяться, пока запись ждёт очереди.
        List<FileWrite> batch =
        [
            .. writes.Select(write => write with
            {
                Content = write.Content.ToArray(),
                // Явно: null массива при неявном переводе стал бы пустыми, но заданными байтами.
                Expected = write.Expected is { } expected ? expected.ToArray() : (ReadOnlyMemory<byte>?)null,
            }),
        ];
        var text = Label(label);

        return EditAsync((snapshot, store) =>
        {
            // Отпечатки — раньше первого байта: слежение может увидеть запись раньше, чем она вернётся.
            foreach (var write in batch)
                _written[write.Path] = Convert.ToHexString(SHA256.HashData(write.Content.Span));

            var done = FileWorker.Write(batch, text, snapshot, store, _words);

            if (done.Result.HasErrors)
            {
                foreach (var write in batch)
                    _written.TryRemove(write.Path, out _);
            }

            return done;
        }, cancellationToken);
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
        IWatchHold? hold = null;

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

            // Придерживается здесь, а не до очереди: пока правка ждёт своей очереди, перемены
            // снаружи судятся без задержки.
            hold = session.Hold();

            try
            {
                var done = work(snapshot, _host.History.Store);

                // Сейчас, до перечитывания: оно начнётся позже и прочтёт входы такими или новее. Правке,
                // которая модель не перечитывает, запоминать нечего — подтверждать будет некому.
                if (done.Rereads)
                    hold.Expect(Inputs(snapshot, done.Change));

                outcome.TrySetResult(done);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                outcome.TrySetException(e);
            }

            return Task.CompletedTask;
        });

        ObjectDisposedException.ThrowIf(!queued, this);

        try
        {
            var done = await outcome.Task.ConfigureAwait(false);

            if (done.Rereads && await _host.RereadAsync(session, ProjectsLoadReason.Files).ConfigureAwait(false))
                hold?.Confirm();

            if (done.Change is { } change)
                _thread.Post(() => Changed?.Invoke(this, change));

            return done.Result;
        }
        finally
        {
            hold?.Dispose();
        }
    }

    /// <summary>
    /// Входы оценки, которые правка могла переписать: файлы проектов — в них переписываются
    /// ссылки, — решение и входы под путями, которые правка тронула.
    /// </summary>
    /// <param name="snapshot">Снимок, над которым шла правка.</param>
    /// <param name="change">Что правка сделала; null — ничего.</param>
    /// <remarks>
    /// Не все входы: их у проекта сотни, от SDK до пакетов, а правка трогает свои файлы. Лишний
    /// запомненный вход безвреден — модель после правки его прочла, — пропущенный стоит одной
    /// загрузки.
    /// </remarks>
    private static IEnumerable<CanonicalPath> Inputs(SolutionSnapshot snapshot, FilesChangedEventArgs? change)
    {
        ImmutableArray<CanonicalPath> touched = change is null
            ? []
            :
            [
                .. change.Moved.SelectMany(move => new[] { move.From, move.To }),
                .. change.Copied.Select(copy => copy.To),
                .. change.Deleted,
                .. change.Created,
            ];

        return snapshot.Projects
            .Select(project => project.ProjectFilePath)
            .Append(snapshot.EntryPoint.Path)
            .Concat(snapshot.Projects
                .SelectMany(project => project.EvaluationInputs)
                .Where(input => touched.Any(input.StartsWith)))
            .Where(path => !path.IsEmpty)
            .Distinct();
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
