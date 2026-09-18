using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>Что меню строки умеет делать — действия окна, в которые пункты уходят.</summary>
/// <param name="Open">Открыть файл в редакторе студии.</param>
/// <param name="Reveal">Показать в файловом менеджере.</param>
/// <param name="Copy">Положить текст в буфер обмена.</param>
/// <param name="ExpandBranch">Раскрыть ветку строки целиком.</param>
/// <param name="CollapseBranch">Свернуть ветку строки.</param>
internal sealed record MenuActions(
    Action<Node> Open,
    Action<string> Reveal,
    Action<string> Copy,
    Action<Row> ExpandBranch,
    Action<Row> CollapseBranch);

/// <summary>
/// Контекстное меню строки дерева: что можно сделать с тем, на чём стоят.
/// </summary>
/// <remarks>
/// Пункты зависят от вида узла. Файл открывают, показывают в проводнике и копируют его путь —
/// полный и от решения, как в Rider; проект и решение открывают своим файлом; у зависимости
/// копируют имя, а путь — только если он у неё есть. Раскрыть и свернуть ветку можно у всего, у
/// чего есть дети. Ни создания, ни переименования, ни удаления: служба проектов файлов не пишет,
/// и окно, правящее диск в обход неё, расходилось бы с её снимком.
/// <para>
/// Меню собирается на каждый показ: пункты зависят от строки, и держать их между показами значило
/// бы пересобирать их на каждый щелчок в дереве.
/// </para>
/// </remarks>
/// <param name="strings">Словари модуля.</param>
/// <param name="actions">Действия окна.</param>
internal sealed class ProjectMenu(IStudioStrings strings, MenuActions actions)
{
    /// <summary>Показывает меню там, где его попросили.</summary>
    /// <param name="anchor">К чему привязать: у мыши — список, у клавиатуры — строка.</param>
    /// <param name="row">Строка, на которой стоят.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем.</param>
    public void ShowAt(Control anchor, Row row, bool atPointer)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(row);

        var items = Items(row);

        if (items.Count == 0)
            return;

        var flyout = new AxMenuFlyout();

        foreach (var item in items)
            flyout.Items.Add(item);

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>
    /// Собирает пункты меню строки.
    /// </summary>
    /// <param name="row">Строка.</param>
    /// <returns>Пункты в порядке показа.</returns>
    /// <remarks>
    /// Отдельно от показа: попап — отдельное окно, которого у безголового прогона нет, и проверять
    /// пункты честнее там, где они рождаются.
    /// </remarks>
    public IReadOnlyList<AxMenuItem> Items(Row row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var node = row.Node;
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

        if (path is not null)
        {
            items.Add(Item(Reveal.Words, null, () => actions.Reveal(path)));
            items.Add(Item("project.menu.copyPath", new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift), () => actions.Copy(path)));

            if (node.Relative is { Length: > 0 } relative && node.Kind is not (NodeKind.Dependency or NodeKind.Solution))
                items.Add(Item("project.menu.copyRelative", null, () => actions.Copy(relative)));
        }

        if (node.Kind == NodeKind.Dependency)
            items.Add(Item("project.menu.copyName", null, () => actions.Copy(node.Name)));

        if (row.HasChildren)
        {
            items.Add(Item("project.menu.expand", null, () => actions.ExpandBranch(row)));
            items.Add(Item("project.menu.collapse", null, () => actions.CollapseBranch(row)));
        }

        return items;

        AxMenuItem Item(string key, KeyGesture? gesture, Action act)
        {
            var item = new AxMenuItem { Header = strings[key], InputGesture = gesture };

            item.Click += (_, _) => act();

            return item;
        }
    }
}
