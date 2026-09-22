using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// Файлы, принесённые в окно проекта из проводника: куда они ложатся, что отмечено целью и где окно
/// отказывает.
/// </summary>
/// <remarks>
/// Несёт их безголовая платформа тем же сырым событием, каким несёт система: подлёт, движение и
/// сброс над точкой окна. Принесённое — настоящие файлы во временной папке теста: окно спрашивает
/// диск, есть ли то, что несут, и каталог ли это.
/// </remarks>
public sealed class ProjectWindowDropTests : IDisposable
{
    private readonly string _outside = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-explorer-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outside, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Принесённое на каталог ложится в него копией одним действием: файл, каталог и файл из того же
    /// каталога — с номером, как при вставке. Каталог отмечен целью, пока над ним держат, и отметка
    /// уходит со сбросом.
    /// </summary>
    [AvaloniaFact]
    public async Task Files_from_explorer_land_in_the_folder_they_are_dropped_on()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var views = studio.Row("Views");
        var logo = File("logo.png");
        var docs = Folder("docs");
        var own = views.Node.Path.Combine("MainWindow.axaml");
        var carried = await Carry(studio, logo, docs, own.Value);

        Assert.Equal(DragDropEffects.Copy, studio.Drag(studio.Item(views), carried));
        Assert.True(views.IsDropTarget, "каталог под курсором не отмечен целью");
        Assert.True(studio.Item(views).IsDropTarget, "строка каталога не показывает цель");

        Assert.Equal(DragDropEffects.Copy, studio.Drag(studio.Item(views), carried, drop: true));

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(
            [
                new FileMove(CanonicalPath.Create(logo), views.Node.Path.Combine("logo.png")),
                new FileMove(CanonicalPath.Create(docs), views.Node.Path.Combine("docs")),
                new FileMove(own, views.Node.Path.Combine("MainWindow (2).axaml")),
            ],
            files.Copied[0]);
        Assert.Empty(files.Moved);
        Assert.Equal(Format(studio, "project.drop.added.label.many", "logo.png", 2), Assert.Single(files.Labels));
        Assert.Contains(Format(studio, "project.drop.added.many", "logo.png", 2), studio.Status.Said);
        Assert.False(views.IsDropTarget, "отметка цели пережила сброс");
    }

    /// <summary>
    /// Над файлом целью становится его каталог — и отмечена строка каталога, а не файла; ушедший за
    /// край окна курсор отметку снимает.
    /// </summary>
    [AvaloniaFact]
    public async Task Over_a_file_its_folder_is_the_target()
    {
        using var studio = await Opened(new FilesProbe());

        var app = studio.Row("App");
        var program = studio.Row("Program.cs");
        var carried = await Carry(studio, File("logo.png"));

        Assert.Equal(DragDropEffects.Copy, studio.Drag(studio.Item(program), carried));
        Assert.True(app.IsDropTarget, "каталог проекта, куда ляжет файл, не отмечен");
        Assert.False(program.IsDropTarget, "отмечен файл, в который ничего не кладут");

        studio.Leave(carried);

        Assert.DoesNotContain(studio.Rows, row => row.IsDropTarget);
    }

    /// <summary>
    /// Куда класть нечего — решение, папка решения, зависимости, — несут не файлы или несут каталог в
    /// него самого и глубже, окно отказывает курсором и не кладёт ничего.
    /// </summary>
    [AvaloniaFact]
    public async Task Where_nothing_can_land_the_window_refuses()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var logo = await Carry(studio, File("logo.png"));
        var views = studio.Row("Views");
        var text = new DataTransfer();

        text.Add(DataTransferItem.Create(DataFormat.Text, "не файл"));

        // Курсор говорит «нельзя» ещё на подлёте, и сброс ничего не кладёт.
        void Refused(Row row, IDataTransfer data)
        {
            Assert.Equal(DragDropEffects.None, studio.Drag(studio.Item(row), data));
            Assert.DoesNotContain(studio.Rows, candidate => candidate.IsDropTarget);
            Assert.Equal(DragDropEffects.None, studio.Drag(studio.Item(row), data, drop: true));
        }

        Refused(studio.Row("Hello"), logo);
        Refused(studio.Row("src"), logo);
        Refused(studio.Rows.First(row => row.Node.Kind == NodeKind.Dependencies), logo);
        Refused(views, text);
        Refused(views, await Carry(studio, views.Node.Path.Value));
        Refused(views, await Carry(studio, views.Node.Path.Directory.Value));

        Dispatcher.UIThread.RunJobs();

        Assert.Empty(files.Copied);
    }

    /// <summary>
    /// В две колонки пустое место и файл колонки кладут в каталог, который она показывает, — обведена
    /// вся колонка, — а плитка каталога — в сам каталог, отмеченный и плиткой, и строкой дерева.
    /// Сброшенное на строку дерева колонка показывает плиткой, и клавиатура уходит к ней.
    /// </summary>
    [AvaloniaFact]
    public async Task The_column_takes_files_into_the_folder_it_shows()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        studio.Select("App");

        var pane = studio.Panel.Pane!;
        var list = pane.Shown;
        var carried = await Carry(studio, File("notes.md"));
        var empty = list.TranslatePoint(new Point(list.Bounds.Width - 4, list.Bounds.Height - 4), studio.Window)!.Value;

        Assert.Equal(DragDropEffects.Copy, studio.Drag(empty, carried));
        Assert.True(studio.Model.IsColumnDropTarget, "пустое место колонки не отметило её каталог");

        Assert.Equal(DragDropEffects.Copy, studio.Drag(TileItem(studio, "Program.cs"), carried));
        Assert.True(studio.Model.IsColumnDropTarget, "файл колонки не отметил её каталог");

        Assert.Equal(DragDropEffects.Copy, studio.Drag(TileItem(studio, "Models"), carried));
        Assert.False(studio.Model.IsColumnDropTarget, "колонка отмечена, хотя ляжет в плитку каталога");
        Assert.True(Tile(studio, "Models").IsDropTarget, "плитка каталога не отмечена");
        Assert.True(studio.Row("Models").IsDropTarget, "строка каталога в дереве не отмечена");

        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(more: ["Models/notes.md"])));
        studio.Drag(TileItem(studio, "Models"), carried, drop: true);

        await Settled(studio, () => pane.Selected?.Name == "notes.md");

        Assert.Equal(studio.Row("Models").Node.Path.Combine("notes.md"), Assert.Single(files.Copied[0]).To);
        Assert.False(studio.Model.IsColumnDropTarget);

        studio.Select("App");
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution(more: ["Models/notes.md", "Views/notes.md"])));
        studio.Drag(studio.Item(studio.Row("Views")), carried, drop: true);

        await Settled(studio, () => studio.Model.Browser.Current?.Name == "Views" && pane.Selected?.Name == "notes.md" && pane.Shown.IsKeyboardFocusWithin);
    }

    /// <summary>
    /// Принесённое из проводника на сегмент крошек ложится копией на этот уровень пути; сегмент решения
    /// целью не бывает.
    /// </summary>
    [AvaloniaFact]
    public async Task A_crumb_takes_files_from_explorer_into_its_folder()
    {
        var files = new FilesProbe();

        using var studio = new ProjectWindowStudio(twoColumns: true, files: files, width: 900);

        await studio.Open();

        studio.Select("Views");

        var carried = await Carry(studio, File("notes.md"));

        Assert.Equal(DragDropEffects.None, studio.Drag(studio.Crumb("Hello"), carried));
        Assert.Equal(DragDropEffects.Copy, studio.Drag(studio.Crumb("App"), carried));
        Assert.True(studio.Crumb("App").IsDropTarget, "сегмент под курсором не отмечен целью");

        studio.Drag(studio.Crumb("App"), carried, drop: true);

        await Settled(studio, () => files.Copied.Count == 1);

        Assert.Equal(studio.Row("App").Node.Path.Directory.Combine("notes.md"), Assert.Single(files.Copied[0]).To);
        Assert.All(studio.Model.Browser.Segments, segment => Assert.False(segment.IsDropTarget, $"{segment.Node.Name} остался отмечен"));
    }

    /// <summary>
    /// Кнопка переполнения крошек — не сегмент: уровни, спрятанные в её меню, целью не становятся, и
    /// сама она не каталог колонки, хотя стоит над ней.
    /// </summary>
    [AvaloniaFact]
    public async Task The_crumbs_overflow_button_is_not_a_target()
    {
        using var studio = new ProjectWindowStudio(twoColumns: true, files: new FilesProbe(), width: 360);

        await studio.Open();

        studio.Select("Views");
        Dispatcher.UIThread.RunJobs();

        var overflow = studio.View.Path.GetVisualDescendants().OfType<AxButton>().Single(button => button.Name == "PART_Overflow");

        Assert.True(overflow.IsEffectivelyVisible, "крошки уместились — переполнения нет, проверять нечего");
        Assert.Equal(DragDropEffects.None, studio.Drag(overflow, await Carry(studio, File("notes.md"))));
        Assert.False(studio.Model.IsColumnDropTarget, "кнопка переполнения отметила колонку");
    }

    /// <summary>
    /// Свёрнутый каталог, над которым держат файлы, раскрывается; унесли раньше — нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_collapsed_folder_opens_when_files_are_held_over_it()
    {
        using var studio = await Opened(new FilesProbe());

        var views = studio.Row("Views");
        var assets = studio.Row("Assets");
        var carried = await Carry(studio, File("logo.png"));

        studio.Drag(studio.Item(views), carried);
        studio.Panel.Drop!.Expand();
        Dispatcher.UIThread.RunJobs();

        Assert.True(views.IsExpanded, "каталог под файлами не раскрылся");
        Assert.Contains(studio.Rows, row => row.Name == "MainWindow.axaml");

        studio.Drag(studio.Item(assets), carried);
        studio.Leave(carried);
        studio.Panel.Drop!.Expand();
        Dispatcher.UIThread.RunJobs();

        Assert.False(assets.IsExpanded, "каталог раскрылся, хотя файлы унесли");
    }

    /// <summary>Файлы, которые держат у края дерева, прокручивают его к этому краю, а в середине — нет.</summary>
    [AvaloniaFact]
    public async Task Files_held_at_an_edge_scroll_the_tree()
    {
        using var studio = new ProjectWindowStudio(height: 200, files: new FilesProbe());

        await studio.Open();

        var tree = studio.View.Tree;
        var viewer = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        var carried = await Carry(studio, File("logo.png"));

        Assert.True(viewer.Extent.Height > viewer.Viewport.Height, "дереву некуда ехать — проверять нечего");

        Point At(double y) => tree.TranslatePoint(new Point(tree.Bounds.Width / 2, y), studio.Window)!.Value;

        studio.Drag(At(tree.Bounds.Height - 2), carried);
        studio.Panel.Drop!.Scroll();
        studio.Window.UpdateLayout();

        var down = viewer.Offset.Y;

        Assert.True(down > 0, "у нижнего края дерево не поехало вниз");

        studio.Drag(At(tree.Bounds.Height / 2), carried);
        studio.Panel.Drop!.Scroll();
        studio.Window.UpdateLayout();

        Assert.Equal(down, viewer.Offset.Y);

        studio.Drag(At(2), carried);
        studio.Panel.Drop!.Scroll();
        studio.Window.UpdateLayout();

        Assert.True(viewer.Offset.Y < down, "у верхнего края дерево не поехало вверх");
    }

    /// <summary>Без службы файлов окно файлов не берёт: класть их некому.</summary>
    [AvaloniaFact]
    public async Task Without_the_files_service_the_window_takes_no_files()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        Assert.Null(studio.Panel.Drop);
        Assert.Equal(DragDropEffects.None, studio.Drag(studio.Item(studio.Row("Views")), await Carry(studio, File("logo.png")), drop: true));
    }

    /// <summary>
    /// Из принесённого берётся то, что лежит на диске, по разу и своим видом: путь, пропавший, пока
    /// несли, и повтор пропускаются.
    /// </summary>
    [Fact]
    public void Only_what_lies_on_disk_is_carried_and_once()
    {
        var logo = File("logo.png");
        var docs = Folder("docs");

        var items = Dropping.Items([logo, Path.Combine(_outside, "gone.txt"), docs, logo]);

        Assert.Equal(
            [(CanonicalPath.Create(logo), false), (CanonicalPath.Create(docs), true)],
            items.Select(item => (item.Root, item.IsFolder)));
    }

    private static async Task<ProjectWindowStudio> Opened(FilesProbe files, bool twoColumns = false)
    {
        var studio = new ProjectWindowStudio(twoColumns: twoColumns, files: files);

        await studio.Open();

        return studio;
    }

    /// <summary>Файл снаружи решения — настоящий, во временной папке теста.</summary>
    private string File(string name)
    {
        var path = Path.Combine(_outside, name);

        System.IO.File.WriteAllText(path, name);

        return path;
    }

    /// <summary>Каталог снаружи решения с файлом внутри.</summary>
    private string Folder(string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(_outside, name)).FullName;

        System.IO.File.WriteAllText(Path.Combine(path, "readme.md"), name);

        return path;
    }

    /// <summary>
    /// Что несёт проводник: файлы и каталоги по путям — элементами хранилища самой платформы, тем же
    /// видом, каким их отдаёт система.
    /// </summary>
    /// <remarks>
    /// Элемент хранилища своим кодом не собрать — интерфейс закрыт для реализации, — и его выдаёт
    /// поставщик хранилища окна. Пропавший с диска путь поставщик не отдаёт: такой несут текстом пути,
    /// и окно должно пропустить его само.
    /// </remarks>
    private static async Task<DataTransfer> Carry(ProjectWindowStudio studio, params string[] paths)
    {
        var data = new DataTransfer();
        var storage = studio.Window.StorageProvider;

        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(path)
                : await storage.TryGetFileFromPathAsync(path);

            Assert.True(item is not null || !System.IO.File.Exists(path), $"поставщик хранилища не отдал {path}");

            if (item is not null)
                data.Add(DataTransferItem.CreateFile(item));
        }

        return data;
    }

    private static Tile Tile(ProjectWindowStudio studio, string name) =>
        studio.Model.Browser.Items.Single(tile => tile.Name == name);

    /// <summary>Контейнер плитки в показанном списке — прокрутив до неё.</summary>
    private static AxListBoxItem TileItem(ProjectWindowStudio studio, string name)
    {
        var list = studio.Panel.Pane!.Shown;
        var tile = Tile(studio, name);

        list.ScrollIntoView(tile);
        Dispatcher.UIThread.RunJobs();

        return Assert.IsType<AxListBoxItem>(list.ContainerFromItem(tile));
    }
}
