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
/// <param name="newItems">Служба создания студии; пусто — её нет, и создавать нечем.</param>
internal sealed class Editing(
    IStudioContext context,
    IStudioFiles files,
    IStudioHistory? history,
    Func<Window?> owner,
    ISystemFiles system,
    IStudioNewItems? newItems = null)
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
        return await Exclusive(async window =>
        {
            var clip = await Buffered().ConfigureAwait(true);

            if (clip is null)
            {
                context.Tell(context.Strings.Format("project.paste.nothing"));
                return null;
            }

            if (clip.Items.FirstOrDefault(item => Pasting.IntoItself(item, folder)) is { } inside)
            {
                await FailureDialog.ShowAsync(window, context.Strings.Format("project.paste.intoItself", inside.Root.FileName)).ConfigureAwait(true);
                return null;
            }

            var cut = clip.Mode == ClipMode.Cut;
            var roots = await PlaceAsync(window, clip, folder, cut ? "project.paste.moved" : "project.paste.copied").ConfigureAwait(true);

            // Вырезанное вставляется один раз: после переноса по старым путям ничего нет.
            if (roots is not null && cut)
            {
                Take(null);
                await Forget(clip).ConfigureAwait(true);
            }

            return roots;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Копирует в каталог то, что принесли из проводника: занятое имя спрашивает, как вставка.
    /// </summary>
    /// <param name="items">Принесённое.</param>
    /// <param name="folder">Каталог назначения.</param>
    /// <returns>Где теперь лежат скопированные корни; пусто — копирования не было.</returns>
    /// <remarks>
    /// Буфер правки сброс не трогает: вырезанное остаётся вырезанным, а буфер системы — чем был.
    /// Каталог, который несут в него самого, окно отвергает ещё на подлёте, и здесь такой сброс
    /// молча ничего не делает.
    /// </remarks>
    public async Task<IReadOnlyList<CanonicalPath>?> DropAsync(IReadOnlyList<ClipItem> items, CanonicalPath folder)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!Dropping.Fits(items, folder))
            return null;

        return await Exclusive(window => PlaceAsync(window, new FileClip(ClipMode.Copy, items), folder, "project.drop.added")).ConfigureAwait(true);
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

        if (selection.IsEmpty)
            return false;

        return await Exclusive(async window =>
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
            var label = more == 0 ? context.Strings.Format("project.delete.label", first) : context.Strings.Format("project.delete.label.many", first, more);

            if (await Run(window, () => files.DeleteAsync(selection.Paths, label)) is not { HasErrors: false })
                return false;

            context.Tell(more == 0 ? context.Strings.Format("project.deleted", first) : context.Strings.Format("project.deleted.many", first, more));

            return true;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Спрашивает новое имя и переименовывает узел вместе с вложенными.
    /// </summary>
    /// <param name="node">Файл или папка.</param>
    /// <returns>Новый путь узла; пусто — переименования не было.</returns>
    public async Task<CanonicalPath?> RenameAsync(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!EditSelection.IsEditable(node))
            return null;

        return await Exclusive<CanonicalPath?>(async window =>
        {
            if (await RenameDialog.AskAsync(window, node, context.Strings, Exists) is not { } name)
                return null;

            var moves = Renaming.Plan(node, name)
                .Select(step => new FileMove(step.Node.Path, step.Node.Path.Directory.Combine(step.Name)))
                .ToList();

            if (await Run(window, () => files.MoveAsync(moves, context.Strings.Format("project.rename.label", node.Name))) is not { HasErrors: false })
                return null;

            context.Tell(context.Strings.Format("project.renamed", node.Name, name));

            return moves[0].To;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Спрашивает имя и создаёт по пункту «Добавить ▸»: собирает службой создания, кладёт службой файлов.
    /// </summary>
    /// <param name="item">Пункт.</param>
    /// <param name="folder">Каталог, на котором позвали меню.</param>
    /// <param name="project">Проект этого каталога — имя и пространство имён для шаблона; пусто — не известен.</param>
    /// <returns>Созданное — на что встать и что открыть; пусто — создания не было.</returns>
    /// <remarks>
    /// <para>
    /// Путь, набранный в имени, окно раскладывает само: каталоги по дороге — цели, последнее — имени.
    /// Пространство имён считается от каталога, куда ляжет пункт, а не от того, где позвали меню:
    /// <c>Models/Person</c> — это класс в <c>…Models</c>.
    /// </para>
    /// <para>
    /// Отказ пункта — диалогом с причиной, как отказ службы файлов; «передумал» в окне расширения —
    /// молча. Создание идёт одним действием истории с меткой «Создание имя», и Ctrl+Z уносит его целиком.
    /// </para>
    /// </remarks>
    public async Task<Created?> CreateAsync(StudioNewItem item, CanonicalPath folder, ProjectSnapshot? project)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (newItems is null)
            return null;

        return await Exclusive(async window =>
        {
            var answer = await CreateDialog.AskAsync(
                window,
                item,
                newItems.Suggest(item, folder.Value),
                context.Strings,
                (typed, variant) => Adding.Check(typed, item, folder, name => newItems.Paths(item, name, variant), File.Exists, Exists));

            if (answer is null)
                return null;

            var typed = Adding.Split(answer.Typed);
            var target = typed.In(folder);
            var root = project is null ? null : Adding.RootNamespace(project);

            var request = new NewItemRequest(typed.Name, target.Value)
            {
                Variant = answer.Variant,
                ProjectFile = project?.ProjectFilePath.Value,
                Project = project?.Name,
                RootNamespace = root,
                Namespace = project is null || root is null ? null : Adding.Namespace(root, project.ProjectDirectory, target),
            };

            NewItemResult made;

            try
            {
                made = await newItems.MakeAsync(item, request);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                context.Log.Write(StudioLogLevel.Error, ProjectModule.LogSource, $"Пункт «{item.Title}» не собрался: {e.Message}");
                await FailureDialog.ShowAsync(window, e.Message);

                return null;
            }

            if (made.Error is { } error)
            {
                await FailureDialog.ShowAsync(window, error);
                return null;
            }

            if (made.Files.Count == 0)
                return null;

            var creations = made.Files
                .Select(file => new FileCreation(target.Combine(file.Path)) { Content = file.Content, IsDirectory = file.IsDirectory })
                .ToList();

            if (await Run(window, () => files.CreateAsync(creations, context.Strings.Format("project.add.label", typed.Name))) is not { HasErrors: false })
                return null;

            return new Created(
                typed.Name,
                creations[0].Path,
                creations[0].IsDirectory,
                [.. made.Files.Zip(creations).Where(pair => pair.First.Open && !pair.First.IsDirectory).Select(pair => pair.Second.Path)]);
        }).ConfigureAwait(true);
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
        if (history is null)
            return null;

        return await Exclusive(async window =>
        {
            if (!history.IsOn)
            {
                context.Tell(context.Strings.Format("project.undo.off"));
                return null;
            }

            if (history.LastStudioAction is not { } last)
            {
                context.Tell(context.Strings.Format("project.undo.nothing"));
                return null;
            }

            if (!await UndoDialog.AskAsync(window, context.Strings, last.Label))
                return null;

            if (await Run(window, () => history.UndoAsync(last.Id), context.Strings["project.undo.partial"]) is not { HasErrors: false })
                return null;

            context.Tell(context.Strings.Format("project.undone", last.Label));

            return last;
        }).ConfigureAwait(true);
    }

    /// <summary>Ставит метку в локальной истории — спросив её текст.</summary>
    /// <returns>Поставлена ли метка.</returns>
    public async Task<bool> LabelAsync()
    {
        if (history is null)
            return false;

        return await Exclusive(async window =>
        {
            if (await LabelDialog.AskAsync(window).ConfigureAwait(true) is not { } text)
                return false;

            if (await Run(window, () => history.PutLabelAsync(text)).ConfigureAwait(true) is not { HasErrors: false })
                return false;

            context.Tell(context.Strings.Format("project.history.labelled", text));

            return true;
        }).ConfigureAwait(true);
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
            [var one] => strings.Format("project.delete.large", one, megabytes),
            _ => strings.Format("project.delete.large.many", large.Count, megabytes),
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

        if (selection.Roots is [var root])
        {
            if (root.Kind == NodeKind.Folder)
            {
                return count(root.Path) switch
                {
                    null => strings.Format("project.delete.folder", root.Name),
                    > CountLimit => strings.Format("project.delete.folder.count", root.Name, $"{CountLimit}+"),
                    var files => strings.Format("project.delete.folder.count", root.Name, files),
                };
            }

            return EditSelection.Nested(root) switch
            {
                [] => strings.Format("project.delete.file", root.Name),
                [var nested] => strings.Format("project.delete.file.nested", root.Name, nested.Name),
                var nested => strings.Format("project.delete.file.nestedMany", root.Name, nested.Count),
            };
        }

        return (selection.Files, selection.Folders) switch
        {
            (var files, 0) => strings.Format("project.delete.files", files),
            (0, var folders) => strings.Format("project.delete.folders", folders),
            var (files, folders) => strings.Format("project.delete.mixed", files, folders),
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
    /// Переносит или копирует в каталог то, что несли мышью внутри окна: занятое имя спрашивает, как
    /// вставка.
    /// </summary>
    /// <param name="clip">Несомое: вырезанным — перенос, скопированным — копия.</param>
    /// <param name="folder">Каталог назначения.</param>
    /// <returns>Где теперь лежат корни; пусто — ничего не сдвинулось.</returns>
    /// <remarks>
    /// Перенос — та же правка, что вырезать и вставить: ссылки файлов проекта едут следом, и отменяет
    /// её Ctrl+Z. Вырезанное, которое унесли мышью, по старым путям больше не лежит, и буфер правки с
    /// ним кончается, как после вставки.
    /// </remarks>
    public async Task<IReadOnlyList<CanonicalPath>?> CarryAsync(FileClip clip, CanonicalPath folder)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (!Dropping.Fits(clip.Items, folder, clip.Mode))
            return null;

        return await Exclusive(async window =>
        {
            var moved = clip.Mode == ClipMode.Cut;
            var roots = await PlaceAsync(window, clip, folder, moved ? "project.paste.moved" : "project.drop.copied").ConfigureAwait(true);

            if (roots is not null && moved && Clip is { Mode: ClipMode.Cut } cut && cut.Paths.Intersect(clip.Paths).Any())
            {
                Take(null);
                await Forget(cut).ConfigureAwait(true);
            }

            return roots;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Кладёт в каталог вставку или сброс: раскладка с вопросом о занятом, служба файлов одним
    /// действием истории и строка состояния.
    /// </summary>
    /// <param name="window">Окно для вопросов.</param>
    /// <param name="clip">Что кладётся: вырезанное переносится, прочее копируется.</param>
    /// <param name="folder">Каталог назначения.</param>
    /// <param name="said">
    /// Ключ строки состояния; метка действия — тот же ключ с <c>.label</c>, а у нескольких корней к
    /// обоим добавляется <c>.many</c>.
    /// </param>
    /// <returns>Где теперь лежат корни; пусто — человек бросил или служба отказала.</returns>
    private async Task<IReadOnlyList<CanonicalPath>?> PlaceAsync(Window window, FileClip clip, CanonicalPath folder, string said)
    {
        if (await Plan(window, clip, folder).ConfigureAwait(true) is not { Pairs.Count: > 0 } plan)
            return null;

        var first = plan.Roots[0].FileName;
        var more = plan.Roots.Count - 1;
        var many = more == 0 ? string.Empty : ".many";
        var label = context.Strings.Format(said + ".label" + many, first, more);

        var result = await Run(window, () => clip.Mode == ClipMode.Cut ? files.MoveAsync(plan.Pairs, label) : files.CopyAsync(plan.Pairs, label))
            .ConfigureAwait(true);

        if (result is not { HasErrors: false })
            return null;

        context.Tell(context.Strings.Format(said + many, first, more));

        return plan.Roots;
    }

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
        context.Tell(context.Strings.Format((mode == ClipMode.Cut ? "project.clip.cut" : "project.clip.copied") + (more == 0 ? string.Empty : ".many"), first, more));

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

    /// <summary>Что создано: как назвать, на что встать и что открыть.</summary>
    /// <param name="Name">Имя, как его набрали, — для строки состояния.</param>
    /// <param name="First">Первое созданное — на него встаёт выделение.</param>
    /// <param name="IsDirectory">Первое созданное — каталог: файлом проекта он не бывает.</param>
    /// <param name="Open">Файлы, которые пункт просил открыть.</param>
    internal sealed record Created(string Name, CanonicalPath First, bool IsDirectory, IReadOnlyList<CanonicalPath> Open);

    /// <summary>Разложенная вставка.</summary>
    /// <param name="Pairs">Что куда.</param>
    /// <param name="Roots">Где теперь лежат корни — на них встанет выделение.</param>
    private sealed record PastePlan(IReadOnlyList<FileMove> Pairs, IReadOnlyList<CanonicalPath> Roots);

    /// <summary>
    /// Делает правку, если её можно начать: другая не идёт, и спросить человека есть где.
    /// </summary>
    /// <param name="work">Правка — с окном, которому принадлежат её вопросы.</param>
    /// <returns>Итог правки; не начатая — значение по умолчанию, то есть «ничего не сделано».</returns>
    /// <remarks>
    /// Правка идёт одна: выбор под второй могла увести первая. Занятость снимается и тогда, когда
    /// правка бросила, — иначе окно перестало бы править до перезапуска.
    /// </remarks>
    private async Task<T?> Exclusive<T>(Func<Window, Task<T?>> work)
    {
        if (_busy || owner() is not { } window)
            return default;

        _busy = true;

        try
        {
            return await work(window).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Зовёт службу и говорит об отказе; исключение службы — тоже отказ, а не падение окна.</summary>
    /// <param name="window">Окно для диалога.</param>
    /// <param name="operation">Дело службы.</param>
    /// <param name="partial">Заголовок удачи, вернувшей не всё; пусто — о ней не говорят.</param>
    /// <returns>Итог службы; пусто — служба бросила.</returns>
    private async Task<ProjectOperationResult?> Run(Window window, Func<Task<ProjectOperationResult>> operation, string? partial = null)
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

        await FailureDialog.ReportAsync(window, result, partial).ConfigureAwait(true);

        return result;
    }
}
