using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Shell;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Дизайнер — первый источник инспектора: выбранное на холсте в нём, правка — шаг истории формы (запись 363).
/// </summary>
/// <remarks>
/// Восемь первых — правила прежнего инспектора дизайнера (запись 337) на общем инспекторе студии: строка
/// показывает написанное, пустое снимает атрибут, непрочитываемое не пишется, у нескольких выбранных разное —
/// «—», флажок и выбор пишут сразу, присоединённые родителя — своим разделом, Enter и Esc в поле, имя — директивой.
/// </remarks>
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

    /// <summary>Полка проекта: панель со своим присоединённым членом, который читает у детей.</summary>
    private const string ProjectSource = """
        using Avalonia;
        using Avalonia.Controls;

        namespace App.Controls;

        public class Shelf : StackPanel
        {
            public static readonly AttachedProperty<int> SlotProperty =
                AvaloniaProperty.RegisterAttached<Shelf, Control, int>("Slot");

            public static int GetSlot(Control control) => control.GetValue(SlotProperty);

            public static void SetSlot(Control control, int value) => control.SetValue(SlotProperty, value);
        }
        """;

    private const string ShelfForm = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:c="using:App.Controls">
          <c:Shelf>
            <Button x:Name="Go" Content="Пуск" />
          </c:Shelf>
        </UserControl>
        """;

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Input => XamlElementPath.Parse("/0/1");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Шапка — тип и имя выбранного; строка ширины показывает написанное, а правка пишет его одним шагом истории,
    /// который отменяется целиком.
    /// </summary>
    [AvaloniaFact]
    public async Task The_inspector_shows_the_selected_element_and_writes_a_width_in_one_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("Button «Go»", panel.View!.Title.Text);
        Assert.Contains("Avalonia.Controls", panel.View.Subtitle.Text, StringComparison.Ordinal);

        var width = Row(panel, "Width");

        Assert.Equal("120", width.Value?.Text);
        Assert.True(width.IsOverridden, "написанная ширина не помечена заданной здесь");

        Type(studio, Number(width), "150");

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"150\"", StringComparison.Ordinal), "ширина не записана");
        await XamlStudio.UntilAsync(() => Row(panel, "Width").Value?.Text == "150", "строка не показала записанное");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"120\"", StringComparison.Ordinal), "отмена не вернула ширину одним шагом");
    }

    /// <summary>Сброс — снятый атрибут, а не пустое значение: член возвращается к раскладке.</summary>
    [AvaloniaFact]
    public async Task Resetting_takes_the_attribute_out()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Cell(panel, "Width").Focus(NavigationMethod.Directional);
        LiveFormStudio.Frame();
        Press(studio, Key.Delete);

        await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("Width") is null, "атрибут не снят");
        await XamlStudio.UntilAsync(() => Row(panel, "Width").Value is { IsWritten: false }, "строка осталась заданной");

        Assert.False(Row(panel, "Width").IsOverridden);
    }

    /// <summary>Текст, который загрузка не прочтёт, остаётся в поле с причиной и в документ не пишется.</summary>
    [AvaloniaFact]
    public async Task A_value_that_does_not_read_stays_in_the_field_with_its_reason()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var before = Text(document);
        var origin = Row(panel, "RenderTransformOrigin");
        var box = Box(origin);

        Type(studio, box, "широко");

        Assert.Equal("широко", box.Text);
        Assert.Contains("широко", origin.Note?.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, Text(document));
        Assert.False(document.Document!.CanUndo);
    }

    /// <summary>
    /// У двух выбранных разные ширины — строка показывает «—», а набранное пишется в оба одной записью, которую
    /// одна отмена и возвращает.
    /// </summary>
    [AvaloniaFact]
    public async Task Two_selected_with_different_values_show_a_dash_and_take_one_value_at_once()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go, Input]);
        LiveFormStudio.Frame();

        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.Strings["inspector.many"], 2), panel.View!.Title.Text);

        var width = Row(panel, "Width");

        Assert.True(width.Value?.IsMixed, "разные ширины не помечены разными");
        Assert.True(Number(width).IsMixed);

        Type(studio, Number(width), "180");

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "180" &&
                  Element(document, "Input").GetAttribute("Width")?.GetValueText() == "180",
            "ширина не записана в оба");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "120" &&
                  Element(document, "Input").GetAttribute("Width")?.GetValueText() == "200",
            "одна отмена не вернула оба");
    }

    /// <summary>Флажок и перечисление пишут сразу: щелчок — уже правка.</summary>
    [AvaloniaFact]
    public async Task A_flag_and_a_choice_write_at_once()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var enabled = Row(panel, "IsEnabled").GetVisualDescendants().OfType<AxCheckBox>().Single();

        Assert.True(enabled.IsChecked);

        enabled.IsChecked = false;

        await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("IsEnabled")?.GetValueText() == "False", "флажок не записан");

        var segments = Row(panel, "HorizontalAlignment").GetVisualDescendants().OfType<AxSegmentedControl>().Single();

        segments.SelectedItem = segments.Items.OfType<AxSegmentItem>().Single(item => Equals(item.Tag, "Right"));

        await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("HorizontalAlignment")?.GetValueText() == "Right", "выравнивание не записано");
    }

    /// <summary>Присоединённые члены, которые читает родитель, — свой раздел: у ребёнка Canvas это его положение.</summary>
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
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        var title = string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.InspectorModule.Studio!.Strings["category.parent"], "Canvas");
        var group = panel.List!.Items.OfType<GroupCell>().Single(cell => Equals(cell.Header.Content, title));
        var after = panel.List.Items.SkipWhile(item => !ReferenceEquals(item, group)).Skip(1)
            .TakeWhile(static item => item is not GroupCell).OfType<PropertyCell>().Select(static cell => cell.Row.Property.Id).ToList();

        Assert.Contains("Canvas.Left", after);
        Assert.Contains("Canvas.Top", after);
        Assert.DoesNotContain(panel.List.Rows, row => row.Property.Id.StartsWith("Grid.", StringComparison.Ordinal));

        var left = Row(panel, "Canvas.Left");
        var top = Row(panel, "Canvas.Top");

        Assert.Equal("10", left.Value?.Text);
        Assert.True(left.IsOverridden, "написанное присоединённое не помечено заданным");
        Assert.False(top.Value?.IsWritten);
    }

    /// <summary>
    /// Enter пишет набранное, и каретка остаётся в поле того же члена; Esc возвращает написанное, а Ctrl+Z в поле без
    /// набранного — шаг истории формы, и каретка остаётся в инспекторе.
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_writes_and_keeps_the_caret_escape_reverts_and_ctrl_z_steps_the_form()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var field = Number(Row(panel, "Width"));

        Type(studio, field, "160");

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"160\"", StringComparison.Ordinal), "Enter не записал ширину");
        await XamlStudio.UntilAsync(() => Number(Row(panel, "Width")).IsFocused, "каретка не осталась в поле ширины");

        field = Number(Row(panel, "Width"));
        field.SelectAll();
        studio.Window.KeyTextInput("999");
        Press(studio, Key.Escape);

        Assert.Equal("160", field.Text);
        Assert.Contains("Width=\"160\"", Text(document), StringComparison.Ordinal);

        field.Focus();
        LiveFormStudio.Frame();
        Press(studio, Key.Z, RawInputModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"120\"", StringComparison.Ordinal), "Ctrl+Z в поле не отменил шаг формы");
        await XamlStudio.UntilAsync(() => Row(panel, "Width").Value?.Text == "120", "строка не показала отменённое");

        Assert.True(panel.View!.IsKeyboardFocusWithin, "отмена увела каретку из инспектора");
    }

    /// <summary>Строка называет диктору свой член, а заголовок раздела каретку не берёт.</summary>
    [AvaloniaFact]
    public async Task Every_row_tells_a_screen_reader_its_member()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("Width", AutomationProperties.GetName(Cell(panel, "Width")));
        Assert.All(panel.List!.Items.OfType<GroupCell>(), static group => Assert.False(group.Focusable, "заголовок раздела берёт клавиатуру"));
    }

    /// <summary>Имя пишется директивой; занятое имя не пишется, и строка говорит почему.</summary>
    [AvaloniaFact]
    public async Task Renaming_writes_the_name_and_refuses_one_already_taken()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var name = Row(panel, FormEdits.NameDirective);
        var box = Box(name);

        Assert.Equal("Go", box.Text);

        Type(studio, box, "Input");

        Assert.Contains("Input", name.Note?.ToString(), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Go\"", Text(document), StringComparison.Ordinal);

        Type(studio, box, "Start");

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:Name=\"Start\"", StringComparison.Ordinal), "имя не записано");
        await XamlStudio.UntilAsync(() => panel.View!.Title.Text == "Button «Start»", "шапка не показала новое имя");
    }

    /// <summary>
    /// Инспектор идёт за холстом впереди: без выбора — корень формы, с выбором — выбранное, а другая вкладка впереди
    /// — её форма.
    /// </summary>
    [AvaloniaFact]
    public async Task The_inspector_follows_the_canvas_in_front_and_shows_the_root_without_a_selection()
    {
        const string Second = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <TextBlock Text="Вторая" />
            </UserControl>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        await XamlStudio.UntilAsync(() => panel.View!.Title.Text == "Window", "без выбора не показан корень");

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("Button «Go»", panel.View!.Title.Text);

        document.Select([]);

        await XamlStudio.UntilAsync(() => panel.View.Title.Text == "Window", "снятый выбор не вернул корень");

        await studio.OpenAsync("Second.axaml", Second);

        await XamlStudio.UntilAsync(() => panel.View.Title.Text == "UserControl", "инспектор не пошёл за вкладкой впереди");
    }

    /// <summary>
    /// На доске правка из инспектора — шаг её общей истории: Ctrl+Z в инспекторе отменяет его, где бы его ни сделали.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_an_edit_is_a_step_of_the_boards_history()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Form));
        var panel = studio.Inspector();
        var slot = board.Canvas!.Slots.Single();

        board.Canvas.Select(slot, [Go]);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => panel.View!.Title.Text == "Button «Go»", "выбранное на доске не показано");

        Type(studio, Number(Row(panel, "Width")), "150");

        await XamlStudio.UntilAsync(() => Text(slot).Contains("Width=\"150\"", StringComparison.Ordinal), "ширина не записана");

        Cell(panel, "Width").Focus(NavigationMethod.Directional);
        LiveFormStudio.Frame();
        Press(studio, Key.Z, RawInputModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(slot).Contains("Width=\"120\"", StringComparison.Ordinal), "история доски не отменила правку инспектора");

        Assert.True(board.History!.CanRedo, "отменила история документа, а не доски: доска не может вернуть шаг");
    }

    /// <summary>
    /// «—» ставит разное написанное, а не разное действующее: «120» и «120.0» — одно число, но разный текст, и
    /// правят текст.
    /// </summary>
    [AvaloniaFact]
    public async Task A_dash_marks_different_writing_not_a_different_value()
    {
        const string Twins = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <StackPanel>
                <Button x:Name="Go" Content="Пуск" Width="120" Height="30" />
                <Button x:Name="Stop" Content="Стоп" Width="120.0" Height="30" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Twins);
        var panel = studio.Inspector();

        document.Select([Go, XamlElementPath.Parse("/0/1")]);
        LiveFormStudio.Frame();

        Assert.True(Row(panel, "Width").Value?.IsMixed, "разно написанное число не помечено разным");
        Assert.False(Row(panel, "Height").Value?.IsMixed, "одинаково написанное помечено разным");
    }

    /// <summary>
    /// Плагин показал в инспекторе своё поверх выбранного на холсте — вернувшийся к холсту возвращает туда выбранное.
    /// </summary>
    [AvaloniaFact]
    public async Task Working_with_the_canvas_again_brings_the_selection_back_over_another_target()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();
        var inspector = studio.InspectorModule.Studio!.Inspector()!;

        document.Select([Go]);
        LiveFormStudio.Frame();

        using var other = inspector.Show(new FakeTarget(typeof(Border)));

        LiveFormStudio.Frame();

        Assert.Equal("Border", panel.View!.Title.Text);

        document.View.Sheet.Focus();
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => panel.View.Title.Text == "Button «Go»", "выбранное на холсте не вернулось наверх");
    }

    /// <summary>
    /// Замок держит закреплённый элемент, пока на холсте выбирают другое; снятый — показывает выбранное.
    /// </summary>
    [AvaloniaFact]
    public async Task A_locked_inspector_keeps_its_element_while_the_canvas_selects_another()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        panel.View!.Lock.IsChecked = true;
        document.Select([Input]);
        LiveFormStudio.Frame();

        Assert.Equal("Button «Go»", panel.View.Title.Text);

        panel.View.Lock.IsChecked = false;
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => panel.View.Title.Text == "TextBox «Input»", "снятый замок не показал выбранное");
    }

    /// <summary>
    /// Закреплённая цель, на чьём пути встал другой элемент, закрывается: править чужой элемент хуже, чем показать,
    /// что своего нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_locked_target_whose_element_moved_away_closes()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Input]);
        LiveFormStudio.Frame();

        panel.View!.Lock.IsChecked = true;
        LiveFormStudio.Frame();

        await document.Canvas.Active!.Session.Edits!.DeleteAsync([Go]);

        await XamlStudio.UntilAsync(() => panel.View.EmptyTitle.Text == studio.InspectorModule.Studio!.Strings["inspector.closed"] && panel.View.Empty.IsVisible,
            "закреплённая цель показала чужой элемент вместо закрытия");
    }

    /// <summary>
    /// Присоединённый член владельца из проекта пишется с приставкой его пространства и стоит разделом родителя,
    /// который его читает.
    /// </summary>
    [AvaloniaFact]
    public async Task An_attached_member_of_a_project_owner_is_written_with_its_prefix()
    {
        await using var studio = new LiveFormStudio();

        Emit(studio);

        var document = await studio.OpenAsync("MainWindow.axaml", ShelfForm);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var slot = Row(panel, "Shelf.Slot");

        Assert.Contains(panel.List!.Items.OfType<GroupCell>(), cell => cell.Header.Content is string title && title.Contains("Shelf", StringComparison.Ordinal));

        Type(studio, Number(slot), "2");

        await XamlStudio.UntilAsync(() => Text(document).Contains("c:Shelf.Slot=\"2\"", StringComparison.Ordinal), "присоединённое не записано с приставкой владельца");
    }

    /// <summary>Размер корня пишется туда, откуда его берёт дизайнер, — в размер дизайна.</summary>
    [AvaloniaFact]
    public async Task The_root_size_goes_where_the_designer_reads_it()
    {
        const string Sized = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                         xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                         mc:Ignorable="d" d:DesignWidth="640" d:DesignHeight="400">
              <TextBlock Text="Форма" />
            </UserControl>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Sized);
        var panel = studio.Inspector();

        await XamlStudio.UntilAsync(() => panel.View!.Title.Text == "UserControl", "корень не показан");

        var width = Row(panel, "Width");

        Assert.Equal("640", width.Value?.Text);
        Assert.True(width.IsOverridden, "размер дизайна не помечен заданным");

        Type(studio, Number(width), "800");

        await XamlStudio.UntilAsync(() => Text(document).Contains("d:DesignWidth=\"800\"", StringComparison.Ordinal), "размер дизайна не записан");

        Assert.DoesNotContain(" Width=", Text(document), StringComparison.Ordinal);
    }

    /// <summary>Выравнивание Stretch той же правкой отпускает ширину, и одна отмена возвращает обе.</summary>
    [AvaloniaFact]
    public async Task Stretch_releases_the_width_in_the_same_edit()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml",
            Form.Replace("Width=\"120\"", "Width=\"120\" HorizontalAlignment=\"Left\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var segments = Row(panel, "HorizontalAlignment").GetVisualDescendants().OfType<AxSegmentedControl>().Single();

        segments.SelectedItem = segments.Items.OfType<AxSegmentItem>().Single(item => Equals(item.Tag, "Stretch"));

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("HorizontalAlignment")?.GetValueText() == "Stretch" &&
                  Element(document, "Go").GetAttribute("Width") is null,
            "Stretch не отпустил ширину");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "120" &&
                  Element(document, "Go").GetAttribute("HorizontalAlignment")?.GetValueText() == "Left",
            "одна отмена не вернула ширину и выравнивание");
    }

    /// <summary>Член, заданный элементом-свойством, правится только там: в инспекторе он — чтение.</summary>
    [AvaloniaFact]
    public async Task A_member_set_as_an_element_is_read_only()
    {
        const string Long = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <StackPanel>
                <Button x:Name="Go">
                  <Button.Content>
                    <TextBlock Text="Пуск" />
                  </Button.Content>
                </Button>
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Long);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var content = Row(panel, "Content");

        Assert.True(content.Property.IsReadOnly, "заданное элементом можно править атрибутом");
        Assert.True(Box(content).IsReadOnly);
    }

    /// <summary>
    /// Выбор на холсте каретки в инспектор не уводит, а правка в инспекторе её там оставляет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_canvas_selection_keeps_the_caret_on_the_canvas_and_an_inspector_edit_keeps_it_in_the_inspector()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();
        var sheet = document.View.Sheet;

        sheet.Focus();
        LiveFormStudio.Frame();
        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("Button «Go»", panel.View!.Title.Text);
        Assert.True(sheet.IsFocused, "показ в инспекторе увёл каретку с холста");

        Type(studio, Number(Row(panel, "Width")), "130");

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"130\"", StringComparison.Ordinal), "ширина не записана");

        Assert.True(Number(Row(panel, "Width")).IsFocused, "правка в инспекторе увела каретку");
    }

    /// <summary>Файл, переписанный снаружи, — новые значения в инспекторе.</summary>
    [AvaloniaFact]
    public async Task A_file_rewritten_outside_updates_the_values()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("120", Row(panel, "Width").Value?.Text);

        File.WriteAllText(
            studio.Xaml.PathOf("MainWindow.axaml").Value,
            Form.Replace("Width=\"120\"", "Width=\"333\"", StringComparison.Ordinal));

        await XamlStudio.UntilAsync(() => Row(panel, "Width").Value?.Text == "333", "инспектор не показал текст с диска");
    }

    /// <summary>
    /// Замена поколения при открытом инспекторе: прежнее поколение уходит из процесса, а строки после неё — снова
    /// со значениями нового.
    /// </summary>
    [AvaloniaFact]
    public async Task A_generation_swap_with_the_inspector_open_lets_the_generation_go_and_comes_back()
    {
        await using var studio = new LiveFormStudio();

        Emit(studio);

        var document = await studio.OpenAsync("MainWindow.axaml", ShelfForm);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var slot = Row(panel, "Shelf.Slot");
        var field = Number(slot);

        field.Focus();
        LiveFormStudio.Frame();

        var report = await studio.Xaml.Session.Host.SwapAsync("проверка инспектора", Token);

        Assert.True(report.Reclaimed, report.ToString());

        await XamlStudio.UntilAsync(() => document.Form.Root is not null, "новая форма не встала");
        await XamlStudio.UntilAsync(() => panel.View!.Notice.IsVisible == false, "шапка осталась с заметкой о замене");

        // Строки пережили замену — с полем и кареткой в нём: замена идёт после каждой сборки кода.
        Assert.Same(slot, Row(panel, "Shelf.Slot"));
        Assert.True(field.IsFocused, "замена поколения увела каретку из поля");
        Assert.Equal("0", slot.Value?.Text);
    }

    /// <summary>Кладёт выход проекта: сборку с полкой — поколению собирать нечего.</summary>
    private static CanonicalPath Emit(LiveFormStudio studio)
    {
        var name = "DesignerInspector" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.Xaml.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, ProjectSource);

        studio.Xaml.AssemblyName = name;
        studio.Xaml.Output = CanonicalPath.Create(output);

        return studio.Xaml.Output.Value;
    }

    private static PropertyCell Cell(InspectorPanel panel, string id)
    {
        LiveFormStudio.Frame();

        return Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);
    }

    private static InspectorRow Row(InspectorPanel panel, string id)
    {
        var cell = Cell(panel, id);

        panel.View!.List.ScrollIntoView(cell);
        LiveFormStudio.Frame();

        return cell.Row;
    }

    private static AxNumberField Number(InspectorRow row) =>
        Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(row.Content).Child);

    private static TextBox Box(InspectorRow row) =>
        row.GetVisualDescendants().OfType<TextBox>().First();

    /// <summary>Набирает в поле и пишет Enter — как человек.</summary>
    private static void Type(LiveFormStudio studio, TextBox field, string text)
    {
        field.Focus();
        field.SelectAll();
        studio.Window.KeyTextInput(text);
        Press(studio, Key.Enter);
    }

    private static void Press(LiveFormStudio studio, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        studio.Window.KeyPress(key, modifiers, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, modifiers, PhysicalKey.None, string.Empty);
        LiveFormStudio.Frame();
    }

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    private static string Text(FormSlot slot) => slot.Session.Document!.Syntax.SourceText.ToString();

    private static XamlElement Element(LiveFormDocument document, string name) =>
        document.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);
}
