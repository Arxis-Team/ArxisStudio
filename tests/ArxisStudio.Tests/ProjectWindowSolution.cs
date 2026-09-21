using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Tests;

/// <summary>
/// Решение для окна проекта, собранное руками: проекты, файлы, папки решения и то, что есть на диске.
/// </summary>
/// <remarks>
/// Что «на диске», решение помнит само — всякий добавленный файл есть, пока его не назвали
/// отсутствующим, — и построитель дерева проверяется без файловой системы. Окну этого мало: оно
/// спрашивает настоящий диск, и тесту окна решение кладёт свои файлы в его временную папку
/// (<see cref="OnDisk"/>).
/// </remarks>
internal sealed class ProjectWindowSolution
{
    private readonly List<ProjectSnapshotBuilder> _projects = [];
    private readonly List<SolutionFolder> _folders = [];
    private readonly HashSet<CanonicalPath> _directories = [];

    /// <summary>Заводит решение.</summary>
    /// <param name="name">Имя решения — и папки, в которой оно лежит.</param>
    /// <param name="workspace">Сеанс; пусто — новый.</param>
    /// <param name="root">Папка, в которой лежит папка решения; пусто — общая временная, без файлов.</param>
    public ProjectWindowSolution(string name = "Hello", WorkspaceIdentity? workspace = null, string? root = null)
    {
        Name = name;
        Request = new WorkspaceLoadRequest
        {
            EntryPointPath = CanonicalPath.Create(Path.Combine(
                root ?? Path.Combine(Path.GetTempPath(), "arxis-project-window"), name, name + ".slnx")),
            Workspace = workspace ?? WorkspaceIdentity.New(),
        };
    }

    /// <summary>Имя решения.</summary>
    public string Name { get; }

    /// <summary>Запрос, которым решение «загрузили».</summary>
    public WorkspaceLoadRequest Request { get; }

    /// <summary>Файл решения.</summary>
    public CanonicalPath Entry => Request.EntryPointPath;

    /// <summary>Папка решения.</summary>
    public CanonicalPath Home => Entry.Directory;

    /// <summary>Что есть на диске — ответ, который дала бы проверка диска.</summary>
    public HashSet<CanonicalPath> Present { get; } = [];

    /// <summary>Проект <c>папка/имя/имя.csproj</c> от папки решения.</summary>
    /// <param name="name">Имя проекта.</param>
    /// <param name="under">Папка на диске между решением и проектом.</param>
    public ProjectSnapshotBuilder Project(string name, string under = "src")
    {
        var file = Home.Combine(Path.Combine(under, name, name + ".csproj"));

        var project = new ProjectSnapshotBuilder
        {
            Identity = ProjectIdentity.Create(Request.Workspace, file),
            ProjectFilePath = file,
            Name = name,
            ActiveTargetFramework = "net10.0",
        };

        _projects.Add(project);

        return project;
    }

    /// <summary>Элемент проекта по пути от его папки; есть на диске, пока не сказано иное.</summary>
    /// <param name="project">Проект.</param>
    /// <param name="include">Путь от папки проекта, через прямую черту.</param>
    /// <param name="type">Тип элемента MSBuild.</param>
    /// <param name="dependentUpon">Метаданные <c>DependentUpon</c>.</param>
    /// <param name="onDisk">Есть ли файл на диске.</param>
    /// <param name="visible">Метаданные <c>Visible</c>: <c>false</c> — проект прячет элемент от среды.</param>
    public CanonicalPath File(
        ProjectSnapshotBuilder project,
        string include,
        string type = ProjectItemTypes.Compile,
        string? dependentUpon = null,
        bool onDisk = true,
        bool visible = true) =>
        Item(project, include, project.ProjectFilePath.Directory.Combine(include), type, link: null, dependentUpon, onDisk, visible);

    /// <summary>Элемент, лежащий где угодно, — с путём целиком и, может быть, ссылкой.</summary>
    /// <param name="project">Проект.</param>
    /// <param name="path">Путь файла.</param>
    /// <param name="link">Место в проекте.</param>
    public CanonicalPath Linked(ProjectSnapshotBuilder project, CanonicalPath path, string? link) =>
        Item(project, path.Value, path, ProjectItemTypes.Compile, link, dependentUpon: null, onDisk: true, visible: true);

    /// <summary>Явно объявленная папка: <c>&lt;Folder Include="Models\"/&gt;</c>.</summary>
    /// <param name="project">Проект.</param>
    /// <param name="include">Путь папки от проекта.</param>
    /// <param name="onDisk">Есть ли папка на диске.</param>
    public CanonicalPath Folder(ProjectSnapshotBuilder project, string include, bool onDisk = true)
    {
        var path = File(project, include, ProjectItemTypes.Folder, onDisk: onDisk);

        _directories.Add(path);

        return path;
    }

    /// <summary>Папка решения со своими проектами.</summary>
    /// <param name="path">Путь папки: «/src/Libs/».</param>
    /// <param name="projects">Проекты в ней.</param>
    public void SolutionFolder(string path, params ProjectSnapshotBuilder[] projects) =>
        _folders.Add(new SolutionFolder
        {
            Name = path.Trim('/').Split('/')[^1],
            Path = path,
            Projects = [.. projects.Select(project => project.Identity)],
        });

    /// <summary>Снимок решения.</summary>
    public SolutionSnapshot ToSnapshot()
    {
        var solution = new SolutionSnapshotBuilder
        {
            Workspace = Request.Workspace,
            Solution = SolutionIdentity.Create(Request.Workspace, Entry),
            Name = Name,
            Request = Request,
        };

        foreach (var project in _projects)
            solution.Projects.Add(project.ToSnapshot());

        foreach (var folder in _folders)
            solution.Folders.Add(folder);

        return solution.ToSnapshot();
    }

    /// <summary>Дерево, каким его строит окно, с английскими подписями.</summary>
    public Node Tree() => SolutionTree.Build(ToSnapshot(), Present, Words.English);

    /// <summary>Кладёт на диск всё, что «есть на диске»: файлы пустыми, объявленные папки папками.</summary>
    public ProjectWindowSolution OnDisk()
    {
        foreach (var path in Present)
        {
            if (_directories.Contains(path))
            {
                Directory.CreateDirectory(path.Value);
                continue;
            }

            Directory.CreateDirectory(path.Directory.Value);

            if (!System.IO.File.Exists(path.Value))
                System.IO.File.WriteAllText(path.Value, string.Empty);
        }

        return this;
    }

    /// <summary>
    /// Решение, похожее на то, что создаёт шаблон Avalonia: приложение и библиотека в папке решения.
    /// </summary>
    /// <remarks>
    /// В нём есть всё, что дерево умеет: зависимости трёх видов, выведенные и явная пустая папки,
    /// разметка с кодом за ней, картинка и манифест. Тесты, которым нужно «обычное решение», берут
    /// его, а не собирают своё: одно и то же дерево узнаётся от теста к тесту.
    /// </remarks>
    /// <param name="name">Имя решения.</param>
    /// <param name="workspace">Сеанс; пусто — новый.</param>
    /// <param name="extra">Ещё один файл приложения — так приходит перезагрузка «файл добавили».</param>
    /// <param name="root">Папка, в которой лежит папка решения; пусто — общая временная, без файлов.</param>
    /// <param name="window">Имя главного окна — так приходит перезагрузка «окно переименовали».</param>
    /// <param name="without">Файлы приложения, которых нет, путём от проекта, — «файлы удалили».</param>
    public static ProjectWindowSolution Avalonia(
        string name = "Hello",
        WorkspaceIdentity? workspace = null,
        string? extra = null,
        string? root = null,
        string window = "MainWindow",
        IReadOnlyCollection<string>? without = null)
    {
        var solution = new ProjectWindowSolution(name, workspace, root);
        var app = solution.Project("App");
        var lib = solution.Project("Lib");

        app.FrameworkReferences.Add(new FrameworkReferenceInfo { Name = "Microsoft.NETCore.App" });
        app.PackageReferences.Add(new PackageReferenceInfo { PackageId = "CommunityToolkit.Mvvm", VersionText = "8.4.0" });
        app.PackageReferences.Add(new PackageReferenceInfo { PackageId = "Avalonia", VersionText = "12.1.2" });
        app.ProjectReferences.Add(new ProjectReferenceInfo { ProjectFilePath = lib.ProjectFilePath, Project = lib.Identity });

        void Add(string include, string type = ProjectItemTypes.Compile, string? dependentUpon = null)
        {
            if (without?.Contains(include) != true)
                solution.File(app, include, type, dependentUpon);
        }

        Add("Program.cs");
        Add("App.axaml", "AvaloniaXaml");
        Add("App.axaml.cs", dependentUpon: "App.axaml");
        Add("app.manifest", ProjectItemTypes.None);
        Add("Assets/avalonia-logo.ico", "AvaloniaResource");
        Add($"Views/{window}.axaml", "AvaloniaXaml");
        Add($"Views/{window}.axaml.cs", dependentUpon: $"{window}.axaml");
        solution.Folder(app, "Models/");

        if (extra is not null)
            solution.File(app, extra);

        solution.File(lib, "Class1.cs");
        solution.SolutionFolder("/src/", app, lib);

        return solution;
    }

    /// <summary>
    /// Дерево текстом: узел на строку, два пробела на уровень, имя и вторая подпись.
    /// </summary>
    /// <remarks>
    /// Форма дерева целиком читается одним взглядом и сравнивается одной проверкой: разошедшаяся
    /// строка видна в сообщении сразу, с соседями, а не номером ребёнка.
    /// </remarks>
    /// <param name="root">Корень.</param>
    /// <param name="dependencies">Показывать ли содержимое «Зависимостей» — у настоящего снимка оно дело SDK.</param>
    public static string Shape(Node root, bool dependencies = true)
    {
        var lines = new List<string>();

        Walk(root, 0);

        return string.Join('\n', lines);

        void Walk(Node node, int depth)
        {
            lines.Add(new string(' ', depth * 2) + node.Name + (node.Detail is { Length: > 0 } detail ? " " + detail : string.Empty));

            if (!dependencies && node.Kind == NodeKind.Dependencies)
                return;

            foreach (var child in node.Children)
                Walk(child, depth + 1);
        }
    }

    private CanonicalPath Item(
        ProjectSnapshotBuilder project,
        string include,
        CanonicalPath path,
        string type,
        string? link,
        string? dependentUpon,
        bool onDisk,
        bool visible)
    {
        var metadata = new List<KeyValuePair<string, string>>();

        if (dependentUpon is not null)
            metadata.Add(new("DependentUpon", dependentUpon));

        if (!visible)
            metadata.Add(new("Visible", "false"));

        project.Items.Add(new ProjectItem
        {
            ItemType = type,
            Include = include,
            FullPath = path,
            Link = link,
            Metadata = metadata.Count == 0 ? ProjectMetadata.Empty : ProjectMetadata.Create(metadata),
        });

        if (onDisk)
            Present.Add(path);

        return path;
    }
}
