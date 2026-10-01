using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>Что показывает доска.</summary>
internal enum BoardState
{
    /// <summary>Службы проектов нет: модуль «Проекты» не поднят.</summary>
    NoService,

    /// <summary>Решение не открыто.</summary>
    Closed,

    /// <summary>Решение открывается.</summary>
    Opening,

    /// <summary>Решение не открылось, и показать нечего.</summary>
    Failed,

    /// <summary>Снимок есть: доска показывает его формы.</summary>
    Ready,
}

/// <summary>
/// Доска форм открытого решения: карточки, их места и файл, в котором места живут.
/// </summary>
/// <remarks>
/// Решение приходит от службы проектов (<see cref="IStudioProjects"/>), события — в поток интерфейса.
/// Новый снимок разбирается вне его: список файлов берётся из снимка сразу, а корни разметки читает
/// <see cref="FormScan"/> в пуле, и пришедший следом снимок отменяет прежнее чтение. Готовое
/// сводится с доской в потоке интерфейса: карточка узнаётся по пути и остаётся той же, новая получает
/// место из файла доски, а нет его там — встаёт под занятой частью (<see cref="BoardLayout.Below"/>).
/// <para>
/// Места помнятся и у форм, которых сейчас на доске нет: проект, не загрузившийся на этот раз, вернёт
/// свои карточки туда, где они стояли. Из файла место уходит только вместе с самим файлом формы.
/// </para>
/// <para>
/// Запись — в пуле и по очереди: правки подряд не обгоняют друг друга, и последняя на диске — последняя
/// сделанная. Неудачная запись говорит в журнал и доску не останавливает.
/// </para>
/// </remarks>
internal sealed class BoardModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "UI designer";

    private static readonly IReadOnlyDictionary<CanonicalPath, ReadRoot> Nothing = new Dictionary<CanonicalPath, ReadRoot>();

    private readonly IStudioContext _context;
    private readonly IStudioProjects? _projects;
    private readonly Func<Pitch> _pitch;
    private readonly Dictionary<CanonicalPath, FormCard> _cards = [];
    private Dictionary<CanonicalPath, Spot> _spots = [];
    private IReadOnlyDictionary<CanonicalPath, ReadRoot> _known = Nothing;
    private CanonicalPath _board = CanonicalPath.None;
    private SolutionSnapshot? _snapshot;
    private CancellationTokenSource? _building;
    private Task _writing = Task.CompletedTask;
    private long _sequence = -1;
    private bool _scanning;
    private bool _disposed;

    /// <summary>Заводит доску и подписывает её на службу проектов.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="pitch">Шаг раскладки: его знает вид — по теме и по измеренной карточке.</param>
    public BoardModel(IStudioContext context, Func<Pitch> pitch)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pitch);

        _context = context;
        _pitch = pitch;
        _projects = context.Projects();

        if (_projects is null)
        {
            State = BoardState.NoService;
            return;
        }

        // Сначала подписка, потом чтение: так велит контракт службы, и перемена между ними не пропадёт.
        _projects.Changed += OnChanged;
        Apply(_projects.Status);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Доска сменила решение: прежние карточки ушли, а с ними и смысл их отмены.
    /// </summary>
    public event EventHandler? Replaced;

    /// <summary>Карточки доски.</summary>
    public ObservableCollection<FormCard> Cards { get; } = [];

    /// <summary>Что показывает доска.</summary>
    public BoardState State { get; private set; } = BoardState.Closed;

    /// <summary>Службы проектов нет.</summary>
    public bool IsNoService => State == BoardState.NoService;

    /// <summary>Решение не открыто.</summary>
    public bool IsClosed => State == BoardState.Closed;

    /// <summary>Решение открывается.</summary>
    public bool IsOpening => State == BoardState.Opening;

    /// <summary>Решение не открылось.</summary>
    public bool IsFailed => State == BoardState.Failed;

    /// <summary>Снимок есть, и на доске есть что показать.</summary>
    public bool IsReady => State == BoardState.Ready;

    /// <summary>
    /// Решение открыто, а форм в нём нет.
    /// </summary>
    /// <remarks>Пока корни читаются, доска не пуста, а ещё не заполнена: иначе «форм нет» мигало бы.</remarks>
    public bool IsEmpty => State == BoardState.Ready && !_scanning && Cards.Count == 0;

    /// <summary>Служба перечитывает решение, а доска показывает прежний снимок.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Что открывается: «Открываю TestApp.sln…».</summary>
    public string? Opening { get; private set; }

    /// <summary>Почему решение не открылось.</summary>
    public string? Failure { get; private set; }

    /// <summary>Сколько форм на доске — строкой полосы.</summary>
    public string Summary => Format("board.count", Cards.Count);

    /// <summary>Последняя постройка доски — тестам, чтобы ждать её, а не время.</summary>
    internal Task Settled { get; private set; } = Task.CompletedTask;

    /// <summary>Последняя запись файла доски — тестам.</summary>
    internal Task Written => _writing;

    /// <summary>
    /// Места карточек сменились — тягой, стрелками, отменой или раскладкой: пора записать.
    /// </summary>
    public void Moved()
    {
        if (_disposed || _board.IsEmpty)
            return;

        foreach (var card in Cards)
            _spots[card.Path] = card.Spot;

        Save();
    }

    /// <summary>
    /// Раскладывает доску заново: сеткой в порядке решения от левого верхнего угла занятой части.
    /// </summary>
    /// <returns>Сделанная раскладка — для истории отмены; нечего раскладывать — null.</returns>
    public BoardMoves? Arrange()
    {
        if (Cards.Count == 0)
            return null;

        var ordered = Cards
            .OrderBy(card => card.Project, StringComparer.Ordinal)
            .ThenBy(card => card.Path)
            .ToList();
        var origin = new Spot(Cards.Min(card => card.Location.X), Cards.Min(card => card.Location.Y));
        var spots = BoardLayout.Grid(ordered.Count, origin, _pitch());
        var moves = ordered
            .Select((card, index) => new BoardMove(card, card.Location, new Point(spots[index].X, spots[index].Y)))
            .Where(move => move.From != move.To)
            .ToList();

        if (moves.Count == 0)
            return null;

        var change = new BoardMoves(moves);

        change.Reapply();

        return change;
    }

    /// <summary>
    /// Открывает формы в редакторе документов студии — по одной, в порядке выбора.
    /// </summary>
    /// <param name="cards">Что открыть.</param>
    public async Task OpenAsync(IEnumerable<FormCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (_context.GetService<IStudioDocuments>() is not { } documents)
            return;

        foreach (var card in cards.ToList())
        {
            try
            {
                await documents.OpenAsync(card.Path.Value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _context.Log.Write(StudioLogLevel.Warning, LogSource, $"{card.Path.Value} не открылся: {e.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_projects is not null)
            _projects.Changed -= OnChanged;

        _building?.Cancel();
        _building?.Dispose();
        _building = null;
    }

    private void OnChanged(object? sender, ProjectsChangedEventArgs e)
    {
        if (_disposed || e.Current.Sequence <= _sequence)
            return;

        Apply(e.Current);
    }

    private void Apply(ProjectsStatus status)
    {
        _sequence = status.Sequence;

        var snapshot = status.Snapshot;
        var failed = status.LastLoad?.Result is { HasErrors: true, HasSnapshot: false } result ? result : null;

        IsLoading = status.IsLoading && snapshot is not null;
        Opening = status.EntryPoint.IsEmpty ? null : Format("board.state.opening", status.EntryPoint.FileName);
        Failure = failed?.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError)?.Message
                  ?? status.LastLoad?.Result.Diagnostics.FirstOrDefault()?.Message;

        State = snapshot is not null ? BoardState.Ready
            : status.State switch
            {
                ProjectsState.Opening => BoardState.Opening,
                ProjectsState.Failed => BoardState.Failed,
                _ => BoardState.Closed,
            };

        if (snapshot is null)
            Close();
        else if (!ReferenceEquals(snapshot, _snapshot))
            Build(snapshot, status.EntryPoint);

        Raise(null);
    }

    /// <summary>Снимка нет: доска пустеет и забывает решение — открытое снова прочтёт свой файл.</summary>
    private void Close()
    {
        _snapshot = null;
        _scanning = false;
        _building?.Cancel();

        if (_board.IsEmpty && Cards.Count == 0)
            return;

        _board = CanonicalPath.None;
        _spots = [];
        _cards.Clear();
        Cards.Clear();
        Replaced?.Invoke(this, EventArgs.Empty);
    }

    private void Build(SolutionSnapshot snapshot, CanonicalPath entry)
    {
        _snapshot = snapshot;
        _scanning = true;
        _building?.Cancel();
        _building?.Dispose();

        var building = new CancellationTokenSource();
        var token = building.Token;
        var files = FormFiles.Of(snapshot);
        var known = _known;
        var changed = entry != _board;
        var board = changed ? BoardFile.PathFor(entry) : null;
        var folder = BoardFile.FolderOf(entry);

        _building = building;

        // Модель, заведённая вне потока интерфейса, сводит доску там, где кончилось чтение: переносить
        // некуда. Так её строят тесты модели без окна.
        var scheduler = SynchronizationContext.Current is null
            ? TaskScheduler.Current
            : TaskScheduler.FromCurrentSynchronizationContext();

        Settled = Task.Run(
                () =>
                {
                    var scan = FormScan.Run(files, known, token);
                    // Места читаются, только когда решение сменилось: у того же решения они уже на доске,
                    // и файл, перечитанный поверх, вернул бы карточки на места до последней тяги.
                    Dictionary<CanonicalPath, Spot>? spots = !changed ? null
                        : board is not null && folder is { } at ? BoardFile.Read(board, at)
                        : [];

                    return (scan, spots);
                },
                token)
            .ContinueWith(
                task =>
                {
                    if (_disposed || token.IsCancellationRequested || task.IsCanceled)
                        return;

                    if (task.Exception is { } failure)
                    {
                        _scanning = false;
                        _context.Log.Write(StudioLogLevel.Error, LogSource, $"Доска не построилась: {failure.GetBaseException()}");
                        Raise(null);
                        return;
                    }

                    var (scan, spots) = task.Result;

                    Fill(entry, scan.Forms, scan.Known, spots);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                scheduler);
    }

    /// <summary>
    /// Сводит прочитанное с доской.
    /// </summary>
    /// <param name="entry">Решение, для которого читали.</param>
    /// <param name="forms">Формы снимка.</param>
    /// <param name="known">Прочитанные корни.</param>
    /// <param name="spots">Места из файла — если решение сменилось; иначе null.</param>
    private void Fill(
        CanonicalPath entry,
        IReadOnlyList<FoundForm> forms,
        IReadOnlyDictionary<CanonicalPath, ReadRoot> known,
        Dictionary<CanonicalPath, Spot>? spots)
    {
        _known = known;
        _scanning = false;

        var replaced = spots is not null;

        if (replaced)
        {
            _board = entry;
            _spots = spots!;
            _cards.Clear();
            Cards.Clear();
        }

        var present = new HashSet<CanonicalPath>();
        var fresh = new List<FormCard>();

        foreach (var form in forms)
        {
            var path = form.File.Path;
            var kind = KindText(form.Root.Kind);

            present.Add(path);

            if (_cards.TryGetValue(path, out var card))
            {
                card.Update(form.File, form.Root, kind);
                continue;
            }

            card = new FormCard(form.File, form.Root, kind);

            if (_spots.TryGetValue(path, out var spot))
                card.Location = new Point(spot.X, spot.Y);
            else
                fresh.Add(card);

            _cards[path] = card;
            Cards.Add(card);
        }

        for (var index = Cards.Count - 1; index >= 0; index--)
        {
            if (present.Contains(Cards[index].Path))
                continue;

            _cards.Remove(Cards[index].Path);
            Cards.RemoveAt(index);
        }

        if (fresh.Count > 0)
        {
            var placed = Cards.Except(fresh).Select(card => card.Spot).ToList();
            var places = BoardLayout.Below(placed, fresh.Count, _pitch());

            for (var index = 0; index < fresh.Count; index++)
            {
                fresh[index].Location = new Point(places[index].X, places[index].Y);
                _spots[fresh[index].Path] = places[index];
            }

            Save();
        }

        if (replaced)
            Replaced?.Invoke(this, EventArgs.Empty);

        Raise(null);
    }

    private void Save()
    {
        if (BoardFile.PathFor(_board) is not { } file || BoardFile.FolderOf(_board) is not { } folder)
            return;

        var spots = new Dictionary<CanonicalPath, Spot>(_spots);
        var present = _cards.Keys.ToHashSet();

        _writing = _writing.ContinueWith(
            _ => Write(file, folder, spots, present),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Пишет места; место формы, которой нет ни на доске, ни на диске, в файл не идёт.
    /// </summary>
    private void Write(string file, CanonicalPath folder, Dictionary<CanonicalPath, Spot> spots, HashSet<CanonicalPath> present)
    {
        foreach (var gone in spots.Keys.Where(path => !present.Contains(path) && !File.Exists(path.Value)).ToList())
            spots.Remove(gone);

        try
        {
            BoardFile.Write(file, folder, spots);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Log.Write(StudioLogLevel.Warning, LogSource, $"Доска не записалась в {file}: {e.Message}");
        }
    }

    private string KindText(FormKind kind) => _context.Strings[kind switch
    {
        FormKind.Window => "board.kind.window",
        FormKind.UserControl => "board.kind.userControl",
        FormKind.Control => "board.kind.control",
        _ => "board.kind.unreadable",
    }];

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], values);

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
