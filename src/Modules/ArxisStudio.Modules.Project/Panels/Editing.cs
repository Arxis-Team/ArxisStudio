using System.Globalization;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Правка файлов из окна проекта: спросить человека, сделать службой файлов и сказать, чем кончилось.
/// </summary>
/// <remarks>
/// <para>
/// Диск окно не трогает само: дерево — снимок службы проектов, и правка в обход неё разошлась бы с
/// ним. Служба файлов переписывает ссылки в файлах проектов, перечитывает модель и пишет действие в
/// локальную историю, а окно спрашивает, передаёт и докладывает: удача — строкой состояния, отказ —
/// диалогом с причиной, которую назвала служба.
/// </para>
/// <para>
/// Правка идёт одна: пока служба занята, второе удаление или переименование не начинается — выбор
/// под ним мог уже уехать.
/// </para>
/// <para>
/// <b>Отмена</b> — Ctrl+Z, как в Rider: последнее действие студии над файлами этого решения, с
/// вопросом, что именно отменится. Отменяет служба истории целиком — оба переименованных файла,
/// всю удалённую папку, ссылки в файле проекта, — а без неё отмены нет вовсе.
/// </para>
/// </remarks>
/// <param name="context">Контекст модуля: словари, строка состояния, журнал.</param>
/// <param name="files">Служба файлов.</param>
/// <param name="history">Служба истории; пусто — её нет, и отменять нечем.</param>
/// <param name="owner">Окно, которому принадлежат вопросы; пусто — спросить негде.</param>
/// <param name="system">Буфер обмена системы.</param>
internal sealed class Editing(IStudioContext context, IStudioFiles files, IStudioHistory? history, Func<Window?> owner, ISystemFiles system)
{
    /// <summary>Сколько файлов папки считать для вопроса: дальше — «больше».</summary>
    internal const int CountLimit = 1000;

    private bool _busy;

    /// <summary>Идёт ли правка.</summary>
    public bool IsBusy => _busy;

    /// <summary>Что вырезано или скопировано в окне; пусто — ничего.</summary>
    public FileClip? Clip { get; private set; }

    /// <summary>Буфер правки сменился: вырезанное приглушается и снимает приглушение по нему.</summary>
    public event Action? ClipChanged;

    /// <summary>Вырезает выбранное: вставка его перенесёт.</summary>
    /// <param name="selection">Выбор.</param>
    public Task CutAsync(EditSelection selection) => PutAsync(ClipMode.Cut, selection);

    /// <summary>Копирует выбранное: вставка его скопирует, а проводник вставит файлы.</summary>
    /// <param name="selection">Выбор.</param>
    public Task CopyAsync(EditSelection selection) => PutAsync(ClipMode.Copy, selection);

    /// <summary>Снимает вырезанное — Esc, как в проводнике: приглушение уходит, вставлять нечего.</summary>
    /// <returns>Было ли что снимать.</returns>
    public bool Uncut()
    {
        if (Clip is not { Mode: ClipMode.Cut } clip)
            return false;

        Take(null);
        _ = Forget(clip);

        return true;
    }

    /// <summary>
    /// Вставляет в папку то, что лежит в буфере: своё вырезанное переносит, своё скопированное и
    /// чужие файлы копирует; занятое имя спрашивает.
    /// </summary>
    /// <param name="folder">Папка назначения.</param>
    /// <returns>Где теперь лежат вставленные корни; пусто — вставки не было.</returns>
    public async Task<IReadOnlyList<CanonicalPath>?> PasteAsync(CanonicalPath folder)
    {
        if (_busy || owner() is not { } window)
            return null;

        _busy = true;

        try
        {
            var clip = await Buffered().ConfigureAwait(true);

            if (clip is null)
            {
                Tell(Format("project.paste.nothing"));
                return null;
            }

            if (clip.Items.FirstOrDefault(item => Pasting.IntoItself(item, folder)) is { } inside)
            {
                await FailureDialog.ShowAsync(window, Format("project.paste.intoItself", inside.Root.FileName)).ConfigureAwait(true);
                return null;
            }

            if (await Plan(window, clip, folder).ConfigureAwait(true) is not { Pairs.Count: > 0 } plan)
                return null;

            var first = plan.Roots[0].FileName;
            var more = plan.Roots.Count - 1;
            var cut = clip.Mode == ClipMode.Cut;
            var label = Format(
                (cut ? "project.paste.moved" : "project.paste.copied") + (more == 0 ? ".label" : ".label.many"),
                first,
                more);

            var result = await Run(window, () => cut ? files.MoveAsync(plan.Pairs, label) : files.CopyAsync(plan.Pairs, label))
                .ConfigureAwait(true);

            if (result is not { HasErrors: false })
                return null;

            // Вырезанное вставляется один раз: после переноса по старым путям ничего нет.
            if (cut)
            {
                Take(null);
                await Forget(clip).ConfigureAwait(true);
            }

            Tell(Format((cut ? "project.paste.moved" : "project.paste.copied") + (more == 0 ? string.Empty : ".many"), first, more));

            return plan.Roots;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Спрашивает и удаляет выбранное.
    /// </summary>
    /// <param name="selection">Что удалить.</param>
    /// <returns>
    /// Удалено ли всё: тогда дерево перестроится без удалённого. Отказ посередине — удаление, которое
    /// успело уйти частью; о нём говорит диалог, а выделение остаётся тем, каким его оставит дерево.
    /// </returns>
    public async Task<bool> DeleteAsync(EditSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty || _busy || owner() is not { } window)
            return false;

        _busy = true;

        try
        {
            var kept = history?.IsOn == true;
            var large = kept ? Large(selection, history!.MaxFileBytes) : [];

            if (!await DeleteDialog.AskAsync(
                    window,
                    Question(selection, context.Strings, CountFiles),
                    context.Strings[kept ? "project.delete.note" : "project.delete.note.off"],
                    Warning(large, history?.MaxFileBytes ?? 0, context.Strings)))
            {
                return false;
            }

            var first = selection.Roots[0].Name;
            var more = selection.Roots.Count - 1;
            var label = more == 0 ? Format("project.delete.label", first) : Format("project.delete.label.many", first, more);

            if (await Run(window, () => files.DeleteAsync(selection.Paths, label)) is not { HasErrors: false })
                return false;

            Tell(more == 0 ? Format("project.deleted", first) : Format("project.deleted.many", first, more));

            return true;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Спрашивает новое имя и переименовывает узел вместе с вложенными.
    /// </summary>
    /// <param name="node">Файл или папка.</param>
    /// <returns>Новый путь узла; пусто — переименования не было.</returns>
    public async Task<CanonicalPath?> RenameAsync(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!EditSelection.IsEditable(node) || _busy || owner() is not { } window)
            return null;

        _busy = true;

        try
        {
            if (await RenameDialog.AskAsync(window, node, context.Strings, Exists) is not { } name)
                return null;

            var moves = Renaming.Plan(node, name)
                .Select(step => new FileMove(step.Node.Path, step.Node.Path.Directory.Combine(step.Name)))
                .ToList();

            if (await Run(window, () => files.MoveAsync(moves, Format("project.rename.label", node.Name))) is not { HasErrors: false })
                return null;

            Tell(Format("project.renamed", node.Name, name));

            return moves[0].To;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Отменяет последнее действие студии над файлами — спросив, как Rider.
    /// </summary>
    /// <returns>Отменённое действие; пусто — отмены не было.</returns>
    /// <remarks>
    /// Отказ службы — файл с тех пор изменился, действие уже отменено — говорит диалог ошибки.
    /// Удача, вернувшая не всё, говорит тем же диалогом, что именно не вернулось: файл больше предела
    /// истории, ссылки в файле проекта, правленом с тех пор. Промолчать об этом значило бы оставить
    /// человека думать, что всё на месте.
    /// </remarks>
    public async Task<LocalHistoryAction?> UndoAsync()
    {
        if (history is null || _busy || owner() is not { } window)
            return null;

        _busy = true;

        try
        {
            if (!history.IsOn)
            {
                Tell(Format("project.undo.off"));
                return null;
            }

            if (history.LastStudioAction is not { } last)
            {
                Tell(Format("project.undo.nothing"));
                return null;
            }

            if (!await UndoDialog.AskAsync(window, context.Strings, last.Label))
                return null;

            if (await Run(window, () => history.UndoAsync(last.Id)) is not { HasErrors: false } result)
                return null;

            var skipped = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Warning)
                .Select(diagnostic => diagnostic.Message)
                .ToList();

            if (skipped.Count > 0)
                await FailureDialog.ShowAsync(window, string.Join(Environment.NewLine, skipped), Format("project.undo.partial"));

            Tell(Format("project.undone", last.Label));

            return last;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Файлы больше предела истории среди удаляемого — их удаление не вернёт ничто.
    /// </summary>
    /// <param name="selection">Что удаляют.</param>
    /// <param name="limit">Предел истории, в байтах.</param>
    /// <remarks>В папке смотрится столько же файлов, сколько считает вопрос: ему нужно «есть такие», а не опись.</remarks>
    internal static IReadOnlyList<string> Large(EditSelection selection, long limit)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var large = new List<string>();

        foreach (var path in selection.Paths)
        {
            try
            {
                var files = Directory.Exists(path.Value)
                    ? new DirectoryInfo(path.Value).EnumerateFiles("*", SearchOption.AllDirectories).Take(CountLimit)
                    : [new FileInfo(path.Value)];

                large.AddRange(files.Where(file => file.Exists && file.Length > limit).Select(file => file.Name));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Не прочиталось — промолчим о нём: вопрос и так скажет, что удаление насовсем.
            }
        }

        return large;
    }

    /// <summary>Предупреждение о том, чего история не вернёт; пусто — такого нет.</summary>
    /// <param name="large">Файлы больше предела.</param>
    /// <param name="limit">Предел, в байтах.</param>
    /// <param name="strings">Словари модуля.</param>
    internal static string? Warning(IReadOnlyList<string> large, long limit, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(large);
        ArgumentNullException.ThrowIfNull(strings);

        var megabytes = Math.Max(1, limit / (1024 * 1024));

        return large switch
        {
            [] => null,
            [var one] => string.Format(CultureInfo.CurrentCulture, strings["project.delete.large"], one, megabytes),
            _ => string.Format(CultureInfo.CurrentCulture, strings["project.delete.large.many"], large.Count, megabytes),
        };
    }

    /// <summary>
    /// Вопрос перед удалением: что уйдёт — по имени, сколько — числом.
    /// </summary>
    /// <param name="selection">Что удаляют.</param>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="count">Сколько файлов в папке, самое большее <see cref="CountLimit"/> и ещё один; пусто — не сосчиталось.</param>
    /// <remarks>
    /// Имена, а не формы множественного числа: словари модулей их не знают, а число после двоеточия
    /// читается одинаково при любом числе.
    /// </remarks>
    internal static string Question(EditSelection selection, IStudioStrings strings, Func<CanonicalPath, int?> count)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(count);

        string Say(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, strings[key], values);

        if (selection.Roots is [var root])
        {
            if (root.Kind == NodeKind.Folder)
            {
                return count(root.Path) switch
                {
                    null => Say("project.delete.folder", root.Name),
                    > CountLimit => Say("project.delete.folder.count", root.Name, $"{CountLimit}+"),
                    var files => Say("project.delete.folder.count", root.Name, files),
                };
            }

            return EditSelection.Nested(root) switch
            {
                [] => Say("project.delete.file", root.Name),
                [var nested] => Say("project.delete.file.nested", root.Name, nested.Name),
                var nested => Say("project.delete.file.nestedMany", root.Name, nested.Count),
            };
        }

        return (selection.Files, selection.Folders) switch
        {
            (var files, 0) => Say("project.delete.files", files),
            (0, var folders) => Say("project.delete.folders", folders),
            var (files, folders) => Say("project.delete.mixed", files, folders),
        };
    }

    /// <summary>Сколько файлов в папке на любой глубине — с пределом: вопросу нужно «сколько», а не точная опись.</summary>
    internal static int? CountFiles(CanonicalPath folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder.Value, "*", SearchOption.AllDirectories).Take(CountLimit + 1).Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// Заменить можно файл файлом: у папки и у файла, место которого заняла папка, — нельзя.
    /// </summary>
    private static bool CanReplace(ClipItem item, CanonicalPath folder) =>
        !item.IsFolder && Pasting.Pairs(item, folder, item.Root.FileName)
            .Where(pair => Exists(pair.To.Value))
            .All(pair => File.Exists(pair.To.Value));

    /// <summary>
    /// Раскладывает буфер по папке: своё место, номер при копии туда же, вопрос о занятом.
    /// </summary>
    /// <returns>Пары и корни на новых местах; пусто — человек бросил вставку.</returns>
    private async Task<PastePlan?> Plan(Window window, FileClip clip, CanonicalPath folder)
    {
        var taken = clip.Items.Where(item => !Pasting.SameFolder(item, folder) && Pasting.Taken(item, folder, Exists)).ToList();
        var pairs = new List<FileMove>();
        var roots = new List<CanonicalPath>();
        ConflictAnswer? all = null;
        var asked = 0;

        foreach (var item in clip.Items)
        {
            var name = item.Root.FileName;
            var replace = false;

            if (Pasting.SameFolder(item, folder))
            {
                // Вырезанное, вставленное туда же, остаётся где лежало; скопированное получает номер.
                if (clip.Mode == ClipMode.Cut)
                    continue;

                name = Pasting.Free(item, folder, Exists);
            }
            else if (taken.Contains(item))
            {
                var answer = all ?? await ConflictDialog.AskAsync(
                    window, context.Strings, folder.FileName, name, CanReplace(item, folder), taken.Count - asked - 1).ConfigureAwait(true);

                asked++;

                if (answer.Choice == ConflictChoice.Cancel)
                    return null;

                if (answer.ForAll)
                    all = answer;

                if (answer.Choice == ConflictChoice.Skip)
                    continue;

                // «Заменить все» у папки — оставить обе: слить папки служба не берётся, а потерять
                // вставку человек не просил.
                replace = answer.Choice == ConflictChoice.Replace && CanReplace(item, folder);

                if (!replace)
                    name = Pasting.Free(item, folder, Exists);
            }

            var steps = Pasting.Pairs(item, folder, name);

            pairs.AddRange(replace ? steps.Select(pair => pair with { Replace = Exists(pair.To.Value) }) : steps);
            roots.Add(steps[0].To);
        }

        return new PastePlan(pairs, roots);
    }

    /// <summary>Что вставлять: своё, если оно ещё в буфере системы, иначе чужие файлы.</summary>
    /// <remarks>Буфер системы заняли чужим — своё вырезанное кончилось, и приглушение снимается.</remarks>
    private async Task<FileClip?> Buffered()
    {
        FileClip? clip;

        try
        {
            clip = await system.TakeAsync(Clip).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            context.Log.Write(StudioLogLevel.Warning, ProjectModule.LogSource, $"Буфер обмена не прочитался: {e.Message}");
            clip = Clip;
        }

        if (Clip is not null && !ReferenceEquals(clip, Clip))
            Take(null);

        return clip;
    }

    private async Task PutAsync(ClipMode mode, EditSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty)
            return;

        var clip = FileClip.Of(mode, selection);
        var first = selection.Roots[0].Name;
        var more = selection.Roots.Count - 1;

        Take(clip);
        Tell(Format((mode == ClipMode.Cut ? "project.clip.cut" : "project.clip.copied") + (more == 0 ? string.Empty : ".many"), first, more));

        try
        {
            await system.PutAsync(clip).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Проводник вставить не сможет, а окно — сможет: своё состояние уже стоит.
            context.Log.Write(StudioLogLevel.Warning, ProjectModule.LogSource, $"Файлы не легли в буфер обмена: {e.Message}");
        }
    }

    private async Task Forget(FileClip clip)
    {
        try
        {
            await system.ForgetAsync(clip).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            context.Log.Write(StudioLogLevel.Warning, ProjectModule.LogSource, $"Буфер обмена не очистился: {e.Message}");
        }
    }

    private void Take(FileClip? clip)
    {
        if (ReferenceEquals(Clip, clip))
            return;

        Clip = clip;
        ClipChanged?.Invoke();
    }

    /// <summary>Разложенная вставка.</summary>
    /// <param name="Pairs">Что куда.</param>
    /// <param name="Roots">Где теперь лежат корни — на них встанет выделение.</param>
    private sealed record PastePlan(IReadOnlyList<FileMove> Pairs, IReadOnlyList<CanonicalPath> Roots);

    /// <summary>Зовёт службу и говорит об отказе; исключение службы — тоже отказ, а не падение окна.</summary>
    /// <returns>Итог службы; пусто — служба бросила.</returns>
    private async Task<ProjectOperationResult?> Run(Window window, Func<Task<ProjectOperationResult>> operation)
    {
        ProjectOperationResult result;

        try
        {
            result = await operation().ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            context.Log.Write(StudioLogLevel.Error, ProjectModule.LogSource, $"Правка файлов не удалась: {e.Message}");
            await FailureDialog.ShowAsync(window, e.Message).ConfigureAwait(true);

            return null;
        }

        if (result.HasErrors)
        {
            var reasons = result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message);

            await FailureDialog.ShowAsync(window, string.Join(Environment.NewLine, reasons)).ConfigureAwait(true);
        }

        return result;
    }

    private void Tell(string message) => context.GetService<IStudioStatus>()?.Show(message);

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, context.Strings[key], values);
}
