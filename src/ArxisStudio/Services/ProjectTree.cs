using System.Collections.ObjectModel;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Services;

/// <summary>Что за узел показывает панель проекта.</summary>
public enum ProjectNodeKind
{
    /// <summary>Решение целиком.</summary>
    Solution,

    /// <summary>Проект.</summary>
    Project,

    /// <summary>Папка внутри проекта.</summary>
    Folder,

    /// <summary>Файл.</summary>
    File,
}

/// <summary>Узел дерева проекта: решение, проект, папка или файл.</summary>
/// <param name="Kind">Что это за узел.</param>
/// <param name="Name">Отображаемое имя.</param>
/// <param name="FullPath">Полный путь; у решения и папок — путь на диске, если он есть.</param>
public sealed record ProjectNode(ProjectNodeKind Kind, string Name, string FullPath)
{
    /// <summary>Дочерние узлы.</summary>
    public ObservableCollection<ProjectNode> Children { get; } = [];

    /// <summary>Расширение файла в нижнем регистре, с точкой.</summary>
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>Файл открывается дизайнером форм.</summary>
    public bool IsDesignable => Kind == ProjectNodeKind.File && Extension == ".axaml";

    /// <summary>Узел показывает файл, который можно открыть.</summary>
    public bool IsFile => Kind == ProjectNodeKind.File;
}

/// <summary>
/// Строит дерево панели проекта из снапшота модели.
/// </summary>
/// <remarks>
/// Модель отдаёт плоский список элементов проекта, а панели нужна иерархия,
/// поэтому дерево собирается здесь. Попутно отсекается то, что пользователю
/// показывать незачем: результаты сборки в <c>obj</c> и <c>bin</c>, повторы
/// одного файла под разными типами элементов и записи, за которыми нет файла.
/// Последних много: MSBuild отдаёт и служебные элементы вроде
/// <c>_IsExecutable</c>, и файлы, которые могли бы существовать —
/// <c>.editorconfig</c> в каждой папке вверх по дереву. Отличить их от
/// настоящих файлов можно только обращением к диску, поэтому дерево строится
/// не на потоке интерфейса.
/// </remarks>
public static class ProjectTree
{
    private static readonly string[] IgnoredFolders = ["obj", "bin", ".vs", ".git"];

    /// <summary>Собирает дерево решения.</summary>
    /// <param name="snapshot">Снапшот модели.</param>
    public static ProjectNode Build(SolutionSnapshot snapshot)
    {
        var root = new ProjectNode(
            ProjectNodeKind.Solution,
            snapshot.Name,
            snapshot.Request.EntryPointPath.Value);

        foreach (var project in snapshot.Projects)
            root.Children.Add(BuildProject(project));

        return root;
    }

    /// <summary>Собирает поддерево одного проекта.</summary>
    /// <param name="project">Снапшот проекта.</param>
    public static ProjectNode BuildProject(ProjectSnapshot project)
    {
        var root = new ProjectNode(
            ProjectNodeKind.Project,
            project.Name,
            project.ProjectFilePath.Value);

        var directory = project.ProjectDirectory.Value.TrimEnd('/', '\\');

        var files = project.Items
            .Where(item => !item.FullPath.IsEmpty)
            .Select(item => item.FullPath.Value)
            .Where(path => path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            .Select(path => path[directory.Length..].TrimStart('/', '\\'))
            .Where(relative => relative.Length > 0 && !IsIgnored(relative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(relative => File.Exists(Path.Combine(directory, relative)))
            .OrderBy(relative => relative, StringComparer.OrdinalIgnoreCase);

        foreach (var relative in files)
            Insert(root, directory, relative);

        Sort(root);
        return root;
    }

    private static bool IsIgnored(string relative)
    {
        var segments = relative.Split('/', '\\');
        return segments.Any(segment => IgnoredFolders.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static void Insert(ProjectNode root, string directory, string relative)
    {
        var segments = relative.Split('/', '\\');
        var current = root;
        var path = directory;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            path = Path.Combine(path, segment);

            var isFile = i == segments.Length - 1;
            var existing = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new ProjectNode(
                    isFile ? ProjectNodeKind.File : ProjectNodeKind.Folder,
                    segment,
                    path);

                current.Children.Add(existing);
            }

            current = existing;
        }
    }

    private static void Sort(ProjectNode node)
    {
        var ordered = node.Children
            .OrderBy(child => child.Kind == ProjectNodeKind.File)
            .ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        node.Children.Clear();
        foreach (var child in ordered)
        {
            Sort(child);
            node.Children.Add(child);
        }
    }
}
