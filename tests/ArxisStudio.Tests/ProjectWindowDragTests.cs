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
