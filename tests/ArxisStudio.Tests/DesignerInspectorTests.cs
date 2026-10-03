using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Инспектор: члены выбранного по разделам, правка — одним шагом истории, пустое поле снимает атрибут,
/// непрочитываемое не пишется, у нескольких выбранных разное — «—».
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerInspectorTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" MinHeight="30" />
            <TextBox x:Name="Input" Text="поле" Width="200" Height="24" />
            <TextBlock Text="Подпись" />
          </StackPanel>
        </Window>
        """;

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Input => XamlElementPath.Parse("/0/1");

    /// <summary>
    /// Шапка — тип и имя; строка ширины показывает написанное, а правка пишет его одним шагом истории,
    /// который отменяется целиком.
    /// </summary>
    [AvaloniaFact]
    public async Task The_inspector_shows_the_selected_element_and_writes_a_width_in_one_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();
        var model = panel.Model!;

        Assert.False(model.HasSelection);

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.True(model.HasSelection);
        Assert.Equal("Button", model.Title);
        Assert.Equal("Avalonia.Controls", model.Detail);
        Assert.Equal("Go", model.Name);

        var width = Row(panel, "Width");

        Assert.Equal("120", width.Written);
        Assert.True(width.IsNumber);
        Assert.Contains(model.Items.OfType<InspectorGroup>(), group => group.Title == studio.Strings["inspector.group.layout"]);

        width.Draft = "150";
        await width.CommitAsync();

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"150\"", StringComparison.Ordinal), "ширина не записана");
        await XamlStudio.UntilAsync(() => Row(panel, "Width").Written == "150", "строка не показала записанное");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"120\"", StringComparison.Ordinal), "отмена не вернула ширину одним шагом");
    }

    /// <summary>Пустое поле — снятый атрибут, а не пустое значение: член возвращается к раскладке.</summary>
    [AvaloniaFact]
    public async Task An_empty_field_takes_the_attribute_out()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var width = Row(panel, "Width");

        width.Draft = string.Empty;
        await width.CommitAsync();

        await XamlStudio.UntilAsync(() => !Element(document, "Go").Attributes.Any(attribute => attribute.Name.LocalName == "Width"), "атрибут не снят");

        var cleared = Row(panel, "Width");

        // Умолчание — не новость: подсказка — само значение, без «по умолчанию».
        Assert.False(cleared.IsWritten);
        Assert.Equal("NaN", cleared.Hint);
    }

    /// <summary>Текст, который загрузка не прочтёт, остаётся в поле с причиной и в документ не пишется.</summary>
    [AvaloniaFact]
    public async Task A_value_that_does_not_read_stays_in_the_field_with_its_reason()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var before = Text(document);
        var width = Row(panel, "Width");

        width.Draft = "широко";
        await width.CommitAsync();
        LiveFormStudio.Frame();

        Assert.True(width.HasError);
        Assert.Contains("широко", width.Error, StringComparison.Ordinal);
        Assert.Equal(before, Text(document));
        Assert.False(document.Document!.CanUndo);
    }

    /// <summary>
    /// У двух выбранных разные ширины — строка показывает «—», а набранное пишется в оба одной записью.
    /// </summary>
    [AvaloniaFact]
    public async Task Two_selected_with_different_values_show_a_dash_and_take_one_value_at_once()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go, Input]);
        LiveFormStudio.Frame();

        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.Strings["inspector.many"], 2), panel.Model!.Title);
        Assert.False(panel.Model.CanName);

        var width = Row(panel, "Width");

        Assert.True(width.IsMixed);
        Assert.Equal(InspectorRow.Mixed, width.Hint);
        Assert.Empty(width.Draft);

        width.Draft = "180";
        await width.CommitAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "180"
                && Element(document, "Input").GetAttribute("Width")?.GetValueText() == "180",
            "ширина не записана в оба");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "120"
                && Element(document, "Input").GetAttribute("Width")?.GetValueText() == "200",
            "одна отмена не вернула оба");
    }

    /// <summary>Флажок и перечисление пишут сразу: щелчок — уже правка.</summary>
    [AvaloniaFact]
    public async Task A_flag_and_a_choice_write_at_once()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var enabled = Row(panel, "IsEnabled");

        Assert.True(enabled.IsFlag);
        Assert.Null(enabled.Flag);

        enabled.Flag = false;

        await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("IsEnabled")?.GetValueText() == "False", "флаг не записан");

        var alignment = Row(panel, "HorizontalAlignment");

        Assert.True(alignment.IsSegmented, "четыре значения выравнивания — сегменты");
        Assert.Equal(-1, alignment.ChoiceIndex);

        alignment.ChoiceIndex = alignment.Choices.ToList().IndexOf("Center");

        await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("HorizontalAlignment")?.GetValueText() == "Center", "выравнивание не записано");
    }

    /// <summary>
    /// Присоединённые члены, которые читает родитель, — свой раздел; у ребёнка Canvas это его положение.
    /// </summary>
    [AvaloniaFact]
    public async Task The_parents_attached_members_have_a_section_of_their_own()
    {
        const string Board = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Canvas x:Name="Board">
                <Border x:Name="Note" Canvas.Left="10" Width="50" Height="20" />
              </Canvas>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Board);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        var items = panel.Model!.Items;
        var section = items.OfType<InspectorGroup>().Single(group => group.Title.Contains("Canvas", StringComparison.Ordinal));
        var after = items.SkipWhile(item => !ReferenceEquals(item, section)).Skip(1).TakeWhile(item => item is InspectorRow).OfType<InspectorRow>().ToList();

        Assert.Contains(after, row => row.Member == "Canvas.Left" && row.Written == "10");
        Assert.Contains(after, row => row.Member == "Canvas.Top" && !row.IsWritten);
        Assert.DoesNotContain(items.OfType<InspectorRow>(), row => row.Member.StartsWith("Grid.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Enter в поле пишет набранное, и каретка остаётся в поле того же члена, хотя строки перестроены; Esc
    /// возвращает написанное.
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_writes_the_field_and_keeps_the_caret_and_escape_reverts()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var field = Field(panel, "Width");

        field.Focus();
        field.Text = "160";
        LiveFormStudio.Press(field, Key.Enter);

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"160\"", StringComparison.Ordinal), "Enter не записал ширину");
        await XamlStudio.UntilAsync(() => Field(panel, "Width").IsFocused, "каретка не вернулась в поле ширины");

        field = Field(panel, "Width");
        field.Text = "999";
        LiveFormStudio.Press(field, Key.Escape);

        Assert.Equal("160", field.Text);
        Assert.Contains("Width=\"160\"", Text(document), StringComparison.Ordinal);

        // В поле без ненаписанного Ctrl+Z — шаг истории формы, а не правка текста поля.
        LiveFormStudio.Press(field, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"120\"", StringComparison.Ordinal), "Ctrl+Z в поле не отменил шаг формы");
    }

    /// <summary>Диктор читает строку инспектора по имени члена, а заголовок — по разделу, не по имени класса.</summary>
    [AvaloniaFact]
    public async Task Every_row_tells_a_screen_reader_its_member()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var rows = panel.View!.Rows;
        var width = Assert.IsAssignableFrom<Control>(rows.ContainerFromItem(Row(panel, "Width")));
        var layout = panel.Model!.Items.OfType<InspectorGroup>().First();
        var header = Assert.IsAssignableFrom<Control>(rows.ContainerFromItem(layout));

        Assert.Equal("Width", Avalonia.Automation.AutomationProperties.GetName(width));
        Assert.Equal(layout.Title, Avalonia.Automation.AutomationProperties.GetName(header));
        Assert.False(header.Focusable, "заголовок раздела берёт клавиатуру");
    }

    /// <summary>Имя пишется директивой; занятое имя не пишется, и шапка говорит почему.</summary>
    [AvaloniaFact]
    public async Task Renaming_writes_the_name_and_refuses_one_already_taken()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<InspectorPanel>();
        var view = panel.View!;

        document.Select([Go]);
        LiveFormStudio.Frame();

        view.NameBox.Text = "Input";
        LiveFormStudio.Press(view.NameBox, Key.Enter);

        Assert.True(view.NameError.IsVisible);
        Assert.Contains("Input", view.NameError.Text, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Go\"", Text(document), StringComparison.Ordinal);

        view.NameBox.Text = "Start";
        LiveFormStudio.Press(view.NameBox, Key.Enter);

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:Name=\"Start\"", StringComparison.Ordinal), "имя не записано");
        await XamlStudio.UntilAsync(() => panel.Model!.Name == "Start", "шапка не показала новое имя");
    }

    private static InspectorRow Row(InspectorPanel panel, string member) =>
        Assert.Single(panel.Model!.Items.OfType<InspectorRow>(), row => row.Member == member);

    private static TextBox Field(InspectorPanel panel, string member)
    {
        var row = Row(panel, member);
        var container = Assert.IsAssignableFrom<Control>(panel.View!.Rows.ContainerFromItem(row));

        return container.GetVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible);
    }

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    private static XamlElement Element(LiveFormDocument document, string name) =>
        document.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);
}
