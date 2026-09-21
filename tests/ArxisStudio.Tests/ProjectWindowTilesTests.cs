using System.Runtime.InteropServices;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Project;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
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
    /// Ползунок идёт по лестнице плитки: первое положение — список, дальше ступени темы от малой до
    /// крупной, — и размер ступени ложится в настройки.
    /// </summary>
    [AvaloniaFact]
    public async Task The_slider_walks_the_tile_ladder()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var (small, normal, large, step) = Ladder(studio);

        Assert.Equal(((large - small) / step) + 1, studio.View.Size.Maximum);
        Assert.Equal(((normal - small) / step) + 1, studio.View.Size.Value);
        Assert.Equal(normal, Silhouette(studio, "Program.cs").Bounds.Width);

        studio.View.Size.Value = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ProjectSettings.List, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));
        Assert.True(studio.View.Files.IsEffectivelyVisible, "в положении «список» списка не видно");
        Assert.False(studio.View.Tiles.IsEffectivelyVisible);

        studio.View.Size.Value = studio.View.Size.Maximum;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(large, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));
        Assert.Equal(large, Silhouette(studio, "Program.cs").Bounds.Width);
        Assert.Equal(studio.Resource("AxTileWidthLarge"), Body(studio, "Program.cs").Bounds.Width);

        // Подложка крупнее обычной ступени не растёт — значок на ней размера набора, — а подписи ряда
        // стоят на одной линии: место под значок растёт со ступенью у всех плиток.
        Assert.Equal(normal, Plate(studio, "Dependencies").Bounds.Width);
        Assert.Equal(Top(studio, Label(studio, "Assets")), Top(studio, Label(studio, "Dependencies")));

        // Подложка наведения и выбора — квадрат места под значок у каждой плитки ряда: подложка
        // предмета модели мельче места, но место от этого не сужается и не вытягивается.
        var cell = Backdrop(TileItem(studio, "Program.cs")).Bounds.Size;

        Assert.Equal(cell.Width, cell.Height);
        Assert.Equal(cell, Backdrop(TileItem(studio, "Dependencies")).Bounds.Size);

        studio.View.Size.Value = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(small, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));
        Assert.Equal(small, Silhouette(studio, "Program.cs").Bounds.Width);

        // Мельче обычной ступени подложка идёт за силуэтом: места под значок ей больше не дано.
        Assert.Equal(small, Plate(studio, "Dependencies").Bounds.Width);

        // И подпись там в строку с многоточием: две строки на узкой плитке переломили бы почти
        // каждое имя посреди слова.
        var line = Label(studio, "Views").Bounds.Height;

        Assert.All(["Dependencies", "App.axaml.cs", "app.manifest"], name => Assert.Equal(line, Label(studio, name).Bounds.Height));
    }

    /// <summary>
    /// Колесо с Ctrl меняет ступень по щелчку: от себя — крупнее, на себя — мельче, мельче малой —
    /// список; у крупной лестница кончается. Без Ctrl колесо ступени не трогает.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_and_the_wheel_step_through_the_ladder()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var (_, normal, _, step) = Ladder(studio);
        var start = studio.View.Size.Value;

        studio.Wheel(TileItem(studio, "Views"), 1, RawInputModifiers.Control);

        Assert.Equal(start + 1, studio.View.Size.Value);
        Assert.Equal(normal + step, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));
        Assert.Equal(normal + step, Silhouette(studio, "Program.cs").Bounds.Width);

        studio.Wheel(TileItem(studio, "Views"), -2, RawInputModifiers.Control);

        Assert.Equal(start - 1, studio.View.Size.Value);

        studio.Wheel(TileItem(studio, "Views"), -1);

        Assert.Equal(start - 1, studio.View.Size.Value);

        studio.View.Size.Value = 1;
        Dispatcher.UIThread.RunJobs();
        studio.Wheel(TileItem(studio, "Views"), -1, RawInputModifiers.Control);

        Assert.Equal(0d, studio.View.Size.Value);
        Assert.True(studio.View.Files.IsEffectivelyVisible, "мельче малой ступени колонка не стала списком");

        studio.Wheel(studio.View.Files, 1, RawInputModifiers.Control);

        Assert.Equal(1d, studio.View.Size.Value);

        studio.View.Size.Value = studio.View.Size.Maximum;
        Dispatcher.UIThread.RunJobs();

        // Колесо с Ctrl — ступень, а не прокрутка: у крупной ступени лестница кончилась, и список
        // всё равно стоит на месте.
        var viewer = studio.View.Tiles.GetVisualDescendants().OfType<ScrollViewer>().First();

        viewer.Offset = new Vector(0, viewer.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        var offset = viewer.Offset;

        Assert.True(offset.Y > 0, "крупные плитки уместились без прокрутки — проверять нечего");

        studio.Wheel(TileItem(studio, "Views"), 1, RawInputModifiers.Control);

        Assert.Equal(studio.View.Size.Maximum, studio.View.Size.Value);
        Assert.Equal(offset, viewer.Offset);
    }

    /// <summary>
    /// Тачпад шлёт щелчок колеса долями, и ступень меняется, когда доли сложатся в целый щелчок, —
    /// а не на каждое событие.
    /// </summary>
    [AvaloniaFact]
    public async Task A_touchpad_steps_once_per_whole_notch()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var start = studio.View.Size.Value;

        studio.Wheel(TileItem(studio, "Views"), 0.4, RawInputModifiers.Control);
        studio.Wheel(TileItem(studio, "Views"), 0.4, RawInputModifiers.Control);

        Assert.Equal(start, studio.View.Size.Value);

        // Смена направления копилку сбрасывает: целый щелчок назад — целая ступень назад, и восемь
        // десятых, накопленных вперёд, его не съедают.
        studio.Wheel(TileItem(studio, "Views"), -1, RawInputModifiers.Control);

        Assert.Equal(start - 1, studio.View.Size.Value);

        studio.Wheel(TileItem(studio, "Views"), 0.4, RawInputModifiers.Control);
        studio.Wheel(TileItem(studio, "Views"), 0.4, RawInputModifiers.Control);
        studio.Wheel(TileItem(studio, "Views"), 0.4, RawInputModifiers.Control);

        Assert.Equal(start, studio.View.Size.Value);

        // Десять десятых в двоичной записи — 0,999…, и без поправки на погрешность щелчок не
        // засчитывался бы. Копилку опустошает целый щелчок назад.
        studio.Wheel(TileItem(studio, "Views"), -1, RawInputModifiers.Control);

        for (var tenth = 0; tenth < 10; tenth++)
            studio.Wheel(TileItem(studio, "Views"), 0.1, RawInputModifiers.Control);

        Assert.Equal(start, studio.View.Size.Value);
    }

    /// <summary>
    /// С клавиатуры ступень меняют Ctrl с плюсом и минусом — основными и цифровыми, — а Ctrl+0
    /// возвращает обычную.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_plus_minus_and_zero_step_from_the_keyboard()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var (_, normal, _, _) = Ladder(studio);
        var start = studio.View.Size.Value;

        studio.Press(studio.View.Tiles, Key.OemPlus, KeyModifiers.Control);
        studio.Press(studio.View.Tiles, Key.Add, KeyModifiers.Control);

        Assert.Equal(start + 2, studio.View.Size.Value);

        studio.Press(studio.View.Tiles, Key.OemMinus, KeyModifiers.Control);

        Assert.Equal(start + 1, studio.View.Size.Value);

        studio.Press(studio.View.Tiles, Key.D0, KeyModifiers.Control);

        Assert.Equal(start, studio.View.Size.Value);
        Assert.Equal(normal, studio.Settings.Get<double?>(ProjectSettings.IconSizeKey));

        // «+» на основной клавиатуре — это Shift и «=»: так его нажимает всякий, кто не знает о «=».
        studio.Press(studio.View.Tiles, Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal(start + 1, studio.View.Size.Value);
    }

    /// <summary>
    /// Клавиатура остаётся в колонке, когда ступень превращает плитки в список и обратно — на том же
    /// предмете.
    /// </summary>
    /// <remarks>
    /// Ступень «список» прячет плитки вместе с плиткой в фокусе, и Ctrl+минус оставлял бы каретку
    /// нигде: следующее нажатие шло бы в пустоту, а не в колонку.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_keyboard_stays_in_the_column_when_tiles_turn_into_a_list()
    {
        using var studio = await TwoColumns();

        studio.Select("App");
        studio.View.Size.Value = 1;
        Dispatcher.UIThread.RunJobs();

        var views = TileItem(studio, "Views");

        studio.View.Tiles.SelectedItem = views.DataContext;
        views.Focus();
        Dispatcher.UIThread.RunJobs();

        studio.Press(views, Key.OemMinus, KeyModifiers.Control);

        Assert.Equal(0d, studio.View.Size.Value);
        Assert.True(studio.View.Files.IsKeyboardFocusWithin, "плитки стали строками, а клавиатура осталась нигде");
        Assert.Same(views.DataContext, Focused(studio).DataContext);

        studio.Press(Focused(studio), Key.OemPlus, KeyModifiers.Control);

        Assert.Equal(1d, studio.View.Size.Value);
        Assert.True(studio.View.Tiles.IsKeyboardFocusWithin, "строки стали плитками, а клавиатура осталась нигде");
        Assert.Same(views.DataContext, Focused(studio).DataContext);
    }

    /// <summary>
    /// Выбранная плитка остаётся в виду, когда ступень растёт: список перекладывается, и колонка
    /// прокручивается к ней, а не остаётся там, где стояла.
    /// </summary>
    [AvaloniaFact]
    public async Task The_picked_tile_stays_in_view_when_the_step_changes()
    {
        using var studio = await TwoColumns();

        studio.Select("App");
        studio.View.Size.Value = 1;
        Dispatcher.UIThread.RunJobs();

        var program = studio.Model.Browser.Items.Single(tile => tile.Name == "Program.cs");

        studio.View.Tiles.SelectedItem = program;
        Dispatcher.UIThread.RunJobs();

        Assert.True(InView(studio.View.Tiles, program), "на малой ступени плитка не видна и без прокрутки");

        studio.Wheel(TileItem(studio, "Dependencies"), studio.View.Size.Maximum - 1, RawInputModifiers.Control);

        Assert.Equal(studio.View.Size.Maximum, studio.View.Size.Value);
        Assert.True(InView(studio.View.Tiles, program), "выбранная плитка уехала из виду, когда плитки выросли");
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

    /// <summary>
    /// Наведение — подложка под значком, выбор — ещё и плашка под подписью; заливки во всю плитку нет.
    /// </summary>
    /// <remarks>
    /// Плашка горит полным цветом, пока клавиатура в колонке, и гаснет, когда фокус ушёл, — правило
    /// выделения студии. Наведённую плитку от выбранной отличает плашка, а не оттенок серого: заливка
    /// во всю плитку давала им в тёмной теме почти один цвет.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_tile_shows_hover_on_its_glyph_and_selection_on_its_caption()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var item = TileItem(studio, "Views");
        var backdrop = Backdrop(item);
        var caption = Caption(item);
        var chrome = item.GetVisualDescendants().OfType<ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");

        studio.Hover(item);

        Assert.Same(studio.Resource("AxHoverBrush"), backdrop.Background);
        Assert.True(Clear(caption.Background), "наведение зажгло плашку подписи, а она — знак выбора");
        Assert.True(Clear(chrome.Background), "наведение залило плитку целиком");

        // Мышь уходит: иначе снятая заливка наведения прятала бы заливку выбора.
        studio.Hover(studio.View.Query);

        Assert.True(Clear(backdrop.Background), "подложка горит и без мыши над плиткой");

        studio.View.Tiles.SelectedItem = item.DataContext;
        item.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(studio.Resource("AxSelectionActiveBrush"), caption.Background);
        Assert.Same(studio.Resource("AxSelectionInactiveBrush"), backdrop.Background);
        Assert.True(Clear(chrome.Background), "выбор залил плитку целиком");
        Assert.False(
            item.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "PART_SelectionMarker").IsEffectivelyVisible,
            "у плитки метка строки — выбор плитки держат подложка и плашка");

        studio.View.Query.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(studio.Resource("AxSelectionInactiveBrush"), caption.Background);
    }

    /// <summary>
    /// Плашка не отнимает у подписи ширину: подписи отдана вся колонка плитки.
    /// </summary>
    /// <remarks>
    /// Плашка стоит у каждой плитки, прозрачной, и поле рамки сузило бы подпись у всех: «ViewLocator.cs»,
    /// которому колонки хватало, переломился посреди слова. Заливка плашки выходит за края подписи, а
    /// не отнимает место у неё.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_caption_plate_leaves_the_label_the_whole_tile()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var offered = LayoutInformation.GetPreviousMeasureConstraint(Label(studio, "Program.cs"));

        Assert.NotNull(offered);
        Assert.Equal(Assert.IsType<double>(studio.Resource("AxTileWidth")), offered.Value.Width);
    }

    /// <summary>
    /// Выбранная плашка нарисована и шире подписи: заливка выходит за края букв, а не жмётся к ним.
    /// </summary>
    /// <remarks>
    /// Цвет плашки проверен выше свойством; здесь — кадром, потому что свойство могло поменяться, а
    /// картинка — нет: плашка рисует себя сама, за своими границами.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_selected_caption_plate_is_painted_past_the_label()
    {
        using var studio = await TwoColumns();

        studio.Select("App");

        var item = TileItem(studio, "Program.cs");
        var caption = Caption(item);
        var label = Label(studio, "Program.cs");

        studio.View.Tiles.SelectedItem = item.DataContext;
        item.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();

        var edge = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), studio.Window);
        var fill = Assert.IsAssignableFrom<ISolidColorBrush>(studio.Resource("AxSelectionActiveBrush")).Color;

        Assert.NotNull(edge);
        Assert.True(caption.Outset.Left >= 2, "плашка не выходит за края подписи");
        Assert.Equal(fill, Pixel(studio.Window, edge.Value.X - (caption.Outset.Left / 2), edge.Value.Y));
    }

    /// <summary>
    /// Подпись в две строки не поднимает значок над соседями: плитки стоят по верху ряда.
    /// </summary>
    [AvaloniaFact]
    public async Task A_two_line_caption_keeps_the_glyph_in_line_with_its_row()
    {
        using var studio = new ProjectWindowStudio(twoColumns: true);

        await studio.Open(studio.Solution(extra: "MainWindowViewModelBase.cs"));

        studio.Select("App");

        var wrapped = TileItem(studio, "MainWindowViewModelBase.cs");
        var row = studio.View.Tiles.GetRealizedContainers().OfType<AxListBoxItem>()
            .Where(item => Top(studio, item) == Top(studio, wrapped))
            .ToList();

        Assert.True(row.Count > 1, "плитке с длинной подписью не нашлось соседей по ряду");
        Assert.True(Label(studio, "MainWindowViewModelBase.cs").Bounds.Height > Label(studio, "Program.cs").Bounds.Height,
            "длинная подпись уместилась в строку — проверять нечего");

        var line = Top(studio, Backdrop(wrapped));

        Assert.All(row, item => Assert.Equal(line, Top(studio, Backdrop(item))));
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
    /// Клавиатура, пришедшая в саму колонку, в пустой папке остаётся в ней, а при плитках уходит на
    /// выбранную, без выбора — на первую.
    /// </summary>
    /// <remarks>
    /// Пустой папке больше некуда отдать клавиатуру, а вставить в неё хотят. При плитках же стрелки
    /// ходят от плитки, и список, забравший клавиатуру себе, их бы не пустил. Щелчок приходится в
    /// середину колонки — туда, где лежит надпись «Папка пуста»: живая проверка нашла, что надпись
    /// ловила его сама, и клавиатура в колонку не шла.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_column_keeps_the_keyboard_itself_only_in_an_empty_folder()
    {
        using var studio = await TwoColumns();

        var tiles = studio.View.Tiles;

        studio.Select("Models");
        studio.Press(tiles);

        Assert.Same(tiles, Focused(studio));

        studio.Select("App");
        tiles.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(studio.Model.Browser.Items[0], Focused(studio).DataContext);

        tiles.SelectedItem = studio.Model.Browser.Items.Single(tile => tile.Name == "Views");
        tiles.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(tiles.SelectedItem, Focused(studio).DataContext);
    }

    /// <summary>
    /// Правый щелчок мимо плиток снимает выбор и открывает меню папки, которую колонка показывает, —
    /// как в проводнике и в Unity.
    /// </summary>
    /// <remarks>
    /// Меню говорит о папке, и выбранное не должно казаться его предметом. Щелчок приходится в угол
    /// колонки, далеко от плиток.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_right_click_past_the_tiles_clears_the_choice_and_opens_the_folder_menu()
    {
        using var studio = await TwoColumns();

        var tiles = studio.View.Tiles;

        studio.Select("App");
        tiles.SelectedItem = studio.Model.Browser.Items.Single(tile => tile.Name == "Views");

        var corner = tiles.TranslatePoint(new Point(tiles.Bounds.Width - 4, tiles.Bounds.Height - 4), studio.Window)!.Value;

        studio.Window.MouseDown(corner, MouseButton.Right);
        studio.Window.MouseUp(corner, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(tiles.SelectedItems!);
        Assert.Equal(studio.Panel.Pane!.FolderItems().Select(item => item.Header), Menu(studio));
    }

    /// <summary>Клавиша меню в пустой папке открывает меню папки: выбранного нет, и меню — её.</summary>
    [AvaloniaFact]
    public async Task The_menu_key_in_an_empty_folder_opens_its_menu()
    {
        using var studio = await TwoColumns();

        var tiles = studio.View.Tiles;

        studio.Select("Models");
        studio.Press(tiles);
        studio.Press(tiles, Key.Apps);

        Assert.Equal(studio.Panel.Pane!.FolderItems().Select(item => item.Header), Menu(studio));
    }

    /// <summary>
    /// Щелчок по невыбранной плитке оставляет клавиатуру на ней: к выбранной прежде её уводит только
    /// приход в сам список, а не в плитку.
    /// </summary>
    /// <remarks>
    /// Клавиатура приходит в плитку раньше, чем щелчок её выбирает, и колонка, отдававшая выбранной
    /// всякий приход, увела бы её на прежнюю: выбор на одной плитке, стрелки — от другой.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_click_on_a_tile_keeps_the_keyboard_on_that_tile()
    {
        using var studio = await TwoColumns();

        var tiles = studio.View.Tiles;

        studio.Select("App");

        var views = TileItem(studio, "Views");

        tiles.SelectedItem = views.DataContext;
        views.Focus();
        Dispatcher.UIThread.RunJobs();

        studio.Press(TileItem(studio, "App.axaml"));

        Assert.Equal("App.axaml", Assert.IsType<Tile>(tiles.SelectedItem).Name);
        Assert.Same(tiles.SelectedItem, Focused(studio).DataContext);
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

    /// <summary>
    /// Размер из настроек встаёт на ближайшую ступень: прежние номера 1 и 2 — обычные и крупные
    /// плитки, ноль и меньше — список, чужое число — ближайшая ступень, из двух равных — меньшая.
    /// </summary>
    /// <remarks>
    /// Настройку правят руками и в окне настроек, а номера 1 и 2 лежат в ней с тех пор, как ступеней
    /// было три: окно не имеет права понять их как 32 и 48 точек.
    /// </remarks>
    [AvaloniaFact]
    public void A_stored_size_lands_on_the_nearest_step()
    {
        using var studio = new ProjectWindowStudio(twoColumns: true);

        var (small, normal, large, step) = Ladder(studio);

        Assert.Equal(((normal - small) / step) + 1, studio.View.Size.Value);

        foreach (var (written, glyph) in new (double, double?)[]
        {
            (1, normal),
            (2, large),
            (0, null),
            (-3, null),
            (7, small),
            (normal + (step / 2), normal),
            (normal + (step * 0.6), normal + step),
            (1000, large),
        })
        {
            studio.Settings.Set(ProjectSettings.IconSizeKey, written);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(glyph is { } size ? ((size - small) / step) + 1 : 0, studio.View.Size.Value);
        }
    }

    /// <summary>Лестница плитки темы: малая, обычная и крупная ступени и шаг.</summary>
    private static (double Small, double Normal, double Large, double Step) Ladder(ProjectWindowStudio studio) => (
        Assert.IsType<double>(studio.Resource("AxTileGlyphSizeSmall")),
        Assert.IsType<double>(studio.Resource("AxTileGlyphSize")),
        Assert.IsType<double>(studio.Resource("AxTileGlyphSizeLarge")),
        Assert.IsType<double>(studio.Resource("AxTileGlyphSizeStep")));

    /// <summary>Виден ли контейнер плитки в окне прокрутки списка — целиком, без прокрутки к нему.</summary>
    private static bool InView(AxListBox list, Tile tile)
    {
        var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var container = Assert.IsAssignableFrom<Control>(list.ContainerFromItem(tile));
        var top = container.TranslatePoint(default, viewer)!.Value.Y;

        return top >= 0 && top + container.Bounds.Height <= viewer.Viewport.Height;
    }

    /// <summary>Подписи открытого меню; меню нет — пусто.</summary>
    private static List<object?> Menu(ProjectWindowStudio studio) =>
        [.. studio.Window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().SingleOrDefault()?.Items.OfType<AxMenuItem>().Select(item => item.Header) ?? []];

    /// <summary>Контрол, у которого сейчас клавиатура.</summary>
    private static Control Focused(ProjectWindowStudio studio) =>
        Assert.IsAssignableFrom<Control>(studio.Window.FocusManager?.GetFocusedElement());

    /// <summary>Тело плитки: силуэт и подпись, шириной плитки.</summary>
    private static StackPanel Body(ProjectWindowStudio studio, string name) =>
        TileItem(studio, name).GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Classes.Contains("tile"));

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

    /// <summary>Подложка значка плитки.</summary>
    private static Border Backdrop(AxListBoxItem item) =>
        item.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("backdrop"));

    /// <summary>Плашка подписи плитки.</summary>
    private static Pill Caption(AxListBoxItem item) => item.GetVisualDescendants().OfType<Pill>().Single();

    /// <summary>Цвет пикселя окна в точке — в кадре, который окно только что нарисовало.</summary>
    private static Color Pixel(Window window, double x, double y)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = frame.Lock();

        var at = ((int)Math.Floor(y) * pixels.RowBytes) + ((int)Math.Floor(x) * 4);
        var (red, blue) = pixels.Format == PixelFormat.Rgba8888 ? (0, 2) : (2, 0);

        return Color.FromRgb(
            Marshal.ReadByte(pixels.Address, at + red),
            Marshal.ReadByte(pixels.Address, at + 1),
            Marshal.ReadByte(pixels.Address, at + blue));
    }

    /// <summary>Ничего не рисует: кисти нет или она прозрачна.</summary>
    private static bool Clear(IBrush? brush) => brush is null or ISolidColorBrush { Color.A: 0 };

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
