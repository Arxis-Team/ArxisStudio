using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.Delivery;
using ArxisStudio.Modules.Projects.Files;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>
/// Служба локальной истории: что было с файлами решения, возврат файла и отмена действия.
/// </summary>
/// <remarks>
/// <para>
/// <b>Чтение</b> — ревизии и содержимое — идёт мимо очереди записи: хранилище отвечает из памяти и
/// с диска под своими замками, и окно истории не должно ждать, пока опорный снимок дообойдёт большое
/// решение.
/// </para>
/// <para>
/// <b>Правка</b> — отмена, возврат, метка — идёт той же дорогой, что у службы файлов
/// (<see cref="FilesService.EditAsync"/>): очередью записи, с перечитыванием модели и с
/// <see cref="IStudioFiles.Changed"/> о переехавшем и удалённом. Отмена, сделанная мимо этой дороги,
/// разошлась бы со снимком так же, как правка файлов мимо службы.
/// </para>
/// <para>
/// Отдельный класс, а не лицо службы проектов, — по той же причине, что у службы файлов: своё
/// событие <see cref="Changed"/> спорило бы за имя с событием модели.
/// </para>
/// </remarks>
internal sealed class HistoryService : IStudioHistory
{
    private readonly ProjectsHost _host;
    private readonly IStudioContext _context;
    private readonly IProjectsThread _thread;
    private readonly HistoryWords _words;
    private int _posted;

    /// <summary>Заводит службу.</summary>
    /// <param name="host">Служба проектов: сессия, снимок, история, очереди.</param>
    /// <param name="context">Контекст модуля: словари и настройки.</param>
    /// <param name="thread">Поток интерфейса.</param>
    public HistoryService(ProjectsHost host, IStudioContext context, IProjectsThread thread)
    {
        _host = host;
        _context = context;
        _thread = thread;
        _words = new HistoryWords(context.Strings);
        host.History.Recorded += OnRecorded;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public bool IsOn => _host.History.Store is not null;

    /// <inheritdoc/>
    public long MaxFileBytes => ProjectsSettings.Read(_context.Settings).HistoryMaxFileBytes;

    /// <inheritdoc/>
    public LocalHistoryAction? LastStudioAction
    {
        get
        {
            if (_host.History.Store is not { } store || _host.Current is not { } snapshot)
                return null;

            var since = _host.History.Since;
            var undone = store.Undone();
            var actions = store.Actions;

            for (var at = actions.Length - 1; at >= 0 && actions[at].Id >= since; at--)
            {
                var action = actions[at];

                // Отмену Ctrl+Z не отменяет: он идёт назад по сделанному, а не качается туда-обратно.
                if (action.Origin != HistoryOrigin.Studio || action.Undoes is not null || undone.Contains(action.Id))
                    continue;

                if (HistoryUndo.Belongs(action, snapshot))
                    return Contract(action, undone: false);
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LocalHistoryRevision>> RevisionsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
    {
        if (path.IsEmpty)
            throw new ArgumentException("История пустого пути не ведётся", nameof(path));

        return Task.Run(() => Revisions(path), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<byte[]?> ReadAsync(LocalHistoryContent content, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty || !ContentId.TryParse(content.Id, out var id))
            return Task.FromResult<byte[]?>(null);

        return Task.Run(() => _host.History.Store?.Read(id), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> UndoAsync(long actionId, CancellationToken cancellationToken = default) =>
        _host.Files.EditAsync(
            (snapshot, store) => store is null ? Off() : HistoryUndo.Undo(actionId, snapshot, store, _words),
            cancellationToken);

    /// <inheritdoc/>
    public Task<ProjectOperationResult> RevertAsync(
        CanonicalPath path,
        LocalHistoryContent content,
        string label,
        CancellationToken cancellationToken = default)
    {
        if (path.IsEmpty)
            throw new ArgumentException("Возвращать нечего: путь пуст", nameof(path));

        if (!ContentId.TryParse(content.Id, out var id))
            throw new ArgumentException("Ручка содержимого пуста или не из истории", nameof(content));

        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return _host.Files.EditAsync(
            (snapshot, store) => store is null ? Off() : HistoryUndo.Revert(path, id, label, snapshot, store, _words),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult> PutLabelAsync(string label, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return _host.Files.EditAsync((snapshot, store) =>
        {
            if (store is null)
                return Off();

            // Метка — на папке решения: её видно в истории всего, что в ней лежит.
            var scope = HistoryFilter.Folder(snapshot.EntryPoint.Path) ?? snapshot.EntryPoint.Path;

            store.PutLabel(label.Trim(), scope.Value);
            store.Flush();

            return new FileWorkResult(ProjectOperationResult.Succeeded(), null);
        }, cancellationToken);
    }

    /// <summary>Действие истории словами контракта.</summary>
    /// <param name="action">Действие.</param>
    /// <param name="undone">Отменено ли оно.</param>
    internal static LocalHistoryAction Contract(HistoryAction action, bool undone) => new()
    {
        Id = action.Id,
        Time = action.Time,
        Label = action.Label,
        Origin = action.Origin == HistoryOrigin.Studio ? LocalHistoryOrigin.Studio : LocalHistoryOrigin.External,
        Changes = [.. action.Changes.Select(Contract).OfType<LocalHistoryChange>()],
        Undoes = action.Undoes,
        IsUndone = undone,
    };

    /// <summary>Правка словами контракта; null — путь в журнале не разобрался.</summary>
    private static LocalHistoryChange? Contract(HistoryChange change)
    {
        if (!CanonicalPath.TryCreate(change.Path, out var path))
            return null;

        return new LocalHistoryChange
        {
            Kind = change.Kind switch
            {
                HistoryChangeKind.Created => LocalHistoryChangeKind.Created,
                HistoryChangeKind.Modified => LocalHistoryChangeKind.Modified,
                HistoryChangeKind.Deleted => LocalHistoryChangeKind.Deleted,
                _ => LocalHistoryChangeKind.Moved,
            },
            Path = path,
            From = CanonicalPath.TryCreate(change.From, out var from) ? from : CanonicalPath.None,
            Before = Handle(change.Before),
            After = Handle(change.After),
            IsDirectory = change.IsDirectory,
            TooLarge = change.TooLarge,
        };
    }

    private static LocalHistoryContent Handle(ContentId? id) =>
        id is { } content ? new LocalHistoryContent(content.Value) : LocalHistoryContent.None;

    private IReadOnlyList<LocalHistoryRevision> Revisions(CanonicalPath path)
    {
        if (_host.History.Store is not { } store)
            return [];

        var undone = store.Undone();
        var folder = Directory.Exists(path.Value) || (!File.Exists(path.Value) && WasFolder(store, path.Value));
        var changes = folder ? store.RevisionsUnder(path.Value) : store.Revisions(path.Value);

        // Правки идут от новой к старой; действие собирает свои подряд, и порядок действий тот же.
        var rows = changes
            .GroupBy(revision => revision.Action.Id)
            .Select(group => new LocalHistoryRevision
            {
                Action = Contract(group.First().Action, undone.Contains(group.Key)),
                Changes = [.. group.Select(revision => Contract(revision.Change)).OfType<LocalHistoryChange>()],
            })
            .Concat(store.Labels(path.Value).Select(label => new LocalHistoryRevision { Action = Contract(label, undone: false) }));

        return [.. rows.OrderByDescending(row => row.Action.Id)];
    }

    /// <summary>Был ли путь папкой в истории — у удалённой папки на диске не спросить.</summary>
    private static bool WasFolder(LocalHistoryStore store, string path) =>
        store.Actions.Any(action => action.Changes.Any(change => change.IsDirectory
            && (string.Equals(change.Path, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(change.From, path, StringComparison.OrdinalIgnoreCase))));

    private FileWorkResult Off() => Refusals.Work(ProjectsDiagnosticCodes.HistoryUnavailable, _words.Off);

    /// <summary>
    /// Записано действие: окну — одно событие на пачку, в потоке интерфейса.
    /// </summary>
    /// <remarks>
    /// Переключение ветки пишет десятки пачек подряд, и окно истории, перечитывающее ревизии на
    /// каждую, читало бы их без толку: следующее событие ставится, только когда прежнее дошло.
    /// </remarks>
    private void OnRecorded()
    {
        if (Interlocked.Exchange(ref _posted, 1) == 1)
            return;

        _thread.Post(() =>
        {
            Volatile.Write(ref _posted, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
