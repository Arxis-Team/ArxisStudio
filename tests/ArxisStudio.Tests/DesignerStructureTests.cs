using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правки строения формы: буфер обмена, дубликат, обёртка и её снятие — клавишами холста и иерархии и
/// пунктами меню, одной правкой каждая.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerStructureTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Grid x:Name="Layout" RowDefinitions="Auto,*">
            <StackPanel x:Name="Panel">
              <Button x:Name="Go" Content="Пуск" />
              <TextBox x:Name="Input" Text="поле" />
            </StackPanel>
            <TextBlock x:Name="Caption" Grid.Row="1" Text="Подпись" />
          </Grid>
        </Window>
        """;

    private static XamlElementPath Layout => XamlElementPath.Parse("/0");

    private static XamlElementPath Panel => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0/0");

    private static XamlElementPath Input => XamlElementPath.Parse("/0/0/1");

    private static XamlElementPath Caption => XamlElementPath.Parse("/0/1");

    /// <summary>
    /// Ctrl+C кладёт в буфер разметку выбранного с её пространствами, а Ctrl+V ставит копию после выбранного
    /// одной правкой — без имени, которое уже носит оригинал, — и выбирает её.
    /// </summary>
    [AvaloniaFact]
    public async Task Copy_and_paste_on_the_canvas_put_an_anonymous_copy_after_the_selected()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        document.Select([Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.C, KeyModifiers.Control);

        var copied = await ClipboardAsync(studio, text => text.Contains("Button", StringComparison.Ordinal));

        Assert.Contains("x:Name=\"Go\"", copied, StringComparison.Ordinal);
        Assert.Contains("xmlns=\"https://github.com/avaloniaui\"", copied, StringComparison.Ordinal);
        Assert.Equal(string.Format(studio.Strings["form.copied.one"], "Button «Go»"), studio.Xaml.Status.Last);

        LiveFormStudio.Press(sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<Button") == 2, "копия не встала");

        var panel = Panel.Resolve(document.Document!.Syntax)!;
        var buttons = panel.ContentElements.Where(element => element.Name.LocalName == "Button").ToList();

        Assert.Equal(["Button", "Button", "TextBox"], panel.ContentElements.Select(element => element.Name.LocalName));
        Assert.Null(buttons[1].Identity);
        Assert.Equal("Пуск", buttons[1].GetAttribute("Content")?.GetValueText());
        Assert.Equal(XamlElementPath.Parse("/0/0/1"), Assert.Single(document.Selection));

        Assert.True(document.Document.CanUndo);
        await document.Document.UndoAsync();
        await XamlStudio.UntilAsync(() => Count(document, "<Button") == 1, "отмена не убрала копию одним шагом");
    }

    /// <summary>Вырезанное и вставленное в другую панель сохраняет имя: его больше никто не носит.</summary>
    [AvaloniaFact]
    public async Task What_is_cut_and_pasted_elsewhere_keeps_its_name()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        document.Select([Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.X, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<Button") == 0, "кнопка не вырезана");
        Assert.Equal(Panel, Assert.Single(document.Selection));
        Assert.Contains("x:Name=\"Go\"", await ClipboardAsync(studio, text => text.Length > 0), StringComparison.Ordinal);

        document.Select([Layout]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<Button") == 1, "кнопка не вставлена");

        var layout = Layout.Resolve(document.Document!.Syntax)!;

        Assert.Equal(["StackPanel", "TextBlock", "Button"], layout.ContentElements.Select(element => element.Name.LocalName));
        Assert.Equal("Go", layout.ContentElements.Last().Identity);
        Assert.Equal(XamlElementPath.Parse("/0/2"), Assert.Single(document.Selection));
    }

    /// <summary>
    /// Несколько выбранных копируются по порядку документа, а не выбора, и встают подряд одной правкой —
    /// сразу после выбранного, перед его соседом.
    /// </summary>
    [AvaloniaFact]
    public async Task Several_elements_are_copied_in_document_order_and_pasted_together()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        document.Select([Input, Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.C, KeyModifiers.Control);

        var copied = await ClipboardAsync(studio, text => text.Contains("TextBox", StringComparison.Ordinal));

        Assert.True(copied.IndexOf("<Button", StringComparison.Ordinal) < copied.IndexOf("<TextBox", StringComparison.Ordinal));
        Assert.Equal(string.Format(studio.Strings["form.copied.many"], 2), studio.Xaml.Status.Last);

        document.Select([Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<TextBox") == 2, "копии не встали");

        var panel = Panel.Resolve(document.Document!.Syntax)!.ContentElements.ToList();

        Assert.Equal(["Button", "Button", "TextBox", "TextBox"], panel.Select(element => element.Name.LocalName));
        Assert.Equal(["Go", null, null, "Input"], panel.Select(element => element.Identity));
        Assert.Equal([XamlElementPath.Parse("/0/0/1"), XamlElementPath.Parse("/0/0/2")], document.Selection);

        await document.Document.UndoAsync();
        await XamlStudio.UntilAsync(() => Count(document, "<TextBox") == 1, "вставка двух — не один шаг");
    }

    /// <summary>
    /// Несколько элементов встают только туда, где мест сколько угодно: окно держит одно содержимое, и
    /// вставка в пустое окно двух не пишет ничего, а говорит почему.
    /// </summary>
    [AvaloniaFact]
    public async Task Several_elements_are_not_pasted_into_a_single_slot()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("Empty.axaml", """
            <Window xmlns="https://github.com/avaloniaui" Width="400" Height="300">
            </Window>
            """);

        document.Select([XamlElementPath.Root]);
        LiveFormStudio.Frame();

        await studio.Window.Clipboard!.SetTextAsync("<Button xmlns=\"https://github.com/avaloniaui\" />\n<CheckBox xmlns=\"https://github.com/avaloniaui\" />");
        LiveFormStudio.Press(document.View.Sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => studio.Xaml.Status.Last == studio.Strings["form.paste.nowhere"], "о вставке некуда не сказано");
        Assert.False(document.Document!.IsModified);
    }

    /// <summary>
    /// Выбран элемент внутри элемента свойства — определение строки: вставленное встаёт после сетки, которой
    /// оно принадлежит, а не среди её детей по номеру строки.
    /// </summary>
    [AvaloniaFact]
    public async Task An_element_inside_a_property_element_is_no_place_to_paste_beside()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("Rows.axaml", """
            <Window xmlns="https://github.com/avaloniaui" Width="400" Height="300">
              <StackPanel>
                <Grid>
                  <Grid.RowDefinitions>
                    <RowDefinition />
                    <RowDefinition />
                  </Grid.RowDefinitions>
                  <Button Content="В сетке" />
                </Grid>
              </StackPanel>
            </Window>
            """);

        document.Select([XamlElementPath.Parse("/0/0/RowDefinitions:1")]);
        LiveFormStudio.Frame();

        await studio.Window.Clipboard!.SetTextAsync("<CheckBox xmlns=\"https://github.com/avaloniaui\" />");
        LiveFormStudio.Press(document.View.Sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<CheckBox") == 1, "флажок не встал");

        var stack = XamlElementPath.Parse("/0").Resolve(document.Document!.Syntax)!;

        Assert.Equal(["Grid", "CheckBox"], stack.ContentElements.Select(element => element.Name.LocalName));
    }

    /// <summary>
    /// Выбранная панель и её ребёнок копируются один раз: ребёнок уже в разметке панели, а второй его
    /// дубликат встал бы внутри оригинала.
    /// </summary>
    [AvaloniaFact]
    public async Task What_lies_inside_the_selected_is_not_taken_twice()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        document.Select([Go, Panel]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(document.View.Sheet, Key.D, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<StackPanel") == 2, "панель не продублирована");

        Assert.Equal(2, Count(document, "<Button"));
        Assert.Equal(2, Panel.Resolve(document.Document!.Syntax)!.ContentElements.Count());
    }

    /// <summary>
    /// Разметка, скопированная в другом файле, встаёт со своими пространствами: недостающее объявляется на
    /// корне; текст, в котором разметки нет, не вставляется, и строка состояния говорит почему.
    /// </summary>
    [AvaloniaFact]
    public async Task Markup_from_elsewhere_lands_with_its_namespaces_and_plain_text_does_not()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;
        var clipboard = studio.Window.Clipboard!;

        document.Select([Panel]);
        LiveFormStudio.Frame();

        await clipboard.SetTextAsync("просто слова");
        LiveFormStudio.Press(sheet, Key.V, KeyModifiers.Control);
        await XamlStudio.UntilAsync(() => studio.Xaml.Status.Last == studio.Strings["form.paste.empty"], "о пустой вставке не сказано");

        Assert.False(document.Document!.IsModified);

        // Слова вокруг разметки фрагментом не становятся: строка из переписки вставляет то, что в ней размечено.
        await clipboard.SetTextAsync("вот флажок: <av:CheckBox xmlns:av=\"https://github.com/avaloniaui\" Content=\"Флаг\" />");
        LiveFormStudio.Press(sheet, Key.V, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(document).Contains("<av:CheckBox", StringComparison.Ordinal), "флажок не встал");

        Assert.Contains("xmlns:av=\"https://github.com/avaloniaui\"", Text(document), StringComparison.Ordinal);
        Assert.Equal("CheckBox", Panel.Resolve(document.Document.Syntax)!.ContentElements.Last().Name.LocalName);
    }

    /// <summary>
    /// Ctrl+D ставит копию каждого выбранного сразу после него одной правкой; выбранными встают копии.
    /// </summary>
    [AvaloniaFact]
    public async Task Duplicate_puts_a_copy_after_each_selected_in_one_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var sheet = document.View.Sheet;

        document.Select([Go, Input]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(sheet, Key.D, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Count(document, "<TextBox") == 2, "копии не встали");

        var panel = Panel.Resolve(document.Document!.Syntax)!.ContentElements.ToList();

        Assert.Equal(["Button", "Button", "TextBox", "TextBox"], panel.Select(element => element.Name.LocalName));
        Assert.Equal(["Go", null, "Input", null], panel.Select(element => element.Identity));
        Assert.Equal([XamlElementPath.Parse("/0/0/1"), XamlElementPath.Parse("/0/0/3")], document.Selection);

        await document.Document.UndoAsync();
        await XamlStudio.UntilAsync(() => Count(document, "<TextBox") == 1, "дубликат двух — не один шаг");
    }

    /// <summary>
    /// Обёртка встаёт на место элемента и забирает его ячейку сетки: рамка вокруг подписи во второй строке
    /// стоит во второй строке, а подпись внутри неё строки больше не носит. Снятие возвращает строку подписи.
    /// </summary>
    /// <remarks>
    /// Текст до буквы возвращает отмена, а не снятие: где среди атрибутов стояла строка, снятие не знает и
    /// дописывает её последней.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_wrapper_takes_the_place_of_what_it_wraps_and_unwrapping_gives_it_back()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        document.Select([Caption]);
        LiveFormStudio.Frame();

        Assert.True(await document.Commands!.WrapAsync("Border"), "обёртка не записана");

        var border = Caption.Resolve(document.Document!.Syntax)!;

        Assert.Equal("Border", border.Name.LocalName);
        Assert.Equal("1", border.GetAttribute("Grid.Row")?.GetValueText());

        var caption = Assert.Single(border.ContentElements);

        Assert.Equal("Caption", caption.Identity);
        Assert.Null(caption.GetAttribute("Grid.Row"));
        Assert.Equal(Caption, Assert.Single(document.Selection));

        Assert.True(await document.Commands.UnwrapAsync(), "обёртка не снята");

        var back = Caption.Resolve(document.Document.Syntax)!;

        Assert.Equal("TextBlock", back.Name.LocalName);
        Assert.Equal("Caption", back.Identity);
        Assert.Equal("1", back.GetAttribute("Grid.Row")?.GetValueText());
        Assert.Equal(Caption, Assert.Single(document.Selection));
    }

    /// <summary>
    /// Соседи оборачиваются одной панелью, а рамка, которая держит одно содержимое, для двух выключена;
    /// снятие панели возвращает их на место.
    /// </summary>
    [AvaloniaFact]
    public async Task Siblings_are_wrapped_by_one_panel_and_come_back_when_it_is_taken_off()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var commands = document.Commands!;
        var original = Text(document);

        document.Select([Input, Go]);
        LiveFormStudio.Frame();

        Assert.False(commands.CanWrap("Border"), "в рамку обёрнуты два элемента");
        Assert.False(await commands.WrapAsync("Border"));
        Assert.True(await commands.WrapAsync("DockPanel"), "соседи не обёрнуты");

        var wrapper = Assert.Single(Panel.Resolve(document.Document!.Syntax)!.ContentElements);

        Assert.Equal("DockPanel", wrapper.Name.LocalName);
        Assert.Equal(["Go", "Input"], wrapper.ContentElements.Select(element => element.Identity));
        Assert.Equal(Go, Assert.Single(document.Selection));

        await XamlStudio.UntilAsync(() => commands.CanUnwrap, "снять обёртку нельзя");
        Assert.True(await commands.UnwrapAsync());

        Assert.Equal(original, Text(document));
        Assert.Equal([Go, Input], document.Selection);
    }

    /// <summary>
    /// Панель с несколькими детьми в рамке не снимается: в рамку встанет только один, — и строка состояния
    /// говорит об этом, а меню выключает пункт.
    /// </summary>
    [AvaloniaFact]
    public async Task A_panel_of_several_is_not_unwrapped_into_a_single_slot()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Border x:Name="Frame">
                <StackPanel x:Name="Panel">
                  <Button Content="Раз" />
                  <Button Content="Два" />
                </StackPanel>
              </Border>
            </Window>
            """);

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        Assert.False(document.Commands!.CanUnwrap);
        Assert.False(await document.Commands.UnwrapAsync());
        Assert.False(document.Document!.IsModified);
        Assert.Equal(
            string.Format(studio.Strings["form.unwrap.single"], "StackPanel «Panel»", "Border «Frame»", 2),
            studio.Xaml.Status.Last);
    }

    /// <summary>
    /// Меню предлагает то, что можно сделать с выбранным: у корня — только вставку и «вписать всё», у
    /// элемента — правки, у двух соседей — панели, но не рамку. Пункт делает то же, что клавиша.
    /// </summary>
    [AvaloniaFact]
    public async Task The_canvas_menu_offers_what_the_selection_allows()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);

        document.Select([XamlElementPath.Root]);
        LiveFormStudio.Frame();

        var items = Rows(document.MenuItems());

        Assert.False(items["form.menu.cut"].IsEnabled);
        Assert.False(items["form.menu.copy"].IsEnabled);
        Assert.True(items["form.menu.paste"].IsEnabled);
        Assert.False(items["form.menu.duplicate"].IsEnabled);
        Assert.False(items["form.menu.wrap"].IsEnabled);
        Assert.False(items["form.menu.unwrap"].IsEnabled);
        Assert.False(items["form.menu.parent"].IsEnabled);
        Assert.True(items["form.menu.frame"].IsEnabled);
        Assert.Equal(FormKeys.Duplicate, items["form.menu.duplicate"].InputGesture);

        document.Select([Go, Input]);
        LiveFormStudio.Frame();

        items = Rows(document.MenuItems());

        var wrappers = items["form.menu.wrap"].Items.OfType<AxMenuItem>().ToDictionary(item => (string)item.Header!);

        Assert.True(items["form.menu.wrap"].IsEnabled);
        Assert.False(wrappers["Border"].IsEnabled);
        Assert.True(wrappers["StackPanel"].IsEnabled);
        Assert.True(items["form.menu.parent"].IsEnabled);

        // С кнопки и поля снимать нечего: элемент без детей — не обёртка, и снятие было бы удалением.
        Assert.False(items["form.menu.unwrap"].IsEnabled);

        items["form.menu.duplicate"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await XamlStudio.UntilAsync(() => Count(document, "<TextBox") == 2, "пункт «Дублировать» не дублирует");

        Dictionary<string, AxMenuItem> Rows(IReadOnlyList<Control> menu) =>
            menu.OfType<AxMenuItem>().ToDictionary(item => KeyOf(studio, (string)item.Header!));
    }

    /// <summary>
    /// Иерархия правит теми же клавишами и тем же меню, что холст, — без пунктов, которых у неё нет.
    /// </summary>
    [AvaloniaFact]
    public async Task The_hierarchy_copies_and_pastes_with_the_canvas_keys()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();
        var tree = panel.View!.Tree;

        document.Select([Caption]);
        LiveFormStudio.Frame();

        LiveFormStudio.Press(tree, Key.C, KeyModifiers.Control);
        await ClipboardAsync(studio, text => text.Contains("Caption", StringComparison.Ordinal));

        LiveFormStudio.Press(tree, Key.V, KeyModifiers.Control);
        await XamlStudio.UntilAsync(() => Count(document, "<TextBlock") == 2, "иерархия не вставила");

        var copy = Layout.Resolve(document.Document!.Syntax)!.ContentElements.Last();

        Assert.Equal("1", copy.GetAttribute("Grid.Row")?.GetValueText());
        Assert.Null(copy.Identity);

        var items = panel.MenuItems().OfType<AxMenuItem>().Select(item => KeyOf(studio, (string)item.Header!)).ToList();

        Assert.Contains("form.menu.paste", items);
        Assert.DoesNotContain("form.menu.frame", items);
        Assert.DoesNotContain("form.menu.parent", items);
    }

    /// <summary>Ключ словаря по тексту пункта — меню подписано словарём модуля.</summary>
    private static string KeyOf(LiveFormStudio studio, string header) =>
        new[]
        {
            "form.menu.cut", "form.menu.copy", "form.menu.paste", "form.menu.duplicate", "form.menu.delete",
            "form.menu.wrap", "form.menu.unwrap", "form.menu.parent", "form.menu.frame",
        }.Single(key => studio.Strings[key] == header);

    /// <summary>Ждёт, пока в буфере обмена окна окажется нужный текст, и отдаёт его.</summary>
    private static async Task<string> ClipboardAsync(LiveFormStudio studio, Func<string, bool> ready)
    {
        var clipboard = studio.Window.Clipboard;

        Assert.NotNull(clipboard);

        string? text = null;

        await XamlStudio.UntilAsync(() =>
        {
            var reading = clipboard.TryGetTextAsync();

            text = reading.IsCompleted ? reading.Result : null;

            return text is not null && ready(text);
        }, "в буфере обмена не то");

        return text!;
    }

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    private static int Count(LiveFormDocument document, string what)
    {
        var text = Text(document);
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
