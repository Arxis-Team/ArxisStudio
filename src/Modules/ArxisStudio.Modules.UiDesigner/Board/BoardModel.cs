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
/// Форму можно убрать с доски — не удалить файл, а снять карточку (<see cref="Remove"/>). Убранная уходит
/// из коллекции холста, а путь её помнится в файле доски: следующий снимок решения её не вернёт, вернёт
/// человек — отменой или из меню холста (<see cref="Return"/>), — и встанет она на прежнее место.
/// </para>
/// <para>
/// Запись — в пуле и по одной: ждущая запись подменяется новой, и последняя на диске — последняя
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
    private readonly Dictionary<CanonicalPath, FormCard> _parked = [];
    private readonly Lock _gate = new();
    private Dictionary<CanonicalPath, Spot> _spots = [];
    private HashSet<CanonicalPath> _removed = [];
    private Dictionary<CanonicalPath, FoundForm> _found = [];
    private IReadOnlyDictionary<CanonicalPath, ReadRoot> _known = Nothing;
    private CanonicalPath _board = CanonicalPath.None;
    private SolutionSnapshot? _snapshot;
    private CancellationTokenSource? _building;
    private Task _writing = Task.CompletedTask;
    private PendingWrite? _pending;
    private bool _draining;
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

    /// <summary>
    /// Карточки убраны с доски или вернулись на неё — откуда бы ни пришла перемена: из меню, клавишей,
    /// отменой, повтором или возвратом.
    /// </summary>
    /// <remarks>
    /// Сказать человеку о перемене должен тот, кто её видит целиком, — иначе после отмены уборки строка
    /// состояния так и говорила бы «убрана», и после «Вернуть все» тоже.
    /// </remarks>
    public event EventHandler<PresenceEventArgs>? PresenceChanged;

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
    public bool IsEmpty => State == BoardState.Ready && !_scanning && Cards.Count == 0 && !HasRemoved;

    /// <summary>
    /// Формы в решении есть, но все убраны с доски: показать нечего, а вернуть — есть что.
    /// </summary>
    public bool IsAllRemoved => State == BoardState.Ready && !_scanning && Cards.Count == 0 && HasRemoved;

    /// <summary>
    /// Убранные с доски формы, которые в решении есть, — в порядке решения: их можно вернуть.
    /// </summary>
    /// <remarks>
    /// Убранная форма, которой в снимке нет, — проект не загрузился, файл исключили, — вернуть нечего, и
    /// в список она не идёт; помнится она всё равно, пока файл на диске.
    /// </remarks>
    public IReadOnlyList<FoundForm> Removed => _removed
        .Where(_found.ContainsKey)
        .Select(path => _found[path])
        .OrderBy(form => form.File.Project, StringComparer.Ordinal)
        .ThenBy(form => form.File.Path)
        .ToList();

    private bool HasRemoved => _removed.Any(_found.ContainsKey);

    /// <summary>
    /// Где форма лежит — папкой от папки решения через прямую черту; у формы в самой папке решения — её
    /// проектом.
    /// </summary>
    /// <param name="form">Форма.</param>
    public string Where(FoundForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var folder = BoardFile.FolderOf(_board) is { } root
            ? Path.GetDirectoryName(BoardFile.Key(root, form.File.Path))?.Replace(Path.DirectorySeparatorChar, '/')
            : null;

        return string.IsNullOrEmpty(folder) ? form.File.Project : folder;
    }

    /// <summary>На доске есть карточки: органам холста есть что вписывать, масштабировать и раскладывать.</summary>
    public bool HasCards => State == BoardState.Ready && Cards.Count > 0;

    /// <summary>Служба перечитывает решение, а доска показывает прежний снимок.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Что открывается: «Открываю TestApp.sln…».</summary>
    public string? Opening { get; private set; }

    /// <summary>Почему решение не открылось.</summary>
    public string? Failure { get; private set; }

    /// <summary>Последняя постройка доски — тестам, чтобы ждать её, а не время.</summary>
    internal Task Settled { get; private set; } = Task.CompletedTask;

    /// <summary>Последняя запись файла доски — тестам.</summary>
    internal Task Written => _writing;

    /// <summary>
    /// Места карточек, возможно, сменились — тягой, стрелками, отменой или раскладкой: пора записать.
    /// </summary>
    /// <remarks>
    /// Зовёт её история на каждую свою перемену, и не каждая из них двигает карточки: очистка истории
    /// при смене решения, отмена уборки. Файл пишется, только если место сменилось хоть у одной.
    /// </remarks>
    public void Moved()
    {
        if (_disposed || _board.IsEmpty)
            return;

        var moved = false;

        foreach (var card in Cards)
        {
            if (_spots.TryGetValue(card.Path, out var spot) && spot == card.Spot)
                continue;

            _spots[card.Path] = card.Spot;
            moved = true;
        }

        if (moved)
            Save();
    }

    /// <summary>
    /// Убирает карточки с доски — из коллекции холста, а не прячет их; файлы форм на месте.
    /// </summary>
    /// <param name="cards">Что убрать.</param>
    /// <returns>Сделанная уборка — для истории отмены; убирать нечего — null.</returns>
    /// <remarks>
    /// Убранная карточка уходит из <see cref="Cards"/>: у холста не остаётся ни контейнера, ни места в
    /// протяжённости, и «Вписать всё», рамка выбора и Ctrl+A её не видят. Спрятанная осталась бы всем
    /// этим — замер в записи 329 плана. Место карточки помнится: вернувшись, она встанет туда же.
    /// </remarks>
    public BoardPresence? Remove(IReadOnlyList<FormCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var taken = cards
            .Where(card => _cards.TryGetValue(card.Path, out var held) && ReferenceEquals(held, card))
            .Distinct()
            .ToList();

        if (taken.Count == 0)
            return null;

        var change = new BoardPresence(this, taken, Removing: true);

        change.Reapply();

        return change;
    }

    /// <summary>
    /// Возвращает убранные формы на доску — туда, где они стояли.
    /// </summary>
    /// <param name="paths">Какие формы вернуть.</param>
    /// <returns>Сделанный возврат — для истории отмены; возвращать нечего — null.</returns>
    public BoardPresence? Return(IEnumerable<CanonicalPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var cards = paths
            .Distinct()
            .Where(path => _removed.Contains(path) && _found.ContainsKey(path))
            .Select(path => _parked.TryGetValue(path, out var parked)
                ? parked
                : new FormCard(_found[path].File, _found[path].Root, KindText(_found[path].Root.Kind)))
            .ToList();

        if (cards.Count == 0)
            return null;

        var change = new BoardPresence(this, cards, Removing: false);

        change.Reapply();

        return change;
    }

    /// <summary>
    /// Что из несомого мышью доска может поставить: формы решения, по одной, в порядке несомого.
    /// </summary>
    /// <param name="files">Пути, которые несут, — файлы и каталоги.</param>
    /// <remarks>
    /// Формой считается то, что доска нашла в снимке, — так же, как она решает, какие карточки ставить:
    /// разметка приложения и словарей стилей, файл вне проекта и каталог формами не бывают, и
    /// поставленная такая карточка ушла бы со следующим снимком. Пока решения нет, ставить некуда.
    /// </remarks>
    public IReadOnlyList<Landing> Landings(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (State != BoardState.Ready)
            return [];

        var seen = new HashSet<CanonicalPath>();
        var landings = new List<Landing>();

        foreach (var file in files)
        {
            if (!CanonicalPath.TryCreate(file, out var path) || !_found.TryGetValue(path, out var form) || !seen.Add(path))
                continue;

            if (_cards.TryGetValue(path, out var card))
                landings.Add(new Landing(path, card, OnBoard: true));
            else
                landings.Add(new Landing(path, _parked.GetValueOrDefault(path) ?? new FormCard(form.File, form.Root, KindText(form.Root.Kind)), OnBoard: false));
        }

        return landings;
    }

    /// <summary>
    /// Ставит формы на доску в заданные места: убранные возвращаются, стоящие передвигаются.
    /// </summary>
    /// <param name="places">Формы и левые верхние углы их мест.</param>
    /// <returns>
    /// Карточки, вставшие на места, в порядке мест, и сделанное — для истории отмены; карточки уже стояли
    /// там — сделанного нет.
    /// </returns>
    public (IReadOnlyList<FormCard> Cards, BoardLanded? Change) Land(IReadOnlyList<(CanonicalPath Form, Spot At)> places)
    {
        ArgumentNullException.ThrowIfNull(places);

        if (State != BoardState.Ready)
            return ([], null);

        var wanted = places
            .Where(place => _found.ContainsKey(place.Form))
            .DistinctBy(place => place.Form)
            .ToList();

        var returned = Return(wanted.Select(place => place.Form).Where(_removed.Contains).ToList());
        var landed = wanted
            .Where(place => _cards.ContainsKey(place.Form))
            .Select(place => (Card: _cards[place.Form], To: new Point(place.At.X, place.At.Y)))
            .ToList();
        var moves = landed
            .Select(place => new BoardMove(place.Card, place.Card.Location, place.To))
            .Where(move => move.From != move.To)
            .ToList();
        var moved = moves.Count == 0 ? null : new BoardMoves(moves);

        moved?.Reapply();

        return ([.. landed.Select(place => place.Card)], returned is null && moved is null ? null : new BoardLanded(returned, moved));
    }

    /// <summary>Снимает карточки с доски: их место запоминается, путь — в список убранных.</summary>
    /// <param name="cards">Карточки; те, которых на доске уже нет, пропускаются.</param>
    internal void Take(IReadOnlyList<FormCard> cards)
    {
        var taken = new List<FormCard>();

        foreach (var card in cards)
        {
            if (!_cards.TryGetValue(card.Path, out var held))
                continue;

            _spots[card.Path] = held.Spot;
            _cards.Remove(card.Path);
            Cards.Remove(held);
            _removed.Add(card.Path);
            _parked[card.Path] = held;
            taken.Add(held);
        }

        if (taken.Count == 0)
            return;

        Settle();
        PresenceChanged?.Invoke(this, new PresenceEventArgs(taken, Removed: true));
    }

    /// <summary>
    /// Ставит карточки на доску: на прежнее место, а без него — под занятой частью.
    /// </summary>
    /// <param name="cards">Карточки; форма, которой в решении больше нет, не возвращается.</param>
    /// <remarks>
    /// Карточка возвращается тем же объектом, что ушла: на него ссылаются прежние записи истории —
    /// тяга, раскладка, — и отмена их двигает то, что на экране.
    /// </remarks>
    internal void Put(IReadOnlyList<FormCard> cards)
    {
        var fresh = new List<FormCard>();
        var put = new List<FormCard>();

        foreach (var card in cards)
        {
            if (_cards.ContainsKey(card.Path) || !_found.TryGetValue(card.Path, out var form))
                continue;

            _removed.Remove(card.Path);
            _parked.Remove(card.Path);
            card.Update(form.File, form.Root, KindText(form.Root.Kind));

            if (_spots.TryGetValue(card.Path, out var spot))
                card.Location = new Point(spot.X, spot.Y);
            else
                fresh.Add(card);

            _cards[card.Path] = card;
            Cards.Add(card);
            put.Add(card);
        }

        if (put.Count == 0)
            return;

        Place(fresh);
        Settle();
        PresenceChanged?.Invoke(this, new PresenceEventArgs(put, Removed: false));
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
        _removed = [];
        _found = [];
        _parked.Clear();
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

        // Шаг рядов — здесь, в потоке интерфейса: его меряют по видимым карточкам, а файл читается в фоне.
        var rowPitch = _pitch().Y;

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
                    // Доска читается, только когда решение сменилось: у того же решения она уже на экране,
                    // и файл, перечитанный поверх, вернул бы карточки на места до последней тяги.
                    var saved = !changed ? null
                        : board is not null && folder is { } at ? BoardFile.Read(board, at, rowPitch)
                        : BoardData.Empty();

                    return (scan, saved);
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

                    var (scan, saved) = task.Result;

                    Fill(entry, scan.Forms, scan.Known, saved);
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
    /// <param name="saved">Доска из файла — если решение сменилось; иначе null.</param>
    /// <remarks>
    /// Убранная форма карточки не получает: она помнится и стоит в списке возврата. Удалённая с диска
    /// уходит и из него при следующей записи.
    /// </remarks>
    private void Fill(
        CanonicalPath entry,
        IReadOnlyList<FoundForm> forms,
        IReadOnlyDictionary<CanonicalPath, ReadRoot> known,
        BoardData? saved)
    {
        _known = known;
        _scanning = false;
        _found = forms.ToDictionary(form => form.File.Path);

        var replaced = saved is not null;

        if (replaced)
        {
            _board = entry;
            _spots = saved!.Spots;
            _removed = saved.Removed;
            _parked.Clear();
            _cards.Clear();
            Cards.Clear();
        }

        var present = new HashSet<CanonicalPath>();
        var fresh = new List<FormCard>();

        foreach (var form in forms)
        {
            var path = form.File.Path;
            var kind = KindText(form.Root.Kind);

            if (_removed.Contains(path))
                continue;

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
            Place(fresh);
            Save();
        }

        if (replaced)
            Replaced?.Invoke(this, EventArgs.Empty);

        Raise(null);
    }

    /// <summary>Новым карточкам — места под занятой частью доски; места сразу помнятся.</summary>
    private void Place(List<FormCard> fresh)
    {
        if (fresh.Count == 0)
            return;

        var placed = Cards.Except(fresh).Select(card => card.Spot).ToList();
        var places = BoardLayout.Below(placed, fresh.Count, _pitch());

        for (var index = 0; index < fresh.Count; index++)
        {
            fresh[index].Location = new Point(places[index].X, places[index].Y);
            _spots[fresh[index].Path] = places[index];
        }
    }

    /// <summary>Состав доски сменился: записать и сказать виду.</summary>
    private void Settle()
    {
        Save();
        Raise(null);
    }

    /// <summary>
    /// Ставит доску в очередь записи: пишет её пул, и пишет последнюю.
    /// </summary>
    /// <remarks>
    /// Правки идут очередями — стрелку держат, и каждый шаг холст сдаёт истории, — а запись медленнее
    /// правки. Записывать каждую значило бы переписывать файл десятки раз в секунду ради последней.
    /// Поэтому ждущая запись одна: новая правка её подменяет, а писатель, закончив, берёт ту, что ждёт
    /// сейчас. Последней на диск всегда ложится последняя правка.
    /// </remarks>
    private void Save()
    {
        if (BoardFile.PathFor(_board) is not { } file || BoardFile.FolderOf(_board) is not { } folder)
            return;

        var pending = new PendingWrite(
            file,
            folder,
            new BoardData(new Dictionary<CanonicalPath, Spot>(_spots), [.. _removed]),
            [.. _cards.Keys]);

        lock (_gate)
        {
            _pending = pending;

            if (_draining)
                return;

            _draining = true;
        }

        _writing = Task.Run(Drain);
    }

    private void Drain()
    {
        while (true)
        {
            PendingWrite next;

            lock (_gate)
            {
                if (_pending is not { } pending)
                {
                    _draining = false;
                    return;
                }

                next = pending;
                _pending = null;
            }

            Write(next);
        }
    }

    /// <summary>
    /// Пишет доску; место и уборка формы, которой нет ни на доске, ни на диске, в файл не идут.
    /// </summary>
    private void Write(PendingWrite write)
    {
        var board = write.Board;

        foreach (var gone in board.Spots.Keys.Where(path => !write.Present.Contains(path) && !File.Exists(path.Value)).ToList())
            board.Spots.Remove(gone);

        board.Removed.RemoveWhere(path => !File.Exists(path.Value));

        try
        {
            BoardFile.Write(write.File, write.Folder, board);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Log.Write(StudioLogLevel.Warning, LogSource, $"Доска не записалась в {write.File}: {e.Message}");
        }
    }

    /// <summary>Запись, ждущая очереди: куда и что.</summary>
    private sealed record PendingWrite(string File, CanonicalPath Folder, BoardData Board, HashSet<CanonicalPath> Present);

    private string KindText(FormKind kind) => FormCard.KindTextOf(_context.Strings, kind);

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, _context.Strings[key], values);

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Перемена состава доски: какие карточки и в какую сторону.</summary>
/// <param name="Cards">Карточки, которые ушли или вернулись.</param>
/// <param name="Removed">Убраны — или вернулись.</param>
internal sealed record PresenceEventArgs(IReadOnlyList<FormCard> Cards, bool Removed);
