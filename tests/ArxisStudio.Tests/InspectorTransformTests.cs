using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Editors;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Преобразование полями, как Transform у Unity, и точка опоры с единицей: что читается полями, что пишется и что
/// остаётся текстом (запись 379).
/// </summary>
public class InspectorTransformTests
{
    /// <summary>
    /// Полями читается запись, которую поля и напишут: сдвиг, поворот, масштаб и наклон — каждое не больше раза и по
    /// порядку; углы — в любых единицах Avalonia, а пишутся градусами; тождество пишется сбросом.
    /// </summary>
    [Fact]
    public void The_record_reads_and_writes_in_the_order_of_the_fields()
    {
        const string Full = "translate(10px, 20px) rotate(45deg) scale(1.5, 2) skew(10deg, 5deg)";

        Assert.True(TransformText.TryRead(Full, out var parts));
        Assert.Equal(new TransformParts(10, 20, 45, 1.5, 2, 10, 5), parts);
        Assert.Equal(Full, TransformText.Write(parts));

        Assert.True(TransformText.TryRead("rotate(0.5turn)", out parts));
        Assert.Equal(180, parts.Angle, 9);
        Assert.True(TransformText.TryRead("rotate(100grad)", out parts));
        Assert.Equal(90, parts.Angle, 9);
        Assert.True(TransformText.TryRead("rotate(3.141592653589793rad)", out parts));
        Assert.Equal(180, parts.Angle, 9);

        Assert.True(TransformText.TryRead("scale(2)", out parts));
        Assert.Equal("scale(2)", TransformText.Write(parts));
        Assert.True(TransformText.TryRead("translateX(5px) skewY(3deg)", out parts));
        Assert.Equal("translate(5px, 0px) skew(0deg, 3deg)", TransformText.Write(parts));

        Assert.True(TransformText.TryRead(null, out parts));
        Assert.True(TransformText.TryRead("none", out parts));
        Assert.Null(TransformText.Write(parts));
        Assert.Null(TransformText.Write(TransformParts.Identity with { Angle = 0, ScaleX = 1 }));

        foreach (var other in new[]
                 {
                     "rotate(10deg) translate(5px)", "rotate(1deg) rotate(2deg)", "matrix(1,0,0,1,10,0)", "translate(5)",
                     "scale(1px)", "rotate(45)", "rotate(45deg", "spin(45deg)",
                 })
        {
            Assert.False(TransformText.TryRead(other, out _), $"«{other}» прочлось полями");
        }
    }

    /// <summary>
    /// Преобразование — строками под подписью: сдвиг, поворот, масштаб, наклон. Поворот пишется <c>rotate(…deg)</c>, а
    /// возвращённый к тождеству — сбросом атрибута.
    /// </summary>
    [AvaloniaFact]
    public void A_rotation_typed_in_its_field_is_written_as_an_operation_and_identity_resets()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var row = Row(panel, "RenderTransform");

        Assert.Equal(InspectorFieldLayout.Below, row.Editor?.Layout);
        Assert.Equal(
            [
                studio.Strings["inspector.transform.translate"], studio.Strings["inspector.transform.rotate"],
                studio.Strings["inspector.transform.scale"], studio.Strings["inspector.transform.skew"],
            ],
            SubRows(row).Select(static sub => sub.Label as string));

        // Строки — шагом строк списка: поле и зазор, как у соседних строк.
        var rowStep = panel.View!.TryFindResource("AxPropertyRowHeight", out var step) && step is double value ? value : double.NaN;

        Assert.Equal(rowStep, SubRows(row)[1].Bounds.Y - SubRows(row)[0].Bounds.Y);

        var rotate = Assert.IsType<AxNumberField>(SubRows(row)[1].Content);

        Type(studio, rotate, "45");

        Assert.Equal([new InspectorWrite("RenderTransform", "rotate(45deg)")], Assert.Single(target.Writes));

        var scale = Assert.IsType<AxVectorField>(SubRows(row)[2].Content);

        Type(studio, scale.Cells[0], "2");

        Assert.Equal([new InspectorWrite("RenderTransform", "rotate(45deg) scale(2, 1)")], target.Writes[^1]);

        Type(studio, scale.Cells[0], "1");
        Type(studio, rotate, "0");

        Assert.Equal([new InspectorWrite("RenderTransform", null)], target.Writes[^1]);
    }

    /// <summary>
    /// Запись, которую поля не передадут, стоит текстом; запись полей «Изменить как текст» открывает текстом, и
    /// набранное в нём пишется как есть.
    /// </summary>
    [AvaloniaFact]
    public void A_record_the_fields_cannot_carry_stays_text_and_any_record_opens_as_text()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("RenderTransform", new InspectorValue("matrix(1,0,0,1,10,0)") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var row = Row(panel, "RenderTransform");
        var text = TextOf(row);

        Assert.True(text.IsVisible, "запись, которую поля не передадут, не стоит текстом");
        Assert.Equal("matrix(1,0,0,1,10,0)", text.Text);
        Assert.False(SubRows(row)[0].IsEffectivelyVisible, "поля стоят рядом с текстом");

        target.Set("RenderTransform", new InspectorValue("rotate(30deg)") { Source = InspectorValueSource.Local });
        studio.Frame();

        Assert.False(text.IsVisible, "запись полей стоит текстом");
        Assert.True(row.CanEditFieldAsText, "запись полей не открывается текстом");

        var edit = panel.Menus!.ForRow(row).OfType<AxMenuItem>().Single(item => Equals(item.Header, studio.Strings["inspector.expression.edit"]));

        edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        studio.Frame();

        Assert.True(text.IsVisible, "«Изменить как текст» не открыл текст");
        Assert.True(text.IsKeyboardFocusWithin, "каретка не в тексте");

        text.SelectAll();
        studio.Window.KeyTextInput("rotate(30deg) translate(4px)");
        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("RenderTransform", "rotate(30deg) translate(4px)")], target.Writes[^1]);
    }

    /// <summary>
    /// Точка опоры — X, Y и единица: доли размера пишутся процентами, точки — числами; смена единицы чисел не
    /// пересчитывает и пишется одной записью.
    /// </summary>
    [AvaloniaFact]
    public void The_origin_is_written_in_percent_or_in_points()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("RenderTransformOrigin", new InspectorValue("50%, 50%"));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var view = Field(panel, "RenderTransformOrigin");
        var point = view.GetVisualDescendants().OfType<AxVectorField>().Single();
        var unit = view.GetVisualDescendants().OfType<AxSegmentedControl>().Single();

        Assert.Equal([50d, 50d], point.Values);
        Assert.Equal("%", Assert.IsType<AxSegmentItem>(unit.SelectedItem).Content);

        Type(studio, point.Cells[0], "25");

        Assert.Equal([new InspectorWrite("RenderTransformOrigin", "25%,50%")], Assert.Single(target.Writes));

        unit.SelectedItem = unit.Items.OfType<AxSegmentItem>().Single(item => Equals(item.Content, "px"));
        studio.Frame();

        Assert.Equal([new InspectorWrite("RenderTransformOrigin", "25,50")], target.Writes[^1]);

        target.Set("RenderTransformOrigin", new InspectorValue("10, 20") { Source = InspectorValueSource.Local });
        studio.Frame();

        Assert.Equal([10d, 20d], point.Values);
        Assert.Equal("px", Assert.IsType<AxSegmentItem>(unit.SelectedItem).Content);
        Assert.Equal(2, target.Writes.Count);
    }

    private static FakeTarget Target()
    {
        var target = new FakeTarget(
            typeof(Button),
            FakeTarget.Property("RenderTransform", typeof(ITransform)),
            FakeTarget.Property("RenderTransformOrigin", typeof(RelativePoint)));

        // Незаданное преобразование служба XAML отдаёт без текста — тождеством; проба по умолчанию отвечает «0».
        target.Put("RenderTransform", new InspectorValue(null));

        return target;
    }

    private static InspectorRow Row(InspectorPanel panel, string id) =>
        Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id).Row;

    private static Control Field(InspectorPanel panel, string id) =>
        Assert.IsAssignableFrom<Control>(Assert.IsType<PluginSurface>(Row(panel, id).Content).Child);

    private static List<AxPropertyRow> SubRows(InspectorRow row) =>
        [.. Assert.IsType<PluginSurface>(row.Content).Child!.GetVisualDescendants().OfType<AxPropertyRow>()];

    /// <summary>Текст записи у поля преобразования — поле ввода не в строках полей.</summary>
    private static AxTextBox TextOf(InspectorRow row)
    {
        var view = Assert.IsType<Grid>(Assert.IsType<PluginSurface>(row.Content).Child);

        return Assert.Single(view.Children.OfType<AxTextBox>());
    }

    private static void Type(InspectorStudio studio, TextBox field, string text)
    {
        field.Focus();
        field.SelectAll();
        studio.Window.KeyTextInput(text);
        Press(studio, Key.Enter);
    }

    private static void Press(InspectorStudio studio, Key key)
    {
        studio.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Frame();
    }
}
