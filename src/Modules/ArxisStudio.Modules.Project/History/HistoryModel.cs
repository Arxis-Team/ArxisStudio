using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project.History;

/// <summary>Строка списка истории: действие словами для человека.</summary>
/// <param name="Revision">Строка истории.</param>
/// <param name="Title">Что случилось: метка действия, у метки — её текст.</param>
/// <param name="Detail">Когда и кто: «Сегодня, 10:05 · вне студии».</param>
/// <param name="When">Когда — тем же словом, что в подробностях: «Сегодня, 10:05».</param>
internal sealed record HistoryRow(LocalHistoryRevision Revision, string Title, string Detail, string When)
{
    /// <summary>Номер действия.</summary>
    public long Id => Revision.Action.Id;
}

/// <summary>Правка файла в строке истории папки.</summary>
/// <param name="Change">Правка.</param>
/// <param name="Title">Что с каким файлом: «Удалён Views\Readme.txt».</param>
internal sealed record HistoryFile(LocalHistoryChange Change, string Title);

/// <summary>Строка разницы, как её рисует окно.</summary>
/// <param name="Row">Строка разницы.</param>
internal sealed record DiffLine(DiffRow Row)
{
    /// <summary>Номер слева; пусто — строки нет.</summary>
    public string LeftNumber => Row.Left > 0 ? Row.Left.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Номер справа; пусто — строки нет.</summary>
    public string RightNumber => Row.Right > 0 ? Row.Right.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Текст слева.</summary>
    public string? LeftText => Row.LeftText;

    /// <summary>Текст справа.</summary>
    public string? RightText => Row.RightText;

    /// <summary>Строка пропала.</summary>
    public bool IsRemoved => Row.Kind == DiffKind.Removed;

    /// <summary>Строка появилась.</summary>
    public bool IsAdded => Row.Kind == DiffKind.Added;

    /// <summary>Строка переписана.</summary>
    public bool IsChanged => Row.Kind == DiffKind.Changed;
}

/// <summary>
/// Окно локальной истории файла или папки: строки истории, разница выбранной и то, что с ней можно
/// сделать.
/// </summary>
/// <remarks>
/// <para>
/// <b>У файла</b> разница — каким он был до выбранной строки (<see cref="Timeline"/>) против того,
/// каким лежит сейчас: «Вернуть» сделает правую сторону левой, и разница показывает ровно то, что
/// он сделает. <b>У папки</b> выбранная строка раскрывается файлами, которые действие задело, а
/// разница — что действие сделало с выбранным файлом: до и после.
/// </para>
/// <para>
/// Разница считается вне потока интерфейса: большой файл и чтение из истории не должны держать
/// окно. Каждый показ берёт билет, и применяется только последний — щелчки по строкам подряд не
/// показывают разницу строки, с которой уже ушли.
/// </para>
/// </remarks>
internal sealed class HistoryModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioHistory _history;
    private readonly IStudioStrings _strings;
    private readonly TimeProvider _time;
    private IReadOnlyList<FileState> _states = [];
    private HistoryRow? _selected;
    private HistoryFile? _file;
    private IReadOnlyList<DiffLine> _diff = [];
    private string _left = string.Empty;
    private string _right = string.Empty;
    private string _status = string.Empty;
    private FileState _state = FileState.Now;
    private bool _differs;
    private long _ticket;
    private bool _disposed;

    /// <summary>Заводит окно истории пути.</summary>
    /// <param name="history">Служба истории.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="target">Файл или папка.</param>
    /// <param name="folder">Это папка: у неё строки раскрываются файлами.</param>
    /// <param name="name">Как путь назвать в заголовке.</param>
    /// <param name="time">Часы — для «сегодня» и «вчера»; пусто — системные.</param>
    public HistoryModel(IStudioHistory history, IStudioStrings strings, CanonicalPath target, bool folder, string name, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(name);

        _history = history;
        _strings = strings;
        _time = time ?? TimeProvider.System;
        Target = target;
        IsFolder = folder;
        Name = name;
        Title = Format("project.history.title", name);
        _history.Changed += OnHistoryChanged;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Файл или папка, чья история.</summary>
    public CanonicalPath Target { get; }

    /// <summary>Это папка.</summary>
    public bool IsFolder { get; }

    /// <summary>Имя пути.</summary>
    public string Name { get; }

    /// <summary>Заголовок окна.</summary>
    public string Title { get; }

    /// <summary>Строки истории, от новой к старой.</summary>
    public ObservableCollection<HistoryRow> Rows { get; } = [];

    /// <summary>Файлы, которые задело выбранное действие, — у папки.</summary>
    public ObservableCollection<HistoryFile> Files { get; } = [];

    /// <summary>Разница: строки в две колонки.</summary>
    public IReadOnlyList<DiffLine> Diff
    {
        get => _diff;
        private set => Set(ref _diff, value, nameof(Diff), nameof(HasChanges));
    }

    /// <summary>Подпись левой стороны.</summary>
    public string LeftCaption
    {
        get => _left;
        private set => Set(ref _left, value, nameof(LeftCaption));
    }

    /// <summary>Подпись правой стороны.</summary>
    public string RightCaption
    {
        get => _right;
        private set => Set(ref _right, value, nameof(RightCaption));
    }

    /// <summary>Строка состояния окна.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value, nameof(Status));
    }

    /// <summary>Выбранная строка истории.</summary>
    public HistoryRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
                return;

            _selected = value;
            Raise(nameof(Selected), nameof(CanUndo), nameof(CanRevert));
            Shown = ShowRowAsync();
        }
    }

    /// <summary>Выбранный файл действия — у папки.</summary>
    public HistoryFile? File
    {
        get => _file;
        set
        {
            if (ReferenceEquals(_file, value))
                return;

            _file = value;
            Raise(nameof(File), nameof(CanRevert));
            Shown = ShowFileAsync();
        }
    }

    /// <summary>Что написано на кнопке возврата.</summary>
    public string RevertText => _strings[IsFolder ? "project.history.revertFile" : "project.history.revert"];

    /// <summary>Можно ли вернуть: у файла — к тому, что слева; у папки — выбранный файл к «до».</summary>
    public bool CanRevert => IsFolder
        ? File is { Change: { IsDirectory: false, Before.IsEmpty: false, Kind: LocalHistoryChangeKind.Modified or LocalHistoryChangeKind.Deleted } }
        : Selected is not null && _state.Kind == FileStateKind.Stored && _differs;

    /// <summary>Можно ли отменить выбранное действие.</summary>
    public bool CanUndo => Selected is { Revision.Action: { IsLabel: false, IsUndone: false } };

    /// <summary>Есть ли в разнице изменения — ходить по ним есть куда.</summary>
    public bool HasChanges => _diff.Any(line => line.Row.Kind != DiffKind.Same);

    /// <summary>Последний начатый показ: тесты ждут его конца.</summary>
    internal Task Shown { get; private set; } = Task.CompletedTask;

    /// <summary>Читает историю и встаёт на строку — ту же, что была, или самую новую.</summary>
    public async Task LoadAsync()
    {
        var keep = Selected?.Id;
        IReadOnlyList<LocalHistoryRevision> revisions;

        try
        {
            revisions = await _history.RevisionsAsync(Target).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Status = e.Message;
            return;
        }

        if (_disposed)
            return;

        var now = _time.GetLocalNow();

        _states = IsFolder ? [] : Timeline.Before(revisions);
        Rows.Clear();

        foreach (var revision in revisions)
            Rows.Add(Row(revision, now));

        if (Rows.Count == 0)
        {
            Selected = null;
            Diff = [];
            LeftCaption = RightCaption = string.Empty;
            Status = _history.IsOn ? Format("project.history.empty", Name) : _strings["project.history.off"];
            return;
        }

        Selected = Rows.FirstOrDefault(row => row.Id == keep) ?? Rows[0];
    }

    /// <summary>
    /// Следующее изменение в разнице — начало следующего куска правок после строки.
    /// </summary>
    /// <param name="from">С какой строки разницы; −1 — с начала.</param>
    /// <param name="forward">Вперёд или назад.</param>
    /// <returns>Номер строки; −1 — дальше изменений нет.</returns>
    public int NextChange(int from, bool forward)
    {
        var starts = Enumerable.Range(0, _diff.Count)
            .Where(at => _diff[at].Row.Kind != DiffKind.Same && (at == 0 || _diff[at - 1].Row.Kind == DiffKind.Same))
            .ToList();

        return forward
            ? starts.FirstOrDefault(at => at > from, -1)
            : starts.LastOrDefault(at => at < from, -1);
    }

    /// <summary>Возвращает: файл — к тому, что слева; у папки — выбранный файл к тому, каким был до действия.</summary>
    /// <returns>Итог службы; пусто — возвращать нечего.</returns>
    public async Task<ProjectOperationResult?> RevertAsync()
    {
        if (!CanRevert || Selected is not { } row)
            return null;

        var (path, content) = IsFolder && File is { } file
            ? (file.Change.Path, file.Change.Before)
            : (Target, _state.Content);

        var result = await _history.RevertAsync(path, content, Format("project.history.revert.label", path.FileName, row.Title))
            .ConfigureAwait(true);

        if (!result.HasErrors)
            Status = Format("project.history.reverted", path.FileName, row.Title);

        return result;
    }

    /// <summary>Отменяет выбранное действие.</summary>
    /// <returns>Итог службы; пусто — отменять нечего.</returns>
    public async Task<ProjectOperationResult?> UndoAsync()
    {
        if (!CanUndo || Selected is not { } row)
            return null;

        var result = await _history.UndoAsync(row.Id).ConfigureAwait(true);

        if (!result.HasErrors)
            Status = Format("project.undone", row.Revision.Action.Label);

        return result;
    }

    /// <summary>Ставит метку на решение.</summary>
    /// <param name="text">Текст метки.</param>
    public async Task<ProjectOperationResult> LabelAsync(string text)
    {
        var result = await _history.PutLabelAsync(text).ConfigureAwait(true);

        if (!result.HasErrors)
            Status = Format("project.history.labelled", text);

        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ticket++;
        _history.Changed -= OnHistoryChanged;
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            _ = LoadAsync();
    }

    private HistoryRow Row(LocalHistoryRevision revision, DateTimeOffset now)
    {
        var action = revision.Action;
        var local = action.Time.ToLocalTime();
        var when = (now.Date - local.Date).Days switch
        {
            0 => Format("project.history.today", local),
            1 => Format("project.history.yesterday", local),
            _ => Format("project.history.date", local),
        };

        var detail = new List<string> { when, _strings[action.Origin == LocalHistoryOrigin.Studio ? "project.history.studio" : "project.history.outside"] };

        if (action.IsUndone)
            detail.Add(_strings["project.history.undone"]);

        var title = action.IsLabel ? Format("project.history.labelRow", action.Label) : action.Label;

        return new HistoryRow(revision, title, string.Join(" · ", detail), when);
    }

    private async Task ShowRowAsync()
    {
        var ticket = ++_ticket;

        if (Selected is not { } row)
            return;

        if (IsFolder)
        {
            Files.Clear();

            foreach (var change in row.Revision.Changes)
                Files.Add(new HistoryFile(change, Describe(change)));

            _file = Files.FirstOrDefault(file => !file.Change.IsDirectory) ?? Files.FirstOrDefault();
            Raise(nameof(File), nameof(CanRevert));

            if (_file is null)
            {
                Diff = [];
                LeftCaption = RightCaption = string.Empty;
                Status = Format("project.history.labelRow", row.Revision.Action.Label);
                return;
            }

            await ShowFileAsync().ConfigureAwait(true);
            return;
        }

        var at = Rows.IndexOf(row);

        _state = at >= 0 && at < _states.Count ? _states[at] : FileState.Now;
        LeftCaption = Format("project.history.before", row.Revision.Action.Label, row.When);
        RightCaption = _strings["project.history.now"];

        var left = _state.Kind switch
        {
            FileStateKind.Stored => await Stored(_state.Content).ConfigureAwait(true),
            FileStateKind.Absent => Side.Nothing,
            FileStateKind.NotStored => Side.NotKept,
            _ => Disk(Target),
        };

        await Show(ticket, left, Disk(Target), _state.Kind == FileStateKind.Absent ? "project.history.absent" : null)
            .ConfigureAwait(true);
    }

    private async Task ShowFileAsync()
    {
        var ticket = ++_ticket;

        if (!IsFolder || File is not { } file)
            return;

        var change = file.Change;

        LeftCaption = _strings["project.history.was"];
        RightCaption = _strings["project.history.became"];

        if (change.IsDirectory)
        {
            Diff = [];
            Status = file.Title;
            return;
        }

        Side left, right;

        if (change is { Kind: LocalHistoryChangeKind.Moved, Before.IsEmpty: true, After.IsEmpty: true, TooLarge: false })
        {
            // Переезд без записанного содержимого байт не менял: слева и справа файл, каков он есть.
            left = right = Disk(change.Path);
        }
        else
        {
            left = change.Kind == LocalHistoryChangeKind.Created ? Side.Nothing : await Stored(change.Before).ConfigureAwait(true);
            right = change.Kind == LocalHistoryChangeKind.Deleted ? Side.Nothing : await Stored(change.After).ConfigureAwait(true);
        }

        await Show(ticket, left, right, null).ConfigureAwait(true);
    }

    /// <summary>Считает разницу вне потока интерфейса и кладёт её, если показ ещё тот же.</summary>
    private async Task Show(long ticket, Side left, Side right, string? note)
    {
        var (diff, status, differs) = await Task.Run(() => Compare(left, right)).ConfigureAwait(true);

        if (ticket != _ticket || _disposed)
            return;

        _differs = differs;
        Diff = diff;
        Status = note is null ? status : _strings[note];
        Raise(nameof(CanRevert));
    }

    private (IReadOnlyList<DiffLine> Diff, string Status, bool Differs) Compare(Side left, Side right)
    {
        if (left.Kind == SideKind.NotKept || right.Kind == SideKind.NotKept)
            return ([], _strings["project.history.notStored"], true);

        if (left.Kind == SideKind.Corrupted || right.Kind == SideKind.Corrupted)
            return ([], _strings["project.history.corrupted"], true);

        if (left.Kind == SideKind.Busy || right.Kind == SideKind.Busy)
            return ([], _strings["project.history.busy"], true);

        var before = left.Kind == SideKind.Nothing ? string.Empty : TextSniff.Decode(left.Bytes!);
        var after = right.Kind == SideKind.Nothing ? string.Empty : TextSniff.Decode(right.Bytes!);
        var differs = left.Kind != right.Kind || (left.Bytes is { } a && right.Bytes is { } b && !a.AsSpan().SequenceEqual(b));

        if (before is null || after is null)
            return ([], _strings["project.history.binary"], differs);

        var rows = LineDiff.Rows(LineDiff.Lines(before), LineDiff.Lines(after)).Select(row => new DiffLine(row)).ToList();
        var blocks = rows.Where((line, at) => line.Row.Kind != DiffKind.Same && (at == 0 || rows[at - 1].Row.Kind == DiffKind.Same)).Count();

        var status = right.Kind == SideKind.Nothing && !IsFolder
            ? _strings["project.history.gone"]
            : blocks == 0
                ? _strings[differs ? "project.history.whitespace" : "project.history.same"]
                : Format("project.history.changes", blocks);

        return (rows, status, differs);
    }

    /// <summary>Содержимое из истории: пустая ручка — не хранится, не прочиталось — испорчено.</summary>
    private async Task<Side> Stored(LocalHistoryContent content)
    {
        if (content.IsEmpty)
            return Side.NotKept;

        try
        {
            return await _history.ReadAsync(content).ConfigureAwait(true) is { } bytes
                ? new Side(SideKind.Bytes, bytes)
                : Side.Corrupted;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Side.Corrupted;
        }
    }

    /// <summary>Файл, каким он лежит сейчас: нет — так и сказано, не прочесть — тоже.</summary>
    private static Side Disk(CanonicalPath path)
    {
        try
        {
            return System.IO.File.Exists(path.Value)
                ? new Side(SideKind.Bytes, System.IO.File.ReadAllBytes(path.Value))
                : Side.Nothing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Side.Busy;
        }
    }

    private string Describe(LocalHistoryChange change)
    {
        var path = Relative(change.Path);
        var kind = change.Kind switch
        {
            LocalHistoryChangeKind.Created => "created",
            LocalHistoryChangeKind.Modified => "modified",
            LocalHistoryChangeKind.Deleted => "deleted",
            _ => "moved",
        };

        return Format($"project.history.{(change.IsDirectory ? "folder" : "file")}.{kind}", path, Relative(change.From));
    }

    private string Relative(CanonicalPath path)
    {
        if (path.IsEmpty)
            return string.Empty;

        return path.StartsWith(Target) && path != Target
            ? Path.GetRelativePath(Target.Value, path.Value)
            : path.FileName;
    }

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], values);

    private void Set<T>(ref T field, T value, params string[] names)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        Raise(names);
    }

    private void Raise(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Что лежит на стороне разницы.</summary>
    private enum SideKind
    {
        /// <summary>Байты.</summary>
        Bytes,

        /// <summary>Файла нет.</summary>
        Nothing,

        /// <summary>Содержимое не хранится.</summary>
        NotKept,

        /// <summary>Содержимое из истории не прочиталось.</summary>
        Corrupted,

        /// <summary>Файл на диске держит другая программа.</summary>
        Busy,
    }

    /// <summary>Сторона разницы.</summary>
    /// <param name="Kind">Что на ней.</param>
    /// <param name="Bytes">Байты — у <see cref="SideKind.Bytes"/>.</param>
    private sealed record Side(SideKind Kind, byte[]? Bytes)
    {
        public static Side Nothing { get; } = new(SideKind.Nothing, null);

        public static Side NotKept { get; } = new(SideKind.NotKept, null);

        public static Side Corrupted { get; } = new(SideKind.Corrupted, null);

        public static Side Busy { get; } = new(SideKind.Busy, null);
    }
}
