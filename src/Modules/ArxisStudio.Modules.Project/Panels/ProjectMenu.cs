using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>Откуда пришла правка: где потом встать на то, что получилось.</summary>
internal enum EditOrigin
{
    /// <summary>Из дерева.</summary>
    Tree,

    /// <summary>Из правой колонки двух колонок.</summary>
    Pane,
}

/// <summary>Что меню строки умеет делать — действия окна, в которые пункты уходят.</summary>
/// <param name="Open">Открыть файл в редакторе студии.</param>
/// <param name="Reveal">Показать в файловом менеджере.</param>
/// <param name="Copy">Положить текст в буфер обмена.</param>
/// <param name="ExpandBranch">Раскрыть ветку строки целиком.</param>
/// <param name="CollapseBranch">Свернуть ветку строки.</param>
/// <param name="Delete">Удалить выбранное; пусто — службы файлов нет, и правки в меню нет.</param>
/// <param name="Rename">Переименовать выбранное; пусто — так же.</param>
/// <param name="CutFiles">Вырезать выбранное; пусто — так же.</param>
/// <param name="CopyFiles">Скопировать выбранное; пусто — так же.</param>
/// <param name="Paste">Вставить в папку; пусто — так же.</param>
/// <param name="Uncut">Снять вырезанное — Esc; ответ — было ли что снимать.</param>
/// <param name="Undo">Отменить последнее действие над файлами — Ctrl+Z; пусто — службы истории нет.</param>
/// <param name="ShowHistory">Показать локальную историю узла; пусто — службы истории нет.</param>
/// <param name="PutLabel">Поставить метку в локальной истории; пусто — так же.</param>
internal sealed record MenuActions(
    Action<Node> Open,
    Action<string> Reveal,
    Action<string> Copy,
    Action<Row> ExpandBranch,
    Action<Row> CollapseBranch,
    Action<EditSelection, EditOrigin>? Delete = null,
    Action<EditSelection, EditOrigin>? Rename = null,
    Action<EditSelection, EditOrigin>? CutFiles = null,
    Action<EditSelection, EditOrigin>? CopyFiles = null,
    Action<CanonicalPath, EditOrigin>? Paste = null,
    Func<bool>? Uncut = null,
    Action<EditOrigin>? Undo = null,
    Action<Node>? ShowHistory = null,
    Action? PutLabel = null);

/// <summary>
/// Контекстное меню строки дерева и плитки правой колонки: что можно сделать с тем, на чём стоят.
/// </summary>
/// <remarks>
/// Пункты зависят от вида узла. Файл открывают, показывают в проводнике и копируют его путь —
/// полный и от решения, как в Rider; проект и решение открывают своим файлом; у зависимости
/// копируют имя, а путь — только если он у неё есть. Раскрыть и свернуть ветку можно у строки
/// дерева, у которой есть дети; у найденного поиском есть «Показать в папке». У пустого места правой
/// колонки меню — её папки, как у проводника и Unity: то же меню узла, только без выбора.
/// <para>
/// У файлов и папок — «Правка», как в Rider: удалить и переименовать. Правка берёт весь выбор, а не
/// одну строку под мышью, и вложенные файлы идут с владельцем (<see cref="EditSelection"/>). Есть она,
/// только когда у студии есть служба файлов: окно, правящее диск в обход неё, расходилось бы с её
/// снимком. Переименовать можно одно, и при выборе из нескольких пункт выключен, а не спрятан: так
/// видно, почему его нельзя нажать.
/// </para>
/// <para>
/// Меню собирается на каждый показ: пункты зависят от узла, и держать их между показами значило
/// бы пересобирать их на каждый щелчок в дереве.
/// </para>
/// </remarks>
/// <param name="strings">Словари модуля.</param>
/// <param name="actions">Действия окна.</param>
internal sealed class ProjectMenu(IStudioStrings strings, MenuActions actions)
{
    /// <summary>Действия окна — правой колонке, чтобы её клавиши шли теми же дорогами, что пункты.</summary>
    public MenuActions Actions => actions;

    /// <summary>Показывает меню там, где его попросили.</summary>
    /// <param name="anchor">К чему привязать: у мыши — список, у клавиатуры — строка.</param>
    /// <param name="items">Пункты.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем.</param>
    public static void ShowAt(Control anchor, IReadOnlyList<AxMenuItem> items, bool atPointer)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return;

        var flyout = new AxMenuFlyout();

        foreach (var item in items)
            flyout.Items.Add(item);

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>
    /// Собирает пункты меню строки дерева.
    /// </summary>
    /// <param name="row">Строка.</param>
    /// <param name="edit">Что возьмёт правка — выбор дерева; пусто — правки нет.</param>
    /// <returns>Пункты в порядке показа.</returns>
    /// <remarks>
    /// Отдельно от показа: попап — отдельное окно, которого у безголового прогона нет, и проверять
    /// пункты честнее там, где они рождаются.
    /// </remarks>
    public IReadOnlyList<AxMenuItem> Items(Row row, EditSelection? edit = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return Items(row.Node, row.HasChildren ? row : null, showInFolder: null, edit, EditOrigin.Tree);
    }

    /// <summary>Собирает пункты меню плитки правой колонки.</summary>
    /// <param name="tile">Плитка.</param>
    /// <param name="showInFolder">Показать найденное в его папке; пусто — колонка и так в ней.</param>
    /// <param name="edit">Что возьмёт правка — выбор колонки; пусто — правки нет.</param>
    /// <returns>Пункты в порядке показа.</returns>
    public IReadOnlyList<AxMenuItem> Items(Tile tile, Action? showInFolder, EditSelection? edit = null)
    {
        ArgumentNullException.ThrowIfNull(tile);

        return Items(tile.Node, branch: null, showInFolder, edit, EditOrigin.Pane);
    }

    /// <summary>
    /// Собирает пункты меню пустого места правой колонки — меню папки, которую колонка показывает.
    /// </summary>
    /// <param name="container">Контейнер колонки.</param>
    /// <returns>Пункты в порядке показа; пусто — у контейнера меню нет.</returns>
    /// <remarks>
    /// Меню того же узла, что у строки дерева, только без выбора: у пустого места выбранного нет, и
    /// из правки остаётся одна вставка — в эту папку, той же дорогой, что Ctrl+V в колонке. Вырезать,
    /// удалить и переименовать здесь нечего, и этих пунктов нет вовсе, как у проекта в дереве.
    /// </remarks>
    public IReadOnlyList<AxMenuItem> Items(Node container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return Items(container, branch: null, showInFolder: null, EditSelection.Empty, EditOrigin.Pane);
    }

    private List<AxMenuItem> Items(Node node, Row? branch, Action? showInFolder, EditSelection? edit, EditOrigin origin)
    {
        var items = new List<AxMenuItem>();
        var path = node.Path.IsEmpty ? null : node.Path.Value;

        switch (node.Kind)
        {
            case NodeKind.File:
                items.Add(Item("project.menu.open", new KeyGesture(Key.Enter), () => actions.Open(node)));
                break;
            case NodeKind.Project:
                items.Add(Item("project.menu.openProject", null, () => actions.Open(node)));
                break;
            case NodeKind.Solution when path is not null:
                items.Add(Item("project.menu.openSolution", null, () => actions.Open(node)));
                break;
        }

        if (showInFolder is not null)
            items.Add(Item("project.menu.showInFolder", null, showInFolder));

        if (actions.Delete is not null && (edit is { IsEmpty: false } || Pasting.Folder(node) is not null))
            items.Add(Edit(edit ?? EditSelection.Empty, Pasting.Folder(node), origin));

        if (actions.ShowHistory is { } show && HistoryTarget(node) is not null)
            items.Add(History(node, show));

        if (path is not null)
        {
            items.Add(Item(Reveal.Words, null, () => actions.Reveal(path)));
            items.Add(Item("project.menu.copyPath", new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift), () => actions.Copy(path)));

            if (node.Relative is { Length: > 0 } relative && node.Kind is not (NodeKind.Dependency or NodeKind.Solution))
                items.Add(Item("project.menu.copyRelative", null, () => actions.Copy(relative)));
        }

        if (node.Kind == NodeKind.Dependency)
            items.Add(Item("project.menu.copyName", null, () => actions.Copy(node.Name)));

        if (branch is not null)
        {
            items.Add(Item("project.menu.expand", null, () => actions.ExpandBranch(branch)));
            items.Add(Item("project.menu.collapse", null, () => actions.CollapseBranch(branch)));
        }

        return items;
    }

    /// <summary>
    /// «Правка ▸», как у Rider: вырезать, копировать, вставить, удалить, переименовать.
    /// </summary>
    /// <param name="edit">Что выбрано; у проекта — пусто, и из правки у него только вставка.</param>
    /// <param name="folder">Куда вставлять на этом узле; пусто — вставлять некуда.</param>
    /// <param name="origin">Откуда правка.</param>
    /// <remarks>
    /// Пункты, которым нечего делать, выключены, а не спрятаны, — кроме тех, что к узлу не относятся
    /// вовсе: у проекта нечего вырезать, и пунктов выбора у него нет. «Вставить» включена всегда, где
    /// есть куда: что лежит в буфере системы, меню узнать не успевает, а пустая вставка скажет об этом
    /// строкой состояния.
    /// </remarks>
    private AxMenuItem Edit(EditSelection edit, CanonicalPath? folder, EditOrigin origin)
    {
        var menu = new AxMenuItem { Header = strings["project.edit"] };

        if (!edit.IsEmpty)
        {
            if (actions.CutFiles is { } cut)
                menu.Items.Add(Item("project.edit.cut", new KeyGesture(Key.X, KeyModifiers.Control), () => cut(edit, origin)));

            if (actions.CopyFiles is { } copy)
                menu.Items.Add(Item("project.edit.copy", new KeyGesture(Key.C, KeyModifiers.Control), () => copy(edit, origin)));
        }

        if (folder is { } target && actions.Paste is { } paste)
            menu.Items.Add(Item("project.edit.paste", new KeyGesture(Key.V, KeyModifiers.Control), () => paste(target, origin)));

        if (edit.IsEmpty)
            return menu;

        if (actions.Delete is { } delete)
            menu.Items.Add(Item("project.edit.delete", new KeyGesture(Key.Delete), () => delete(edit, origin)));

        if (actions.Rename is { } rename)
        {
            var item = Item("project.edit.rename", new KeyGesture(Key.F2), () => rename(edit, origin));

            item.IsEnabled = edit.CanRename;
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>
    /// Что показывает история узла: файл — свою, папка — всё, что в ней было; проект и решение — свою
    /// папку. Зависимостям и узлам без пути истории нет.
    /// </summary>
    /// <param name="node">Узел.</param>
    /// <returns>Путь, папка ли это и как назвать; пусто — истории нет.</returns>
    internal static (CanonicalPath Path, bool Folder, string Name)? HistoryTarget(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Path.IsEmpty)
            return null;

        return node.Kind switch
        {
            NodeKind.File => (node.Path, false, node.Name),
            NodeKind.Folder => (node.Path, true, node.Name),
            NodeKind.Project or NodeKind.Solution => (node.Path.Directory, true, node.Name),
            _ => null,
        };
    }

    /// <summary>«Локальная история ▸», как у Rider: показать историю и поставить метку.</summary>
    private AxMenuItem History(Node node, Action<Node> show)
    {
        var menu = new AxMenuItem { Header = strings["project.menu.history"] };

        menu.Items.Add(Item("project.menu.history.show", null, () => show(node)));

        if (actions.PutLabel is { } label)
            menu.Items.Add(Item("project.menu.history.label", null, label));

        return menu;
    }

    private AxMenuItem Item(string key, KeyGesture? gesture, Action act)
    {
        var item = new AxMenuItem { Header = strings[key], InputGesture = gesture };

        item.Click += (_, _) => act();

        return item;
    }
}
