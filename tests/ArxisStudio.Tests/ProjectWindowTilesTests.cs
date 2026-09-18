using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Project;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Tree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно проекта в две колонки, как Project в Unity: дерево контейнеров слева, содержимое справа.
/// </summary>
/// <remarks>
/// Раскладку и ступень плиток окно берёт из настроек и применяет из настроек, откуда бы правка ни
/// пришла — из ⋮, ползунка или окна настроек студии. Поэтому тесты правят настройку и смотрят на
/// окно, а не зовут раскладку напрямую.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowTilesTests
{
    /// <summary>
    /// Окно открывается в две колонки: дерево — только контейнеры, их содержимое — справа.
    /// </summary>
    [AvaloniaFact]
    public async Task The_window_opens_in_two_columns()
    {
        using var studio = new ProjectWindowStudio(twoColumns: null);

        await studio.Open();

        Assert.True(studio.Model.IsTwoColumns, "окно открылось не в две колонки");
        Assert.True(studio.View.Tiles.IsEffectivelyVisible, "правой колонки не видно");
        Assert.Contains(studio.Rows, row => row.Name == "Views");
        Assert.DoesNotContain(studio.Rows, row => row.Name == "Program.cs");
        Assert.Equal(["src"], Names(studio));
    }

    /// <summary>
    /// Контейнер, выбранный в дереве, раскрывается справа, и путь до него стоит крошками.
    /// </summary>
    [AvaloniaFact]
    public async Task A_container_chosen_in_the_tree_opens_on_the_right()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        Assert.Equal("App", studio.Model.Browser.Current!.Name);
        Assert.Contains("Program.cs", Names(studio));
        Assert.Equal(
            ["Hello", "src", "App"],
            studio.View.Path.GetVisualDescendants().OfType<AxBreadcrumbItem>().Select(item => item.Content?.ToString()));
    }

    /// <summary>
    /// Двойной щелчок по папке ведёт в неё, и дерево слева идёт следом; по файлу — открывает файл.
    /// </summary>
    [AvaloniaFact]
    public async Task A_double_click_goes_into_a_folder_and_opens_a_file()
    {
        using var studio = await TwoColumns();

        studio.Select("App");
        studio.DoubleClick(TileItem(studio, "Views"));

        Assert.Equal("Views", studio.Model.Browser.Current!.Name);
        Assert.Equal("Views", studio.Selected.Name);

        studio.Press(studio.View.Query);
        studio.DoubleClick(TileItem(studio, "MainWindow.axaml"));

        Assert.Equal("MainWindow.axaml", Path.GetFileName(Assert.Single(studio.Documents.Opened)));
    }

    /// <summary>Backspace поднимается к родителю, а крошка ведёт на свой уровень.</summary>
    [AvaloniaFact]
    public async Task Backspace_climbs_and_a_crumb_leads_back()
    {
        using var studio = await TwoColumns();

        studio.Select("Views");
        studio.Press(studio.View.Tiles, Key.Back);

        Assert.Equal("App", studio.Model.Browser.Current!.Name);
        Assert.Equal("App", studio.Selected.Name);

        var crumb = studio.View.Path.GetVisualDescendants().OfType<AxBreadcrumbItem>().First();

        studio.Click(crumb);

        Assert.Equal("Hello", studio.Model.Browser.Current!.Name);
    }

    /// <summary>
    /// ⋮ пишет раскладку в настройки, и новое окно читает её оттуда.
    /// </summary>
    [AvaloniaFact]
    public async Task The_layout_menu_writes_the_setting_and_a_new_window_reads_it()
    {
        using var studio = await TwoColumns();

        var items = studio.Panel.LayoutItems();

        Assert.Equal([false, true], items.Select(item => item.IsChecked));

        studio.Click(items, studio.Strings["project.layout.one"]);

        Assert.False(studio.Settings.Get<bool?>(ProjectSettings.TwoColumnsKey), "⋮ не записал раскладку");
        Assert.False(studio.Model.IsTwoColumns, "записанная раскладка не применилась");
        Assert.Contains(studio.Rows, row => row.Name == "Program.cs");

        studio.Reopen();

        Assert.False(studio.Model.IsTwoColumns, "новое окно не прочло раскладку из настроек");
    }

    /// <summary>Раскладку, изменённую в окне настроек студии, окно подхватывает на ходу.</summary>
    [AvaloniaFact]
    public async Task A_layout_changed_in_the_settings_is_picked_up_on_the_fly()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Settings.Set(ProjectSettings.TwoColumnsKey, true);
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.Model.IsTwoColumns);
        Assert.True(studio.View.Tiles.IsEffectivelyVisible);
    }

    /// <summary>
    /// Ползунок идёт по ступеням: список, плитки, крупные плитки — и ступень ложится в настройки.
    /// </summary>
    [AvaloniaFact]
    public async Task The_slider_steps_through_a_list_tiles_and_large_tiles()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        studio.View.Size.Value = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0d, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));
        Assert.True(studio.View.Files.IsEffectivelyVisible, "на ступени «список» списка не видно");
        Assert.False(studio.View.Tiles.IsEffectivelyVisible);

        studio.View.Size.Value = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("tile-large", studio.View.Tiles.Classes);
        Assert.Equal(studio.Resource("AxTileGlyphSizeLarge"), Silhouette(studio, "Program.cs").Bounds.Width);

        // Подложка на крупной ступени не растёт — значок на ней размера набора, — а подписи ряда
        // стоят на одной линии: место под значок растёт со ступенью у всех плиток.
        Assert.Equal(studio.Resource("AxTileGlyphSize"), Plate(studio, "Dependencies").Bounds.Width);
        Assert.Equal(Top(studio, Label(studio, "Assets")), Top(studio, Label(studio, "Dependencies")));

        studio.View.Size.Value = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("tile-large", studio.View.Tiles.Classes);
        Assert.Equal(studio.Resource("AxTileGlyphSize"), Silhouette(studio, "Program.cs").Bounds.Width);
    }

    /// <summary>
    /// Выбранное переживает смену ступени: плитка становится строкой списка, и строка под колонкой
    /// говорит о ней же.
    /// </summary>
    [AvaloniaFact]
    public async Task The_picked_item_survives_a_change_of_step()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var program = studio.Model.Browser.Items.Single(tile => tile.Name == "Program.cs");

        studio.View.Tiles.SelectedItem = program;
        Dispatcher.UIThread.RunJobs();

        studio.View.Size.Value = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(program, studio.View.Files.SelectedItem);
        Assert.Equal(program.Hint, studio.Model.Status);

        studio.View.Size.Value = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(program, studio.View.Tiles.SelectedItem);
    }

    /// <summary>
    /// Плитка файла — силуэт документа цветом своего вида, папки — силуэт папки, предмета модели —
    /// подложка со значком.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tile_shows_what_it_is()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var program = Silhouette(studio, "Program.cs");
        var views = Silhouette(studio, "Views");

        Assert.Same(AxIcons.DocumentTile, program.Data);
        Assert.Same(studio.Resource("AxTintGreenBrush"), program.Foreground);
        Assert.Same(AxIcons.FolderTile, views.Data);
        Assert.Same(studio.Resource("AxTextSecondaryBrush"), views.Foreground);

        var plate = Plate(studio, "Dependencies");

        Assert.True(plate.IsEffectivelyVisible, "у предмета модели нет подложки");
        Assert.Same(AxIcons.Dependencies, plate.GetVisualDescendants().OfType<AxIcon>().Single().Data);
    }

    /// <summary>Пустая папка говорит, что она пуста, а строка под колонкой — сколько в ней предметов.</summary>
    [AvaloniaFact]
    public async Task An_empty_folder_says_so()
    {
        using var studio = await TwoColumns();

        studio.Select("Models");

        Assert.True(studio.Shown(studio.Strings["project.browser.empty"]), "пустая папка промолчала");
        Assert.True(studio.Shown(string.Format(studio.Strings["project.browser.items"], 0)));
    }

    /// <summary>
    /// Поиск в две колонки заполняет правую колонку, а найденное ведёт в свою папку.
    /// </summary>
    [AvaloniaFact]
    public async Task Search_fills_the_right_column_and_leads_to_the_folder()
    {
        using var studio = await TwoColumns();

        var containers = studio.Rows.Count;

        studio.View.Query.Text = "axaml";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, studio.Model.Browser.Items.Count);
        Assert.True(studio.Shown(string.Format(studio.Strings["project.browser.results"], 4)), "колонка не сказала, сколько нашлось");
        Assert.Equal(containers, studio.Rows.Count);

        var main = studio.Model.Browser.Items.Single(tile => tile.Name == "MainWindow.axaml");

        studio.Click(studio.Panel.Pane!.Items(main), studio.Strings["project.menu.showInFolder"]);

        Assert.Equal("Views", studio.Model.Browser.Current!.Name);
        Assert.Same(main, studio.View.Tiles.SelectedItem);
        Assert.True(string.IsNullOrEmpty(studio.View.Query.Text), "поиск остался в строке, хотя колонка уже в папке");
        Assert.Equal("Views", studio.Selected.Name);
    }

    /// <summary>
    /// Смена раскладки оставляет человека на том же: файл дерева — плиткой в своей папке, и обратно.
    /// </summary>
    [AvaloniaFact]
    public async Task Switching_layouts_keeps_the_person_where_they_stood()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Select("Program.cs");
        studio.Settings.Set(ProjectSettings.TwoColumnsKey, true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("App", studio.Selected.Name);
        Assert.Equal("App", studio.Model.Browser.Current!.Name);
        Assert.Equal("Program.cs", Assert.IsType<Tile>(studio.View.Tiles.SelectedItem).Name);

        studio.Settings.Set(ProjectSettings.TwoColumnsKey, false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Program.cs", studio.Selected.Name);
    }

    /// <summary>Ступень из чужих рук — файла, окна настроек — зажимается в три ступени.</summary>
    [AvaloniaFact]
    public void An_icon_size_out_of_range_is_clamped()
    {
        using var studio = new ProjectWindowStudio();

        foreach (var (written, read) in new[] { (7d, 2), (-3d, 0), (1.4d, 1), (1.6d, 2) })
        {
            studio.Settings.Set(ProjectSettings.IconSizeKey, written);

            Assert.Equal(read, ProjectSettings.Read(studio.Settings).IconSize);
        }
    }

    /// <summary>Окно в две колонки с открытым обычным решением.</summary>
    private static async Task<ProjectWindowStudio> TwoColumns()
    {
        var studio = new ProjectWindowStudio(twoColumns: true);

        await studio.Open();

        return studio;
    }

    private static List<string> Names(ProjectWindowStudio studio) => [.. studio.Model.Browser.Items.Select(tile => tile.Name)];

    /// <summary>Контейнер плитки в показанном списке — прокрутив до неё.</summary>
    private static AxListBoxItem TileItem(ProjectWindowStudio studio, string name)
    {
        var list = studio.Panel.Pane!.Shown;
        var tile = studio.Model.Browser.Items.Single(item => item.Name == name);

        list.ScrollIntoView(tile);
        Dispatcher.UIThread.RunJobs();

        return Assert.IsType<AxListBoxItem>(list.ContainerFromItem(tile));
    }

    /// <summary>Силуэт плитки.</summary>
    private static AxIcon Silhouette(ProjectWindowStudio studio, string name) =>
        TileItem(studio, name).GetVisualDescendants().OfType<AxIcon>().Single(icon => icon.Classes.Contains("tile"));

    /// <summary>Подложка плитки предмета модели.</summary>
    private static Border Plate(ProjectWindowStudio studio, string name) =>
        TileItem(studio, name).GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("plate"));

    /// <summary>Подпись плитки.</summary>
    private static TextBlock Label(ProjectWindowStudio studio, string name) =>
        TileItem(studio, name).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == name);

    /// <summary>Верх контрола в координатах списка плиток.</summary>
    private static double Top(ProjectWindowStudio studio, Visual visual) =>
        visual.TranslatePoint(default, studio.View.Tiles)!.Value.Y;
}
