using System.Globalization;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Строит дерево решения из снимка — так, как его показывает Rider.
/// </summary>
/// <remarks>
/// Корень — решение и число его проектов; под ним папки решения, вложенные по префиксу пути, и
/// проекты. У проекта первыми «Зависимости» — платформы, пакеты с версиями, проекты, сборки,
/// анализаторы, пустые группы опущены, — затем папки и файлы. Файл, зависимый от другого, стоит под
/// ним: <c>App.axaml.cs</c> под <c>App.axaml</c>.
/// <para>
/// Дерево строится целиком из снимка и ответа диска — какие пути есть и какие папки лежат пустыми, —
/// и больше ничего не спрашивает: ни диска — это сделал <see cref="DiskProbe"/>, — ни словаря —
/// подписи пришли в <see cref="Words"/>. Поэтому строить его можно вне потока интерфейса, а
/// проверять — без окна.
/// </para>
/// </remarks>
public static class SolutionTree
{
    private static readonly DependencyKind[] Groups =
        [DependencyKind.Frameworks, DependencyKind.Packages, DependencyKind.Projects, DependencyKind.Assemblies, DependencyKind.Analyzers];

    /// <summary>Строит дерево.</summary>
    /// <param name="snapshot">Снимок решения.</param>
    /// <param name="disk">Что есть на диске — ответ <see cref="DiskProbe"/>.</param>
    /// <param name="words">Подписи на языке студии.</param>
    public static Node Build(SolutionSnapshot snapshot, DiskAnswer disk, Words words)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(words);

        var entry = snapshot.EntryPoint.Path;
        var home = entry.IsEmpty ? CanonicalPath.None : entry.Directory;

        var root = new Node
        {
            Key = "s:" + entry.Value,
            Kind = NodeKind.Solution,
            Name = snapshot.Name,
            Detail = words.Count(snapshot.Projects.Length),
            Path = entry,
            Relative = entry.IsEmpty ? null : entry.FileName,
        };

        var folders = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<ProjectIdentity, Node>();

        // Папка решения — путь вида «/src/Libs/»: вложенность только в нём, и родитель — самый
        // длинный уже заведённый префикс. Короткие пути идут первыми, поэтому родитель заведён раньше.
        foreach (var folder in snapshot.Folders.OrderBy(folder => folder.Path.Length))
        {
            var path = Slashed(folder.Path);
            var node = new Node { Key = "sf:" + path, Kind = NodeKind.SolutionFolder, Name = folder.Name };

            (ParentFolder(folders, path) ?? root).Add(node);
            folders[path] = node;

            foreach (var member in folder.Projects)
                owners[member] = node;
        }

        foreach (var project in snapshot.Projects)
        {
            var parent = owners.TryGetValue(project.Identity, out var folder) ? folder : root;

            parent.Add(ProjectNode(snapshot, project, disk, words, home));
        }

        Sort(root);

        return root;
    }

    /// <summary>Путь от папки решения — для подсказки и копирования.</summary>
    /// <param name="home">Папка решения.</param>
    /// <param name="path">Путь файла.</param>
    public static string RelativeTo(CanonicalPath home, CanonicalPath path)
    {
        if (!home.IsEmpty && path.StartsWith(home))
            return path.Value[home.Value.Length..].TrimStart('\\', '/');

        return path.Value;
    }

    private static Node ProjectNode(
        SolutionSnapshot snapshot, ProjectSnapshot project, DiskAnswer disk, Words words, CanonicalPath home)
    {
        var file = project.ProjectFilePath;
        var key = "p:" + file.Value;

        // Проект, который не загрузился, остаётся в снимке одним именем, путём и диагностикой:
        // показывать под ним нечего, а причина — главное, что о нём можно сказать.
        var broken = project.HasErrors && project.Items.IsEmpty;
        var problem = project.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError)?.Message;

        var node = new Node
        {
            Key = key,
            Kind = NodeKind.Project,
            Name = project.Name,
            Detail = broken ? words.NotLoaded : null,
            Path = file,
            Project = file,
            Relative = RelativeTo(home, file),
            IsBroken = broken,
            Problem = broken ? problem : null,
        };

        if (broken)
            return node;

        if (Dependencies(snapshot, project, words, key) is { } dependencies)
            node.Add(dependencies);

        Items(project, disk, node, key, home);

        return node;
    }

    private static Node? Dependencies(SolutionSnapshot snapshot, ProjectSnapshot project, Words words, string key)
    {
        var node = new Node
        {
            Key = key + ">deps",
            Kind = NodeKind.Dependencies,
            Name = words.Dependencies,
            Detail = project.ActiveTargetFramework,
            Project = project.ProjectFilePath,
        };

        foreach (var kind in Groups)
        {
            var members = Members(snapshot, project, kind).ToList();

            if (members.Count == 0)
                continue;

            var group = new Node
            {
                Key = $"{node.Key}>{kind}",
                Kind = NodeKind.DependencyGroup,
                Name = words.Of(kind),
                Dependency = kind,
                Project = project.ProjectFilePath,
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, detail, path) in members)
            {
                if (!seen.Add(name))
                    continue;

                group.Add(new Node
                {
                    Key = $"{group.Key}>{name}",
                    Kind = NodeKind.Dependency,
                    Name = name,
                    Detail = detail,
                    Path = path,
                    Dependency = kind,
                    Project = project.ProjectFilePath,
                });
            }

            node.Add(group);
        }

        return node.Children.Count == 0 ? null : node;
    }

    private static IEnumerable<(string Name, string? Detail, CanonicalPath Path)> Members(
        SolutionSnapshot snapshot, ProjectSnapshot project, DependencyKind kind) => kind switch
    {
        DependencyKind.Frameworks => project.FrameworkReferences.Select(reference => (reference.Name, (string?)null, CanonicalPath.None)),
        DependencyKind.Packages => project.PackageReferences.Select(reference => (reference.PackageId, reference.VersionText, CanonicalPath.None)),
        DependencyKind.Projects => project.ProjectReferences.Select(reference => (
            snapshot.TryGetProject(reference.ProjectFilePath, out var referenced) && referenced is not null
                ? referenced.Name
                : Path.GetFileNameWithoutExtension(reference.ProjectFilePath.FileName),
            (string?)null,
            reference.ProjectFilePath)),
        DependencyKind.Assemblies => project.AssemblyReferences.Select(reference => (reference.Name, (string?)null, reference.ResolvedPath)),
        _ => project.AnalyzerReferences.Select(reference => (
            Path.GetFileNameWithoutExtension(reference.AssemblyPath.FileName), (string?)null, reference.AssemblyPath)),
    };

    private static void Items(ProjectSnapshot project, DiskAnswer disk, Node node, string key, CanonicalPath home)
    {
        var filter = new ItemFilter(project);
        var folders = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = node };
        var placed = new HashSet<CanonicalPath>();
        var files = new List<(Node Node, string Folder, ProjectItem Item)>();

        foreach (var item in project.Items)
        {
            if (!filter.Shows(item, out var relative) || !disk.Present.Contains(item.FullPath) || !placed.Add(item.FullPath))
                continue;

            // Явная папка стоит и пустой: её объявили, чтобы она была видна, и лист без шеврона —
            // ровно то, что показывает Rider.
            if (ItemFilter.IsFolder(item))
            {
                Folder(folders, node, key, relative, item.FullPath, home);
                continue;
            }

            var slash = relative.LastIndexOf('/');
            var directory = slash < 0 ? string.Empty : relative[..slash];
            var name = slash < 0 ? relative : relative[(slash + 1)..];

            var file = new Node
            {
                Key = $"{key}>f:{relative}",
                Kind = NodeKind.File,
                Name = name,
                Path = item.FullPath,
                Project = project.ProjectFilePath,
                Relative = RelativeTo(home, item.FullPath),
                FileKind = FileKinds.Of(Path.GetExtension(name)),
            };

            Folder(folders, node, key, directory, CanonicalPath.None, home).Add(file);
            files.Add((file, directory, item));
        }

        Empty(project, disk, filter, folders, node, key, home);
        Nest(files);
    }

    /// <summary>
    /// Ставит пустые папки с диска — как Rider: папка SDK-проекта — то, что лежит на диске, и
    /// опустевшая после удаления последнего файла не пропадает, а остаётся листом без шеврона.
    /// </summary>
    /// <remarks>
    /// Папка встаёт только под тем, что дерево уже показало, — под проектом, папкой его файлов или
    /// другой пустой: короткие пути идут первыми, поэтому пустой родитель заведён раньше. Папку чужого
    /// проекта, лежащего внутри этого, дерево так и не покажет: её родителя этот проект не показывает.
    /// </remarks>
    private static void Empty(
        ProjectSnapshot project, DiskAnswer disk, ItemFilter filter, Dictionary<string, Node> folders, Node node, string key, CanonicalPath home)
    {
        foreach (var path in disk.Empty.Where(path => path.StartsWith(project.ProjectDirectory)).OrderBy(path => path.Value.Length))
        {
            if (!filter.ShowsFolder(path, out var relative))
                continue;

            var slash = relative.LastIndexOf('/');

            if (folders.ContainsKey(slash < 0 ? string.Empty : relative[..slash]))
                Folder(folders, node, key, relative, path, home);
        }
    }

    /// <summary>Находит или заводит цепочку папок до относительного пути.</summary>
    private static Node Folder(
        Dictionary<string, Node> folders, Node project, string key, string directory, CanonicalPath path, CanonicalPath home)
    {
        if (folders.TryGetValue(directory, out var existing))
            return existing;

        var slash = directory.LastIndexOf('/');
        var parent = slash < 0 ? project : Folder(folders, project, key, directory[..slash], CanonicalPath.None, home);
        var where = path.IsEmpty ? project.Path.Directory.Combine(directory.Replace('/', System.IO.Path.DirectorySeparatorChar)) : path;

        var node = new Node
        {
            Key = $"{key}>d:{directory}",
            Kind = NodeKind.Folder,
            Name = slash < 0 ? directory : directory[(slash + 1)..],
            Path = where,
            Project = project.Path,
            Relative = RelativeTo(home, where),
        };

        parent.Add(node);
        folders[directory] = node;

        return node;
    }

    /// <summary>
    /// Кладёт зависимый файл под тот, от которого он зависит.
    /// </summary>
    /// <remarks>
    /// Сперва по метаданным <c>DependentUpon</c> — их ставит сам проект или SDK: Avalonia ставит их
    /// каждому <c>*.axaml.cs</c>. Затем по имени: файл, чьё имя — имя другого файла той же папки с
    /// ещё одним расширением, принадлежит ему. Это правило каждой среды, и настраивать его не нужно.
    /// Файл в чужой папке не вкладывается, как бы ни было похоже имя; цепочка, ведущая по кругу,
    /// обрывается на файле, который уже стоит над своим владельцем.
    /// </remarks>
    private static void Nest(List<(Node Node, string Folder, ProjectItem Item)> files)
    {
        foreach (var group in files.GroupBy(file => file.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var byName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

            foreach (var (node, _, _) in group)
                byName.TryAdd(node.Name, node);

            foreach (var (node, _, item) in group)
            {
                var owner = Owner(byName, node, item);

                if (owner is null || owner == node || owner.Ancestors().Contains(node) || node.Parent is null)
                    continue;

                node.Parent.Remove(node);
                owner.Add(node);
            }
        }
    }

    private static Node? Owner(Dictionary<string, Node> byName, Node file, ProjectItem item)
    {
        if (item.Metadata.GetValueOrDefault("DependentUpon") is { Length: > 0 } upon)
        {
            var name = upon.Replace('\\', '/');
            var slash = name.LastIndexOf('/');

            if (byName.TryGetValue(slash < 0 ? name : name[(slash + 1)..], out var declared))
                return declared;
        }

        var dot = file.Name.LastIndexOf('.');

        return dot > 0 && byName.TryGetValue(file.Name[..dot], out var owner) ? owner : null;
    }

    /// <summary>
    /// Порядок, как в Rider: «Зависимости» первыми, затем папки, затем файлы и проекты, по имени.
    /// </summary>
    /// <remarks>
    /// Группы зависимостей стоят в своём порядке — платформы, пакеты, проекты, сборки, анализаторы, —
    /// и по имени не сортируются: их порядок и есть смысл. Имя сравнивается по культуре и без учёта
    /// регистра, а при равенстве — побайтно, чтобы порядок не зависел от того, как пришли элементы.
    /// </remarks>
    private static void Sort(Node node)
    {
        if (node.Kind != NodeKind.Dependencies)
            node.Order(Compare);

        foreach (var child in node.Children)
            Sort(child);
    }

    private static int Compare(Node left, Node right)
    {
        var rank = Rank(left).CompareTo(Rank(right));

        if (rank != 0)
            return rank;

        var name = string.Compare(left.Name, right.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

        return name != 0 ? name : string.CompareOrdinal(left.Name, right.Name);
    }

    private static int Rank(Node node) => node.Kind switch
    {
        NodeKind.Dependencies => 0,
        NodeKind.SolutionFolder or NodeKind.Folder => 1,
        _ => 2,
    };

    /// <summary>Путь папки решения с чертой в начале и в конце: «/src/Libs/».</summary>
    private static string Slashed(string path)
    {
        var trimmed = path.Replace('\\', '/').Trim('/');

        return trimmed.Length == 0 ? "/" : $"/{trimmed}/";
    }

    private static Node? ParentFolder(Dictionary<string, Node> folders, string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');

        return slash <= 0 ? null : folders.GetValueOrDefault(trimmed[..(slash + 1)]);
    }
}
