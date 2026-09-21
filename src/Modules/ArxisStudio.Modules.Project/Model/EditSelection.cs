using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Что правка берёт из выбранного: файлы и папки на диске, вложенные — вместе с владельцем.
/// </summary>
/// <remarks>
/// <para>
/// Файл уносит с собой вложенные в него, как у Rider: выбран <c>MainWindow.axaml</c> — удаляется и
/// переименовывается вместе с ним <c>MainWindow.axaml.cs</c>. Вложенный, выбранный без владельца,
/// идёт один. Узел, лежащий в выбранной папке или под выбранным файлом, уходит вместе с ними и
/// отдельно не считается.
/// </para>
/// <para>
/// Выбор, в котором есть что-то кроме файлов и папок, правке не отдаётся вовсе. Ctrl+A в дереве
/// выделяет и решение с проектами и зависимостями, и удалить из такого выбора «что получится»
/// значило бы удалить не то, о чём человек думал, нажимая Delete.
/// </para>
/// </remarks>
internal sealed class EditSelection
{
    private EditSelection(IReadOnlyList<Node> roots) => Roots = roots;

    /// <summary>Правке нечего взять.</summary>
    public static EditSelection Empty { get; } = new([]);

    /// <summary>
    /// Выбранное без того, что уходит вместе с другим выбранным, — по пути, чтобы порядок не зависел
    /// от порядка щелчков.
    /// </summary>
    public IReadOnlyList<Node> Roots { get; }

    /// <summary>Правке нечего взять.</summary>
    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Переименовать можно одно — вместе с вложенным в него.</summary>
    public bool CanRename => Roots.Count == 1;

    /// <summary>Файлов, которые уйдут прямо: выбранные и вложенные в них.</summary>
    public int Files => Roots.Where(root => root.Kind == NodeKind.File).Sum(root => 1 + Nested(root).Count);

    /// <summary>Папок, которые уйдут со всем содержимым.</summary>
    public int Folders => Roots.Count(root => root.Kind == NodeKind.Folder);

    /// <summary>Пути на удаление: выбранное и вложенное в выбранные файлы.</summary>
    public IReadOnlyList<CanonicalPath> Paths =>
        [.. Roots.SelectMany(root => root.Kind == NodeKind.File ? root.Descendants() : [root]).Select(node => node.Path)];

    /// <summary>Собирает правку из выбранного.</summary>
    /// <param name="selected">Выбранные узлы в любом порядке; пустые пропускаются.</param>
    public static EditSelection Of(IEnumerable<Node?> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var nodes = selected.OfType<Node>().Distinct().ToList();

        if (nodes.Count == 0 || !nodes.All(IsEditable))
            return Empty;

        var chosen = nodes.ToHashSet();

        return new EditSelection(
        [
            .. nodes
                .Where(node => !node.Ancestors().Any(chosen.Contains))
                .OrderBy(node => node.Path.Value, StringComparer.OrdinalIgnoreCase),
        ]);
    }

    /// <summary>Правится ли узел: файл или папка проекта на диске.</summary>
    /// <param name="node">Узел.</param>
    public static bool IsEditable(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind is NodeKind.File or NodeKind.Folder && !node.Path.IsEmpty;
    }

    /// <summary>Вложенные в файл — всё, что стоит под ним в дереве; у папки их нет.</summary>
    /// <param name="node">Узел.</param>
    public static IReadOnlyList<Node> Nested(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind == NodeKind.File ? [.. node.Descendants().Skip(1)] : [];
    }
}
