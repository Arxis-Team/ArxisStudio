using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правка документа текстом — то, чем занята XAML-вкладка: разметка идёт в
/// документ целиком, а обратно текст приходит после любой правки дизайнера.
/// </summary>
public class XamlTextTests
{
    [AvaloniaFact]
    public async Task Text_follows_what_the_designer_changed()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        Assert.DoesNotContain("Width=\"180\"", document.Text);

        try
        {
            Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", "180"));
            Assert.Contains("Width=\"180\"", document.Text);
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    [AvaloniaFact]
    public async Task Markup_typed_as_text_reaches_the_tree_and_the_live_objects()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            var edited = document.Text.Replace(
                "<Button x:Name=\"ClearButton\"",
                "<Button x:Name=\"ExtraButton\" Content=\"Ещё\"/>\n      <Button x:Name=\"ClearButton\"",
                StringComparison.Ordinal);

            Assert.Null(await document.SetTextAsync(edited, "правка текстом"));

            var added = fixture.Node("ExtraButton");

            Assert.Equal("Button", added.TypeName);
            Assert.NotNull(added.Control);
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }

        Assert.DoesNotContain("ExtraButton", document.Text);
    }

    /// <summary>
    /// Наполовину набранная разметка не должна ни попадать в документ, ни
    /// откатывать форму у человека под руками.
    /// </summary>
    [AvaloniaFact]
    public async Task Markup_that_does_not_parse_is_refused_before_it_reaches_the_document()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Text;

        var history = document.CanUndo;

        Assert.NotNull(await document.SetTextAsync(before + "<Button", "плохая правка"));

        Assert.Equal(before, document.Text);
        Assert.Equal(history, document.CanUndo);
    }

    [AvaloniaFact]
    public async Task The_same_text_is_not_an_edit()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Text;
        var history = document.CanUndo;

        Assert.Null(await document.SetTextAsync(before, "та же разметка"));

        Assert.Equal(before, document.Text);
        Assert.Equal(history, document.CanUndo);
    }
}
