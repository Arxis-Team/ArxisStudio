using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панель инспектора: строки по разделам, поля редакторов, каретка и набранное — по правилу UX на тест
/// (запись 362).
/// </summary>
[Collection(StudioStateCollection.Name)]
public class InspectorPanelTests
{
    /// <summary>Каждая строка называет диктору своё свойство, а пояснением — значение и откуда оно.</summary>
    [AvaloniaFact]
    public void Every_row_names_its_property_to_the_screen_reader()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        target.Put("Width", new InspectorValue("120") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var cells = Cells(panel);

        Assert.NotEmpty(cells);

        foreach (var cell in cells)
            Assert.Equal(cell.Row.Property.Label, AutomationProperties.GetName(cell));

        var width = Cell(panel, "Width");

        Assert.Contains("120", AutomationProperties.GetHelpText(width), StringComparison.Ordinal);
        Assert.Contains(studio.Strings["inspector.source.local"], AutomationProperties.GetHelpText(width), StringComparison.Ordinal);
    }

    /// <summary>
    /// Показанную цель просят добавить то, что метаданные велят показывать всегда: подсказку и имя для диктора, о
    /// которых забывают.
    /// </summary>
    [AvaloniaFact]
    public void A_shown_target_is_asked_to_include_what_the_tables_want_always()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        Assert.Contains("ToolTip.Tip", target.Included);
        Assert.Contains("AutomationProperties.Name", target.Included);
        Assert.NotNull(panel.List);
    }

    /// <summary>
    /// Сегменты, которым тесно рядом с подписью, уходят под неё во всю ширину, а не обрезаются у края колонки.
    /// </summary>
    [AvaloniaFact]
    public void Segments_that_do_not_fit_beside_the_label_go_under_it()
    {
        using var studio = new InspectorStudio();

        studio.Window.Width = 360;

        var panel = studio.Panel();

        using var shown = studio.Service.Show(new FakeTarget(typeof(TextBlock), FakeTarget.Property("TextWrapping", typeof(TextWrapping))));

        studio.Frame();

        var row = Row(panel, "TextWrapping");
        var segments = row.GetVisualDescendants().OfType<AxSegmentedControl>().Single();
        var label = row.GetVisualDescendants().OfType<AxScrubLabel>().Single();

        Assert.True(segments.Bounds.Width >= segments.DesiredSize.Width - 0.5, "сегменты обрезаны у края колонки");
        Assert.True(
            segments.TranslatePoint(default, row)!.Value.Y >= label.TranslatePoint(new Point(0, label.Bounds.Height), row)!.Value.Y - 0.5,
            "тесные сегменты остались рядом с подписью");
    }

    /// <summary>
    /// Выравнивание — сегментами-значками в порядке «к началу, по центру, к концу, растянуть»; каждый значок называет
    /// себя диктору и подсказке, а пишет значение перечисления (запись 364).
    /// </summary>
    [AvaloniaFact]
    public void Alignment_is_icon_segments_that_name_themselves()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var segments = Row(panel, "HorizontalAlignment").GetVisualDescendants().OfType<AxSegmentedControl>().Single();
        var items = segments.Items.OfType<AxSegmentItem>().ToList();

        Assert.Equal(new[] { "Left", "Center", "Right", "Stretch" }, items.Select(static item => (string)item.Tag!));
        Assert.All(items, static item => Assert.IsType<AxIcon>(item.Content));
        Assert.Equal(
            new[] { studio.Strings["choice.left"], studio.Strings["choice.center"], studio.Strings["choice.right"], studio.Strings["choice.stretch"] },
            items.Select(static item => AutomationProperties.GetName(item)));

        segments.SelectedItem = items[2];
        studio.Frame();

        Assert.Equal("Right", Assert.Single(Assert.Single(target.Writes)).Text);
    }

    /// <summary>
    /// Цель того же типа получает те же строки: каретка остаётся в поле, набранное — в нём же, и пишется оно новой
    /// цели.
    /// </summary>
    [AvaloniaFact]
    public void Rows_outlive_a_new_target_of_the_same_type_with_the_caret_and_the_typed_text()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var first = Button();
        var second = Button();

        using var shownFirst = studio.Service.Show(first);

        studio.Frame();

        var row = Row(panel, "Content");
        var box = Box(row);

        box.Focus();
        box.SelectAll();
        studio.Window.KeyTextInput("abc");
        studio.Frame();

        using var shownSecond = studio.Service.Show(second);

        studio.Frame();

        Assert.Same(row, Row(panel, "Content"));
        Assert.True(box.IsFocused, "смена цели того же типа увела каретку из поля");
        Assert.Equal("abc", box.Text);

        Press(studio, Key.Enter);

        Assert.Empty(first.Writes);
        Assert.Equal([new InspectorWrite("Content", "abc")], Assert.Single(second.Writes));
    }

    /// <summary>
    /// Значения спрашивают только у строк, которые видно: у цели три сотни свойств, а на экране — десятки.
    /// </summary>
    [AvaloniaFact]
    public void Values_are_asked_only_for_rows_on_screen()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new FakeTarget(
            typeof(Button),
            Enumerable.Range(0, 300).Select(at => FakeTarget.Property($"Probe{at:D3}", typeof(double))).ToArray());

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var visible = Cells(panel).Where(static cell => cell.IsVisible && cell.IsAttachedToVisualTree())
            .Select(static cell => cell.Row.Property.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(target.Asked);
        Assert.True(target.Asked.Count < 60, $"спросили {target.Asked.Count} значений из трёхсот");
        Assert.All(target.Asked.Keys, id => Assert.Contains(id, visible));

        panel.View!.List.ScrollIntoView(Cell(panel, "Probe299"));
        studio.Frame();

        Assert.Contains("Probe299", target.Asked.Keys);
        Assert.Equal(1, target.Asked["Probe000"]);
    }

    /// <summary>
    /// Поле плагина, упавшее в раскладке, становится строкой о сбое, редактор, упавший на постройке поля, —
    /// тоже, а остальные строки живут; сбои засчитаны плагину.
    /// </summary>
    [AvaloniaFact]
    public void A_plugin_field_that_fails_becomes_a_stub_and_the_other_rows_live()
    {
        using var studio = new InspectorStudio();
        var failures = new List<PluginFailure>();

        studio.Guard.Failed += (_, failure) => failures.Add(failure);
        Plugin(studio);
        studio.Raise();

        var panel = studio.Panel();
        var target = new FakeTarget(
            typeof(Button),
            FakeTarget.Property("Width", typeof(double)),
            new InspectorProperty("Falling", new InspectorTypeInfo("Probe.Falling")),
            new InspectorProperty("Crashing", new InspectorTypeInfo("Probe.Crashing")));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var falling = Assert.IsType<PluginSurface>(Row(panel, "Falling").Content);

        Assert.True(falling.IsBroken, "упавший замер поля не заменён заглушкой");
        Assert.Equal(studio.Strings["inspector.fieldFailed"], Assert.IsType<TextBlock>(Row(panel, "Crashing").Content).Text);
        Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(Row(panel, "Width").Content).Child);
        Assert.Contains(failures, failure => failure.PluginId == "probe.fields" && failure.What.StartsWith("раскладка", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.PluginId == "probe.fields" && failure.Message.Contains("поле не строится", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пункт меню из вклада плагина зовётся через шов плагина: упавший пункт засчитан ему, а панель живёт (запись 364).
    /// </summary>
    [AvaloniaFact]
    public void A_plugins_menu_item_runs_through_its_guard_and_its_fault_is_counted_to_it()
    {
        using var studio = new InspectorStudio();
        var failures = new List<PluginFailure>();

        studio.Guard.Failed += (_, failure) => failures.Add(failure);
        TestAssembly.EmitPlugin(studio.Plugins, "probe.menus", "Probe.Menus", source: """
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;

            namespace Probe.Menus;

            public sealed class Entry : StudioPlugin
            {
            }

            public sealed class Falling : InspectorMenu
            {
                public override void Fill(InspectorMenuContext menu) =>
                    menu.Add("Упасть", () => throw new System.InvalidOperationException("пункт упал"));
            }
            """);
        studio.Raise();

        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        var item = Assert.Single(panel.Menus!.ForRow(Row(panel, "Width")).OfType<AxMenuItem>(), item => Equals(item.Header, "Упасть"));

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        studio.Frame();

        Assert.Contains(failures, failure => failure.PluginId == "probe.menus" && failure.Message.Contains("пункт упал", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, failure => failure.PluginId != "probe.menus");
        Assert.NotNull(panel.List!.Target);
    }

    /// <summary>
    /// Заголовок раздела никогда не выбран: стрелки проходят мимо него, а щелчок по нему сворачивает раздел, не
    /// ставя на него выбор.
    /// </summary>
    [AvaloniaFact]
    public void Group_headers_are_never_selected()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        var list = panel.View!.List;
        var first = Cells(panel)[0];

        first.Focus(NavigationMethod.Directional);
        studio.Frame();

        for (var step = 0; step < 6; step++)
        {
            Press(studio, Key.Down);
            Assert.IsType<PropertyCell>(list.SelectedItem);
        }

        var group = panel.List!.Items.OfType<GroupCell>().Last();
        var point = group.Header.TranslatePoint(new Point(4, 4), studio.Window)!.Value;

        studio.Window.MouseDown(point, MouseButton.Left);
        studio.Window.MouseUp(point, MouseButton.Left);
        studio.Frame();

        Assert.True(panel.List.IsFolded(group.CategoryId), "щелчок по заголовку не свернул раздел");
        Assert.IsType<PropertyCell>(list.SelectedItem);
        Assert.DoesNotContain(panel.List.Items.OfType<GroupCell>(), cell => cell.IsSelected);
    }

    /// <summary>
    /// Ctrl+Z отменяет набранное в поле, а без набранного — шаг истории того, кто показал цель; Ctrl+Y — вперёд.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Z_undoes_the_typing_in_a_field_and_steps_the_targets_history_otherwise()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(Row(panel, "Content"));

        box.Focus();
        studio.Window.KeyTextInput("x");
        studio.Frame();

        Press(studio, Key.Z, RawInputModifiers.Control);

        Assert.Empty(target.Steps);

        Cell(panel, "Content").Focus(NavigationMethod.Directional);
        studio.Frame();

        Press(studio, Key.Z, RawInputModifiers.Control);
        Press(studio, Key.Y, RawInputModifiers.Control);

        Assert.Equal([true, false], target.Steps);
    }

    /// <summary>Значение, сменившееся снаружи, не затирает набранного в поле; Esc показывает новое.</summary>
    [AvaloniaFact]
    public void A_value_changed_outside_keeps_what_is_typed_in_the_field()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var box = Box(Row(panel, "Content"));

        box.Focus();
        box.SelectAll();
        studio.Window.KeyTextInput("abc");
        studio.Frame();

        target.Set("Content", new InspectorValue("снаружи") { Source = InspectorValueSource.Local });
        studio.Frame();

        Assert.Equal("abc", box.Text);

        Press(studio, Key.Escape);

        Assert.Equal("снаружи", box.Text);
    }

    /// <summary>
    /// Поиск, не тронувший строку, оставляет её ячейку на месте: перестроенный целиком список снял бы и
    /// каретку, и поле.
    /// </summary>
    [AvaloniaFact]
    public void A_search_keeps_the_cells_it_does_not_drop()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        var width = Cell(panel, "Width");

        panel.View!.Query.Text = "Wid";
        studio.Frame();

        Assert.Same(width, Cell(panel, "Width"));
        Assert.True(width.IsAttachedToVisualTree());
        Assert.DoesNotContain(Cells(panel), cell => cell.Row.Property.Id == "Content");

        panel.View.Query.Text = string.Empty;
        studio.Frame();

        Assert.Same(width, Cell(panel, "Width"));
    }

    /// <summary>Заданное здесь помечено строкой, а Delete на строке сбрасывает его — одной записью.</summary>
    [AvaloniaFact]
    public void A_written_value_marks_its_row_and_delete_resets_it()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        target.Put("Width", new InspectorValue("120") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        Assert.True(Row(panel, "Width").IsOverridden);
        Assert.False(Row(panel, "Height").IsOverridden);

        Cell(panel, "Width").Focus(NavigationMethod.Directional);
        studio.Frame();
        Press(studio, Key.Delete);

        Assert.Equal([new InspectorWrite("Width", null)], Assert.Single(target.Writes));
        Assert.False(Row(panel, "Width").IsOverridden);
    }

    /// <summary>Число из поля пишется одной записью, инвариантным текстом.</summary>
    [AvaloniaFact]
    public void A_number_typed_in_a_field_is_one_write()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var field = Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(Row(panel, "Opacity").Content).Child);

        field.Focus();
        field.SelectAll();
        studio.Window.KeyTextInput("0.5");
        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("Opacity", "0.5")], Assert.Single(target.Writes));
    }

    /// <summary>
    /// Выражение стоит жетоном, а не полем редактора; Enter на строке открывает его текстом, и пишется текст.
    /// </summary>
    [AvaloniaFact]
    public void An_expression_shows_as_a_token_and_opens_as_text()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        target.Put("Width", new InspectorValue("100") { Source = InspectorValueSource.Expression, Written = "{Binding Size}" });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var row = Row(panel, "Width");

        Assert.IsType<AxChip>(row.Content);

        Cell(panel, "Width").Focus(NavigationMethod.Directional);
        studio.Frame();
        Press(studio, Key.Enter);

        var box = Assert.IsAssignableFrom<TextBox>(row.Content);

        Assert.True(box.IsFocused, "открытое текстом выражение не получило каретку");
        Assert.Equal("{Binding Size}", box.Text);

        box.SelectAll();
        studio.Window.KeyTextInput("{Binding Width}");
        Press(studio, Key.Enter);

        Assert.Equal([new InspectorWrite("Width", "{Binding Width}")], Assert.Single(target.Writes));
    }

    /// <summary>
    /// Дополнительное находит поиск, скрытое — только «Все свойства», и стоит оно своим разделом в конце.
    /// </summary>
    [AvaloniaFact]
    public void A_search_finds_advanced_properties_and_hidden_ones_wait_for_all()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new FakeTarget(
            typeof(Button),
            FakeTarget.Property("Width", typeof(double)),
            FakeTarget.Property("ClipToBounds", typeof(bool)),
            FakeTarget.Property("Bounds", typeof(Rect)));

        using var shown = studio.Service.Show(target);

        studio.Frame();

        Assert.Equal(["Width"], Ids(panel));

        panel.View!.Query.Text = "Clip";
        studio.Frame();

        Assert.Equal(["ClipToBounds"], Ids(panel));

        panel.View.Query.Text = "Bounds";
        studio.Frame();

        Assert.Equal(["ClipToBounds"], Ids(panel));

        panel.View.Query.Text = string.Empty;
        panel.View.All.IsChecked = true;
        studio.Frame();

        var groups = panel.List!.Items.OfType<GroupCell>().Select(static cell => cell.CategoryId).ToList();

        Assert.Equal("hidden", groups[^1]);
        Assert.Contains("ClipToBounds", Ids(panel));

        panel.List.Fold("hidden", folded: false);
        studio.Frame();

        Assert.Contains("Bounds", Ids(panel));
    }

    /// <summary>
    /// Пустая панель говорит, что ничего не выбрано, а закрытая закреплённая цель — что закреплённое закрыто, и
    /// предлагает снять замок.
    /// </summary>
    [AvaloniaFact]
    public void Nothing_shown_says_so_and_a_closed_locked_target_offers_to_unlock()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var view = panel.View!;

        Assert.True(view.Empty.IsVisible);
        Assert.Equal(studio.Strings["inspector.empty"], view.EmptyTitle.Text);
        Assert.False(view.Unlock.IsVisible);

        var target = Button();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        Assert.False(view.Empty.IsVisible);
        Assert.Equal("Button", view.Title.Text);

        view.Lock.IsChecked = true;
        studio.Frame();
        target.Shut();
        studio.Frame();

        Assert.True(view.Empty.IsVisible);
        Assert.Equal(studio.Strings["inspector.closed"], view.EmptyTitle.Text);
        Assert.True(view.Unlock.IsVisible);

        view.Unlock.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        studio.Frame();

        Assert.False(studio.Service.IsLocked);
        Assert.False(view.Lock.IsChecked);
        Assert.Equal(studio.Strings["inspector.empty"], view.EmptyTitle.Text);
    }

    /// <summary>Ctrl+стрелка влево сворачивает раздел строки и ставит каретку на его заголовок; вправо — раскрывает.</summary>
    [AvaloniaFact]
    public void Ctrl_left_folds_the_rows_section_and_keeps_the_caret_on_its_header()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Button());

        studio.Frame();

        var width = Cell(panel, "Width");
        var category = panel.List!.CategoryOf(width.Row.Property);

        width.Focus(NavigationMethod.Directional);
        studio.Frame();
        Press(studio, Key.Left, RawInputModifiers.Control);

        var header = panel.List!.GroupOf(category)!.Header;

        Assert.True(panel.List.IsFolded(category));
        Assert.DoesNotContain(Cells(panel), cell => cell.Row.Property.Id == "Width");
        Assert.True(header.IsFocused, "свёрнутый раздел оставил каретку нигде");

        Press(studio, Key.Right, RawInputModifiers.Control);

        Assert.False(panel.List.IsFolded(category));
        Assert.Contains(Cells(panel), cell => cell.Row.Property.Id == "Width");
    }

    /// <summary>
    /// Строка, которая ушла с экрана, отобралась прочь и вернулась в новой ячейке, показывает значение, сменившееся,
    /// пока её не было.
    /// </summary>
    [AvaloniaFact]
    public void A_row_back_in_a_new_cell_shows_the_value_it_missed()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new FakeTarget(
            typeof(Button),
            Enumerable.Range(0, 300).Select(at => FakeTarget.Property($"Probe{at:D3}", typeof(double))).ToArray());

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var list = panel.View!.List;
        var first = Row(panel, "Probe000");

        list.ScrollIntoView(Cell(panel, "Probe299"));
        studio.Frame();
        target.Set("Probe000", new InspectorValue("42") { Source = InspectorValueSource.Local });
        panel.View.Query.Text = "Probe299";
        studio.Frame();
        panel.View.Query.Text = string.Empty;
        studio.Frame();
        list.ScrollIntoView(Cell(panel, "Probe000"));
        studio.Frame();

        Assert.Same(first, Row(panel, "Probe000"));
        Assert.Equal(42d, Assert.IsType<AxNumberField>(Assert.IsType<PluginSurface>(first.Content).Child).Value);
    }

    /// <summary>Ctrl со стрелкой в текстовом поле ходит по словам и раздела не сворачивает.</summary>
    [AvaloniaFact]
    public void Ctrl_arrows_in_a_text_field_move_by_words_and_leave_the_section_open()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Button();

        target.Put("Content", new InspectorValue("один два") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var row = Row(panel, "Content");
        var box = Box(row);

        box.Focus();
        box.CaretIndex = box.Text!.Length;
        Press(studio, Key.Left, RawInputModifiers.Control);

        Assert.Equal("один ".Length, box.CaretIndex);
        Assert.True(box.IsFocused, "Ctrl+← увёл каретку из поля");
        Assert.False(panel.List!.IsFolded(panel.List.CategoryOf(row.Property)), "Ctrl+← в поле свернул раздел");
    }

    private static FakeTarget Button() => new(
        typeof(Button),
        FakeTarget.Property("Width", typeof(double)),
        FakeTarget.Property("Height", typeof(double)),
        FakeTarget.Property("Margin", typeof(Thickness)),
        FakeTarget.Property("HorizontalAlignment", typeof(HorizontalAlignment)),
        FakeTarget.Property("Background", typeof(IBrush)),
        FakeTarget.Property("IsEnabled", typeof(bool)),
        FakeTarget.Property("Content", typeof(object)),
        FakeTarget.Property("Opacity", typeof(double)));

    /// <summary>Плагин с двумя сломанными редакторами: поле одного падает в замере, другой не строит поле вовсе.</summary>
    private static void Plugin(InspectorStudio studio) =>
        TestAssembly.EmitPlugin(studio.Plugins, "probe.fields", "Probe.Fields", source: """
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;
            using Avalonia.Controls;

            namespace Probe.Fields;

            public sealed class Entry : StudioPlugin
            {
            }

            [PropertyEditor("Probe.Falling")]
            public sealed class FallingEditor : PropertyEditor
            {
                public override InspectorField CreateField(InspectorFieldContext context) => new FallingField();
            }

            public sealed class FallingField : InspectorField
            {
                public override Control View { get; } = new Falling();

                public override void Show(InspectorValue value)
                {
                }
            }

            public sealed class Falling : Border
            {
                protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) =>
                    throw new System.InvalidOperationException("замер не удался");
            }

            [PropertyEditor("Probe.Crashing")]
            public sealed class CrashingEditor : PropertyEditor
            {
                public override InspectorField CreateField(InspectorFieldContext context) =>
                    throw new System.InvalidOperationException("поле не строится");
            }
            """);

    private static List<PropertyCell> Cells(InspectorPanel panel) =>
        panel.List!.Items.OfType<PropertyCell>().ToList();

    private static List<string> Ids(InspectorPanel panel) =>
        Cells(panel).Select(static cell => cell.Row.Property.Id).ToList();

    private static PropertyCell Cell(InspectorPanel panel, string id) =>
        Assert.Single(Cells(panel), cell => cell.Row.Property.Id == id);

    private static InspectorRow Row(InspectorPanel panel, string id) => Cell(panel, id).Row;

    private static TextBox Box(InspectorRow row) =>
        row.GetVisualDescendants().OfType<TextBox>().First();

    private static void Press(InspectorStudio studio, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        studio.Window.KeyPress(key, modifiers, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, modifiers, PhysicalKey.None, string.Empty);
        studio.Frame();
    }
}
