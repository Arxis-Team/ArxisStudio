using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правки строения документа: вставка, удаление и перестановка — и то, что
/// дерево и живые объекты идут за ними следом.
/// </summary>
public class StructureTests
{
    [AvaloniaFact]
    public async Task An_inserted_control_reaches_the_markup_and_the_tree()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            var panel = Parent(fixture, "AddButton");
            var before = panel.Children.Count;

            Assert.Null(await document.InsertAsync(
                panel, -1, "<Button Content=\"Третья\"/>", "вставка"));

            Assert.Contains("Content=\"Третья\"", document.Document!.GetText());

            // Дерево перестроилось: вставленный элемент в нём есть, и за ним
            // стоит живой контрол.
            var inserted = fixture.All().Single(node =>
                node.Element.GetAttribute("Content")?.GetValueText() == "Третья");

            Assert.NotNull(inserted.Control);
            Assert.Equal(before + 1, Parent(fixture, "AddButton").Children.Count);
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    [AvaloniaFact]
    public async Task A_removed_control_leaves_the_markup_and_the_tree()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            Assert.Null(await document.RemoveAsync(fixture.Node("ClearButton")));

            Assert.DoesNotContain("ClearButton", document.Document!.GetText());
            Assert.DoesNotContain(fixture.All(), node => node.Identity == "ClearButton");
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }

        Assert.Contains("ClearButton", document.Document!.GetText());
    }

    [AvaloniaFact]
    public async Task Reordering_changes_the_order_the_markup_gives()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            Assert.Equal(
                ["Input", "AddButton", "ClearButton"],
                Parent(fixture, "AddButton").Children.Select(node => node.Identity));

            Assert.Null(await document.MoveAsync(fixture.Node("ClearButton"), 0));

            Assert.Equal(
                ["ClearButton", "Input", "AddButton"],
                Parent(fixture, "AddButton").Children.Select(node => node.Identity));
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    /// <summary>
    /// Разметка, которая не разберётся, не должна ни попасть в документ, ни
    /// уронить студию.
    /// </summary>
    [AvaloniaFact]
    public async Task Markup_that_does_not_parse_is_refused()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Document!.GetText();

        var error = await document.InsertAsync(
            Parent(fixture, "AddButton"), -1, "<Button", "плохая вставка");

        Assert.NotNull(error);
        Assert.Equal(before, document.Document!.GetText());
    }

    private static HierarchyNode Parent(DesignerFixtureState fixture, string childName) =>
        fixture.All().Single(node => node.Children.Any(child => child.Identity == childName));
}
