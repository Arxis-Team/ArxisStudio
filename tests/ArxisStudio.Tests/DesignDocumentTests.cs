using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Открытие настоящего документа настоящего проекта: модель решения, загрузка
/// разметки в живые объекты и дерево элементов — вся связка целиком.
/// </summary>
/// <remarks>
/// Работает на подопытном проекте из <c>tests/fixtures</c>: он собирается вместе
/// с тестами, но в их процесс не подгружается, поэтому дизайнеру приходится
/// пройти весь настоящий путь — найти сборки проекта, разобрать документ с
/// <c>x:Class</c> и поднять по нему объекты.
/// </remarks>
public class DesignDocumentTests
{
    [AvaloniaFact]
    public async Task Opens_a_real_document_with_live_objects_and_a_tree()
    {
        var project = FixtureProject();
        var document = Path.Combine(Path.GetDirectoryName(project)!, "MainWindow.axaml");

        await using var workspace = new StudioWorkspace();
        var openError = await workspace.OpenAsync(project);

        Assert.Null(openError);
        Assert.NotNull(workspace.Snapshot);

        var snapshot = workspace.FindProjectForFile(document);
        Assert.NotNull(snapshot);

        // Панель проекта показывает файл, который мы собираемся открыть.
        var tree = ProjectTree.BuildProject(snapshot);
        Assert.Contains(Flatten(tree), node => node.IsDesignable && node.Name == "MainWindow.axaml");

        var (opened, error) = await DesignDocument.OpenAsync(document, workspace.Snapshot!, snapshot);

        if (error is not null)
            Assert.Fail(error);

        Assert.NotNull(opened);

        await using (opened)
        {
            // Корень документа — окно; на канве оно показывается своим
            // содержимым, поэтому поверхность обязана что-то содержать.
            Assert.False(opened!.IsEmpty);

            var root = Assert.Single(opened.Nodes);
            Assert.Equal("Window", root.TypeName);

            var panel = Assert.Single(root.Children);
            Assert.Equal("DockPanel", panel.TypeName);

            // Дерево должно указывать на живые объекты: без этого не выйдет
            // ни выделения с канвы, ни выделения из дерева.
            var live = Flatten(root).Where(node => node.Control is not null).ToList();
            Assert.True(live.Count > 4, $"Живых контролов в дереве: {live.Count}");

            // Обратный ход: по контролу находится его узел и путь к нему от корня.
            var button = Flatten(root).First(node => node.TypeName == "Button");
            Assert.Same(button, opened.FindNode(button.Control));

            var path = opened.FindPath(button.Control);
            Assert.Same(root, path[0]);
            Assert.Same(button, path[^1]);
        }
    }

    private static IEnumerable<ProjectNode> Flatten(ProjectNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    private static IEnumerable<HierarchyNode> Flatten(HierarchyNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    /// <summary>Путь к проекту-подопытному; он собран рядом с тестами.</summary>
    private static string FixtureProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "DesignFixtureApp", "DesignFixtureApp.csproj");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найден подопытный проект tests/fixtures/DesignFixtureApp");
    }
}
