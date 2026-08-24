using ArxisStudio.Modules.Designer;
using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Инспектор на настоящем документе: строки свойств, правка, история и запись
/// на диск.
/// </summary>
public class InspectorTests
{
    [AvaloniaFact]
    public async Task Shows_layout_rows_and_marks_what_the_markup_sets()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var button = fixture.Node("AddButton");

        var sections = InspectorModel.Build(button, fixture.Document.Session);
        var rows = sections.SelectMany(section => section.Rows).ToList();

        Assert.Contains(sections, section => section.Title == "Раскладка");

        // Ширина у кнопки не задана — строка есть, но пустая, а действующее
        // значение показывается подсказкой.
        var width = Assert.Single(rows, row => row.Name == "Width");
        Assert.False(width.IsSet);
        Assert.Null(width.Value);

        // Content задан в разметке — он и стоит в строке.
        var content = Assert.Single(rows, row => row.Name == "Content");
        Assert.True(content.IsSet);
        Assert.Equal("Добавить", content.Value);

        // Логическое свойство правится флажком, перечисление — списком.
        Assert.Equal(InspectorRowKind.Toggle, Assert.Single(rows, row => row.Name == "IsEnabled").Kind);

        var alignment = Assert.Single(rows, row => row.Name == "HorizontalAlignment");
        Assert.Equal(InspectorRowKind.Choice, alignment.Kind);
        Assert.Contains("Stretch", alignment.Options);
    }

    [AvaloniaFact]
    public async Task An_edit_reaches_the_markup_the_live_object_and_the_history()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", "180"));

            Assert.Contains("Width=\"180\"", document.Document!.GetText());
            Assert.True(document.IsModified);
            Assert.True(document.CanUndo);

            // Живой объект догнал разметку: без этого канва показывала бы старое.
            Assert.Equal(180, fixture.Node("AddButton").Control!.Width);

            Assert.Null(await document.UndoAsync());
            Assert.DoesNotContain("Width=\"180\"", document.Document!.GetText());

            Assert.Null(await document.RedoAsync());
            Assert.Contains("Width=\"180\"", document.Document!.GetText());
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }

        Assert.DoesNotContain("Width=\"180\"", document.Document!.GetText());
    }

    /// <summary>
    /// Сброс убирает атрибут целиком, а не пишет пустую строку: разница видна
    /// на свойстве, у которого пустая строка — законное значение.
    /// </summary>
    [AvaloniaFact]
    public async Task Clearing_a_row_removes_the_attribute()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", "180"));
            Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", null));

            Assert.Null(fixture.Node("AddButton").Element.GetAttribute("Width"));
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    [AvaloniaFact]
    public async Task A_value_the_property_cannot_take_is_refused_before_it_reaches_the_history()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Document!.GetText();

        Assert.NotNull(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", "широкая"));

        Assert.Equal(before, document.Document!.GetText());
    }

    [AvaloniaFact]
    public async Task Repeating_the_value_that_is_already_there_is_not_an_edit()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var before = document.Document!.GetText();

        Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Content", "Добавить"));

        Assert.Equal(before, document.Document!.GetText());
    }

    /// <summary>
    /// Один жест канвы — одна запись в истории, даже если он поменял и размер,
    /// и положение.
    /// </summary>
    [AvaloniaFact]
    public async Task A_gesture_writes_several_properties_as_one_step()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;

        try
        {
            Assert.Null(await document.SetAttributesAsync(
                fixture.Node("AddButton"),
                [("Width", "180"), ("Height", "40")],
                "жест"));

            var text = document.Document!.GetText();

            Assert.Contains("Width=\"180\"", text);
            Assert.Contains("Height=\"40\"", text);

            var undoError = await document.UndoAsync();
            Assert.True(undoError is null, undoError);

            var reverted = document.Document!.GetText();

            Assert.DoesNotContain("Width=\"180\"", reverted);
            Assert.DoesNotContain("Height=\"40\"", reverted);
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
        }
    }

    [AvaloniaFact]
    public async Task Saving_writes_the_document_back_to_disk()
    {
        var fixture = await DesignerFixture.OpenAsync();
        var document = fixture.Document;
        var original = await File.ReadAllTextAsync(document.FilePath, TestContext.Current.CancellationToken);

        try
        {
            Assert.Null(await document.SetAttributeAsync(fixture.Node("AddButton"), "Width", "180"));
            await document.SaveAsync(TestContext.Current.CancellationToken);

            Assert.False(document.IsModified);
            Assert.Contains(
                "Width=\"180\"",
                await File.ReadAllTextAsync(document.FilePath, TestContext.Current.CancellationToken));
        }
        finally
        {
            await DesignerFixture.RollbackAsync(document);
            await File.WriteAllTextAsync(document.FilePath, original, TestContext.Current.CancellationToken);
        }
    }
}
