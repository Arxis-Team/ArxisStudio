using System.Globalization;
using ArxisStudio.Dragging;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Палитра и бросок в форму: контролы по разделам и поиском, Enter ставит в выбранную панель или после
/// выбранного, разметка, принесённая на холст, встаёт туда, куда показал холст.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerToolboxTests
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

    private static XamlElementPath Stack => XamlElementPath.Parse("/0");

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    /// <summary>Разделы Avalonia по порядку, а поиск оставляет контролы, в имени которых набранное.</summary>
    [AvaloniaFact]
    public async Task The_toolbox_lists_controls_by_section_and_the_search_narrows_them()
    {
        await using var studio = new LiveFormStudio();

        await studio.OpenAsync("MainWindow.axaml", Form);

        var panel = studio.Panel<ToolboxPanel>();
        var model = panel.Model!;
        var groups = model.Items.OfType<ToolboxGroup>().Select(group => group.Title).ToList();

        Assert.Equal(studio.Strings["toolbox.group.layout"], groups[0]);
        Assert.Contains(studio.Strings["toolbox.group.shapes"], groups);
        Assert.Contains(model.Items.OfType<ToolboxEntry>(), entry => entry.Name == "Grid");

        // Диктор читает строку по имени контрола, а не по имени класса модели.
        var grid = Assert.IsAssignableFrom<Control>(panel.View!.Entries.ContainerFromItem(Entry(panel, "Grid")));

        Assert.Equal("Grid", Avalonia.Automation.AutomationProperties.GetName(grid));

        panel.View!.Search.Text = "button";
        LiveFormStudio.Frame();

        var found = model.Items.OfType<ToolboxEntry>().Select(entry => entry.Name).ToList();

        Assert.Contains("Button", found);
        Assert.Contains("RadioButton", found);
        Assert.All(found, name => Assert.Contains("button", name, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Items.OfType<ToolboxGroup>(), group => group.Title == studio.Strings["toolbox.group.layout"]);

        panel.View.Search.Text = "нет такого";
        LiveFormStudio.Frame();

        Assert.True(model.IsEmpty);
    }

    /// <summary>
    /// Enter ставит контрол в конец выбранной панели одной правкой, и вставленное встаёт выбранным.
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_places_the_control_at_the_end_of_the_selected_panel()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<ToolboxPanel>();
        var list = panel.View!.Entries;

        document.Select([Stack]);
        list.SelectedItem = Entry(panel, "CheckBox");
        LiveFormStudio.Frame();

        // Каретку держит строка списка, а не сам список.
        var row = Assert.IsAssignableFrom<Control>(list.ContainerFromItem(Entry(panel, "CheckBox")));

        Assert.True(row.Focus(), "строка палитры не взяла клавиатуру");
        LiveFormStudio.Press(list, Key.Enter);

        await XamlStudio.UntilAsync(() => Element(document, "/0/3")?.Name.LocalName == "CheckBox", "флажок не встал в конец панели");

        Assert.Equal("Check", Element(document, "/0/3")!.GetAttribute("Content")?.GetValueText());
        await XamlStudio.UntilAsync(() => document.Selection.SequenceEqual([XamlElementPath.Parse("/0/3")]), "вставленное не выбрано");
        LiveFormStudio.Frame();

        // Вставленное выбрано и на холсте, а клавиатура осталась в палитре: следующий Enter ставит ещё один.
        Assert.True(list.IsKeyboardFocusWithin, "выбор вставленного увёл клавиатуру из палитры");

        await document.Document!.UndoAsync();
        await XamlStudio.UntilAsync(() => Element(document, "/0/3") is null, "отмена не убрала вставку одним шагом");
    }

    /// <summary>Рядом с выбранным контролом — не панелью — палитра ставит сразу после него.</summary>
    [AvaloniaFact]
    public async Task Next_to_a_selected_control_the_toolbox_places_after_it()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<ToolboxPanel>();

        document.Select([Go]);

        Assert.True(await panel.InsertAsync(Entry(panel, "TextBlock")));

        await XamlStudio.UntilAsync(() => Element(document, "/0/1")?.Name.LocalName == "TextBlock", "подпись не встала после кнопки");
        Assert.Equal("Go", Element(document, "/0/0")!.Identity);
        Assert.Equal("Input", Element(document, "/0/2")!.Identity);
    }

    /// <summary>
    /// Рядом с выбранным внутри рамки палитра ставит в ближайшую панель — после рамки: второе содержимое
    /// рамке не встать.
    /// </summary>
    [AvaloniaFact]
    public async Task Inside_a_border_the_toolbox_places_after_the_border_in_the_nearest_panel()
    {
        const string Framed = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <StackPanel x:Name="Panel">
                <Border x:Name="Frame" Width="120" Height="40">
                  <TextBlock x:Name="Inner" Text="Внутри" />
                </Border>
                <TextBlock x:Name="Below" Text="Ниже" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Framed);
        var panel = studio.Panel<ToolboxPanel>();

        document.Select([XamlElementPath.Parse("/0/0/0")]);

        Assert.True(await panel.InsertAsync(Entry(panel, "Button")));

        await XamlStudio.UntilAsync(() => Element(document, "/0/1")?.Name.LocalName == "Button", "кнопка не встала после рамки");
        Assert.Equal("Inner", Element(document, "/0/0/0")!.Identity);
        Assert.Null(Element(document, "/0/0/1"));
        Assert.Equal("Below", Element(document, "/0/2")!.Identity);
    }

    /// <summary>
    /// Разметка, которую несут на холст, встаёт туда, куда показал холст: в конец панели под курсором, — а
    /// у курсора сказано, что и куда встанет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_snippet_carried_onto_the_canvas_lands_where_the_canvas_shows()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var drags = new StudioDrags(() => [studio.Window], studio.Xaml.Log);
        var snippet = new XamlSnippet(XamlSnippet.AvaloniaNamespace, "CheckBox") { Markup = "<CheckBox Content=\"Да\" />" };

        LiveFormStudio.Frame();

        var stack = Assert.IsAssignableFrom<Control>(document.Shown!.ObjectAt(Stack));
        var below = stack.TranslatePoint(new Point(stack.Bounds.Width / 2, stack.Bounds.Height - 4), studio.Window)!.Value;
        var session = drags.Begin(studio.Window, new StudioDragData().With(XamlDataFormats.Snippet, snippet), DragDropEffects.Copy, new StudioDragVisual("CheckBox"));

        Assert.Equal(DragDropEffects.Copy, session.Over(below, KeyModifiers.None));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, studio.Strings["form.drop.add"], "CheckBox", "StackPanel «Panel»"),
            session.Hint);
        Assert.NotNull(document.View.Sheet.DropIndicator);

        Assert.Equal(DragDropEffects.Copy, session.Drop());

        await XamlStudio.UntilAsync(() => Element(document, "/0/3")?.Name.LocalName == "CheckBox", "брошенное не встало в конец панели");
        Assert.Equal("Да", Element(document, "/0/3")!.GetAttribute("Content")?.GetValueText());
        Assert.Null(document.View.Sheet.DropIndicator);

        // Жест кончился на холсте — клавиатура у него: Ctrl+Z отменит бросок, стрелки сдвинут брошенное.
        Assert.True(document.View.Sheet.IsKeyboardFocusWithin, "после броска клавиатура не у холста");
    }

    /// <summary>Саму форму и файл, который не контрол проекта, холст не берёт и говорит почему.</summary>
    [AvaloniaFact]
    public async Task The_form_itself_and_a_file_that_is_no_control_are_refused_with_a_reason()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var drags = new StudioDrags(() => [studio.Window], studio.Xaml.Log);
        var other = studio.Xaml.Write("Notes.txt", "текст");

        LiveFormStudio.Frame();

        var stack = Assert.IsAssignableFrom<Control>(document.Shown!.ObjectAt(Stack));
        var inside = stack.TranslatePoint(new Point(stack.Bounds.Width / 2, stack.Bounds.Height - 4), studio.Window)!.Value;

        var self = drags.Begin(studio.Window, StudioDragData.FromFiles([document.Path.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("…"));

        Assert.Equal(DragDropEffects.None, self.Over(inside, KeyModifiers.None));
        Assert.Equal(studio.Strings["form.drop.self"], self.Hint);
        self.Drop();

        var notes = drags.Begin(studio.Window, StudioDragData.FromFiles([other.Value]), DragDropEffects.Copy | DragDropEffects.Link, new StudioDragVisual("…"));

        Assert.Equal(DragDropEffects.None, notes.Over(inside, KeyModifiers.None));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, studio.Strings["form.drop.notControl"], "Notes.txt"), notes.Hint);
        notes.Drop();

        Assert.False(document.Document!.CanUndo, "отказ записал что-то в форму");
    }

    private static ToolboxEntry Entry(ToolboxPanel panel, string name) =>
        Assert.Single(panel.Model!.Items.OfType<ToolboxEntry>(), entry => entry.Name == name);

    private static XamlElement? Element(LiveFormDocument document, string path) =>
        XamlElementPath.Parse(path).Resolve(document.Document!.Syntax);
}
