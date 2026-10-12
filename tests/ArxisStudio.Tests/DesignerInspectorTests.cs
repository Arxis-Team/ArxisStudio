using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Inspector.Editors;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    /// <summary>
    /// Полка проекта: панель со своим присоединённым членом, который читает у детей, и зазором в границах
    /// <c>[Range]</c>.
    /// </summary>
    private const string ProjectSource = """
        using System.ComponentModel.DataAnnotations;
        using Avalonia;
        using Avalonia.Controls;

        namespace App.Controls;

        public class Shelf : StackPanel
        {
            public static readonly AttachedProperty<int> SlotProperty =
                AvaloniaProperty.RegisterAttached<Shelf, Control, int>("Slot");

            public static readonly StyledProperty<double> GapProperty =
                AvaloniaProperty.Register<Shelf, double>(nameof(Gap));

            [Range(0, 10)]
            public double Gap
            {
                get => GetValue(GapProperty);
                set => SetValue(GapProperty, value);
            }

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

    /// <summary>Подпись формы — без имени.</summary>
    private static XamlElementPath Caption => XamlElementPath.Parse("/0/2");

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

        // Подзаголовок — только тип, как у Unity; пространство и сборка — в подсказке (запись 377).
        Assert.Equal("Button «Go»", panel.View!.Title.Text);
        Assert.Equal("Button", panel.View.Subtitle.Text);
        Assert.Equal("Avalonia.Controls · Avalonia.Controls", ToolTip.GetTip(panel.View.Subtitle));

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

        // Преобразование, открытое текстом (запись 379), — поле текста, который служба XAML проверяет загрузкой.
        var transform = Row(panel, "RenderTransform");

        Assert.True(transform.EditFieldAsText(), "преобразование не открылось текстом");
        LiveFormStudio.Frame();

        var box = transform.GetVisualDescendants().OfType<AxTextBox>().Single(box => box.GetType() == typeof(AxTextBox));

        Type(studio, box, "широко");

        Assert.Equal("широко", box.Text);
        Assert.Contains("широко", transform.Note?.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    /// Имя правится в шапке и пишется директивой; занятое имя не пишется, и строка говорит почему, а в списке имени
    /// нет.
    /// </summary>
    [AvaloniaFact]
    public async Task Renaming_writes_the_name_and_refuses_one_already_taken()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var name = HeaderRow(panel, FormEdits.NameDirective);
        var box = Box(name);

        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == FormEdits.NameDirective);

        Assert.Equal("Go", box.Text);

        Type(studio, box, "Input");

        Assert.Contains("Input", name.Note?.ToString(), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Go\"", Text(document), StringComparison.Ordinal);

        Type(studio, box, "Start");

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:Name=\"Start\"", StringComparison.Ordinal), "имя не записано");
        await XamlStudio.UntilAsync(() => panel.View!.Title.Text == "Button «Start»", "шапка не показала новое имя");
    }

    /// <summary>
    /// <c>x:Name</c> правится в строке заголовка, как имя объекта у Unity: поле рядом со значком и флажком, без подписи
    /// и без отметки «задано здесь»; пустое называет себя подписью свойства — и диктору тоже, — а что это за элемент,
    /// говорит подзаголовок (запись 368).
    /// </summary>
    [AvaloniaFact]
    public async Task The_name_is_edited_in_the_header_line_like_a_unity_object_name()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Caption]);
        LiveFormStudio.Frame();

        var name = HeaderRow(panel, FormEdits.NameDirective);
        var box = Box(name);

        Assert.Same(name, panel.View!.HeaderName.Child);
        Assert.Equal(AxPropertyRowLayout.Full, name.Layout);
        Assert.DoesNotContain(panel.View.HeaderRows.Children.OfType<InspectorRow>(), row => row.IsHeadline);
        Assert.False(panel.View.Title.IsVisible, "имя и заголовок стоят оба");
        Assert.Equal("TextBlock", panel.View.Subtitle.Text);
        Assert.Contains("Avalonia.Controls", Assert.IsType<string>(ToolTip.GetTip(panel.View.Subtitle)), StringComparison.Ordinal);
        Assert.Equal(string.Empty, box.Text ?? string.Empty);
        Assert.Equal(name.Property.Label, box.PlaceholderText);
        Assert.Equal(name.Property.Label, AutomationProperties.GetName(box));

        Type(studio, box, "Caption");

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:Name=\"Caption\"", StringComparison.Ordinal), "имя не записано");
        LiveFormStudio.Frame();

        Assert.Equal("Caption", Box(HeaderRow(panel, FormEdits.NameDirective)).Text);
        Assert.False(HeaderRow(panel, FormEdits.NameDirective).IsOverridden, "у имени в шапке отметка «задано здесь»");
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

    /// <summary>
    /// Свойство проекта с обеими границами из <c>[Range]</c> — ползунок с полем, как у Unity, и набранное в поле пишется
    /// в документ; границы таблицы метаданных — у <c>FontSize</c> — ползунка не дают (запись 378).
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_range_is_a_slider_and_a_metadata_range_is_not()
    {
        await using var studio = new LiveFormStudio();

        Emit(studio);

        var document = await studio.OpenAsync("MainWindow.axaml", ShelfForm);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(Row(panel, "FontSize").Content).Child);

        document.Select([XamlElementPath.Parse("/0")]);
        LiveFormStudio.Frame();

        var range = Assert.IsType<AxRangeField>(Assert.IsType<PluginSurface>(Row(panel, "Gap").Content).Child);

        Assert.Equal(0, range.Minimum);
        Assert.Equal(10, range.Maximum);

        Type(studio, range.NumberField, "4");

        await XamlStudio.UntilAsync(() => Text(document).Contains("Gap=\"4\"", StringComparison.Ordinal), "число из поля ползунка не записано");
    }

    /// <summary>
    /// Преобразование выбранного правят полями: поворот пишется в XAML записью Avalonia, а точка опоры — процентами
    /// (запись 379).
    /// </summary>
    [AvaloniaFact]
    public async Task The_render_transform_is_edited_by_fields_and_written_as_operations()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var rotate = Row(panel, "RenderTransform").GetVisualDescendants().OfType<AxPropertyRow>()
            .Select(static row => row.Content).OfType<AxNumberField>().Single();

        Type(studio, rotate, "45");

        await XamlStudio.UntilAsync(() => Text(document).Contains("RenderTransform=\"rotate(45deg)\"", StringComparison.Ordinal), "поворот не записан");

        var point = Row(panel, "RenderTransformOrigin").GetVisualDescendants().OfType<AxVectorField>().Single();

        Type(studio, point.Cells[0], "0");

        await XamlStudio.UntilAsync(() => Text(document).Contains("RenderTransformOrigin=\"0%,50%\"", StringComparison.Ordinal), "точка опоры не записана");
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

    /// <summary>
    /// Член, заданный элементом-свойством, правится только там: в инспекторе он — чтение, и в списке его нет, пока не
    /// включены «Все свойства» (запись 371).
    /// </summary>
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

        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Content");

        panel.View!.All.IsChecked = true;

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

    /// <summary>
    /// Протяжку за подпись видно на объекте на каждом движении, а текст не тронут; отпускание пишет одно значение —
    /// одним шагом истории (запись 364).
    /// </summary>
    [AvaloniaFact]
    public async Task A_scrub_shows_on_the_object_and_writes_one_step_on_release()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var row = Row(panel, "Width");
        var field = Number(row);
        var button = Assert.IsType<Button>(document.Shown!.ObjectAt(Go));
        var at = Grab(studio, row);

        at = Drag(studio, at, 40);

        Assert.NotEqual(120, field.Value);
        Assert.Equal(field.Value, button.Width);
        Assert.Contains("Width=\"120\"", Text(document), StringComparison.Ordinal);

        at = Drag(studio, at, 40);

        Assert.Equal(field.Value, button.Width);
        Assert.Contains("Width=\"120\"", Text(document), StringComparison.Ordinal);

        var scrubbed = AxNumberField.Format(field.Value, 15);

        studio.Window.MouseUp(at, MouseButton.Left);
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(
            () => Text(document).Contains($"Width=\"{scrubbed}\"", StringComparison.Ordinal), "протяжка не записана при отпускании");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Text(document).Contains("Width=\"120\"", StringComparison.Ordinal), "протяжка отменилась не одним шагом");
    }

    /// <summary>Esc посреди протяжки возвращает объекту прежнее значение, а текст и история не тронуты.</summary>
    [AvaloniaFact]
    public async Task Escape_in_a_scrub_returns_the_object_and_leaves_the_text()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var row = Row(panel, "Width");
        var button = Assert.IsType<Button>(document.Shown!.ObjectAt(Go));
        var before = Text(document);
        var at = Drag(studio, Grab(studio, row), 40);

        Assert.NotEqual(120, button.Width);

        Press(studio, Key.Escape);
        studio.Window.MouseUp(at, MouseButton.Left);
        LiveFormStudio.Frame();

        Assert.Equal(120, button.Width);
        Assert.Equal(120, Number(row).Value);
        Assert.Equal(before, Text(document));
        Assert.False(document.Document!.CanUndo, "отменённая протяжка оставила шаг истории");
    }

    /// <summary>
    /// Окно «Цвет» — сеанс правки: тягу по квадрату видно на объекте на каждом движении, отпускание документа не трогает,
    /// а закрытие окна пишет одну правку, которую отменяет один шаг (записи 364, 375).
    /// </summary>
    [AvaloniaFact]
    public async Task The_color_window_shows_on_the_object_and_writes_once_when_closed()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Background=\"#3C6ECF\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var field = ColorField(Row(panel, "Background"));
        var button = Assert.IsType<Button>(document.Shown!.ObjectAt(Go));
        var root = OpenColorWindow(studio, field);
        var spectrum = field.Picker!.GetVisualDescendants().OfType<AxColorSpectrum>().Single();

        root.MouseDown(At(spectrum, root, 0.2, 0.5), MouseButton.Left);
        root.MouseMove(At(spectrum, root, 0.5, 0.4));
        LiveFormStudio.Frame();

        Assert.NotEqual(Color.Parse("#3C6ECF"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        Assert.Equal(field.Color, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);

        root.MouseMove(At(spectrum, root, 0.8, 0.3));
        root.MouseUp(At(spectrum, root, 0.8, 0.3), MouseButton.Left);
        LiveFormStudio.Frame();

        // Отпускание — шаг сеанса, а не запись: документ ждёт, пока окно закроют.
        Assert.Equal(field.Color, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        Assert.Equal("#3C6ECF", Element(document, "Go").GetAttribute("Background")?.GetValueText());
        Assert.False(document.Document!.CanUndo, "шаг в окне цвета записался до закрытия окна");

        var picked = AxColorField.Format(field.Color);

        root.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
        LiveFormStudio.Frame();

        Assert.False(field.IsPickerOpen, "Enter не закрыл окно цвета");

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Background")?.GetValueText() == picked, "цвет не записан при закрытии окна");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Background")?.GetValueText() == "#3C6ECF", "сеанс окна цвета отменился не одним шагом");
    }

    /// <summary>Esc в окне «Цвет» возвращает объекту цвет начала и не трогает документ (запись 375).</summary>
    [AvaloniaFact]
    public async Task Escape_in_the_color_window_gives_the_object_back_and_leaves_the_text()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Background=\"#3C6ECF\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var before = Text(document);
        var field = ColorField(Row(panel, "Background"));
        var button = Assert.IsType<Button>(document.Shown!.ObjectAt(Go));
        var root = OpenColorWindow(studio, field);

        root.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.None, string.Empty);
        LiveFormStudio.Frame();

        Assert.NotEqual(Color.Parse("#3C6ECF"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);

        root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        LiveFormStudio.Frame();

        Assert.False(field.IsPickerOpen, "Esc не закрыл окно цвета");
        Assert.Equal(Color.Parse("#3C6ECF"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        Assert.Equal(Color.Parse("#3C6ECF"), field.Color);
        Assert.Equal(before, Text(document));
        Assert.False(document.Document!.CanUndo, "отменённый сеанс окна цвета оставил шаг истории");
    }

    /// <summary>
    /// Значение, пришедшее во время сеанса окна «Цвет», — здесь отмена в истории документа — сеанс не перебивает: поле
    /// показывает цвет сеанса, а когда окно закроют, — то, что в документе (запись 375).
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_that_arrives_during_the_color_window_shows_when_it_ends()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Background=\"#3C6ECF\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var field = ColorField(Row(panel, "Background"));

        field.RaiseEvent(new AxColorEventArgs(AxColorPicker.ColorCommittedEvent, Colors.OrangeRed));

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Background")?.GetValueText() == "#FF4500", "цвет не записан");

        var root = OpenColorWindow(studio, field);

        root.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.None, string.Empty);
        LiveFormStudio.Frame();

        var shown = field.Color;

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Background")?.GetValueText() == "#3C6ECF", "отмена не дошла до документа");
        LiveFormStudio.Frame();

        Assert.Equal(shown, field.Color);

        root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        LiveFormStudio.Frame();

        Assert.False(field.IsPickerOpen, "Esc не закрыл окно цвета");
        Assert.Equal(Color.Parse("#3C6ECF"), field.Color);
    }

    /// <summary>
    /// Окно «Цвет» поля инспектора показывает образцы — библиотеки модуля, одни на студию; текущий цвет сохраняется в
    /// них, а в процессе тестов — в память, не в профиль (записи 376).
    /// </summary>
    [AvaloniaFact]
    public async Task The_color_window_offers_the_swatch_libraries()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Background=\"#3C6ECF\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var background = ColorField(Row(panel, "Background"));
        var foreground = ColorField(Row(panel, "Foreground"));
        var store = Assert.IsType<ColorLibraries>(AxColorPicker.GetPresetStore(background));

        Assert.Same(store, AxColorPicker.GetPresetStore(foreground));
        Assert.Null(ColorLibraries.UserFolder());

        var root = OpenColorWindow(studio, background);

        try
        {
            Assert.Same(store, background.Picker!.PresetStore);
            Assert.True(
                background.Picker.GetVisualDescendants().OfType<Control>().Single(part => part.Name == "PART_PresetsSection").IsVisible,
                "в окне цвета нет образцов");
        }
        finally
        {
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            LiveFormStudio.Frame();
        }
    }

    /// <summary>
    /// Вид каналов, выбранный в окне «Цвет», встречает в следующем поле цвета: инспектор помнит его, пока студия открыта
    /// (запись 375).
    /// </summary>
    [AvaloniaFact]
    public async Task The_color_mode_chosen_once_meets_the_next_field()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var background = ColorField(Row(panel, "Background"));

        try
        {
            var root = OpenColorWindow(studio, background);

            background.Picker!.Mode = AxColorPickerMode.Hsv;
            root.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
            LiveFormStudio.Frame();

            // Поле соседней строки построено раньше, чем вид выбрали, — окно у него всё равно встречает выбранным.
            var foreground = ColorField(Row(panel, "Foreground"));

            root = OpenColorWindow(studio, foreground);

            Assert.Equal(AxColorPickerMode.Hsv, foreground.Picker!.Mode);
            Assert.Equal(AxColorPickerMode.Hsv, foreground.PickerMode);

            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            LiveFormStudio.Frame();

            Assert.False(document.Document!.CanUndo, "окно цвета без перемен записало правку");
        }
        finally
        {
            // Вид помнится на весь процесс: следующим тестам — тот, с которого студия начинает.
            background.PickerMode = AxColorPickerMode.Rgb255;
        }
    }

    /// <summary>Отступ пишется кратчайшей записью, которую прочтёт разметка: одно число, пара или все четыре.</summary>
    [AvaloniaFact]
    public async Task A_thickness_is_written_in_its_shortest_form()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Margin=\"8,4\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        // Стороны L, T, R, B: низ отличается — нужны все четыре; вернулся — снова пара; все равны — одно число.
        await Margin(3, "2", "8,4,8,2", "разные стороны записаны не все");
        await Margin(3, "4", "8,4", "пара сторон записана длинно");
        await Margin(1, "8", "8,8,8,4", "разные стороны записаны не все");
        await Margin(3, "8", "8", "равные стороны записаны не одним числом");

        async Task Margin(int side, string typed, string expected, string what)
        {
            Type(studio, VectorOf(Row(panel, "Margin")).Cells[side], typed);

            await XamlStudio.UntilAsync(() => Element(document, "Go").GetAttribute("Margin")?.GetValueText() == expected, what);
        }
    }

    /// <summary>
    /// Видимость — флажком в шапке, а не строкой списка: у разных значений он в третьем состоянии, и щелчок пишет
    /// всем одно — одним шагом истории.
    /// </summary>
    [AvaloniaFact]
    public async Task Visibility_is_a_header_check_box_with_a_third_state_for_different_values()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml",
            Form.Replace("Width=\"200\"", "Width=\"200\" IsVisible=\"False\"", StringComparison.Ordinal));
        var panel = studio.Inspector();
        var toggle = panel.View!.Enabled;

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.True(toggle.IsVisible, "у элемента нет флажка видимости");
        Assert.Equal(true, toggle.IsChecked);
        Assert.Equal("Is Visible", AutomationProperties.GetName(toggle));
        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "IsVisible");

        // Окно-корень на холсте видимостью не правят: ни флажка, ни строки.
        document.Select([]);
        await XamlStudio.UntilAsync(() => panel.View.Title.Text == "Window", "без выбора не показан корень");

        Assert.False(toggle.IsVisible, "у окна-корня флажок видимости");
        Assert.DoesNotContain(panel.List.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "IsVisible");

        document.Select([Go, Input]);
        LiveFormStudio.Frame();

        Assert.True(toggle.IsThreeState, "разные значения — не третье состояние");
        Assert.Null(toggle.IsChecked);

        toggle.IsChecked = true;

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("IsVisible")?.GetValueText() == "True" &&
                  Element(document, "Input").GetAttribute("IsVisible")?.GetValueText() == "True",
            "флажок не записал одно значение обоим");
        await XamlStudio.UntilAsync(() => toggle is { IsThreeState: false, IsChecked: true }, "флажок не показал записанное");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("IsVisible") is null &&
                  Element(document, "Input").GetAttribute("IsVisible")?.GetValueText() == "False",
            "отмена вернула видимость не одним шагом");
    }

    /// <summary>Классы стиля — жетонами в шапке: добавленный и снятый жетон — по одному шагу истории.</summary>
    [AvaloniaFact]
    public async Task Classes_are_header_tokens_and_each_change_is_one_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Width=\"120\"", "Width=\"120\" Classes=\"accent\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var tokens = HeaderRow(panel, "Classes").GetVisualDescendants().OfType<AxTokenField>().Single();

        Assert.Equal(new[] { "accent" }, tokens.Tokens);
        Assert.DoesNotContain(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Classes");

        tokens.Input!.Focus();
        studio.Window.KeyTextInput("large");
        Press(studio, Key.Enter);

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Classes")?.GetValueText() == "accent large", "жетон не дописан в классы");

        Assert.True(tokens.Remove("accent"));

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Classes")?.GetValueText() == "large", "снятый жетон остался в классах");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Classes")?.GetValueText() == "accent large", "снятие жетона — не один шаг");
    }

    /// <summary>
    /// «Только для дизайна» переносит значение в член для дизайна одной правкой — с <c>mc:Ignorable</c>, без которого
    /// <c>d:</c> не соберётся, даже если пространство дизайна документ уже объявил, а игнорировать не просил.
    /// </summary>
    [AvaloniaFact]
    public async Task Design_only_moves_the_value_to_a_design_member_and_makes_its_prefix_ignorable()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml",
            Form.Replace(
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"",
                StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForRow(Row(panel, "Width")), studio.Strings["inspector.menu.designOnly"]);

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetDesignTimeAttribute("Width") == "120" && Element(document, "Go").GetAttribute("Width") is null,
            "ширина не стала членом для дизайна");

        Assert.Contains("mc:Ignorable=\"d\"", Text(document), StringComparison.Ordinal);

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "120" &&
                  Element(document, "Go").GetDesignTimeAttribute("Width") is null,
            "перенос в члены для дизайна отменился не одним шагом");
    }

    /// <summary>У разных значений меню строки предлагает взять значение одного из выбранных — всем, одним шагом.</summary>
    [AvaloniaFact]
    public async Task Taking_one_elements_value_gives_it_to_all_selected_in_one_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Go, Input]);
        LiveFormStudio.Frame();

        var take = string.Format(CultureInfo.CurrentCulture, studio.Strings["inspector.menu.take"], "TextBox «Input»", "200");

        Choose(panel.Menus!.ForRow(Row(panel, "Width")), take);

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "200" &&
                  Element(document, "Input").GetAttribute("Width")?.GetValueText() == "200",
            "значение одного не досталось всем");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Go").GetAttribute("Width")?.GetValueText() == "120", "«Взять у» отменилось не одним шагом");
    }

    /// <summary>«Показать в XAML» выделяет атрибут в тексте формы и отдаёт клавиатуру тексту.</summary>
    [AvaloniaFact]
    public async Task Show_in_xaml_selects_the_attribute_in_the_form_text()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();
        var code = document.View.Code;

        document.Select([Go]);
        await XamlStudio.UntilAsync(() => code.Text == Text(document), "текст формы не встал в просмотр");

        Choose(panel.Menus!.ForRow(Row(panel, "Width")), studio.Strings["inspector.menu.reveal"]);

        Assert.Equal("Width=\"120\"", code.SelectedText);
        Assert.True(code.IsFocused, "клавиатура не перешла к тексту");
    }

    /// <summary>
    /// Заданное здесь копируется атрибутами разметки — без имени — и вставляется другому элементу теми свойствами,
    /// что у него есть, одним шагом; «Сбросить заданные здесь» снимает всё, кроме имени, тоже одним.
    /// </summary>
    [AvaloniaFact]
    public async Task Set_properties_copy_and_paste_onto_another_element_and_reset_together()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();
        var words = panel.List!.Words;
        var clipboard = TopLevel.GetTopLevel(panel.View)!.Clipboard!;

        document.Select([Go]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForTarget(), words["inspector.copyProperties"]);

        var copied = await clipboard.TryGetTextAsync();

        Assert.Contains("Width=\"120\"", copied, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"30\"", copied, StringComparison.Ordinal);
        Assert.Contains("Content=\"Пуск\"", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("Name", copied, StringComparison.Ordinal);

        document.Select([Input]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForTarget(), words["inspector.pasteProperties"]);

        await XamlStudio.UntilAsync(
            () => Element(document, "Input").GetAttribute("Width")?.GetValueText() == "120" &&
                  Element(document, "Input").GetAttribute("MinHeight")?.GetValueText() == "30",
            "скопированное не вставилось");

        Assert.Null(Element(document, "Input").GetAttribute("Content"));

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(
            () => Element(document, "Input").GetAttribute("Width")?.GetValueText() == "200" &&
                  Element(document, "Input").GetAttribute("MinHeight") is null,
            "вставка свойств отменилась не одним шагом");

        document.Select([Go]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForTarget(), words["inspector.resetAll"]);

        await XamlStudio.UntilAsync(
            () => Element(document, "Go") is { } go && go.GetAttribute("Width") is null && go.GetAttribute("MinHeight") is null &&
                  go.GetAttribute("Content") is null,
            "заданное не сброшено");
    }

    /// <summary>Значение строки копируется и вставляется в строку другого элемента; текст, который не прочтётся, не пишется.</summary>
    [AvaloniaFact]
    public async Task A_copied_value_pastes_into_another_row_and_unreadable_text_stays_out()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();
        var words = panel.List!.Words;
        var clipboard = TopLevel.GetTopLevel(panel.View)!.Clipboard!;

        document.Select([Go]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForRow(Row(panel, "Width")), words["inspector.copyValue"]);

        Assert.Equal("120", await clipboard.TryGetTextAsync());

        document.Select([Input]);
        LiveFormStudio.Frame();

        Choose(panel.Menus!.ForRow(Row(panel, "Width")), words["inspector.pasteValue"]);

        await XamlStudio.UntilAsync(
            () => Element(document, "Input").GetAttribute("Width")?.GetValueText() == "120", "значение не вставилось");

        await clipboard.SetTextAsync("широко");
        Choose(panel.Menus!.ForRow(Row(panel, "Width")), words["inspector.pasteValue"]);

        await XamlStudio.UntilAsync(() => Row(panel, "Width").Note is not null, "непрочтённое не показано заметкой");

        Assert.Equal("120", Element(document, "Input").GetAttribute("Width")?.GetValueText());
    }

    /// <summary>
    /// Шрифт — текст с подсказками шрифтов системы: меню поля отбирает их по набранному, а выбранный пишется сразу.
    /// </summary>
    [AvaloniaFact]
    public async Task A_font_family_offers_the_system_fonts_and_a_picked_one_is_written()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([Caption]);
        LiveFormStudio.Frame();

        var row = Row(panel, "FontFamily");
        var choices = Assert.Single(
            row.GetVisualDescendants().OfType<AxButton>(),
            button => AutomationProperties.GetName(button) == panel.List!.Words["inspector.choices"]);
        var menu = Assert.IsType<AxMenuFlyout>(choices.Flyout);

        menu.ShowAt(choices);
        LiveFormStudio.Frame();

        var fonts = menu.Items.OfType<AxMenuItem>().Select(static item => (string)item.Header!).ToList();

        Assert.NotEmpty(fonts);
        Assert.Equal(fonts.Order(StringComparer.CurrentCultureIgnoreCase), fonts);

        menu.Hide();

        var font = fonts[^1];
        var box = Box(row);

        box.Focus();
        box.SelectAll();
        studio.Window.KeyTextInput(font[..Math.Min(4, font.Length)]);
        Press(studio, Key.F4);

        Assert.All(menu.Items.OfType<AxMenuItem>(), item => Assert.Contains(font[..Math.Min(4, font.Length)], (string)item.Header!, StringComparison.CurrentCultureIgnoreCase));

        Choose([.. menu.Items.OfType<Control>()], font);
        menu.Hide();

        await XamlStudio.UntilAsync(
            () => ElementAt(document, Caption).GetAttribute("FontFamily")?.GetValueText() == font, "выбранный шрифт не записан");
    }

    /// <summary>Курсор — список стандартных, а начертание — без синонимов: написанное «Regular» стоит вариантом «Normal».</summary>
    [AvaloniaFact]
    public async Task A_cursor_and_a_weight_are_lists_and_a_synonym_reads_as_its_weight()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync(
            "MainWindow.axaml", Form.Replace("Text=\"Подпись\"", "Text=\"Подпись\" FontWeight=\"Regular\"", StringComparison.Ordinal));
        var panel = studio.Inspector();

        document.Select([Caption]);
        LiveFormStudio.Frame();

        var weight = Row(panel, "FontWeight").GetVisualDescendants().OfType<AxComboBox>().Single();
        var weights = weight.Items.OfType<AxComboBoxItem>().ToList();

        Assert.Equal(11, weights.Count);
        Assert.Equal("Normal · 400", Assert.IsType<AxComboBoxItem>(weight.SelectedItem).Content);

        weight.SelectedItem = weights.Single(item => Equals(item.Tag, "Bold"));

        await XamlStudio.UntilAsync(
            () => ElementAt(document, Caption).GetAttribute("FontWeight")?.GetValueText() == "Bold", "начертание не записано");

        var cursor = Row(panel, "Cursor").GetVisualDescendants().OfType<AxComboBox>().Single();

        cursor.SelectedItem = cursor.Items.OfType<AxComboBoxItem>().Single(item => Equals(item.Tag, "Hand"));

        await XamlStudio.UntilAsync(
            () => ElementAt(document, Caption).GetAttribute("Cursor")?.GetValueText() == "Hand", "курсор не записан");
    }

    /// <summary>Определения столбцов и строк сетки правятся текстом и проверяются разбором: непрочтённое не пишется.</summary>
    [AvaloniaFact]
    public async Task Grid_definitions_are_text_checked_by_the_parser()
    {
        const string Grid = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Grid x:Name="Table" ColumnDefinitions="*,Auto" />
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Grid);
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0")]);
        LiveFormStudio.Frame();

        var row = Row(panel, "ColumnDefinitions");

        Assert.Equal("*,Auto", Box(row).Text);

        Type(studio, Box(row), "2*,*");

        await XamlStudio.UntilAsync(
            () => Element(document, "Table").GetAttribute("ColumnDefinitions")?.GetValueText() == "2*,*", "столбцы не записаны");

        Type(studio, Box(Row(panel, "ColumnDefinitions")), "широко");

        Assert.NotNull(Row(panel, "ColumnDefinitions").Note);
        Assert.Equal("2*,*", Element(document, "Table").GetAttribute("ColumnDefinitions")?.GetValueText());
    }

    /// <summary>
    /// Значение из стиля формы после правки — то, что стоит у объекта, когда показ успокоился: правка ставит значения
    /// на месте, стили на это время снимаются и встают заново, и прочитанное посреди этого было бы значением темы
    /// (найдено живой проверкой записи 365).
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_from_the_forms_style_reads_right_after_an_edit()
    {
        const string Styled = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Styles>
                <Style Selector="Button">
                  <Setter Property="Padding" Value="4" />
                </Style>
                <Style Selector="Button.accent">
                  <Setter Property="Padding" Value="20,8" />
                </Style>
              </Window.Styles>
              <StackPanel>
                <Button x:Name="Go" Content="Пуск" Width="120" Classes="accent" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Styled);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        Assert.Equal("20,8,20,8", Row(panel, "Padding").Value?.Text);

        Type(studio, Number(Row(panel, "Width")), "150");

        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"150\"", StringComparison.Ordinal), "ширина не записана");
        LiveFormStudio.Frame();

        Assert.Equal("20,8,20,8", Row(panel, "Padding").Value?.Text);
        Assert.Equal(InspectorValueSource.Style, Row(panel, "Padding").Value?.Source);

        // Значение дал стиль с классом, а не стиль без него, хоть и тот ставит отступ: кадры стилей идут от слабого
        // к сильному, и действует последний — так же тема контрола уступает стилю формы.
        Assert.Equal("Button.accent", Row(panel, "Padding").Value?.SourceDetail);
    }

    /// <summary>
    /// Член, которого компилятор XAML Avalonia атрибутом не задаст, — только для чтения: у <c>ActualThemeVariant</c>
    /// обёртка только читает. Присоединённое без обёртки — <c>IsHoldWithMouseEnabled</c> у <c>InputElement</c> —
    /// пишется с именем владельца, а простым именем не предлагается. Записанные инспектором простыми, они роняли
    /// сборку проекта, а с ней — каждую его форму в дизайнере (запись 366). В списке оно стоит, когда записано:
    /// владелец — база всякого контрола, и родителю такой член не принадлежит (запись 372).
    /// </summary>
    [AvaloniaFact]
    public async Task A_member_the_xaml_compiler_cannot_set_is_read_only()
    {
        const string Canvased = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Canvas>
                <TextBlock Text="Подпись" InputElement.IsHoldWithMouseEnabled="True" />
              </Canvas>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Canvased);

        studio.Inspector();

        document.Select([XamlElementPath.Parse("/0")]);
        LiveFormStudio.Frame();

        var canvas = studio.Context.Inspector()!.Current!;

        Assert.True(
            Assert.Single(canvas.Properties, property => property.Id == "ActualThemeVariant").IsReadOnly,
            "ActualThemeVariant пишется, а компилятор XAML его не задаст");
        Assert.False(await canvas.WriteAsync([new InspectorWrite("ActualThemeVariant", "Light")]), "ActualThemeVariant записан");
        Assert.DoesNotContain("ActualThemeVariant", Text(document), StringComparison.Ordinal);

        document.Select([XamlElementPath.Parse("/0/0")]);
        LiveFormStudio.Frame();

        var block = studio.Context.Inspector()!.Current!;

        Assert.DoesNotContain(block.Properties, property => property.Id == "IsHoldWithMouseEnabled" && !property.IsReadOnly);

        var hold = Assert.Single(block.Properties, property => property.Id == "InputElement.IsHoldWithMouseEnabled");

        Assert.True(await block.WriteAsync([new InspectorWrite(hold.Id, "False")]));

        await XamlStudio.UntilAsync(
            () => Text(document).Contains("InputElement.IsHoldWithMouseEnabled=\"False\"", StringComparison.Ordinal),
            "присоединённое без обёртки не записано с именем владельца");
    }

    /// <summary>
    /// Незаданная кисть — поле цвета «нет цвета», а не пустая строка ввода; цвет, выбранный в нём, пишется одной правкой
    /// (запись 374).
    /// </summary>
    [AvaloniaFact]
    public async Task An_unset_brush_is_no_color_and_a_chosen_color_is_written_once()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Inspector();

        document.Select([XamlElementPath.Parse("/0")]);
        LiveFormStudio.Frame();

        var field = ColorField(Row(panel, "Background"));

        Assert.True(field.IsNone, "незаданная кисть — не «нет цвета»");
        Assert.Empty(Row(panel, "Background").GetVisualDescendants().OfType<TextBox>());

        field.RaiseEvent(new AxColorEventArgs(AxColorPicker.ColorCommittedEvent, Colors.OrangeRed));

        await XamlStudio.UntilAsync(() => Text(document).Contains("Background=\"#FF4500\"", StringComparison.Ordinal),
            "выбранный цвет не записан");

        await document.Document!.UndoAsync();

        await XamlStudio.UntilAsync(() => !Text(document).Contains("Background=", StringComparison.Ordinal),
            "цвет записан не одной правкой");
    }

    /// <summary>
    /// Цвет, написанный поверх кисти, которую стиль ставит ресурсом, — полем цвета, а не жетоном выражения: живая
    /// проверка нашла такой «#666363» жетоном с карандашом (записи 372, 374).
    /// </summary>
    [AvaloniaFact]
    public async Task A_literal_over_a_themed_brush_is_a_color_not_an_expression()
    {
        const string Themed = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Resources>
                <SolidColorBrush x:Key="Fill" Color="Red" />
              </Window.Resources>
              <Window.Styles>
                <Style Selector="Button">
                  <Setter Property="Background" Value="{DynamicResource Fill}" />
                </Style>
              </Window.Styles>
              <StackPanel>
                <Button x:Name="Go" Content="Пуск" Background="#666363" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Themed);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var row = Row(panel, "Background");

        Assert.Empty(row.GetVisualDescendants().OfType<AxChip>());
        Assert.Equal(Color.Parse("#666363"), ColorField(row).Color);
        Assert.True(row.IsOverridden, "написанный цвет не помечен заданным здесь");
    }

    /// <summary>
    /// Градиент, который ставит стиль, виден образцом самой кисти, а поля цвета у него нет: цветом его не поправишь, а
    /// пишется он только элементом (запись 374).
    /// </summary>
    [AvaloniaFact]
    public async Task A_gradient_shows_its_brush_and_no_color_field()
    {
        const string Graded = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="400" Height="300">
              <Window.Styles>
                <Style Selector="Button">
                  <Setter Property="Background">
                    <LinearGradientBrush StartPoint="0%,0%" EndPoint="100%,0%">
                      <GradientStop Color="Red" Offset="0" />
                      <GradientStop Color="Blue" Offset="1" />
                    </LinearGradientBrush>
                  </Setter>
                </Style>
              </Window.Styles>
              <StackPanel>
                <Button x:Name="Go" Content="Пуск" />
              </StackPanel>
            </Window>
            """;

        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Graded);
        var panel = studio.Inspector();

        document.Select([Go]);
        LiveFormStudio.Frame();

        var row = Row(panel, "Background");
        var swatch = Assert.Single(row.GetVisualDescendants().OfType<AxColorSwatch>(), swatch => swatch.Brush is not null);
        var gradient = Assert.IsAssignableFrom<IGradientBrush>(swatch.Brush);

        Assert.Equal([Colors.Red, Colors.Blue], gradient.GradientStops.Select(static stop => stop.Color));
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<AxColorField>(), field => field.IsEffectivelyVisible);
    }

    /// <summary>Открывает окно «Цвет» полосой поля с клавиатуры и отдаёт его — каретка уже в квадрате.</summary>
    private static TopLevel OpenColorWindow(LiveFormStudio studio, AxColorField field)
    {
        field.GetVisualDescendants().OfType<Button>().Single(swatch => swatch.Name == "PART_Swatch").Focus(NavigationMethod.Tab);
        Press(studio, Key.Enter);
        LiveFormStudio.Frame();

        Assert.True(field.IsPickerOpen, "полоса не открыла окно цвета");

        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(field.Picker!));

        Assert.NotSame(studio.Window, root);
        Assert.True(field.Picker!.GetVisualDescendants().OfType<AxColorSpectrum>().Single().IsKeyboardFocusWithin, "каретка не в квадрате");

        return root;
    }

    private static AxColorField ColorField(InspectorRow row) =>
        Assert.Single(row.GetVisualDescendants().OfType<AxColorField>());

    /// <summary>Кладёт выход проекта: сборку с полкой — поколению собирать нечего.</summary>
    private static CanonicalPath Emit(LiveFormStudio studio)
    {
        var name = "DesignerInspector" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.Xaml.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        // Сборка проекта видит только загруженные сборки: [Range] — из DataAnnotations, которую надо коснуться заранее.
        _ = typeof(System.ComponentModel.DataAnnotations.RangeAttribute).Assembly;
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

    private static InspectorRow HeaderRow(InspectorPanel panel, string id)
    {
        LiveFormStudio.Frame();

        var rows = panel.View!.HeaderRows.Children.OfType<InspectorRow>().Concat(new[] { panel.View.HeaderName.Child }.OfType<InspectorRow>());

        return Assert.Single(rows, row => row.Property.Id == id);
    }

    private static AxNumberField Number(InspectorRow row) =>
        Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(row.Content).Child);

    private static AxVectorField VectorOf(InspectorRow row) =>
        Assert.IsType<AxVectorField>(Assert.IsType<PluginSurface>(row.Content).Child);

    /// <summary>Нажимает на подпись строки и уводит указатель за мёртвую зону: протяжка началась.</summary>
    /// <returns>Где указатель.</returns>
    private static Point Grab(LiveFormStudio studio, InspectorRow row)
    {
        var label = row.GetVisualDescendants().OfType<AxScrubLabel>().Single();
        var at = label.TranslatePoint(new Point(2, label.Bounds.Height / 2), studio.Window)!.Value;

        studio.Window.MouseDown(at, MouseButton.Left);
        at += new Vector(AxScrubLabel.DeadZone + 2, 0);
        studio.Window.MouseMove(at);
        LiveFormStudio.Frame();

        return at;
    }

    /// <summary>Выбирает пункт меню с этой подписью — как человек: пункт должен быть включён.</summary>
    private static void Choose(IReadOnlyList<Control> items, string header)
    {
        var item = Assert.Single(items.OfType<AxMenuItem>(), item => Equals(item.Header, header));

        Assert.True(item.IsEnabled, $"пункт «{header}» выключен");

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        LiveFormStudio.Frame();
    }

    /// <summary>Точка внутри контрола долями его размера — в координатах окна, которому шлют указатель.</summary>
    private static Point At(Visual visual, Visual root, double x, double y) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width * x, visual.Bounds.Height * y), root)!.Value;

    /// <summary>Тянет нажатый указатель вправо.</summary>
    /// <returns>Где указатель.</returns>
    private static Point Drag(LiveFormStudio studio, Point at, double by)
    {
        at += new Vector(by, 0);
        studio.Window.MouseMove(at);
        LiveFormStudio.Frame();

        return at;
    }

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

    private static XamlElement ElementAt(LiveFormDocument document, XamlElementPath path) =>
        path.Resolve(document.Document!.Syntax) ?? throw new InvalidOperationException("элемента нет в тексте");

    private static XamlElement Element(LiveFormDocument document, string name) =>
        document.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);
}
