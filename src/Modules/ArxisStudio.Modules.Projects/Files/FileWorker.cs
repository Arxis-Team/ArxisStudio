using ArxisStudio.LocalHistory;
using ArxisStudio.Modules.Projects.History;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>
/// Правка файлов: диск, ссылки в файлах проектов и история — одним делом очереди записи.
/// </summary>
/// <remarks>
/// <para>
/// <b>Очередь — та же, что у истории.</b> Пачка наблюдателя, вставшая посреди правки, записала бы
/// её перемены внешними, а следом служба записала бы их ещё раз своими. Идя одной очередью, пачка
/// приходит после правки и находит в истории уже записанное.
/// </para>
/// <para>
/// <b>Откат.</b> Создание, перенос и копия откатываются целиком: сделанное возвращается в обратном
/// порядке, если диск отказал посередине или не записался файл проекта. Удаление не откатывается —
/// удалённого не вернуть, — и поэтому до первого удаления содержимое снимается в историю, а отказ
/// посередине записывает в неё то, что успело уйти.
/// </para>
/// </remarks>
internal static class FileWorker
{
    /// <summary>
    /// Создаёт файлы и каталоги: каталоги на пути — первыми, от мелких к глубоким, потом файлы.
    /// </summary>
    /// <param name="items">Что создать.</param>
    /// <param name="label">Метка действия для человека.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="store">История; null — не ведётся.</param>
    /// <param name="words">Слова отказов.</param>
    /// <remarks>
    /// Файл пишется с <see cref="FileMode.CreateNew"/>: появись он на месте между проверкой и
    /// записью — правка откажет, а не затрёт чужое. Отказ диска посередине снимает созданное с
    /// последнего, и каталог — только пустым. В историю файл пишется появлением с содержимым, а каталоги
    /// на пути — появлением каталога: так отмена убирает и их. Ссылок в файлах проектов создание не
    /// пишет — новое SDK-проект берёт своими масками сам.
    /// </remarks>
    public static FileWorkResult Create(
        IReadOnlyList<FileCreation> items,
        string label,
        SolutionSnapshot snapshot,
        LocalHistoryStore? store,
        FileWords words)
    {
        if (FileChecks.Check(items, snapshot, words) is { } refused)
            return new FileWorkResult(ProjectOperationResult.Failed(refused), null);

        var folders = Folders(items);
        var made = new List<(string Path, bool Folder)>();

        try
        {
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(folder);
                made.Add((folder, true));
            }

            foreach (var item in items.Where(item => !item.IsDirectory))
            {
                using (var stream = new FileStream(item.Path.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(item.Content.Span);

                made.Add((item.Path.Value, false));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (var at = made.Count - 1; at >= 0; at--)
            {
                var (path, folder) = made[at];

                Quietly(() =>
                {
                    if (folder)
                        Directory.Delete(path);
                    else
                        File.Delete(path);
                });
            }

            return Failed(words, e);
        }

        if (store is not null)
        {
            var changes = folders
                .Select(folder => new HistoryChange { Kind = HistoryChangeKind.Created, Path = folder, IsDirectory = true })
                .ToList();

            foreach (var item in items.Where(item => !item.IsDirectory))
            {
                if (store.Capture(item.Path.Value) is not { } state)
                    continue;

                store.Learn(item.Path.Value, state);
                changes.Add(new HistoryChange
                {
                    Kind = HistoryChangeKind.Created,
                    Path = item.Path.Value,
                    After = state.Content,
                    TooLarge = state.TooLarge,
                });
            }

            if (changes.Count > 0)
            {
                store.Record(label, HistoryOrigin.Studio, changes);
                store.Flush();
            }
        }

        return Done(new FilesChangedEventArgs([], [], [], [.. items.Select(item => item.Path)]));
    }

    /// <summary>Каталоги, которые заведёт создание: просимые и недостающие на пути — от мелких к глубоким.</summary>
    private static List<string> Folders(IReadOnlyList<FileCreation> items)
    {
        var folders = new HashSet<CanonicalPath>();

        foreach (var item in items)
        {
            if (item.IsDirectory)
                folders.Add(item.Path);

            for (var parent = item.Path.Directory; !parent.IsEmpty && !Directory.Exists(parent.Value); parent = parent.Directory)
                folders.Add(parent);
        }

        return [.. folders.Select(folder => folder.Value).OrderBy(folder => folder.Length)];
    }

    /// <summary>Делает правку.</summary>
    /// <param name="work">Просьба.</param>
    /// <param name="snapshot">Снимок открытого решения.</param>
    /// <param name="store">История; null — не ведётся.</param>
    /// <param name="words">Слова отказов.</param>
    public static FileWorkResult Run(FileWork work, SolutionSnapshot snapshot, LocalHistoryStore? store, FileWords words)
    {
        work = work.Normalize();

        if (FileChecks.Check(work, snapshot, words) is { } refused)
            return new FileWorkResult(ProjectOperationResult.Failed(refused), null);

        return work.Kind switch
        {
            FileWorkKind.Move => Move(work, snapshot, store, words),
            FileWorkKind.Copy => Copy(work, store, words),
            _ => Delete(work, snapshot, store, words),
        };
    }

    private static FileWorkResult Move(FileWork work, SolutionSnapshot snapshot, LocalHistoryStore? store, FileWords words)
    {
        var folders = work.Pairs.ToDictionary(pair => pair, pair => Directory.Exists(pair.From.Value));

        // Что история знает о переезжающем — до переезда: после него под старым именем пусто.
        var known = store is null ? [] : Known(store, work.Pairs.Select(pair => (pair.From.Value, folders[pair])));
        var done = new List<FileMove>();
        List<Aside> aside;

        try
        {
            aside = SetAside(work.Pairs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(words, e);
        }

        try
        {
            foreach (var pair in work.Pairs)
            {
                Relocate(pair.From.Value, pair.To.Value, folders[pair]);
                done.Add(pair);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Undo(done, folders);
            Restore(aside);
            return Failed(words, e);
        }

        var changes = work.Pairs.Select(pair => new PathChange(pair.From.Value, pair.To.Value, folders[pair])).ToList();

        if (!Rewrite(snapshot, changes, store, out var projects, out var failure))
        {
            Undo(done, folders);
            Restore(aside);
            return Failed(words, failure!);
        }

        var replaced = Keep(store, aside);

        if (store is not null)
        {
            Rekey(store, known, path => Relocated(path, changes));

            // Чего история не знала, снимается на новом месте сейчас: иначе пачка наблюдателя, пришедшая
            // следом, записала бы переехавший файл появившимся.
            foreach (var pair in work.Pairs)
            {
                foreach (var file in folders[pair] ? Under(pair.To.Value) : [pair.To.Value])
                {
                    if (store.Known(file) is null
                        && CanonicalPath.TryCreate(file, out var canonical)
                        && HistoryFilter.IsTracked(snapshot, canonical)
                        && store.Capture(file) is { } state)
                    {
                        store.Learn(file, state);
                    }
                }
            }

            store.Record(work.Label, HistoryOrigin.Studio,
            [
                .. replaced.Select(item => new HistoryChange
                {
                    Kind = HistoryChangeKind.Deleted,
                    Path = item.Key,
                    Before = item.Value.Content,
                    TooLarge = item.Value.TooLarge,
                }),
                .. work.Pairs.Select(pair => new HistoryChange
                {
                    Kind = HistoryChangeKind.Moved,
                    Path = pair.To.Value,
                    From = pair.From.Value,
                    IsDirectory = folders[pair],
                }),
                .. projects,
            ]);

            store.Flush();
        }

        return Done(new FilesChangedEventArgs(work.Pairs, [], []));
    }

    private static FileWorkResult Copy(FileWork work, LocalHistoryStore? store, FileWords words)
    {
        var created = new List<string>();
        List<Aside> aside;

        try
        {
            aside = SetAside(work.Pairs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(words, e);
        }

        try
        {
            foreach (var (from, to) in work.Pairs)
            {
                if (Directory.Exists(from.Value))
                {
                    Tree(from.Value, to.Value, created);
                }
                else
                {
                    File.Copy(from.Value, to.Value);
                    created.Add(to.Value);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Созданное снимается от последнего к первому: сперва файлы, потом их папки.
            for (var at = created.Count - 1; at >= 0; at--)
                Quietly(() => Erase(created[at]));

            Restore(aside);
            return Failed(words, e);
        }

        var replaced = Keep(store, aside);

        if (store is not null)
        {
            var changes = new List<HistoryChange>();

            foreach (var file in created.Where(File.Exists))
            {
                if (store.Capture(file) is not { } state)
                    continue;

                store.Learn(file, state);

                // Заменённый файл — правка, а не появление: у него было прежнее содержимое.
                changes.Add(replaced.TryGetValue(file, out var before)
                    ? new HistoryChange
                    {
                        Kind = HistoryChangeKind.Modified,
                        Path = file,
                        Before = before.Content,
                        After = state.Content,
                        TooLarge = state.TooLarge,
                    }
                    : new HistoryChange
                    {
                        Kind = HistoryChangeKind.Created,
                        Path = file,
                        After = state.Content,
                        TooLarge = state.TooLarge,
                    });
            }

            foreach (var folder in created.Where(Directory.Exists))
                changes.Add(new HistoryChange { Kind = HistoryChangeKind.Created, Path = folder, IsDirectory = true });

            if (changes.Count > 0)
            {
                store.Record(work.Label, HistoryOrigin.Studio, changes);
                store.Flush();
            }
        }

        return Done(new FilesChangedEventArgs([], work.Pairs, []));
    }

    private static FileWorkResult Delete(FileWork work, SolutionSnapshot snapshot, LocalHistoryStore? store, FileWords words)
    {
        var folders = work.Paths.ToDictionary(path => path, path => Directory.Exists(path.Value));

        // До первого удаления: удалённое вернуть можно только из истории.
        var captured = new List<(string Path, HistoryFileState State)>();

        if (store is not null)
        {
            foreach (var path in work.Paths)
            {
                foreach (var file in folders[path] ? Under(path.Value) : [path.Value])
                {
                    if (!CanonicalPath.TryCreate(file, out var canonical) || !HistoryFilter.IsTracked(snapshot, canonical))
                        continue;

                    if (State(store, file) is { } state)
                        captured.Add((file, state));
                }
            }
        }

        var deleted = new List<CanonicalPath>();
        Exception? refusal = null;

        foreach (var path in work.Paths)
        {
            try
            {
                Erase(path.Value);
                deleted.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                refusal = e;
                break;
            }
        }

        var changes = deleted.Select(path => new PathChange(path.Value, null, folders[path])).ToList();
        var rewritten = Rewrite(snapshot, changes, store, out var projects, out var failure);

        if (store is not null && deleted.Count > 0)
        {
            var gone = new List<HistoryChange>();

            foreach (var (file, state) in captured.Where(item => !File.Exists(item.Path)))
            {
                store.Forget(file);
                gone.Add(new HistoryChange
                {
                    Kind = HistoryChangeKind.Deleted,
                    Path = file,
                    Before = state.Content,
                    TooLarge = state.TooLarge,
                });
            }

            foreach (var folder in deleted.Where(path => folders[path]))
            {
                foreach (var (file, _) in store.KnownUnder(folder.Value))
                    store.Forget(file);

                gone.Add(new HistoryChange { Kind = HistoryChangeKind.Deleted, Path = folder.Value, IsDirectory = true });
            }

            gone.AddRange(projects);
            store.Record(work.Label, HistoryOrigin.Studio, gone);
            store.Flush();
        }

        var change = deleted.Count > 0 ? new FilesChangedEventArgs([], [], [.. deleted]) : null;

        if (refusal is not null)
            return Failed(words, refusal) with { Change = change };

        return rewritten
            ? new FileWorkResult(ProjectOperationResult.Succeeded(), change)
            : Failed(words, failure!) with { Change = change };
    }

    /// <summary>
    /// Переписывает ссылки в файлах проектов; не записавшийся файл возвращает все записанные.
    /// </summary>
    /// <param name="snapshot">Снимок.</param>
    /// <param name="changes">Что куда переехало и что удалено.</param>
    /// <param name="store">История: правка файла проекта — правка, как всякая другая.</param>
    /// <param name="projects">Правки файлов проектов для истории.</param>
    /// <param name="failure">Почему не записалось.</param>
    /// <returns><c>false</c> — диск отказал, и файлы проектов возвращены как были.</returns>
    internal static bool Rewrite(
        SolutionSnapshot snapshot,
        IReadOnlyList<PathChange> changes,
        LocalHistoryStore? store,
        out List<HistoryChange> projects,
        out Exception? failure)
    {
        projects = [];
        failure = null;

        if (changes.Count == 0)
            return true;

        var edits = new List<(ProjectFileEdit Edit, HistoryFileState? Before)>();

        foreach (var project in snapshot.Projects.DistinctBy(project => project.ProjectFilePath))
        {
            var directory = project.ProjectDirectory.Value;

            // Уехавшее из проекта в чужой — для этого проекта удалено: оставь ссылку переписанной, и
            // проект взял бы чужой файл к себе.
            var own = changes
                .Select(change => change.To is { } to && PathChange.Inside(change.From, directory) && !PathChange.Inside(to, directory)
                    ? change with { To = null }
                    : change)
                .ToList();

            if (ProjectFileEdit.Open(project.ProjectFilePath.Value) is not { } edit
                || !ItemReferences.Rewrite(edit.Document, directory, own))
            {
                continue;
            }

            edits.Add((edit, store is null ? null : State(store, edit.Path)));
        }

        var saved = new List<ProjectFileEdit>();

        try
        {
            foreach (var (edit, _) in edits)
            {
                edit.Save();
                saved.Add(edit);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var edit in saved)
                Quietly(edit.Restore);

            failure = e;
            return false;
        }

        if (store is null)
            return true;

        foreach (var (edit, before) in edits)
        {
            if (store.Capture(edit.Path) is not { } after)
                continue;

            store.Learn(edit.Path, after);
            projects.Add(new HistoryChange
            {
                Kind = HistoryChangeKind.Modified,
                Path = edit.Path,
                Before = before?.Content,
                After = after.Content,
            });
        }

        return true;
    }

    /// <summary>Что история знает о переезжающем: файл — одна запись, папка — всё под ней.</summary>
    private static List<(string Path, HistoryFileState State)> Known(
        LocalHistoryStore store,
        IEnumerable<(string Path, bool Folder)> paths)
    {
        var known = new List<(string Path, HistoryFileState State)>();

        foreach (var (path, folder) in paths)
        {
            if (folder)
                known.AddRange(store.KnownUnder(path).Select(pair => (pair.Key, pair.Value)));
            else if (store.Known(path) is { } state)
                known.Add((path, state));
        }

        return known;
    }

    /// <summary>Состояние файла сейчас: известное, если файл тот же, иначе снятое заново.</summary>
    private static HistoryFileState? State(LocalHistoryStore store, string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
            return null;

        return store.Known(path) is { } known && known.Looks(info.Length, info.LastWriteTimeUtc)
            ? known
            : store.Capture(path);
    }

    /// <summary>Куда уехал путь после переносов; удаления его не трогают.</summary>
    private static string Relocated(string path, IReadOnlyList<PathChange> changes) =>
        PathChange.Map(path, changes.Where(change => change.To is not null)) is (true, { } to) ? to : path;

    /// <summary>
    /// Откладывает файлы, которые просят заменить, под временное имя: пока правка не прошла, их можно
    /// вернуть, а стёртое на месте уже не вернуть ничем.
    /// </summary>
    /// <remarks>
    /// Имя кончается на <c>.tmp</c> — его не видят ни слежение за составом, ни локальная история. Не
    /// отложился один — возвращаются отложенные раньше, и правка отказывает до первого переноса.
    /// </remarks>
    private static List<Aside> SetAside(IEnumerable<FileMove> pairs)
    {
        var aside = new List<Aside>();

        try
        {
            foreach (var pair in pairs.Where(pair => pair.Replace && File.Exists(pair.To.Value)))
            {
                var temporary = Temporary(pair.To.Value);

                File.Move(pair.To.Value, temporary);
                aside.Add(new Aside(pair.To.Value, temporary));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Restore(aside);
            throw;
        }

        return aside;
    }

    /// <summary>
    /// Возвращает отложенное на место — если место свободно: занятое значит, что откат не вернул
    /// новое, и стереть его значило бы потерять единственную его копию.
    /// </summary>
    private static void Restore(List<Aside> aside)
    {
        for (var at = aside.Count - 1; at >= 0; at--)
        {
            var item = aside[at];

            if (!File.Exists(item.Target))
                Quietly(() => File.Move(item.Temporary, item.Target));
        }
    }

    /// <summary>
    /// Правка прошла: прежнее содержимое заменённых — в историю, а отложенные файлы — прочь.
    /// </summary>
    /// <returns>Что было на месте каждого заменённого; без истории — пусто.</returns>
    private static Dictionary<string, HistoryFileState> Keep(LocalHistoryStore? store, List<Aside> aside)
    {
        var replaced = new Dictionary<string, HistoryFileState>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in aside)
        {
            if (store is not null)
            {
                var info = new FileInfo(item.Temporary);
                var state = store.Known(item.Target) is { } known && known.Looks(info.Length, info.LastWriteTimeUtc)
                    ? known
                    : store.Capture(item.Temporary);

                // Прежнее состояние места забывается: на нём теперь другой файл, и узнавать его
                // будут заново.
                store.Forget(item.Target);

                if (state is not null)
                    replaced[item.Target] = state;
            }

            Quietly(() => Erase(item.Temporary));
        }

        return replaced;
    }

    /// <summary>
    /// Переносит путь; смену одного регистра .NET делает сам — и у файла, и у папки, — и
    /// временного имени ей не нужно.
    /// </summary>
    internal static void Relocate(string from, string to, bool folder)
    {
        if (folder)
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    /// <summary>Возвращает сделанные переносы в обратном порядке; вернуть можно не всё — возвращаем, что можем.</summary>
    private static void Undo(List<FileMove> done, Dictionary<FileMove, bool> folders)
    {
        for (var at = done.Count - 1; at >= 0; at--)
        {
            var pair = done[at];

            Quietly(() => Relocate(pair.To.Value, pair.From.Value, folders[pair]));
        }
    }

    /// <summary>Копирует папку целиком и помнит всё созданное — папки раньше их файлов.</summary>
    private static void Tree(string from, string to, List<string> created)
    {
        Directory.CreateDirectory(to);
        created.Add(to);

        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var target = Path.Combine(to, Path.GetFileName(entry));

            if (Directory.Exists(entry))
            {
                Tree(entry, target, created);
            }
            else
            {
                File.Copy(entry, target);
                created.Add(target);
            }
        }
    }

    /// <summary>Удаляет насовсем, сняв «только чтение»: его ставят системы контроля версий, а спрашивали уже.</summary>
    internal static void Erase(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    /// <summary>Файлы под папкой на любой глубине.</summary>
    internal static IEnumerable<string> Under(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Временное имя рядом с файлом: <c>.arxis-…tmp</c>.
    /// </summary>
    /// <remarks>
    /// Окончание — часть правила, а не вкус: файлы <c>.tmp</c> не видят ни слежение за составом, ни
    /// локальная история, и промежуточная запись не становится ни элементом проекта, ни действием.
    /// Имя строилось двумя копиями — у правки файлов и у отмены.
    /// </remarks>
    /// <param name="path">Файл, рядом с которым нужно временное имя.</param>
    internal static string Temporary(string path) => $"{path}.arxis-{Guid.NewGuid():N}.tmp";

    /// <summary>Пересаживает известные истории состояния на новые имена: переезд байт не меняет.</summary>
    /// <param name="store">История.</param>
    /// <param name="known">Состояния под прежними именами.</param>
    /// <param name="moved">Новое имя по прежнему.</param>
    internal static void Rekey(LocalHistoryStore store, IEnumerable<(string Path, HistoryFileState State)> known, Func<string, string> moved)
    {
        foreach (var (path, state) in known)
        {
            store.Forget(path);
            store.Learn(moved(path), state);
        }
    }

    internal static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Откат — лучшее, что можно сделать; отказ отката человек увидит в самом итоге правки.
        }
    }

    /// <summary>Файл, отложенный под временное имя, пока на его место встаёт новый.</summary>
    /// <param name="Target">Где он лежал.</param>
    /// <param name="Temporary">Где он лежит сейчас.</param>
    private sealed record Aside(string Target, string Temporary);

    private static FileWorkResult Done(FilesChangedEventArgs change) =>
        new(ProjectOperationResult.Succeeded(), change);

    private static FileWorkResult Failed(FileWords words, Exception error) =>
        Refusals.Work(ProjectsDiagnosticCodes.FileOperationFailed, words.Failed(error.Message));
}
