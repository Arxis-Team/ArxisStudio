using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
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
internal sealed record MenuActions(
    Action<Node> Open,
    Action<string> Reveal,
    Action<string> Copy,
    Action<Row> ExpandBranch,
    Action<Row> CollapseBranch,
    Action<EditSelection, EditOrigin>? Delete = null,
    Action<EditSelection, EditOrigin>? Rename = null);

/// <summary>
/// Контекстное меню строки дерева и плитки правой колонки: что можно сделать с тем, на чём стоят.
/// </summary>
/// <remarks>
/// Пункты зависят от вида узла. Файл открывают, показывают в проводнике и копируют его путь —
/// полный и от решения, как в Rider; проект и решение открывают своим файлом; у зависимости
/// копируют имя, а путь — только если он у неё есть. Раскрыть и свернуть ветку можно у строки
/// дерева, у которой есть дети; у найденного поиском есть «Показать в папке».
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

        if (edit is { IsEmpty: false } && (actions.Delete is not null || actions.Rename is not null))
            items.Add(Edit(edit, origin));

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

    /// <summary>«Правка ▸»: удалить и переименовать то, что выбрано.</summary>
    private AxMenuItem Edit(EditSelection edit, EditOrigin origin)
    {
        var menu = new AxMenuItem { Header = strings["project.edit"] };

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

    private AxMenuItem Item(string key, KeyGesture? gesture, Action act)
    {
        var item = new AxMenuItem { Header = strings[key], InputGesture = gesture };

        item.Click += (_, _) => act();

        return item;
    }
}
