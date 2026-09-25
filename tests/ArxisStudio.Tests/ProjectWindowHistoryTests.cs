using System.Text;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.History;
using ArxisStudio.Modules.Project.Model;
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
/// Окно локальной истории: открывается из меню строки, показывает строки и разницу, возвращает,
/// отменяет и ставит метки — всё через службу истории.
/// </summary>
/// <remarks>
/// Служба подделана: строки истории и содержимое по ручкам тест кладёт сам, а файл «сейчас» лежит на
/// диске решения. Проверяется окно — что показало и что попросило у службы.
/// </remarks>
public class ProjectWindowHistoryTests
{
    /// <summary>
    /// «Локальная история» есть у файла, папки, проекта и решения — и только при службе истории; у
    /// зависимостей её нет.
    /// </summary>
    [AvaloniaFact]
    public async Task Local_history_is_offered_for_files_folders_projects_and_the_solution()
    {
        using var studio = await Opened(new HistoryProbe());

        foreach (var name in new[] { "App.axaml", "Views", studio.Rows[0].Name })
            Assert.NotNull(History(studio, name));

        var dependencies = studio.Rows.First(row => row.Node.Kind == NodeKind.Dependencies);

        Assert.Null(History(studio, dependencies.Name));

        // Зависимость с путём — ссылка на проект или сборку — тоже не файл решения: её истории нет.
        studio.Model.Tree.ExpandBranch(dependencies);
        Dispatcher.UIThread.RunJobs();

        var reference = studio.Rows.First(row => row.Node is { Kind: NodeKind.Dependency, Path.IsEmpty: false });

        studio.View.Tree.SelectedItem = reference;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(studio.Panel.Items(reference).SingleOrDefault(item => Equals(item.Header, studio.Strings["project.menu.history"])));

        using var bare = new ProjectWindowStudio(files: new FilesProbe());

        await bare.Open();

        Assert.Null(History(bare, "App.axaml"));
    }

    /// <summary>
    /// История файла — окном: строки истории, а справа разница того, каким файл был до строки, с тем,
    /// каким лежит сейчас; второй запрос выводит то же окно, а не открывает новое.
    /// </summary>
    [AvaloniaFact]
    public async Task The_history_of_a_file_shows_its_rows_and_the_difference_to_now()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "a\nb\nc\n");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("a\nx\nc\n");
        history.Contents["c0"] = Encoding.UTF8.GetBytes("a\nb\nc\n");
        history.Revisions[file] = [Row(4, "Внешнее изменение", Modified(file, "c1", "c2")), Row(3, "Давнее", Modified(file, "c0", "c1"))];

        var window = await Show(studio, "App.axaml");
        var model = window.Model!;

        Assert.Equal(Format(studio, "project.history.title", "App.axaml"), window.Title);
        Assert.Equal(["Внешнее изменение", "Давнее"], model.Rows.Select(row => row.Title));
        Assert.StartsWith(Format(studio, "project.history.before", "Внешнее изменение", model.Rows[0].When), model.LeftCaption, StringComparison.Ordinal);
        Assert.Equal(
            [(DiffKind.Same, "a", "a"), (DiffKind.Changed, "x", "b"), (DiffKind.Same, "c", "c")],
            model.Diff.Select(line => (line.Row.Kind, line.LeftText, line.RightText)));
        Assert.Equal(Format(studio, "project.history.changes", 1), model.Status);
        Assert.True(window.Revert.IsEnabled, "вернуть нельзя, хотя слева другое содержимое");

        // До давней правки файл был таким же, как сейчас: возвращать нечего.
        model.Selected = model.Rows[1];
        await model.Shown;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(studio.Strings["project.history.same"], model.Status);
        Assert.False(window.Revert.IsEnabled, "вернуть к тому же самому предлагается");

        await Show(studio, "App.axaml");

        Assert.Single(studio.Window.OwnedWindows.OfType<HistoryWindow>());
    }

    /// <summary>
    /// История проекта — история его папки, а не файла проекта; панель, прощаясь, закрывает окна
    /// истории: за ними стоят служба и словари модуля.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_shows_its_folder_and_the_panel_closes_history_windows_on_leaving()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var project = studio.Rows.First(row => row.Node.Kind == NodeKind.Project);
        var window = await Show(studio, project.Name);

        Assert.Equal(project.Node.Path.Directory, window.Model!.Target);
        Assert.True(window.Model.IsFolder, "историю проекта показали историей файла");

        studio.Reopen();

        Assert.Empty(studio.Window.OwnedWindows.OfType<HistoryWindow>());
    }

    /// <summary>«Вернуть» просит службу вернуть файл к тому, что слева, — меткой с именем и временем.</summary>
    [AvaloniaFact]
    public async Task Revert_asks_the_service_for_what_stands_on_the_left()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "b\n");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("a\n");
        history.Revisions[file] = [Row(4, "Внешнее изменение", Modified(file, "c1", "c2"))];

        var window = await Show(studio, "App.axaml");

        studio.Click(window.Revert);
        await window.Work;

        var (path, content, label) = Assert.Single(history.Reverted);

        Assert.Equal(file, path);
        Assert.Equal("c1", content.Id);
        Assert.Equal(Format(studio, "project.history.revert.label", "App.axaml", "Внешнее изменение"), label);
    }

    /// <summary>
    /// Перед появлением файла не было, и вернуть к этому нечем; содержимое, которого история не
    /// хранит, так и названо.
    /// </summary>
    [AvaloniaFact]
    public async Task What_cannot_be_reverted_to_is_said_and_not_offered()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "a\n");

        history.Revisions[file] =
        [
            Row(5, "Большая правка", new LocalHistoryChange { Kind = LocalHistoryChangeKind.Modified, Path = file, TooLarge = true }),
            Row(4, "Появился", new LocalHistoryChange { Kind = LocalHistoryChangeKind.Created, Path = file, After = new LocalHistoryContent("c1") }),
        ];

        var window = await Show(studio, "App.axaml");
        var model = window.Model!;

        Assert.Equal(studio.Strings["project.history.notStored"], model.Status);
        Assert.False(window.Revert.IsEnabled, "вернуть к тому, чего история не хранит, нечем");

        model.Selected = model.Rows[1];
        await model.Shown;

        Assert.Equal(studio.Strings["project.history.absent"], model.Status);
        Assert.False(window.Revert.IsEnabled, "вернуть к «файла не было» нельзя");
    }

    /// <summary>
    /// История папки раскрывает строку файлами действия, а разница — что действие сделало с выбранным;
    /// «Вернуть файл» ставит его содержимое «до».
    /// </summary>
    [AvaloniaFact]
    public async Task The_history_of_a_folder_lists_the_files_of_the_action()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        studio.Expand("Views");

        var views = studio.Row("Views").Node.Path;
        var gone = views.Combine("Old.axaml");
        var edited = views.Combine("MainWindow.axaml");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("старое\n");
        history.Contents["c2"] = Encoding.UTF8.GetBytes("было\n");
        history.Contents["c3"] = Encoding.UTF8.GetBytes("стало\n");
        history.Revisions[views] =
        [
            Row(7, "Правка Views",
                new LocalHistoryChange { Kind = LocalHistoryChangeKind.Deleted, Path = gone, Before = new LocalHistoryContent("c1") },
                Modified(edited, "c2", "c3")),
        ];

        var window = await Show(studio, "Views");
        var model = window.Model!;

        Assert.Equal(
            [Format(studio, "project.history.file.deleted", "Old.axaml", string.Empty), Format(studio, "project.history.file.modified", "MainWindow.axaml", string.Empty)],
            model.Files.Select(item => item.Title));
        Assert.Equal([(DiffKind.Removed, "старое", (string?)null)], model.Diff.Select(line => (line.Row.Kind, line.LeftText, line.RightText)));

        studio.Click(window.Revert);
        await window.Work;

        Assert.Equal((gone, "c1"), (history.Reverted[0].Path, history.Reverted[0].Content.Id));

        model.File = model.Files[1];
        await model.Shown;

        Assert.Equal([(DiffKind.Changed, "было", "стало")], model.Diff.Select(line => (line.Row.Kind, line.LeftText, line.RightText)));
    }

    /// <summary>«Отменить действие» спрашивает, как Ctrl+Z, и отменяет выбранное; отменённое и метку — нельзя.</summary>
    [AvaloniaFact]
    public async Task Undo_from_the_window_asks_and_undoes_the_chosen_action()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "b\n");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("a\n");
        history.Revisions[file] =
        [
            new LocalHistoryRevision { Action = Action(9, "метка") },
            Row(8, "Отменённое", Modified(file, "c1", "c2")) with { Action = Action(8, "Отменённое", undone: true, Modified(file, "c1", "c2")) },
            Row(4, "Правка", Modified(file, "c1", "c2")),
        ];

        var window = await Show(studio, "App.axaml");
        var model = window.Model!;

        Assert.False(window.Undo.IsEnabled, "метку отменять нечего");

        model.Selected = model.Rows[1];
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.Undo.IsEnabled, "отменённое отменять второй раз нельзя");

        model.Selected = model.Rows[2];
        Dispatcher.UIThread.RunJobs();
        studio.Click(window.Undo);

        var dialog = Assert.Single(window.OwnedWindows.OfType<UndoDialog>());

        Assert.Equal(Format(studio, "project.undo.question", "Правка"), Part<TextBlock>(dialog, "Question").Text);

        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
        await window.Work;

        Assert.Equal([4L], history.Undone);
    }

    /// <summary>Метку ставят из меню строки: текст спрашивается и уходит службе.</summary>
    [AvaloniaFact]
    public async Task A_label_is_put_from_the_row_menu()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var menu = History(studio, "App.axaml")!;

        studio.Click(menu.Items.OfType<AxMenuItem>(), studio.Strings["project.menu.history.label"]);

        var dialog = Dialog<LabelDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");

        Assert.False(Part<AxButton>(dialog, "Confirm").IsEnabled, "пустую метку поставить можно");

        field.Text = "до переделки";
        Enter(dialog);

        await Settled(studio, () => history.Labels.Count > 0);

        Assert.Equal(["до переделки"], history.Labels);
        Assert.Contains(Format(studio, "project.history.labelled", "до переделки"), studio.Status.Said);
    }

    /// <summary>F7 и Shift+F7 ходят по изменениям разницы; Esc закрывает окно.</summary>
    [AvaloniaFact]
    public async Task F7_walks_the_changes_and_escape_closes_the_window()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "a\nB\nc\nd\nE\n");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("a\nb\nc\nd\ne\n");
        history.Revisions[file] = [Row(4, "Правка", Modified(file, "c1", "c2"))];

        var window = await Show(studio, "App.axaml");

        window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, string.Empty);
        Assert.Equal(1, window.Diff.SelectedIndex);

        window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, string.Empty);
        Assert.Equal(4, window.Diff.SelectedIndex);

        window.KeyPress(Key.F7, RawInputModifiers.Shift, PhysicalKey.F7, string.Empty);
        Assert.Equal(1, window.Diff.SelectedIndex);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(studio.Window.OwnedWindows.OfType<HistoryWindow>());
        Assert.Empty(studio.Panel.Histories);
    }

    /// <summary>Окно следит за историей: прибавилось — строки перечитаны, выбранная осталась выбранной.</summary>
    [AvaloniaFact]
    public async Task The_window_follows_the_history()
    {
        var history = new HistoryProbe();

        using var studio = await Opened(history);

        var file = Write(studio, "App.axaml", "b\n");

        history.Contents["c1"] = Encoding.UTF8.GetBytes("a\n");
        history.Revisions[file] = [Row(4, "Правка", Modified(file, "c1", "c2"))];

        var window = await Show(studio, "App.axaml");
        var model = window.Model!;

        history.Revisions[file] = [Row(6, "Ещё правка", Modified(file, "c2", "c3")), Row(4, "Правка", Modified(file, "c1", "c2"))];
        history.Raise();

        await Settled(studio, () => model.Rows.Count == 2);

        Assert.Equal(4, model.Selected?.Id);
    }

    private static Task<ProjectWindowStudio> Opened(HistoryProbe history) =>
        ProjectWindowStudio.OpenedAsync(history: history);

    /// <summary>«Локальная история ▸» в меню строки; пусто — её нет.</summary>
    private static AxMenuItem? History(ProjectWindowStudio studio, string name) =>
        studio.Panel.Items(studio.Select(name)).SingleOrDefault(item => Equals(item.Header, studio.Strings["project.menu.history"]));

    /// <summary>Открывает историю строки из меню и ждёт, пока окно покажет первую строку.</summary>
    private static async Task<HistoryWindow> Show(ProjectWindowStudio studio, string name)
    {
        var menu = History(studio, name) ?? throw new InvalidOperationException($"у {name} нет истории в меню");

        studio.Click(menu.Items.OfType<AxMenuItem>(), studio.Strings["project.menu.history.show"]);

        var window = Assert.Single(studio.Window.OwnedWindows.OfType<HistoryWindow>());

        await window.Work;
        await window.Model!.Shown;
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static CanonicalPath Write(ProjectWindowStudio studio, string name, string text)
    {
        var path = studio.Row(name).Node.Path;

        File.WriteAllText(path.Value, text);

        return path;
    }

    private static LocalHistoryChange Modified(CanonicalPath path, string before, string after) => new()
    {
        Kind = LocalHistoryChangeKind.Modified,
        Path = path,
        Before = new LocalHistoryContent(before),
        After = new LocalHistoryContent(after),
    };

    private static LocalHistoryAction Action(long id, string label, bool undone = false, params LocalHistoryChange[] changes) => new()
    {
        Id = id,
        Time = DateTimeOffset.Now.AddMinutes(-id),
        Label = label,
        Origin = LocalHistoryOrigin.External,
        Changes = [.. changes],
        IsUndone = undone,
    };

    private static LocalHistoryRevision Row(long id, string label, params LocalHistoryChange[] changes) => new()
    {
        Action = Action(id, label, false, changes),
        Changes = [.. changes],
    };
}
