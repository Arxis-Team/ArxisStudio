using System.Collections.ObjectModel;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Design;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Открытый в дизайнере документ: разметка, живые объекты и дерево элементов.
/// </summary>
/// <remarks>
/// Документ владеет тремя вещами сразу — сессией загрузки, поверхностью показа
/// и контекстом сборок проекта, — и разрушать их нужно в обратном порядке,
/// иначе сборки проекта останутся заняты и следующая сборка не запишет файлы.
/// Поэтому создание и закрытие живут в одном типе.
/// </remarks>
public sealed class DesignDocument : IAsyncDisposable
{
    private XamlLoadSession? _session;
    private ProjectXamlPopulation? _population;
    private ProjectAssemblyContext? _assemblies;

    private DesignDocument(string filePath, XamlDesignSurface surface)
    {
        FilePath = filePath;
        Surface = surface;
    }

    /// <summary>Путь к файлу разметки.</summary>
    public string FilePath { get; }

    /// <summary>Имя файла — то, что показывается на вкладке.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Поверхность с живым содержимым документа; её кладут на канву.</summary>
    public XamlDesignSurface Surface { get; }

    /// <summary>Дерево элементов документа; один корень.</summary>
    public ObservableCollection<HierarchyNode> Nodes { get; } = [];

    /// <summary>Документ загрузился, но показывать в нём нечего.</summary>
    public bool IsEmpty => !Surface.HasContent;

    /// <summary>
    /// Открывает документ проекта: разбирает разметку, поднимает живые объекты
    /// и строит дерево.
    /// </summary>
    /// <param name="filePath">Путь к файлу <c>.axaml</c>.</param>
    /// <param name="snapshot">Снапшот модели решения.</param>
    /// <param name="project">Проект, которому принадлежит файл.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Документ или сообщение, почему открыть не удалось.</returns>
    public static async Task<(DesignDocument? Document, string? Error)> OpenAsync(
        string filePath,
        SolutionSnapshot snapshot,
        ProjectSnapshot project,
        CancellationToken cancellationToken = default)
    {
        var (environment, assemblies) = ProjectXamlEnvironment.CreateFor(snapshot, project.Identity);
        ProjectXamlPopulation? population = null;

        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            var parsed = XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = new Uri(filePath) });

            // Документ с x:Class уже имеет скомпилированный близнец в сборке
            // проекта, и загрузчик выберет именно его — то есть разметку с
            // последней сборки. Регистрация сообщает поколению, что содержимое
            // такого класса берётся из документа, открытого сейчас.
            population = ProjectXamlPopulation.Create(assemblies, environment);
            await population.SetDocumentAsync(parsed, cancellationToken);

            var (session, result) = await XamlLoadSession.TryCreateAsync(
                parsed,
                environment,
                new XamlLoadOptions { Mode = XamlLoadMode.Design },
                cancellationToken);

            if (session is null)
            {
                population.Dispose();
                assemblies.Dispose();
                return (null, Describe(result));
            }

            var surface = new XamlDesignSurface();
            surface.Attach(session);

            var document = new DesignDocument(filePath, surface)
            {
                _session = session,
                _population = population,
                _assemblies = assemblies,
            };

            document.BuildTree(parsed.Root, session);
            return (document, null);
        }
        catch (OperationCanceledException)
        {
            population?.Dispose();
            assemblies.Dispose();
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            population?.Dispose();
            assemblies.Dispose();
            return (null, e.Message);
        }
    }

    /// <summary>Находит узел дерева по живому контролу.</summary>
    /// <param name="control">Контрол на канве.</param>
    /// <remarks>
    /// Клик мог попасть внутрь шаблона, где объекты документом не объявлены,
    /// поэтому поиск поднимается вверх, пока не найдёт объявленный предок.
    /// </remarks>
    public HierarchyNode? FindNode(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent as Control)
        {
            if (Find(Nodes, current) is { } found)
                return found;
        }

        return null;

        static HierarchyNode? Find(IEnumerable<HierarchyNode> nodes, Control control)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node.Control, control))
                    return node;

                if (Find(node.Children, control) is { } found)
                    return found;
            }

            return null;
        }
    }

    /// <summary>Возвращает путь от корня документа до узла контрола.</summary>
    /// <param name="control">Контрол на канве.</param>
    /// <remarks>
    /// Дереву мало знать сам узел: чтобы показать выделение, нужно раскрыть
    /// всех его предков.
    /// </remarks>
    public IReadOnlyList<HierarchyNode> FindPath(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent as Control)
        {
            var path = new List<HierarchyNode>();

            if (Search(Nodes, current, path))
                return path;
        }

        return [];

        static bool Search(IEnumerable<HierarchyNode> nodes, Control control, List<HierarchyNode> path)
        {
            foreach (var node in nodes)
            {
                path.Add(node);

                if (ReferenceEquals(node.Control, control) || Search(node.Children, control, path))
                    return true;

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Surface.Detach();
        Surface.Dispose();

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        if (_population is not null)
        {
            _population.Dispose();
            _population = null;
        }

        if (_assemblies is not null)
        {
            // Контекст сборок выгружается последним: пока живы объекты
            // документа, сборки проекта заняты и перестроить его нельзя.
            _assemblies.Dispose();
            _assemblies = null;
        }
    }

    private void BuildTree(XamlElement? root, XamlLoadSession session)
    {
        Nodes.Clear();

        if (root is null)
            return;

        Nodes.Add(CreateNode(root, session));
    }

    private static HierarchyNode CreateNode(XamlElement element, XamlLoadSession session)
    {
        var node = new HierarchyNode(
            element,
            session.GetObject(element) as Control,
            XamlElementPath.Of(element));

        // ContentElements — только то, что порождает объекты: свойства-элементы
        // вроде <Border.Resources> в дереве контролов показывать незачем.
        foreach (var child in element.ContentElements)
            node.Children.Add(CreateNode(child, session));

        return node;
    }

    private static string Describe(XamlLoadResult result) =>
        result.Diagnostics.FirstOrDefault() is { } diagnostic
            ? diagnostic.ToString()
            : "Разметка не дала ни одного объекта";
}
