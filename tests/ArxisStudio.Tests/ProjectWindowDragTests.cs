using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// Файлы и каталоги, которые несут мышью внутри окна проекта: что несут, куда, чем и что видно по
/// дороге.
/// </summary>
/// <remarks>
/// Мышь — безголовой платформы: нажатие, движения и отпускание, как у настоящей. Служба файлов
/// подделана: тест видит, что окно у неё попросило, — перенос, копию и метку действия.
/// </remarks>
public class ProjectWindowDragTests
{
    /// <summary>
    /// Файл, отпущенный над каталогом, переезжает в него вместе с вложенным одним действием «Перенос»;
    /// пока несут, каталог отмечен целью, курсор говорит «перенос», а у курсора — имя несомого.
    /// Вырезанный и унесённый мышью файл по старому пути больше не лежит — буфер правки кончается.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_dropped_on_a_folder_moves_there_with_its_nested_file()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var app = studio.Row("App.axaml");
        var views = studio.Row("Views");
        var drag = studio.Panel.Drag!;

        studio.Press(studio.Item(studio.Select("App.axaml")), Key.X, KeyModifiers.Control);

        Assert.True(app.IsCut, "вырезанное не приглушено");

        studio.Grab(studio.Item(app));
        studio.Carry(studio.Item(views));

        Assert.Equal(DragDropEffects.Move, drag.Effect);
        Assert.True(views.IsDropTarget, "каталог под курсором не отмечен целью");
        Assert.NotNull(drag.Ghost?.Parent);
        Assert.Equal("App.axaml", Assert.IsType<Carried>(drag.Ghost!.DataContext).Label);

        studio.Release(studio.Item(views));

        await Settled(studio, () => files.Moved.Count == 1);

        Assert.Equal(
            [
                new FileMove(app.Node.Path, views.Node.Path.Combine("App.axaml")),
                new FileMove(app.Node.Path.Directory.Combine("App.axaml.cs"), views.Node.Path.Combine("App.axaml.cs")),
            ],
            files.Moved[0]);
        Assert.Empty(files.Copied);
        Assert.Equal(Format(studio, "project.paste.moved.label", "App.axaml"), Assert.Single(files.Labels));
        Assert.Contains(Format(studio, "project.paste.moved", "App.axaml"), studio.Status.Said);
        Assert.Null(drag.Ghost);
        Assert.False(views.IsDropTarget, "отметка цели пережила отпускание");
        Assert.False(app.IsCut, "унесённое мышью осталось вырезанным по старому пути");
    }

    /// <summary>
    /// С Ctrl несомое копируется — и тогда, когда Ctrl нажали, не двигая мышь.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_copies_instead_of_moving()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var views = studio.Row("Views");
        var drag = studio.Panel.Drag!;

        studio.Grab(studio.Item(studio.Row("Program.cs")));
        studio.Carry(studio.Item(views));

        Assert.Equal(DragDropEffects.Move, drag.Effect);

        studio.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Copy, drag.Effect);

        studio.Release(studio.Item(views), RawInputModifiers.Control);

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(views.Node.Path.Combine("Program.cs"), Assert.Single(files.Copied[0]).To);
        Assert.Empty(files.Moved);
        Assert.Equal(Format(studio, "project.drop.copied.label", "Program.cs"), Assert.Single(files.Labels));
    }

    /// <summary>
    /// Выбор из нескольких несут целиком, взявшись за любой из них; щелчок без тяги по одному из
    /// выбранных — по-прежнему щелчок и оставляет выбранным его одного.
    /// </summary>
    [AvaloniaFact]
    public async Task A_chosen_group_is_carried_whole_and_a_click_still_picks_one()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var program = studio.Row("Program.cs");
        var manifest = studio.Row("app.manifest");
        var views = studio.Row("Views");

        Choose(studio, program, manifest);
        studio.Grab(studio.Item(program));

        Assert.Equal(2, studio.View.Tree.SelectedItems!.Count);
        Assert.Equal(Format(studio, "project.drag.many", "Program.cs", 1), Assert.IsType<Carried>(studio.Panel.Drag!.Ghost!.DataContext).Label);

        studio.Release(studio.Item(views));

        await Settled(studio, () => files.Moved.Count == 1);

        Assert.Equal(
            [views.Node.Path.Combine("app.manifest"), views.Node.Path.Combine("Program.cs")],
            files.Moved[0].Select(move => move.To));

        Choose(studio, program, manifest);
        studio.Press(studio.Item(program));

        Assert.Equal(["Program.cs"], studio.View.Tree.SelectedItems!.OfType<Row>().Select(row => row.Name));
    }

    /// <summary>
    /// Туда, где переносить нечего, — в свой же каталог, в решение, каталог в собственного потомка, —
    /// курсор говорит «нельзя» и отпускание ничего не делает; Esc бросает тягу посреди дороги.
    /// </summary>
    [AvaloniaFact]
    public async Task Nowhere_to_carry_is_refused_and_escape_drops_the_carry()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var views = studio.Row("Views");
        var drag = studio.Panel.Drag!;
        var tree = studio.View.Tree;
        var before = tree.Cursor;

        studio.Grab(studio.Item(studio.Row("Program.cs")));

        studio.Carry(studio.Item(studio.Row("App")));
        Assert.Equal(DragDropEffects.None, drag.Effect);

        var refusing = tree.Cursor;

        studio.Carry(studio.Item(studio.Row("Hello")));
        Assert.Equal(DragDropEffects.None, drag.Effect);

        studio.Carry(studio.Item(views));
        Assert.Equal(DragDropEffects.Move, drag.Effect);
        Assert.NotSame(refusing, tree.Cursor);

        studio.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(drag.Ghost);
        Assert.False(views.IsDropTarget, "брошенная тяга оставила отметку");
        Assert.Same(before, tree.Cursor);

        studio.Release(studio.Item(views));

        studio.Model.Tree.Expand(views);
        Dispatcher.UIThread.RunJobs();

        studio.Grab(studio.Item(views));
        studio.Release(studio.Item(studio.Row("MainWindow.axaml")));

        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(files.Moved);
        Assert.Empty(files.Copied);
    }

    /// <summary>
    /// Движение короче порога — щелчок, а не тяга; проект правке не отдаётся — его не несут.
    /// </summary>
    [AvaloniaFact]
    public async Task A_short_move_is_a_click_and_a_project_is_not_carried()
    {
        using var studio = await Opened(new FilesProbe());

        var item = studio.Item(studio.Row("Program.cs"));
        var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), studio.Window)!.Value;

        studio.Window.MouseDown(at, MouseButton.Left);
        studio.Window.MouseMove(at + new Vector(FileDrag.Threshold / 2, 0), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(studio.Panel.Drag!.Ghost);

        studio.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Program.cs", studio.Selected.Name);

        studio.Grab(studio.Item(studio.Row("App")));

        Assert.Null(studio.Panel.Drag.Ghost);

        studio.Release(studio.Item(studio.Row("Views")));
    }

    /// <summary>
    /// В две колонки плитку переносят на плитку каталога — и через край колонки на строку дерева.
    /// Колонка идёт за перенесённым, и клавиатура остаётся с ним; каталог колонка открывает, и
    /// клавиатура — в ней.
    /// </summary>
    /// <remarks>
    /// Так нашла живая проверка: после сброса плитки на строку дерева клавиатура оставалась нигде —
    /// плитка, на которой она стояла, ушла вместе с колонкой, строки у файла в две колонки нет, и
    /// <c>Ctrl+Z</c> было некому нажать.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_tile_moves_into_a_folder_tile_and_across_to_the_tree_and_the_keyboard_follows()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var pane = studio.Panel.Pane!;

        studio.Select("App");
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(
            without: ["Program.cs"], more: ["Models/Program.cs"])));

        studio.Grab(TileItem(studio, "Program.cs"));
        studio.Release(TileItem(studio, "Models"));

        await Settled(studio, () => pane.Selected?.Name == "Program.cs" && pane.Shown.IsKeyboardFocusWithin);

        Assert.Equal(studio.Row("Models").Node.Path.Combine("Program.cs"), Assert.Single(files.Moved[0]).To);
        Assert.Equal("Models", studio.Model.Browser.Current!.Name);

        studio.Select("App");
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution(
            without: ["Program.cs", "app.manifest"], more: ["Models/Program.cs", "Views/app.manifest"])));

        studio.Grab(TileItem(studio, "app.manifest"));
        studio.Release(studio.Item(studio.Row("Views")));

        await Settled(studio, () => pane.Selected?.Name == "app.manifest" && pane.Shown.IsKeyboardFocusWithin);

        Assert.Equal(studio.Row("Views").Node.Path.Combine("app.manifest"), Assert.Single(files.Moved[1]).To);
        Assert.Equal("Views", studio.Model.Browser.Current!.Name);

        studio.Select("App");
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(4, studio.Solution(
            without: ["Program.cs", "app.manifest", "Assets/avalonia-logo.ico"],
            more: ["Models/Program.cs", "Views/app.manifest", "Views/Assets/avalonia-logo.ico"])));

        studio.Grab(TileItem(studio, "Assets"));
        studio.Release(TileItem(studio, "Views"));

        await Settled(studio, () => studio.Model.Browser.Current?.Path.Directory.FileName == "Views" && pane.Shown.IsKeyboardFocusWithin);

        Assert.Equal(studio.Row("Views").Node.Path.Combine("Assets"), Assert.Single(files.Moved[2]).To);
        Assert.Equal("Assets", studio.Model.Browser.Current!.Name);
    }

    /// <summary>
    /// Плитку несут на сегмент крошек — на уровень выше, как на адресную строку проводника: сегмент
    /// отмечен целью вместе со строкой каталога в дереве, перенесённое ложится туда, и колонка идёт
    /// следом. Текущий сегмент, решение и папка решения переноса не принимают; копию с Ctrl текущий
    /// сегмент принимает — и вся колонка отмечена: копия ляжет туда, где человек стоит.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tile_carried_onto_a_crumb_moves_up_to_that_level()
    {
        var files = new FilesProbe();

        using var studio = new ProjectWindowStudio(twoColumns: true, files: files, width: 900);

        await studio.Open();

        var pane = studio.Panel.Pane!;
        var drag = studio.Panel.Drag!;

        studio.Select("Views");
        studio.Grab(TileItem(studio, "MainWindow.axaml"));

        foreach (var refused in new[] { "Views", "src", "Hello" })
        {
            studio.Carry(studio.Crumb(refused));
            Assert.Equal(DragDropEffects.None, drag.Effect);
            Assert.False(studio.Crumb(refused).IsDropTarget, $"сегмент «{refused}» отмечен целью переноса");
        }

        studio.Carry(studio.Crumb("Views"), RawInputModifiers.Control);

        Assert.Equal(DragDropEffects.Copy, drag.Effect);
        Assert.True(studio.Crumb("Views").IsDropTarget, "текущий сегмент не отмечен целью копии");
        Assert.True(studio.Model.IsColumnDropTarget, "колонка не отмечена, хотя копия ляжет в неё");

        // Между сегментами каталога нет: пустое место ряда — не колонка и не цель даже копии.
        var path = studio.View.Path;

        studio.Carry(path.TranslatePoint(new Point(path.Bounds.Width - 4, path.Bounds.Height / 2), studio.Window)!.Value, RawInputModifiers.Control);

        Assert.Equal(DragDropEffects.None, drag.Effect);

        studio.Carry(studio.Crumb("App"));

        Assert.Equal(DragDropEffects.Move, drag.Effect);
        Assert.True(studio.Crumb("App").IsDropTarget, "сегмент под курсором не отмечен целью");
        Assert.False(studio.Crumb("Views").IsDropTarget, "отметка осталась на прежнем сегменте");
        Assert.True(studio.Row("App").IsDropTarget, "строка каталога в дереве не отмечена");

        // Снимок решения, пришедший посреди тяги, пересобирает крошки, и отметка идёт за ними.
        studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution()));
        await studio.Built();

        Assert.True(studio.Crumb("App").IsDropTarget, "пересобранные крошки пришли без отметки цели");

        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution(
            without: ["Views/MainWindow.axaml", "Views/MainWindow.axaml.cs"], more: ["MainWindow.axaml", "MainWindow.axaml.cs"])));
        studio.Release(studio.Crumb("App"));

        await Settled(studio, () => studio.Model.Browser.Current?.Name == "App" && pane.Selected?.Name == "MainWindow.axaml");

        // Путь проекта — его файл, а класть в проект — в каталог рядом с ним.
        var app = studio.Row("App").Node.Path.Directory;

        Assert.Equal([app.Combine("MainWindow.axaml"), app.Combine("MainWindow.axaml.cs")], files.Moved.Single().Select(move => move.To));
        Assert.Empty(files.Copied);
        Assert.All(studio.Model.Browser.Segments, segment => Assert.False(segment.IsDropTarget, $"{segment.Node.Name} остался отмечен"));
        Assert.True(pane.Shown.IsKeyboardFocusWithin, "клавиатура не пришла к перенесённому");
    }

    /// <summary>
    /// Несомое, задержанное над «…» крошек, раскрывает меню спрятанных уровней — не сразу и не забирая
    /// клавиатуры, — и пункт меню принимает перенос, как сам уровень; решение в меню целью не бывает.
    /// Понесли в другое место — меню закрылось; отпустили на пункте — тоже.
    /// </summary>
    /// <remarks>
    /// Меню — отдельное окно поверх колонки, и тяга, которая несёт на захвате указателя, спрашивает
    /// крошки о нём по точке экрана. Клавиатура остаётся там, где тягу начали: Esc и Ctrl посреди неё
    /// должны прийти туда.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_tile_held_over_the_crumbs_overflow_opens_the_hidden_levels()
    {
        var files = new FilesProbe();

        using var studio = new ProjectWindowStudio(twoColumns: true, files: files, width: 360);

        await studio.Open(studio.Solution(more: ["Views/Deep/Note.cs"]));

        var pane = studio.Panel.Pane!;
        var drag = studio.Panel.Drag!;
        var crumbs = studio.View.Path;

        studio.Select("Views");
        studio.DoubleClick(TileItem(studio, "Deep"));

        Assert.True(studio.Overflow().IsEffectivelyVisible, "крошки уместились — прятать нечего");

        studio.Grab(TileItem(studio, "Note.cs"));

        var held = studio.Window.FocusManager?.GetFocusedElement();

        studio.Carry(studio.Overflow());

        Assert.Equal(DragDropEffects.None, drag.Effect);
        Assert.False(crumbs.IsOverflowOpen, "меню раскрылось сразу, без задержки");

        studio.Unfold();

        Assert.True(crumbs.IsOverflowOpen, "меню спрятанных уровней не раскрылось");
        Assert.Same(held, studio.Window.FocusManager?.GetFocusedElement());

        studio.Carry(studio.Hidden("App"));

        Assert.Equal(DragDropEffects.Move, drag.Effect);
        Assert.True(studio.Hidden("App").IsDropTarget, "пункт спрятанного уровня не отмечен целью");
        Assert.True(studio.Row("App").IsDropTarget, "строка каталога в дереве не отмечена");

        studio.Carry(studio.Hidden("Hello"));

        Assert.Equal(DragDropEffects.None, drag.Effect);
        Assert.False(studio.Hidden("Hello").IsDropTarget, "решение отмечено целью");
        Assert.False(studio.Hidden("App").IsDropTarget, "отметка осталась на прежнем пункте");

        studio.Carry(studio.Item(studio.Row("App")));

        Assert.False(crumbs.IsOverflowOpen, "меню осталось, когда понесли в дерево");
        Assert.Equal(DragDropEffects.Move, drag.Effect);
        Assert.True(studio.Row("App").IsDropTarget, "строка дерева под раскрытым меню не стала целью");

        // Esc бросает тягу — и меню уходит вместе с ней.
        studio.Carry(studio.Overflow());
        studio.Unfold();
        studio.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(drag.Ghost);
        Assert.False(crumbs.IsOverflowOpen, "брошенная тяга оставила меню открытым");

        // Щелчок в стороне: время в безголовом прогоне стоит, и следующее нажатие иначе сочлось бы двойным.
        studio.Release(studio.Item(studio.Row("App")));
        studio.Press(studio.View.Query);
        studio.Grab(TileItem(studio, "Note.cs"));
        studio.Carry(studio.Overflow());
        studio.Unfold();

        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(more: ["Note.cs"])));
        studio.Release(studio.Hidden("App"));

        await Settled(studio, () => studio.Model.Browser.Current?.Name == "App" && pane.Selected?.Name == "Note.cs");

        Assert.Equal(studio.Row("App").Node.Path.Directory.Combine("Note.cs"), Assert.Single(files.Moved.Single()).To);
        Assert.False(crumbs.IsOverflowOpen, "меню пережило сброс");
    }

    private static async Task<ProjectWindowStudio> Opened(FilesProbe files, bool twoColumns = false)
    {
        var studio = new ProjectWindowStudio(twoColumns: twoColumns, files: files);

        await studio.Open();

        return studio;
    }

    /// <summary>Выбирает строки, как их выбирают с Ctrl.</summary>
    private static void Choose(ProjectWindowStudio studio, params Row[] rows)
    {
        studio.View.Tree.SelectedItems!.Clear();

        foreach (var row in rows)
            studio.View.Tree.SelectedItems.Add(row);

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Контейнер плитки в показанном списке — прокрутив до неё.</summary>
    private static AxListBoxItem TileItem(ProjectWindowStudio studio, string name)
    {
        var list = studio.Panel.Pane!.Shown;
        var tile = studio.Model.Browser.Items.Single(item => item.Name == name);

        list.ScrollIntoView(tile);
        Dispatcher.UIThread.RunJobs();

        return Assert.IsType<AxListBoxItem>(list.ContainerFromItem(tile));
    }
}
