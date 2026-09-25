using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.Files;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.History;

/// <summary>
/// Отмена действия истории и возврат файла: проверки до первого байта, правка с откатом и запись
/// новым действием.
/// </summary>
/// <remarks>
/// <para>
/// <b>Что делает отмена.</b> Всё, что действие сделало, — в обратную сторону и в таком порядке:
/// убирает появившееся (файлы, потом опустевшие папки, глубокие раньше), возвращает переехавшее на
/// прежние места в обратном порядке переездов, заводит пропавшие папки, потом файлы с прежним
/// содержимым, и возвращает переписанному прежнее содержимое. Порядок не случаен: заменённый при
/// переносе файл встаёт на место только после того, как оттуда уедет заменивший.
/// </para>
/// <para>
/// <b>Новое не затирается.</b> Перед первым байтом каждый путь сверяется с тем, каким его оставило
/// действие: содержимое — по адресу, место — по тому, свободно ли оно. Разошёлся хоть один — отказ, и
/// не тронуто ничего. Совпавшее с тем, что было до действия, пропускается молча: там отменять уже
/// нечего.
/// </para>
/// <para>
/// <b>Файлы проектов.</b> Перенос и удаление переписывают ссылки в файлах проектов, и отмена
/// возвращает файлу проекта прежние байты — если он с тех пор не менялся. Менялся — а человек успел,
/// скажем, поставить пакет, — байты не возвращаются: у переноса ссылки переписываются обратно тем же
/// переписчиком, что и вперёд, а снятые удалением вернуть нечем, и об этом говорит предупреждение.
/// </para>
/// <para>
/// <b>Чего история не хранит</b> — файл больше предела, испорченный объект, — то отмена пропускает и
/// называет предупреждением: вернуть остальное лучше, чем не вернуть ничего из-за одного файла.
/// </para>
/// </remarks>
internal static class HistoryUndo
{
    /// <summary>Отменяет действие.</summary>
    /// <param name="id">Номер действия.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="store">История.</param>
    /// <param name="words">Слова отказов.</param>
    public static FileWorkResult Undo(long id, SolutionSnapshot snapshot, LocalHistoryStore store, HistoryWords words)
    {
        if (store.Find(id) is not { } action)
            return Refusals.Work(ProjectsDiagnosticCodes.HistoryUnavailable, words.Unknown);

        if (action.IsLabel)
            return Refusals.Work(ProjectsDiagnosticCodes.HistoryUnavailable, words.Label);

        if (store.Undone().Contains(action.Id))
            return Refusals.Work(ProjectsDiagnosticCodes.ChangedSince, words.Undone(action.Label));

        if (Outside(action, snapshot) is { } outside)
            return Refusals.Work(ProjectsDiagnosticCodes.OutsideProjects, words.Outside(outside), outside);

        var warnings = new List<ProjectDiagnostic>();
        var (steps, refusal) = Plan(action, snapshot, store, words, warnings);

        if (refusal is not null)
            return new FileWorkResult(ProjectOperationResult.Failed(refusal), null);

        if (steps.Count == 0)
        {
            // Пропущено всё: или вернуть нечем — тогда причины и есть отказ, — или отменять нечего.
            return warnings.Count > 0
                ? new FileWorkResult(ProjectOperationResult.Failed(warnings.Select(warning => warning with { Severity = ProjectDiagnosticSeverity.Error })), null)
                : Refusals.Work(ProjectsDiagnosticCodes.ChangedSince, words.Nothing(action.Label));
        }

        var done = new List<Step>();

        try
        {
            foreach (var step in steps)
            {
                // В списке до начала: откат шага, упавшего на середине, снимает то, что он успел.
                done.Add(step);
                step.Do(store);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (var at = done.Count - 1; at >= 0; at--)
            {
                var step = done[at];

                FileWorker.Quietly(() => step.Back(store));
            }

            return Refusals.Work(ProjectsDiagnosticCodes.FileOperationFailed, words.Failed(e.Message));
        }

        foreach (var step in done)
            step.Settle(store);

        var changes = done.SelectMany(step => step.Changes).ToList();

        if (changes.Count == 0)
            return Refusals.Work(ProjectsDiagnosticCodes.ChangedSince, words.Nothing(action.Label));

        store.Record(words.Undo(action.Label), HistoryOrigin.Studio, changes, undoes: action.Id);
        store.Flush();

        return new FileWorkResult(ProjectOperationResult.Succeeded(warnings), Change(done))
        {
            Rereads = done.Any(step => step.Rereads && step.Changes.Count > 0),
        };
    }

    /// <summary>Переписывает файл содержимым из истории, а пропавший заводит заново.</summary>
    /// <param name="path">Файл.</param>
    /// <param name="content">Содержимое.</param>
    /// <param name="label">Метка действия.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="store">История.</param>
    /// <param name="words">Слова отказов.</param>
    /// <remarks>
    /// Нынешнее содержимое уходит в историю раньше, чем его перепишут: возврат — такое же действие,
    /// как всякое, и отменяется так же. Поэтому файл больше предела не переписывается вовсе — его
    /// нынешнего содержимого история не сохранит, и после возврата его было бы не вернуть.
    /// </remarks>
    public static FileWorkResult Revert(
        CanonicalPath path,
        ContentId content,
        string label,
        SolutionSnapshot snapshot,
        LocalHistoryStore store,
        HistoryWords words)
    {
        var file = path.Value;

        if (!HistoryFilter.IsTracked(snapshot, path))
            return Refusals.Work(ProjectsDiagnosticCodes.OutsideProjects, words.Outside(file), file);

        if (Directory.Exists(file))
            return Refusals.Work(ProjectsDiagnosticCodes.ChangedSince, words.Folder(file), file);

        if (!store.Has(content))
            return Refusals.Work(ProjectsDiagnosticCodes.NotStored, words.Corrupted(file), file);

        HistoryFileState? current = null;

        if (File.Exists(file))
        {
            if (store.Capture(file) is not { } state)
                return Refusals.Work(ProjectsDiagnosticCodes.FileOperationFailed, words.Busy(file), file);

            if (state.TooLarge)
                return Refusals.Work(ProjectsDiagnosticCodes.NotStored, words.CurrentTooLarge(file), file);

            // Файл уже такой: переписывать и записывать нечего.
            if (state.Content == content)
                return new FileWorkResult(ProjectOperationResult.Succeeded(), null);

            current = state;
        }

        var step = current is { Content: { } before }
            ? (Step)new Rewrite(file, content, before, words.Corrupted(file))
            : new Restore(file, content, words.Corrupted(file));

        try
        {
            step.Do(store);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileWorker.Quietly(() => step.Back(store));

            return Refusals.Work(ProjectsDiagnosticCodes.FileOperationFailed, words.Failed(e.Message), file);
        }

        step.Settle(store);
        store.Record(label, HistoryOrigin.Studio, step.Changes);
        store.Flush();

        return new FileWorkResult(ProjectOperationResult.Succeeded(), null) { Rereads = step.Rereads };
    }

    /// <summary>
    /// Действие — над файлами открытого решения: каждый его путь история пишет и в этом решении.
    /// </summary>
    /// <param name="action">Действие.</param>
    /// <param name="snapshot">Снимок.</param>
    public static bool Belongs(HistoryAction action, SolutionSnapshot snapshot) =>
        !action.IsLabel && Outside(action, snapshot) is null;

    /// <summary>
    /// Файл, который читает MSBuild: его правка может поменять модель, и её перечитывают.
    /// </summary>
    /// <param name="path">Путь.</param>
    internal static bool IsBuildInput(string path)
    {
        var extension = Path.GetExtension(path);
        var name = Path.GetFileName(path);

        return extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".targets", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".slnf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "global.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "nuget.config", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Первый путь действия, которого это решение не пишет; null — все свои.</summary>
    private static string? Outside(HistoryAction action, SolutionSnapshot snapshot)
    {
        foreach (var change in action.Changes)
        {
            foreach (var path in change.From is { } from ? new[] { change.Path, from } : [change.Path])
            {
                if (!CanonicalPath.TryCreate(path, out var canonical) || !HistoryFilter.IsTracked(snapshot, canonical))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Раскладывает отмену по шагам и проверяет каждый путь; отказ — ничего не тронуто.
    /// </summary>
    private static (List<Step> Steps, ProjectDiagnostic? Refusal) Plan(
        HistoryAction action,
        SolutionSnapshot snapshot,
        LocalHistoryStore store,
        HistoryWords words,
        List<ProjectDiagnostic> warnings)
    {
        var removes = new List<Step>();
        var created = new List<string>();
        var moves = new List<Step>();
        var deleted = new List<string>();
        var restores = new List<Step>();
        var rewrites = new List<Step>();
        var drifted = new List<string>();

        // Ссылки в файлах проектов переписывает только правка студии, которая что-то двигала или удаляла.
        var references = action.Origin == HistoryOrigin.Studio
            && action.Changes.Any(change => change.Kind is HistoryChangeKind.Moved or HistoryChangeKind.Deleted);

        // Места, которые освободит возврат переехавшего: заменённый переносом файл встанет туда, откуда
        // уедет заменивший, — и проверять, что место свободно, до его отъезда нельзя.
        var vacated = action.Changes
            .Where(change => change.Kind == HistoryChangeKind.Moved)
            .Select(change => change.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var change in action.Changes)
        {
            var path = change.Path;

            switch (change.Kind)
            {
                case HistoryChangeKind.Created when change.IsDirectory:
                    created.Add(path);
                    break;

                case HistoryChangeKind.Created:
                {
                    if (!File.Exists(path))
                        break;

                    if (change.After is not { } made)
                    {
                        warnings.Add(Warning(words.Unverified(path), path));
                        break;
                    }

                    if (Current(store, path) is not { } state)
                        return Refuse(ProjectsDiagnosticCodes.FileOperationFailed, words.Busy(path), path);

                    if (state.Content != made)
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Changed(path), path);

                    removes.Add(new Remove(path, made));
                    break;
                }

                case HistoryChangeKind.Moved when change.From is { } from:
                {
                    var there = change.IsDirectory ? Directory.Exists(path) : File.Exists(path);
                    var back = File.Exists(from) || Directory.Exists(from);
                    var caseOnly = string.Equals(path, from, StringComparison.OrdinalIgnoreCase);

                    // Уже на прежнем месте — вернул кто-то другой.
                    if (!there && back)
                        break;

                    if (!there)
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Gone(path), path);

                    if (back && !caseOnly)
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Occupied(from), from);

                    moves.Add(new MoveBack(path, from, change.IsDirectory));
                    break;
                }

                case HistoryChangeKind.Deleted when change.IsDirectory:
                    if (File.Exists(path) && !vacated.Contains(path))
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Occupied(path), path);

                    deleted.Add(path);
                    break;

                case HistoryChangeKind.Deleted:
                {
                    if (change.Before is not { } content)
                    {
                        warnings.Add(Warning(words.TooLarge(path), path));
                        break;
                    }

                    if (!store.Has(content))
                    {
                        warnings.Add(Warning(words.Corrupted(path), path));
                        break;
                    }

                    if (Directory.Exists(path) && !vacated.Contains(path))
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Occupied(path), path);

                    if (File.Exists(path) && !vacated.Contains(path))
                    {
                        if (Current(store, path) is not { } state)
                            return Refuse(ProjectsDiagnosticCodes.FileOperationFailed, words.Busy(path), path);

                        // Тот же файл уже на месте — вернули его раньше.
                        if (state.Content == content)
                            break;

                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Occupied(path), path);
                    }

                    restores.Add(new Restore(path, content, words.Corrupted(path)));
                    break;
                }

                case HistoryChangeKind.Modified:
                {
                    var project = references
                        && snapshot.Projects.Any(owner => string.Equals(owner.ProjectFilePath.Value, path, StringComparison.OrdinalIgnoreCase));

                    if (!File.Exists(path))
                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Gone(path), path);

                    if (change.After is not { } now)
                    {
                        warnings.Add(Warning(words.Unverified(path), path));
                        break;
                    }

                    if (Current(store, path) is not { } state)
                        return Refuse(ProjectsDiagnosticCodes.FileOperationFailed, words.Busy(path), path);

                    if (state.Content != now)
                    {
                        if (project)
                        {
                            drifted.Add(path);
                            break;
                        }

                        // Уже такой, каким был до действия, — возвращать нечего.
                        if (state.Content is { } same && same == change.Before)
                            break;

                        return Refuse(ProjectsDiagnosticCodes.ChangedSince, words.Changed(path), path);
                    }

                    if (change.Before is not { } before)
                    {
                        warnings.Add(Warning(words.TooLarge(path), path));
                        break;
                    }

                    if (!store.Has(before))
                    {
                        warnings.Add(Warning(words.Corrupted(path), path));
                        break;
                    }

                    rewrites.Add(new Rewrite(path, before, now, words.Corrupted(path)));
                    break;
                }
            }
        }

        moves.Reverse();

        List<Step> steps =
        [
            .. removes,
            .. created.OrderByDescending(folder => folder.Length).Select(folder => new RemoveFolder(folder)),
            .. moves,
            .. deleted.OrderBy(folder => folder.Length).Select(folder => new MakeFolder(folder)),
            .. restores,
            .. rewrites,
        ];

        if (drifted.Count > 0)
        {
            var inverse = action.Changes
                .Where(change => change is { Kind: HistoryChangeKind.Moved, From: not null })
                .Select(change => new PathChange(change.Path, change.From, change.IsDirectory))
                .Reverse()
                .ToList();

            // Последним: после него откатывать нечему, а свой откат у переписчика есть.
            if (inverse.Count > 0)
                steps.Add(new References(snapshot, inverse));

            // Заменённый переносом файл удалён, но его место занял приехавший, и ссылки на это место
            // живы: снятых ссылок нет только у такого удаления.
            var removed = action.Changes.Any(change => change.Kind == HistoryChangeKind.Deleted
                && !action.Changes.Any(moved => moved.Kind == HistoryChangeKind.Moved
                    && string.Equals(moved.Path, change.Path, StringComparison.OrdinalIgnoreCase)));

            if (removed)
                warnings.AddRange(drifted.Select(path => Warning(words.References(path), path)));
        }

        return (steps, null);

        (List<Step>, ProjectDiagnostic?) Refuse(string code, string message, string at) =>
            ([], Refusals.Diagnostic(code, message, at));
    }

    /// <summary>Каков файл сейчас — снятый заново: его содержимое заодно ложится в хранилище, и откату есть что вернуть.</summary>
    private static HistoryFileState? Current(LocalHistoryStore store, string path) => store.Capture(path);

    /// <summary>Что переехало и что удалено — тем, кто держит файлы открытыми.</summary>
    private static FilesChangedEventArgs? Change(List<Step> done)
    {
        var moved = done.OfType<MoveBack>()
            .Where(step => step.Changes.Count > 0)
            .Select(step => new FileMove(CanonicalPath.Create(step.From), CanonicalPath.Create(step.To)))
            .ToList();

        var deleted = done
            .Where(step => step is Remove or RemoveFolder && step.Changes.Count > 0)
            .SelectMany(step => step.Changes.Where(change => change.Kind == HistoryChangeKind.Deleted))
            .Select(change => CanonicalPath.Create(change.Path))
            .ToList();

        return moved.Count + deleted.Count == 0 ? null : new FilesChangedEventArgs([.. moved], [], [.. deleted]);
    }

    /// <summary>Пишет файл через временный рядом: оборванная запись не оставит полфайла.</summary>
    private static void Write(string path, byte[] bytes, bool overwrite)
    {
        var temporary = FileWorker.Temporary(path);

        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileWorker.Quietly(() => File.Delete(temporary));
            throw;
        }
    }

    /// <summary>Заводит недостающие папки над путём — верхние раньше — и записывает их появившимися.</summary>
    private static void Parents(string path, List<string> made, List<HistoryChange> changes)
    {
        var missing = new Stack<string>();

        for (var folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder) && !Directory.Exists(folder); folder = Path.GetDirectoryName(folder))
            missing.Push(folder);

        while (missing.TryPop(out var folder))
        {
            Directory.CreateDirectory(folder);
            made.Add(folder);
            changes.Add(new HistoryChange { Kind = HistoryChangeKind.Created, Path = folder, IsDirectory = true });
        }
    }

    /// <summary>Снимает заведённые папки — глубокие раньше и только пустые.</summary>
    private static void Unmake(List<string> made)
    {
        for (var at = made.Count - 1; at >= 0; at--)
        {
            var folder = made[at];

            FileWorker.Quietly(() =>
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            });
        }
    }

    private static ProjectDiagnostic Warning(string message, string path) =>
        Refusals.Diagnostic(ProjectsDiagnosticCodes.NotStored, message, path, ProjectDiagnosticSeverity.Warning);

    /// <summary>Шаг отмены: сделать, откатить сделанное, запомнить, каким стал диск.</summary>
    private abstract class Step
    {
        /// <summary>Что шаг сделал — так, как это запишет история; пусто — ничего.</summary>
        public List<HistoryChange> Changes { get; } = [];

        /// <summary>Может ли сделанное поменять модель.</summary>
        public virtual bool Rereads => true;

        /// <summary>Делает шаг.</summary>
        public abstract void Do(LocalHistoryStore store);

        /// <summary>Откатывает сделанное — и сделанное наполовину.</summary>
        public abstract void Back(LocalHistoryStore store);

        /// <summary>Отмена прошла: история узнаёт, каким стал диск, и не запишет его чужой правкой.</summary>
        public virtual void Settle(LocalHistoryStore store)
        {
        }
    }

    /// <summary>Убирает файл, который действие завело.</summary>
    private sealed class Remove(string path, ContentId expected) : Step
    {
        private bool _erased;

        public override void Do(LocalHistoryStore store)
        {
            FileWorker.Erase(path);
            _erased = true;
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Deleted, Path = path, Before = expected });
        }

        public override void Back(LocalHistoryStore store)
        {
            if (_erased && !File.Exists(path) && store.Read(expected) is { } bytes)
                Write(path, bytes, overwrite: false);
        }

        public override void Settle(LocalHistoryStore store) => store.Forget(path);
    }

    /// <summary>Убирает папку, которую действие завело, — если она опустела: новое в ней чужое.</summary>
    private sealed class RemoveFolder(string path) : Step
    {
        public override void Do(LocalHistoryStore store)
        {
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
                return;

            Directory.Delete(path);
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Deleted, Path = path, IsDirectory = true });
        }

        public override void Back(LocalHistoryStore store)
        {
            if (Changes.Count > 0)
                Directory.CreateDirectory(path);
        }
    }

    /// <summary>Возвращает переехавшее на прежнее место.</summary>
    /// <param name="from">Где оно сейчас.</param>
    /// <param name="to">Откуда оно уехало.</param>
    /// <param name="folder">Это папка.</param>
    private sealed class MoveBack(string from, string to, bool folder) : Step
    {
        private readonly List<string> _made = [];
        private bool _moved;

        public string From => from;

        public string To => to;

        public override void Do(LocalHistoryStore store)
        {
            Parents(to, _made, Changes);
            FileWorker.Relocate(from, to, folder);
            _moved = true;
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Moved, Path = to, From = from, IsDirectory = folder });
        }

        public override void Back(LocalHistoryStore store)
        {
            if (_moved)
                FileWorker.Relocate(to, from, folder);

            Unmake(_made);
        }

        public override void Settle(LocalHistoryStore store)
        {
            // Что история знала под нынешним именем, она знает теперь под прежним: переезд байт не меняет.
            var known = folder
                ? store.KnownUnder(from).ToList()
                : store.Known(from) is { } file ? [new KeyValuePair<string, HistoryFileState>(from, file)] : [];

            FileWorker.Rekey(store, known.Select(pair => (pair.Key, pair.Value)), path => PathChange.Moved(path, from, to));
        }
    }

    /// <summary>Заводит заново папку, которую действие удалило.</summary>
    private sealed class MakeFolder(string path) : Step
    {
        private readonly List<string> _made = [];

        public override void Do(LocalHistoryStore store)
        {
            if (Directory.Exists(path))
                return;

            Parents(path, _made, Changes);
            Directory.CreateDirectory(path);
            _made.Add(path);
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Created, Path = path, IsDirectory = true });
        }

        public override void Back(LocalHistoryStore store) => Unmake(_made);
    }

    /// <summary>Заводит заново файл с прежним содержимым.</summary>
    private sealed class Restore(string path, ContentId content, string corrupted) : Step
    {
        private readonly List<string> _made = [];
        private bool _written;

        public override void Do(LocalHistoryStore store)
        {
            var bytes = store.Read(content) ?? throw new IOException(corrupted);

            Parents(path, _made, Changes);
            Write(path, bytes, overwrite: false);
            _written = true;
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Created, Path = path, After = content });
        }

        public override void Back(LocalHistoryStore store)
        {
            if (_written)
                FileWorker.Erase(path);

            Unmake(_made);
        }

        public override void Settle(LocalHistoryStore store)
        {
            if (store.Capture(path) is { } state)
                store.Learn(path, state);
        }
    }

    /// <summary>Возвращает файлу прежнее содержимое.</summary>
    private sealed class Rewrite(string path, ContentId content, ContentId expected, string corrupted) : Step
    {
        private bool _written;

        public override bool Rereads => IsBuildInput(path);

        public override void Do(LocalHistoryStore store)
        {
            var bytes = store.Read(content) ?? throw new IOException(corrupted);

            Write(path, bytes, overwrite: true);
            _written = true;
            Changes.Add(new HistoryChange { Kind = HistoryChangeKind.Modified, Path = path, Before = expected, After = content });
        }

        public override void Back(LocalHistoryStore store)
        {
            if (_written && store.Read(expected) is { } bytes)
                Write(path, bytes, overwrite: true);
        }

        public override void Settle(LocalHistoryStore store)
        {
            if (store.Capture(path) is { } state)
                store.Learn(path, state);
        }
    }

    /// <summary>
    /// Переписывает ссылки в файлах проектов обратно — там, где файл проекта с тех пор менялся и
    /// прежние байты вернуть нельзя.
    /// </summary>
    private sealed class References(SolutionSnapshot snapshot, List<PathChange> inverse) : Step
    {
        public override void Do(LocalHistoryStore store)
        {
            if (!FileWorker.Rewrite(snapshot, inverse, store, out var projects, out var failure))
                throw new IOException(failure?.Message, failure);

            Changes.AddRange(projects);
        }

        // Переписчик откатывает себя сам, а после этого шага других нет.
        public override void Back(LocalHistoryStore store)
        {
        }
    }
}
