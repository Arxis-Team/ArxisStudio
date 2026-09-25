using System.Collections.Specialized;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Panels;

// Правка файлов из окна проекта: команды меню и клавиш, куда встать после них и что приглушить.
// Своим файлом, а не классом: командам нужны выбор дерева, колонка и «встать на узел» панели, а
// колонка строится после меню, которому команды нужны раньше неё.
public sealed partial class ProjectPanel
{
    /// <summary>Сколько ждать, пока дерево покажет итог правки, прежде чем встать на него.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Удаляет выбранное — с вопросом — и ставит выделение на соседа удалённого.</summary>
    private void Delete(EditSelection selection, EditOrigin origin) => Guard(DeleteAsync(selection, origin));

    /// <summary>Переименовывает выбранное — с вопросом — и ставит выделение на новое имя.</summary>
    private void Rename(EditSelection selection, EditOrigin origin) => Guard(RenameAsync(selection, origin));

    private async Task DeleteAsync(EditSelection selection, EditOrigin origin)
    {
        if (_editing is not { } editing || _model is not { } model || selection.IsEmpty)
            return;

        // Место удалённого — до удаления: после него строк, по которым его можно найти, уже нет.
        var at = origin == EditOrigin.Pane ? _pane?.FirstSelected() ?? -1 : FirstSelected();
        var container = model.Browser.Current;

        if (!await editing.DeleteAsync(selection))
            return;

        var gone = selection.Paths.ToHashSet();

        if (await model.WhenAsync(root => !root.Descendants().Any(node => gone.Contains(node.Path)), Patience) is null || at < 0)
            return;

        if (origin == EditOrigin.Pane)
            _pane?.SelectAt(Vanished(container, model.Browser.Current) ?? at);
        else if (_model?.Tree.Rows is { Count: > 0 } rows)
            Select(rows[Math.Min(at, rows.Count - 1)]);
    }

    /// <summary>
    /// Колонка ушла вверх, потому что её папка пропала из дерева: место пропавшей среди плиток того,
    /// куда колонка поднялась.
    /// </summary>
    /// <param name="was">Где колонка стояла до удаления — узел прежнего дерева.</param>
    /// <param name="now">Где стоит теперь.</param>
    /// <returns>Место; пусто — колонка осталась, где была.</returns>
    /// <remarks>
    /// Опустевшая папка не пропадает — она стоит на диске, и дерево показывает её пустой. Пропадает
    /// папка, которую убрали мимо окна, пока шло удаление, или чьи файлы проект перестал называть. Тогда
    /// сосед встаёт на место пропавшей папки так же, как на место удалённой плитки: колонка не выделяет
    /// первую попавшуюся, а продолжает там, где человек был.
    /// </remarks>
    private static int? Vanished(Node? was, Node? now)
    {
        if (was is null || now is null || was.Is(now))
            return null;

        return was.Ancestors().Prepend(was)
            .FirstOrDefault(node => node.Parent?.Is(now) == true) is { } gone
            ? Browse.Browser.Place(gone)
            : null;
    }

    private async Task RenameAsync(EditSelection selection, EditOrigin origin)
    {
        if (_editing is not { } editing || !selection.CanRename)
            return;

        if (await editing.RenameAsync(selection.Roots[0]) is { } path)
            await StandOnAsync(path, origin);
    }

    /// <summary>Ставит метку в локальной истории — спросив её текст.</summary>
    private void PutLabel() => Guard(_editing?.LabelAsync() ?? Task.CompletedTask);

    /// <summary>Отменяет последнее действие над файлами — с вопросом — и встаёт на вернувшееся.</summary>
    private void Undo(EditOrigin origin) => Guard(UndoAsync(origin));

    private async Task UndoAsync(EditOrigin origin)
    {
        if (_editing is not { } editing || await editing.UndoAsync() is not { } undone)
            return;

        if (Returned(undone) is { } path)
            await StandOnAsync(path, origin);
    }

    /// <summary>
    /// Что вернула отмена — туда встаёт выделение: переехавшее — на прежнее имя, удалённое — на самое
    /// верхнее из вернувшихся. Отмена копии ничего не возвращает: скопированное просто пропадает.
    /// </summary>
    internal static CanonicalPath? Returned(LocalHistoryAction undone)
    {
        ArgumentNullException.ThrowIfNull(undone);

        if (undone.Changes.FirstOrDefault(change => change.Kind == LocalHistoryChangeKind.Moved) is { From.IsEmpty: false } moved)
            return moved.From;

        return undone.Changes
            .Where(change => change.Kind == LocalHistoryChangeKind.Deleted)
            .OrderBy(change => change.Path.Value.Length)
            .Select(change => (CanonicalPath?)change.Path)
            .FirstOrDefault();
    }

    /// <summary>
    /// Дожидается, пока дерево покажет путь, и встаёт на него там, откуда пришла правка.
    /// </summary>
    /// <remarks>
    /// Правка из дерева встаёт на строку дерева — с клавиатурой, чтобы следующая клавиша пришлась
    /// туда же. Строки нет — в две колонки у файла её нет, — и тогда, как у правки из колонки, узел
    /// выделяется плиткой в своей папке: колонка идёт в неё, где бы ни стояла. Вернувшееся отменой
    /// лежит в папке, которая могла пропасть вместе с удалённым, и колонка тогда поднялась выше.
    /// <para>
    /// Сброс мышью ставит клавиатуру туда, где положенное показано (<paramref name="follow"/>): тяга
    /// уносит с собой плитку, на которой стояла клавиатура, и после переноса на строку дерева в две
    /// колонки она осталась бы нигде — файла в дереве нет, а плитка ушла вместе с колонкой. Каталог
    /// колонка открывает, и своей плитки у него в ней нет: клавиатура тогда уходит в саму колонку.
    /// </para>
    /// </remarks>
    private async Task StandOnAsync(CanonicalPath path, EditOrigin origin, bool follow = false)
    {
        if (_model is not { } model)
            return;

        if (await model.WhenAsync(root => ProjectModel.Find(root, path) is not null, Patience) is not { } root
            || ProjectModel.Find(root, path) is not { } node)
        {
            return;
        }

        if (origin == EditOrigin.Tree)
        {
            model.Tree.Reveal(node);

            if (model.Tree.Find(node.Key) is { } row)
            {
                Select(row);
                return;
            }
        }

        Stand(node);

        if (model.IsTwoColumns && (origin == EditOrigin.Pane || follow) && _pane is { } pane && !pane.Select(node, focus: true))
            pane.Shown.Focus(NavigationMethod.Directional);
    }

    /// <summary>Вырезает выбранное: вставка его перенесёт, а до неё оно приглушено.</summary>
    private void Cut(EditSelection selection, EditOrigin origin) => Guard(_editing?.CutAsync(selection) ?? Task.CompletedTask);

    /// <summary>Копирует выбранное — и в буфер системы, для проводника.</summary>
    private void CopyFiles(EditSelection selection, EditOrigin origin) => Guard(_editing?.CopyAsync(selection) ?? Task.CompletedTask);

    /// <summary>Вставляет в папку и ставит выделение на вставленное.</summary>
    private void Paste(CanonicalPath folder, EditOrigin origin) => Guard(PasteAsync(folder, origin));

    private async Task PasteAsync(CanonicalPath folder, EditOrigin origin)
    {
        if (_editing is { } editing && await editing.PasteAsync(folder) is [var first, ..])
            await StandOnAsync(first, origin);
    }

    /// <summary>Копирует принесённое из проводника в каталог и ставит выделение на скопированное.</summary>
    private void DropFiles(IReadOnlyList<ClipItem> items, CanonicalPath folder, EditOrigin origin) => Guard(DropFilesAsync(items, folder, origin));

    private async Task DropFilesAsync(IReadOnlyList<ClipItem> items, CanonicalPath folder, EditOrigin origin)
    {
        if (_editing is { } editing && await editing.DropAsync(items, folder) is [var first, ..])
            await StandOnAsync(first, origin, follow: true);
    }

    /// <summary>Переносит или копирует несомое мышью внутри окна и ставит выделение на результат.</summary>
    private void CarryFiles(FileClip clip, CanonicalPath folder, EditOrigin origin) => Guard(CarryFilesAsync(clip, folder, origin));

    private async Task CarryFilesAsync(FileClip clip, CanonicalPath folder, EditOrigin origin)
    {
        if (_editing is { } editing && await editing.CarryAsync(clip, folder) is [var first, ..])
            await StandOnAsync(first, origin, follow: true);
    }

    /// <summary>Отмечает каталог, в который ляжет несомое — из проводника или внутри окна; пусто — снимает отметку.</summary>
    private void MarkDrop(CanonicalPath? folder)
    {
        if (_dropFolder == folder)
            return;

        _dropFolder = folder;
        Mark();
    }

    /// <summary>
    /// Пункты «Добавить ▸» для проекта узла: те, чьё условие <c>when</c> проект проходит.
    /// </summary>
    /// <remarks>
    /// Язык и пакеты — из снимка проекта, которому узел принадлежит. Пакеты — объявленные самим
    /// проектом: пункт «для Avalonia» ждёт проекта, который на неё ссылается, а не того, кому она
    /// приехала чужой зависимостью, — так же отбирают шаблоны Rider и Visual Studio. Проекта в снимке
    /// нет — остаются пункты без условий.
    /// </remarks>
    private IReadOnlyList<StudioNewItem> Creatable(IStudioNewItems newItems, Node node)
    {
        var project = ProjectOf(node);
        var packages = project?.PackageReferences.Select(reference => reference.PackageId).ToList() ?? [];

        return [.. newItems.Items.Where(item => item.Fits(project?.Language, packages))];
    }

    /// <summary>Снимок проекта, которому узел принадлежит; пусто — не известен.</summary>
    private ProjectSnapshot? ProjectOf(Node node) =>
        !node.Project.IsEmpty && Context.Projects()?.Current is { } snapshot && snapshot.TryGetProject(node.Project, out var project)
            ? project
            : null;

    /// <summary>Создаёт по пункту «Добавить ▸» в каталоге узла и встаёт на созданное.</summary>
    private void Create(StudioNewItem item, Node node, EditOrigin origin) => Guard(CreateAsync(item, node, origin));

    /// <remarks>
    /// Встать можно только на то, что дерево покажет: файл, которого проект не включает, — классический
    /// проект без масок, — в дереве не появится, и ждать его десять секунд незачем: окно говорит это
    /// строкой и остаётся где было. Каталог дерево показывает с диска, включён он в проект или нет.
    /// Открывается созданное после того, как на него встали: клавиатура уходит в редактор, как у Rider.
    /// <para>
    /// Строка состояния — последней. Открытие говорит своё — «загружаю», «открыть некому», — и
    /// сказанное раньше него заслонилось бы: живая проверка нашла, что после «Заметки» строка
    /// сообщала только, что файл открыть некому, и не говорила, что он создан.
    /// </para>
    /// </remarks>
    private async Task CreateAsync(StudioNewItem item, Node node, EditOrigin origin)
    {
        if (_editing is not { } editing || Pasting.Folder(node) is not { } folder)
            return;

        if (await editing.CreateAsync(item, folder, ProjectOf(node)) is not { } created)
            return;

        var shown = created.IsDirectory || Included(created.First);

        if (shown)
            await StandOnAsync(created.First, origin);

        if (Context.GetService<IStudioDocuments>() is { } documents)
        {
            foreach (var file in created.Open)
                await documents.OpenAsync(file.Value);
        }

        Context.Tell(Context.Strings.Format(shown ? "project.added" : "project.added.outside", created.Name));
    }

    /// <summary>Включает ли файл хоть один проект — по снимку, который служба перечитала, прежде чем вернуться.</summary>
    private bool Included(CanonicalPath path) =>
        Context.Projects()?.Current?.Projects.Any(project => project.Items.Any(item => item.FullPath == path)) == true;

    /// <summary>
    /// Приглушает вырезанное — строки и плитки, чьи пути лежат в буфере правки вырезанными, — и
    /// отмечает каталог, в который ляжет принесённое из проводника.
    /// </summary>
    /// <remarks>
    /// Зовётся на смену буфера и цели, на каждую перемену строк и плиток и на новый снимок: раскрытая
    /// ветка приносит новые строки, и вырезанное в ней должно прийти приглушённым, а каталог
    /// назначения — отмеченным. Крошки пересобираются вместе с плитками или со снимком, и своего
    /// повода им не нужно. Цель отмечена везде, где каталог виден: его строкой, его плиткой, его
    /// сегментом крошек и всей колонкой, если колонка показывает его.
    /// </remarks>
    private void Mark()
    {
        if (_model is not { } model)
            return;

        var cut = _editing?.Clip is { Mode: ClipMode.Cut } clip ? clip.Paths.ToHashSet() : null;
        var drop = _dropFolder;

        foreach (var row in model.Tree.Rows)
        {
            row.IsCut = cut?.Contains(row.Node.Path) == true;
            row.IsDropTarget = drop is { } target && Dropping.Holds(row.Node, target);
        }

        foreach (var tile in model.Browser.Items)
        {
            tile.IsCut = cut?.Contains(tile.Node.Path) == true;
            tile.IsDropTarget = drop is { } target && Dropping.Holds(tile.Node, target);
        }

        foreach (var segment in model.Browser.Segments)
            segment.IsDropTarget = drop is { } target && Dropping.Holds(segment.Node, target);

        model.MarkColumn(drop is { } folder && model.IsBrowsing && model.Browser.Current is { } current && Pasting.Folder(current) == folder);
    }

    private void OnMarksChanged(object? sender, NotifyCollectionChangedEventArgs e) => Mark();

    /// <summary>Место первой выделенной строки дерева; −1 — выделения нет.</summary>
    private int FirstSelected() =>
        _view is { } view && _model is { } model ? ListItems.FirstIndex(view.Tree, model.Tree.Rows) : -1;

    /// <summary>
    /// Дожидается правки, начатой клавишей или пунктом меню: обработчик события ждать не может, а
    /// сбой в ней должен остаться в журнале, а не уйти необработанным в поток интерфейса.
    /// </summary>
    private void Guard(Task work) => _ = work.ContinueWith(
        failed => Context.Log.Write(
            StudioLogLevel.Error, ProjectModule.LogSource, $"Правка файлов оборвалась: {failed.Exception?.GetBaseException().Message}"),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,
        TaskScheduler.Default);
}
