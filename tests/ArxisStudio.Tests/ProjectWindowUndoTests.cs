using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.ProjectWindowDialogs;

namespace ArxisStudio.Tests;

/// <summary>
/// Отмена из окна проекта: Ctrl+Z с вопросом, как у Rider, и честная строка в вопросе удаления.
/// </summary>
/// <remarks>
/// Служба истории подделана, как и служба файлов: что стало после отмены, тест говорит сам — новым
/// снимком службы проектов. Проверяется окно: что спросило, что отдало службе, куда встало и что
/// сказало.
/// </remarks>
public class ProjectWindowUndoTests
{
    /// <summary>
    /// Ctrl+Z спрашивает «Отменить «…»?», отменяет последнее действие и встаёт на вернувшееся имя.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_Z_asks_undoes_and_stands_on_the_name_that_came_back()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history, window: "Main");

        studio.Expand("Views");

        var renamed = studio.Row("Main.axaml").Node.Path;
        var label = "Переименование MainWindow.axaml";

        history.LastStudioAction = Moved(7, label, renamed, renamed.Directory.Combine("MainWindow.axaml"));
        history.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution()));

        studio.Press(studio.Item(studio.Select("Main.axaml")), Key.Z, KeyModifiers.Control);

        var dialog = Dialog<UndoDialog>(studio);

        Assert.Equal(Format(studio, "project.undo.question", label), Part<TextBlock>(dialog, "Question").Text);

        // Ctrl+Z, затем Enter: вопрос открывается на «Отменить».
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "MainWindow.axaml" });

        Assert.Equal([7L], history.Undone);
        Assert.Contains(Format(studio, "project.undone", label), studio.Status.Said);
    }

    /// <summary>«Не отменять» и Esc оставляют всё как было: служба не спрошена.</summary>
    [AvaloniaFact]
    public async Task Keeping_leaves_everything_as_it_was()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        history.LastStudioAction = Moved(3, "Переименование App.axaml", studio.Row("App.axaml").Node.Path, studio.Row("App.axaml").Node.Path);

        studio.Press(studio.Item(studio.Select("App.axaml")), Key.Z, KeyModifiers.Control);
        studio.Click(Part<AxButton>(Dialog<UndoDialog>(studio), "Keep"));

        studio.Press(studio.Item(studio.Selected), Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(history.Undone);
        Assert.Empty(studio.Window.OwnedWindows.OfType<UndoDialog>());
    }

    /// <summary>
    /// Отменять нечего или история не ведётся — окно говорит это строкой состояния, не открывая
    /// вопроса.
    /// </summary>
    [AvaloniaFact]
    public async Task With_nothing_to_undo_the_window_says_so_and_asks_nothing()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        studio.Press(studio.Item(studio.Select("App.axaml")), Key.Z, KeyModifiers.Control);

        Assert.Contains(studio.Strings["project.undo.nothing"], studio.Status.Said);

        history.IsOn = false;
        studio.Press(studio.Item(studio.Selected), Key.Z, KeyModifiers.Control);

        Assert.Contains(studio.Strings["project.undo.off"], studio.Status.Said);
        Assert.Empty(studio.Window.OwnedWindows.OfType<UndoDialog>());
    }

    /// <summary>
    /// Отмена, вернувшая не всё, говорит, чего не вернула, — диалогом, а не только строкой.
    /// </summary>
    [AvaloniaFact]
    public async Task An_undo_that_brought_back_less_says_what_did_not_come_back()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var path = studio.Row("App.axaml").Node.Path;

        history.LastStudioAction = Moved(5, "Удаление Assets", path, path);
        history.Answer = () => ProjectOperationResult.Succeeded(
        [
            new ProjectDiagnostic(ProjectsDiagnosticCodes.NotStored, "big.bin больше предела истории", ProjectDiagnosticSeverity.Warning),
        ]);

        studio.Press(studio.Item(studio.Select("App.axaml")), Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.Window.OwnedWindows.OfType<FailureDialog>().Any());

        var failure = Dialog<FailureDialog>(studio);

        Assert.Equal(studio.Strings["project.undo.partial"], failure.Title);
        Assert.Equal("big.bin больше предела истории", Part<TextBlock>(failure, "Message").Text);
    }

    /// <summary>Ctrl+Z в правой колонке отменяет так же, как в дереве.</summary>
    [AvaloniaFact]
    public async Task Ctrl_Z_in_the_pane_undoes_too()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history, twoColumns: true);

        // Путь, который дерево знает: встать на вернувшееся окно попробует и здесь.
        var path = studio.Rows[0].Node.Path;

        history.LastStudioAction = Moved(9, "Переименование App.axaml", path, path);

        studio.Press(studio.Panel.Pane!.Shown, Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => history.Undone.Count > 0);

        Assert.Equal([9L], history.Undone);
    }

    /// <summary>
    /// В две колонки отмена ведёт колонку в папку вернувшегося и выделяет его плитку — даже когда
    /// колонка стоит в другом месте: папка могла пропасть вместе с удалённым, и колонка поднялась.
    /// </summary>
    [AvaloniaFact]
    public async Task In_two_columns_the_undo_opens_the_folder_of_what_came_back()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history, twoColumns: true);

        var root = await studio.Model.WhenAsync(_ => true, TimeSpan.FromSeconds(10));
        var file = root!.Descendants().First(node => node.Name == "MainWindow.axaml");

        Assert.NotEqual("Views", studio.Model.Browser.Current?.Name);

        history.LastStudioAction = new LocalHistoryAction
        {
            Id = 11,
            Time = DateTimeOffset.UnixEpoch,
            Label = "Удаление MainWindow.axaml",
            Origin = LocalHistoryOrigin.Studio,
            Changes = [new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = file.Path }],
        };

        studio.Press(studio.Panel.Pane!.Shown, Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.Panel.Pane!.Selected?.Name == "MainWindow.axaml");

        Assert.Equal("Views", studio.Model.Browser.Current?.Name);
    }

    /// <summary>
    /// Отмена из дерева в две колонки встаёт на строку дерева — с клавиатурой: следующая клавиша
    /// приходится туда же, где человек нажал Ctrl+Z, а не в колонку.
    /// </summary>
    [AvaloniaFact]
    public async Task In_two_columns_an_undo_from_the_tree_stands_on_the_tree_row()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history, twoColumns: true);

        var root = await studio.Model.WhenAsync(_ => true, TimeSpan.FromSeconds(10));
        var views = root!.Descendants().First(node => node.Name == "Views");

        history.LastStudioAction = new LocalHistoryAction
        {
            Id = 12,
            Time = DateTimeOffset.UnixEpoch,
            Label = "Удаление Views",
            Origin = LocalHistoryOrigin.Studio,
            Changes = [new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = views.Path, IsDirectory = true }],
        };

        studio.Press(studio.Item(studio.Select(studio.Rows[0].Name)), Key.Z, KeyModifiers.Control);
        Dialog<UndoDialog>(studio).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Views" });

        Assert.True(studio.Item(studio.Selected).IsKeyboardFocusWithin, "клавиатура ушла со строки дерева");
    }

    /// <summary>
    /// Вопрос удаления говорит, чем удаление страхуется, а файл больше предела истории называет
    /// предупреждением: его не вернёт ничто.
    /// </summary>
    [AvaloniaFact]
    public async Task The_delete_question_says_what_the_history_will_not_bring_back()
    {
        var history = new HistoryProbe { MaxFileBytes = 1024 * 1024 };

        using var studio = await Opened(history);

        var manifest = studio.Row("app.manifest").Node.Path;

        File.WriteAllBytes(manifest.Value, new byte[(1024 * 1024) + 1]);

        studio.Press(studio.Item(studio.Select("app.manifest")), Key.Delete);

        var dialog = Dialog<DeleteDialog>(studio);

        Assert.Equal(studio.Strings["project.delete.note"], Part<TextBlock>(dialog, "Note").Text);
        Assert.Equal(Format(studio, "project.delete.large", "app.manifest", 1), Part<TextBlock>(dialog, "Large").Text);
        Assert.True(Part<TextBlock>(dialog, "Large").IsVisible, "предупреждение спрятано");

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        history.IsOn = false;
        studio.Press(studio.Item(studio.Selected), Key.Delete);
        dialog = Dialog<DeleteDialog>(studio);

        Assert.Equal(studio.Strings["project.delete.note.off"], Part<TextBlock>(dialog, "Note").Text);
        Assert.False(Part<TextBlock>(dialog, "Large").IsVisible, "без истории предупреждать о её пределе незачем");
    }

    /// <summary>Без службы истории Ctrl+Z окну не принадлежит: ни вопроса, ни слова.</summary>
    [AvaloniaFact]
    public async Task Without_a_history_service_ctrl_z_is_not_the_window_s()
    {
        var studio = new ProjectWindowStudio(files: new FilesProbe());

        using (studio)
        {
            await studio.Open();

            var before = studio.Status.Said.Count;

            studio.Press(studio.Item(studio.Select("App.axaml")), Key.Z, KeyModifiers.Control);

            Assert.Equal(before, studio.Status.Said.Count);
            Assert.Empty(studio.Window.OwnedWindows.OfType<UndoDialog>());
        }
    }

    /// <summary>То, что вернёт отмена: переезд — на прежнее имя, удаление — на самое верхнее из вернувшегося.</summary>
    [Fact]
    public void The_undo_stands_on_what_came_back()
    {
        var root = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "arxis-undo", "Views"));
        var moved = Moved(1, "Переименование", root.Combine("Main.axaml"), root.Combine("MainWindow.axaml"));
        var deleted = new LocalHistoryAction
        {
            Id = 2,
            Time = DateTimeOffset.UnixEpoch,
            Label = "Удаление Views",
            Origin = LocalHistoryOrigin.Studio,
            Changes =
            [
                new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = root.Combine("Readme.txt") },
                new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = root, IsDirectory = true },
            ],
        };
        var copied = deleted with
        {
            Changes = [new LocalHistoryChange { Kind = LocalHistoryChangeKind.Created, Path = root.Combine("Copy.cs") }],
        };

        Assert.Equal(root.Combine("MainWindow.axaml"), ProjectPanel.Returned(moved));
        Assert.Equal(root, ProjectPanel.Returned(deleted));
        Assert.Null(ProjectPanel.Returned(copied));
    }

    private static LocalHistoryAction Moved(long id, string label, CanonicalPath path, CanonicalPath from) => new()
    {
        Id = id,
        Time = DateTimeOffset.UnixEpoch,
        Label = label,
        Origin = LocalHistoryOrigin.Studio,
        Changes = [new LocalHistoryChange { Kind = LocalHistoryChangeKind.Moved, Path = path, From = from }],
    };

    private static Task<ProjectWindowStudio> Opened(HistoryProbe history, bool twoColumns = false, string window = "MainWindow") =>
        ProjectWindowStudio.OpenedAsync(twoColumns: twoColumns, history: history, window: window);
}
