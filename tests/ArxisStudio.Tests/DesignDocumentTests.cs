using ArxisStudio.Modules.Designer;
using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
/// <c>x:Class</c> и поднять по нему объекты. Открывается он один раз на весь
/// прогон — почему, объясняет <see cref="DesignerFixture"/>.
/// </remarks>
public class DesignDocumentTests
{
    [AvaloniaFact]
    public async Task Opens_a_real_document_with_live_objects_and_a_tree()
    {
        var fixture = await DesignerFixture.OpenAsync();

        // Панель проекта показывает файл, который мы открыли.
        var tree = ProjectTree.BuildProject(fixture.Project);
        Assert.Contains(Flatten(tree), node => node.IsDesignable && node.Name == "MainWindow.axaml");

        // Корень документа — окно; на канве оно показывается своим содержимым,
        // поэтому поверхность обязана что-то содержать.
        Assert.False(fixture.Document.IsEmpty);

        var root = Assert.Single(fixture.Document.Nodes);
        Assert.Equal("Window", root.TypeName);

        var panel = Assert.Single(root.Children);
        Assert.Equal("DockPanel", panel.TypeName);

        // Дерево должно указывать на живые объекты: без этого не выйдет ни
        // выделения с канвы, ни выделения из дерева.
        var live = fixture.All().Where(node => node.Control is not null).ToList();
        Assert.True(live.Count > 4, $"Живых контролов в дереве: {live.Count}");
    }

    [AvaloniaFact]
    public async Task A_control_finds_its_node_and_the_path_to_it()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var button = fixture.Node("AddButton");

        Assert.Same(button, fixture.Document.FindNode(button.Control));

        var path = fixture.Document.FindPath(button.Control);

        Assert.Same(fixture.Document.Nodes[0], path[0]);
        Assert.Same(button, path[^1]);
    }

    /// <summary>
    /// Клик мог попасть внутрь шаблона, где объекты документом не объявлены:
    /// узлом тогда считается ближайший объявленный предок.
    /// </summary>
    [AvaloniaFact]
    public async Task A_control_from_inside_a_template_resolves_to_its_declared_ancestor()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var button = fixture.Node("AddButton");

        var inner = button.Control!.GetVisualDescendants()
            .OfType<Avalonia.Controls.Control>()
            .FirstOrDefault();

        if (inner is null)
            return;

        Assert.Same(button, fixture.Document.FindNode(inner));
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
}
