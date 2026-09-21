using System.Collections.Immutable;
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
/// <b>Откат.</b> Перенос и копия откатываются целиком: сделанное возвращается в обратном порядке,
/// если диск отказал посередине или не записался файл проекта. Удаление не откатывается — удалённого
/// не вернуть, — и поэтому до первого удаления содержимое снимается в историю, а отказ посередине
/// записывает в неё то, что успело уйти.
/// </para>
/// </remarks>
internal static class FileWorker
{
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
            return Failed(words, e);
        }

        var changes = work.Pairs.Select(pair => new PathChange(pair.From.Value, pair.To.Value, folders[pair])).ToList();

        if (!Rewrite(snapshot, changes, store, out var projects, out var failure))
        {
            Undo(done, folders);
            return Failed(words, failure!);
        }

        if (store is not null)
        {
            foreach (var (path, state) in known)
            {
                store.Forget(path);
                store.Learn(Relocated(path, changes), state);
            }

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

            return Failed(words, e);
        }

        if (store is not null)
        {
            var changes = new List<HistoryChange>();

            foreach (var file in created.Where(File.Exists))
            {
                if (store.Capture(file) is not { } state)
                    continue;

                store.Learn(file, state);
                changes.Add(new HistoryChange
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
    private static bool Rewrite(
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
                .Select(change => change.To is { } to && Inside(change.From, directory) && !Inside(to, directory)
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

    /// <summary>Куда уехал путь после переносов.</summary>
    private static string Relocated(string path, IReadOnlyList<PathChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.To is not { } to)
                continue;

            if (string.Equals(path, change.From, StringComparison.OrdinalIgnoreCase))
                return to;

            if (change.IsDirectory && Inside(path, change.From))
                return to + path[change.From.Length..];
        }

        return path;
    }

    /// <summary>
    /// Переносит путь; смену одного регистра .NET делает сам — и у файла, и у папки, — и
    /// временного имени ей не нужно.
    /// </summary>
    private static void Relocate(string from, string to, bool folder)
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
    private static void Erase(string path)
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
    private static IEnumerable<string> Under(string folder)
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

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void Quietly(Action action)
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

    private static FileWorkResult Done(FilesChangedEventArgs change) =>
        new(ProjectOperationResult.Succeeded(), change);

    private static FileWorkResult Failed(FileWords words, Exception error) =>
        new(ProjectOperationResult.Failed(new ProjectDiagnostic(
            ProjectsDiagnosticCodes.FileOperationFailed, words.Failed(error.Message), ProjectDiagnosticSeverity.Error)), null);
}
