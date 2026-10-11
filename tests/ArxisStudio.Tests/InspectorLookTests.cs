using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Вид инспектора, как у Unity: подписи словами, разделы полосами компонентов со значком и ⋮, шапка со значком типа и
/// подзаголовком-типом, строки без заливки, плотный шаг и баннер «Все свойства» (запись 377).
/// </summary>
/// <remarks>Очередь общая: проверка шага меняет плотность — ресурсы приложения, одного на процесс.</remarks>
[Collection(StudioStateCollection.Name)]
public class InspectorLookTests
{
    /// <summary>
    /// Имя члена — словами, как <c>NicifyVariableName</c> у Unity: границы регистра, аббревиатуры, цифры; у
    /// присоединённого — без владельца, директива — как написана, данная подпись — её.
    /// </summary>
    [Fact]
    public void Names_read_as_words_like_unity()
    {
        Assert.Equal("Horizontal Alignment", InspectorLabels.Words("HorizontalAlignment"));
        Assert.Equal("Z Index", InspectorLabels.Words("ZIndex"));
        Assert.Equal("Is UI Element", InspectorLabels.Words("IsUIElement"));
        Assert.Equal("Min Width", InspectorLabels.Words("MinWidth"));
        Assert.Equal("Grid 2D", InspectorLabels.Words("Grid2D"));
        Assert.Equal("Speed", InspectorLabels.Words("m_speed"));

        Assert.Equal("Row", InspectorLabels.Of(FakeTarget.Property("Grid.Row", typeof(int))));
        Assert.Equal("x:Name", InspectorLabels.Of(FakeTarget.Property("x:Name", typeof(string))));
        Assert.Equal("d:DesignWidth", InspectorLabels.Of(FakeTarget.Property("d:DesignWidth", typeof(double))));
        Assert.Equal("Ширина", InspectorLabels.Of(FakeTarget.Property("Width", typeof(double)) with { DisplayName = "Ширина" }));
    }

    /// <summary>
    /// Строка подписана словами, диктор слышит ту же подпись, а точное имя XAML — первой строкой подсказки подписи; имя
    /// для диктора у присоединённого — без владельца, в своём разделе.
    /// </summary>
    [AvaloniaFact]
    public void A_row_is_labelled_in_words_and_its_tip_names_the_xaml()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var alignment = Row(panel, "HorizontalAlignment");
        var label = alignment.GetVisualDescendants().OfType<AxScrubLabel>().Single(label => ReferenceEquals(label.TemplatedParent, alignment));

        Assert.Equal("Horizontal Alignment", alignment.Label);
        Assert.Equal("Horizontal Alignment", AutomationProperties.GetName(alignment));
        Assert.StartsWith("HorizontalAlignment", Assert.IsType<string>(ToolTip.GetTip(label)), StringComparison.Ordinal);

        var name = Row(panel, "AutomationProperties.Name");

        Assert.Equal("Name", name.Label);
        Assert.Equal("accessibility", panel.List!.CategoryOf(name.Property));
    }

    /// <summary>Алфавит — по подписи: «Name» присоединённого встаёт среди «N», а не среди «A» своего владельца.</summary>
    [AvaloniaFact]
    public void The_alphabetical_order_follows_the_label()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Target());

        studio.Frame();

        panel.View!.Alphabetical.IsChecked = true;
        studio.Frame();

        Assert.Equal(["Height", "HorizontalAlignment", "AutomationProperties.Name", "Opacity", "Width"], Ids(panel));
    }

    /// <summary>
    /// Поиск находит строку по подписи словами, по ней же без пробелов и по точному имени XAML — даже когда подпись
    /// дана и с именем не сходится.
    /// </summary>
    [AvaloniaFact]
    public void The_search_finds_a_row_by_its_words_and_by_its_xaml_name()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new FakeTarget(
            typeof(Button),
            FakeTarget.Property("Width", typeof(double)),
            FakeTarget.Property("HorizontalAlignment", typeof(HorizontalAlignment)),
            FakeTarget.Property("Elevation", typeof(double)) with { DisplayName = "Drop Shadow" });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        foreach (var (query, found) in new[]
                 {
                     ("horizontal al", "HorizontalAlignment"),
                     ("HorizontalAlignment", "HorizontalAlignment"),
                     ("drop sh", "Elevation"),
                     ("dropshadow", "Elevation"),
                     ("Elevation", "Elevation"),
                 })
        {
            panel.View!.Query.Text = query;
            studio.Frame();

            Assert.True(Ids(panel) is [var only] && only == found, $"«{query}» нашёл [{string.Join(", ", Ids(panel))}]");
        }
    }

    /// <summary>
    /// Раздел — полоса компонента: значок раздела, ⋮ справа; меню раздела сбрасывает заданное в нём одной правкой и
    /// не трогает соседние разделы.
    /// </summary>
    [AvaloniaFact]
    public async Task A_section_is_a_band_with_its_icon_and_its_menu_resets_it_in_one_write()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = Target();

        target.Put("Width", new InspectorValue("120") { Source = InspectorValueSource.Local });
        target.Put("Height", new InspectorValue("40") { Source = InspectorValueSource.Local });
        target.Put("Opacity", new InspectorValue("0.5") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var layout = Group(panel, "layout");

        Assert.Equal(AxGroupHeaderAppearance.Band, layout.Header.Appearance);
        Assert.Same(AxIcons.Grid, layout.Header.Icon);
        Assert.Same(layout.More, layout.Header.Actions);
        Assert.Equal(studio.Strings["inspector.sectionMenu"], AutomationProperties.GetName(layout.More));

        // Полоса — во весь шаг строки, без просветов; ⋮ — квадрат поля, а не полоса шире подписи.
        Assert.Equal(Metric(panel, "AxPropertyRowHeight"), layout.Header.Bounds.Height);
        Assert.Equal(Metric(panel, "AxPropertyFieldHeight"), layout.More.Bounds.Width);
        Assert.Equal(Metric(panel, "AxPropertyFieldHeight"), layout.More.Bounds.Height);

        // ⋮ — не под полосой прокрутки: она лежит поверх списка у правого края, и нажатие досталось бы ей.
        var list = panel.View!.List;
        var right = layout.More.TranslatePoint(new Point(layout.More.Bounds.Width, 0), list)!.Value.X;

        Assert.True(right <= list.Bounds.Width - Metric(panel, "AxScrollBarLane"), $"⋮ кончается на {right} из {list.Bounds.Width}: под полосой прокрутки");

        var reset = panel.Menus!.ForSection("layout").OfType<AxMenuItem>()
            .Single(item => Equals(item.Header, studio.Strings["inspector.resetSection"]));

        Assert.True(reset.IsEnabled);

        reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await XamlStudio.UntilAsync(() => target.Writes.Count > 0, "раздел не сброшен");

        var write = Assert.Single(target.Writes);

        Assert.Equal(["Height", "Width"], write.Select(static item => item.PropertyId).Order(StringComparer.Ordinal));
        Assert.All(write, static item => Assert.Null(item.Text));
    }

    /// <summary>Меню раздела открывают ⋮ на его полосе и Shift+F10 на заголовке — меню раздела, а не всей цели.</summary>
    [AvaloniaFact]
    public void The_section_menu_opens_from_its_button_and_from_the_keyboard()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Target());

        studio.Frame();

        var layout = Group(panel, "layout");
        var section = studio.Strings["inspector.resetSection"];

        layout.More.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        studio.Frame();

        Assert.Contains(section, Menu(studio));
        Assert.DoesNotContain(studio.Strings["inspector.resetAll"], Menu(studio));

        studio.Window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().Single().Close();
        studio.Frame();

        Assert.Empty(Menu(studio));
        Assert.True(layout.Header.Focus(NavigationMethod.Directional), "заголовок раздела не взял клавиатуру");

        studio.Window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.None, string.Empty);
        studio.Frame();

        Assert.Contains(section, Menu(studio));
    }

    /// <summary>Шапка без своего значка берёт значок типа из метаданных: у кнопки — указатель.</summary>
    [AvaloniaFact]
    public void The_header_takes_the_icon_of_its_type()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Target());

        studio.Frame();

        Assert.True(panel.View!.HeaderIcon.IsVisible, "у шапки нет значка");
        Assert.Same(AxIcons.Pointer, panel.View.HeaderIcon.Data);
        Assert.Same(AxIcons.Pointer, studio.Catalog.IconOf(InspectorTypeInfo.Of(typeof(Avalonia.Controls.Primitives.ToggleButton))));
        Assert.Same(AxIcons.Component, studio.Catalog.IconOf(InspectorTypeInfo.Of(typeof(Border))));
    }

    /// <summary>
    /// Строку, в поле которой каретка, не красят — ни заливкой, ни меткой выбора: каретку видно рамкой поля. Шаг строки
    /// — <c>AxPropertyRowHeight</c>, поле — <c>AxPropertyFieldHeight</c>, в любой плотности: строки плотнее, а
    /// попадание не мельче. Поле с кнопкой вариантов внутри — той же высоты: кнопка не распирает его.
    /// </summary>
    /// <remarks>Плотность — ресурсы приложения, общие на процесс: ступень снимается в <c>finally</c>.</remarks>
    [AvaloniaFact]
    public void A_row_with_the_caret_in_its_field_is_not_painted_and_rows_keep_their_step_in_any_density()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var choices = new InspectorConstraints { Choices = [new InspectorChoice("Inter"), new InspectorChoice("Consolas")] };

        using var shown = studio.Service.Show(new FakeTarget(
            typeof(Button),
            FakeTarget.Property("Width", typeof(double)),
            FakeTarget.Property("Typeface", typeof(string)) with { Constraints = choices }));

        studio.Frame();

        var cell = Cell(panel, "Width");
        var box = cell.Row.GetVisualDescendants().OfType<TextBox>().First();
        var suggested = Cell(panel, "Typeface");
        var suggestedBox = suggested.Row.GetVisualDescendants().OfType<AxTextBox>().First();

        Assert.NotNull(suggestedBox.InnerRightContent);

        Assert.True(box.Focus());
        studio.Frame();

        Assert.True(cell.IsSelected, "каретка в поле не выбрала строку");

        var presenter = cell.GetVisualDescendants().OfType<ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");
        var marker = cell.GetVisualDescendants().OfType<Border>().First(part => part.Name == "PART_SelectionMarker");

        Assert.True(presenter.Background is null || presenter.Background is ISolidColorBrush { Color.A: 0 }, "строку с кареткой закрасили");
        Assert.False(marker.IsVisible, "у строки с кареткой метка выбора");

        try
        {
            foreach (var density in new[] { StudioDensity.Normal, StudioDensity.Compact, StudioDensity.Comfortable })
            {
                StudioTheming.Apply(density);
                studio.Frame();

                Assert.True(Metric(panel, "AxPropertyRowHeight") == cell.Bounds.Height, $"шаг строки при плотности {density} — {cell.Bounds.Height}");
                Assert.True(Metric(panel, "AxPropertyFieldHeight") == box.Bounds.Height, $"поле при плотности {density} — {box.Bounds.Height}");
                Assert.True(
                    Metric(panel, "AxPropertyRowHeight") == suggested.Bounds.Height &&
                    Metric(panel, "AxPropertyFieldHeight") == suggestedBox.Bounds.Height,
                    $"поле с вариантами при плотности {density} — {suggestedBox.Bounds.Height}, строка — {suggested.Bounds.Height}");
            }
        }
        finally
        {
            foreach (var tier in Application.Current!.Resources.MergedDictionaries.OfType<ResourceInclude>()
                         .Where(static include => include.Source?.OriginalString.Contains("/Density/", StringComparison.Ordinal) == true).ToList())
            {
                Application.Current.Resources.MergedDictionaries.Remove(tier);
            }
        }
    }

    /// <summary>
    /// «Все свойства» — отладочный вид: баннер говорит, что он включён, а «Выйти» выключает его и оставляет каретку на
    /// переключателе. Закрытый крестиком баннер другие отборы не поднимают.
    /// </summary>
    [AvaloniaFact]
    public void All_properties_show_a_banner_that_leads_back()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();

        using var shown = studio.Service.Show(Target());

        studio.Frame();

        Assert.False(panel.View!.AllBanner.IsVisible);

        panel.View.All.IsChecked = true;
        studio.Frame();

        Assert.True(panel.View.AllBanner.IsVisible, "«Все свойства» без баннера");
        Assert.True(panel.List!.ShowAll);

        Assert.True(panel.View.LeaveAll.Focus(NavigationMethod.Tab), "«Выйти» не взяла клавиатуру");
        panel.View.LeaveAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        studio.Frame();

        Assert.False(panel.View.All.IsChecked);
        Assert.False(panel.View.AllBanner.IsVisible, "баннер остался после «Выйти»");
        Assert.False(panel.List.ShowAll);
        Assert.True(panel.View.All.IsFocused, "каретка ушла вместе с баннером");

        // Закрытый крестиком баннер другие отборы не поднимают; следующий вход — поднимает.
        panel.View.All.IsChecked = true;
        studio.Frame();

        panel.View.AllBanner.GetVisualDescendants().OfType<Button>().Single(part => part.Name == "PART_Close")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.View.Alphabetical.IsChecked = true;
        studio.Frame();

        Assert.True(panel.List.ShowAll);
        Assert.False(panel.View.AllBanner.IsVisible, "закрытый баннер вернулся от другого отбора");

        panel.View.All.IsChecked = false;
        panel.View.All.IsChecked = true;
        studio.Frame();

        Assert.True(panel.View.AllBanner.IsVisible, "новый вход в «Все свойства» без баннера");
    }

    private static FakeTarget Target() => new(
        typeof(Button),
        FakeTarget.Property("Width", typeof(double)),
        FakeTarget.Property("Height", typeof(double)),
        FakeTarget.Property("HorizontalAlignment", typeof(HorizontalAlignment)),
        FakeTarget.Property("Opacity", typeof(double)),
        FakeTarget.Property("AutomationProperties.Name", typeof(string)));

    private static GroupCell Group(InspectorPanel panel, string category) =>
        Assert.Single(panel.List!.Items.OfType<GroupCell>(), cell => cell.CategoryId == category);

    /// <summary>Пункты открытого меню.</summary>
    private static List<object?> Menu(InspectorStudio studio) =>
        [.. studio.Window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().SingleOrDefault()?.Items.OfType<AxMenuItem>().Select(static item => item.Header) ?? []];

    private static double Metric(InspectorPanel panel, string key) =>
        panel.View!.TryFindResource(key, out var value) && value is double metric ? metric : double.NaN;

    private static List<string> Ids(InspectorPanel panel) =>
        panel.List!.Items.OfType<PropertyCell>().Select(static cell => cell.Row.Property.Id).ToList();

    private static PropertyCell Cell(InspectorPanel panel, string id) =>
        Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == id);

    private static InspectorRow Row(InspectorPanel panel, string id) => Cell(panel, id).Row;
}
