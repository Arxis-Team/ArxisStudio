using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Dialogs;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Modules.Project.Tree;
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
/// Правка файлов из окна проекта: «Правка» в меню, Delete и F2, вопросы и то, где потом стоит
/// выделение.
/// </summary>
/// <remarks>
/// Служба файлов подделана: что стало на диске, тест говорит сам — новым снимком службы проектов, как
/// настоящая служба перечитывает модель раньше, чем вернуться. Проверяется окно: что спросило, что
/// отдало службе и куда встало после.
/// </remarks>
public class ProjectWindowEditTests
{
    /// <summary>
    /// «Правка» есть у файлов и папок и только при службе файлов; у проекта из неё — одна вставка, у
    /// решения и зависимостей её нет.
    /// </summary>
    [AvaloniaFact]
    public async Task Files_and_folders_offer_edit_and_nothing_else_does()
    {
        using var studio = await Opened();

        studio.Expand("Views");
        studio.Expand("MainWindow.axaml");

        foreach (var name in new[] { "Program.cs", "Views", "MainWindow.axaml", "MainWindow.axaml.cs" })
        {
            var edit = Edit(studio, name);

            Assert.True(edit is not null, $"у {name} нет «Правки»");
            Assert.Equal(
                [
                    studio.Strings["project.edit.cut"], studio.Strings["project.edit.copy"], studio.Strings["project.edit.paste"],
                    studio.Strings["project.edit.delete"], studio.Strings["project.edit.rename"],
                ],
                edit!.Items.OfType<AxMenuItem>().Select(item => item.Header));
            Assert.All(edit.Items.OfType<AxMenuItem>(), item => Assert.True(item.IsEnabled, $"{item.Header} у {name} выключен"));
        }

        studio.View.Tree.SelectedItem = studio.Row("App");

        Assert.Equal(
            [studio.Strings["project.edit.paste"]],
            studio.EditMenu(studio.Panel.Items(studio.Row("App")))!.Items.OfType<AxMenuItem>().Select(item => item.Header));

        foreach (var row in new[] { studio.Row("Hello"), studio.Rows.First(row => row.Node.Kind == NodeKind.Dependencies) })
        {
            studio.View.Tree.SelectedItem = row;

            Assert.True(studio.EditMenu(studio.Panel.Items(row)) is null, $"у {row.Name} есть «Правка», а править в нём нечего");
        }

        using var bare = new ProjectWindowStudio();

        await bare.Open();

        Assert.True(Edit(bare, "Program.cs") is null, "«Правка» без службы файлов");

        bare.Press(bare.Item(bare.Select("Program.cs")), Key.Delete);
        bare.Press(bare.Item(bare.Row("Program.cs")), Key.F2);

        Assert.Empty(bare.Window.OwnedWindows);
    }

    /// <summary>
    /// Выбор из нескольких удаляется вместе, а переименовать его нельзя; Ctrl+A правке не отдаётся —
    /// остаётся только вставка.
    /// </summary>
    /// <remarks>Пункт выключен, а не спрятан: так видно, почему его нельзя нажать.</remarks>
    [AvaloniaFact]
    public async Task A_choice_of_many_is_deleted_together_and_not_renamed()
    {
        using var studio = await Opened();

        studio.Select("app.manifest");
        studio.Press(studio.Item(studio.Row("app.manifest")), Key.Down, KeyModifiers.Shift);

        Assert.Equal(["app.manifest", "Program.cs"], studio.View.Tree.SelectedItems!.OfType<Row>().Select(row => row.Name));

        var edit = Assert.IsType<AxMenuItem>(studio.EditMenu(studio.Panel.Items(studio.Row("Program.cs"))));

        AxMenuItem Item(string key) => edit.Items.OfType<AxMenuItem>().Single(item => Equals(item.Header, studio.Strings[key]));

        Assert.True(Item("project.edit.delete").IsEnabled, "удалить выбор из нескольких нельзя");
        Assert.True(Item("project.edit.cut").IsEnabled, "вырезать выбор из нескольких нельзя");
        Assert.False(Item("project.edit.rename").IsEnabled, "переименовать выбор из нескольких можно");

        // Ctrl+A выделяет и решение с проектами: правке выбор не отдаётся, и из «Правки» остаётся
        // только вставка в папку строки.
        studio.View.Tree.SelectAll();

        Assert.Equal(
            [studio.Strings["project.edit.paste"]],
            studio.EditMenu(studio.Panel.Items(studio.Row("Program.cs")))!.Items.OfType<AxMenuItem>().Select(item => item.Header));
    }

    /// <summary>
    /// Delete спрашивает, называя файл и вложенный в него, удаляет оба, и выделение встаёт на соседа,
    /// занявшего место удалённого.
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_asks_about_the_file_and_its_nested_file_and_a_neighbour_takes_its_place()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        var owner = studio.Row("App.axaml");
        var at = studio.Rows.IndexOf(owner);
        var paths = owner.Node.Descendants().Select(node => node.Path).ToList();

        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(without: ["App.axaml", "App.axaml.cs"])));

        studio.Select("App.axaml");
        studio.Press(studio.Item(owner), Key.Delete);

        var dialog = Dialog<DeleteDialog>(studio);

        Assert.Equal(Format(studio, "project.delete.file.nested", "App.axaml", "App.axaml.cs"), Part<TextBlock>(dialog, "Question").Text);

        // Delete, затем Enter — как в Rider и в проводнике: вопрос открывается на «Удалить».
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);

        await Settled(studio, () => studio.Rows.All(row => row.Name != "App.axaml") && studio.View.Tree.SelectedItem is Row);

        Assert.Equal(paths, Assert.Single(files.Deleted));
        Assert.Equal(Format(studio, "project.delete.label", "App.axaml"), Assert.Single(files.Labels));
        Assert.Equal("app.manifest", studio.Rows[at].Name);
        Assert.Equal("app.manifest", studio.Selected.Name);
        Assert.Contains(Format(studio, "project.deleted", "App.axaml"), studio.Status.Said);
    }

    /// <summary>
    /// Правый щелчок по выбранной строке оставляет весь выбор — меню о нём, — а по невыбранной
    /// выбирает её одну.
    /// </summary>
    [AvaloniaFact]
    public async Task A_right_click_on_the_choice_keeps_it_and_elsewhere_replaces_it()
    {
        using var studio = await Opened();

        studio.Select("app.manifest");
        studio.View.Tree.SelectedItems!.Add(studio.Row("Program.cs"));

        studio.RightClick(studio.Label("Program.cs"));

        Assert.Equal(["app.manifest", "Program.cs"], studio.View.Tree.SelectedItems.OfType<Row>().Select(row => row.Name));

        // Открытое меню закрывается первым щелчком мимо него, и до строки такой щелчок не доходит.
        studio.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        studio.RightClick(studio.Label("Views"));

        Assert.Equal(["Views"], studio.View.Tree.SelectedItems.OfType<Row>().Select(row => row.Name));
    }

    /// <summary>
    /// Клавиши дерева говорят о строке с кольцом фокуса, а не о первой выделенной.
    /// </summary>
    /// <remarks>
    /// При множественном выборе кольцо и выделение расходятся: Ctrl со стрелкой ведёт кольцо, не
    /// трогая выбора, и стрелка вправо раскрывает то, на чём кольцо стоит.
    /// </remarks>
    [AvaloniaFact]
    public async Task Tree_keys_speak_of_the_row_with_the_focus_ring()
    {
        using var studio = await Opened();

        studio.Select("Program.cs");

        var views = studio.Item(studio.Row("Views"));

        views.Focus(NavigationMethod.Directional);
        studio.Press(views, Key.Right);

        Assert.True(studio.Row("Views").IsExpanded, "стрелка ушла к выделенной строке, а не к той, где фокус");
        Assert.Equal("Program.cs", studio.Selected.Name);
    }

    /// <summary>Esc и «Отмена» оставляют всё как было: служба не слышит ничего.</summary>
    [AvaloniaFact]
    public async Task Escape_and_cancel_leave_everything_as_it_was()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        studio.Press(studio.Item(studio.Select("Program.cs")), Key.Delete);
        Dialog<DeleteDialog>(studio).KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        studio.Press(studio.Item(studio.Row("Program.cs")), Key.F2);
        studio.Click(Part<AxButton>(Dialog<RenameDialog>(studio), "Cancel"));

        Assert.Empty(studio.Window.OwnedWindows);
        Assert.Empty(files.Deleted);
        Assert.Empty(files.Moved);
        Assert.Equal("Program.cs", studio.Selected.Name);
    }

    /// <summary>
    /// Вопрос называет то, что уйдёт, по имени, а остальное — числом: файл, файл с вложенными, папку
    /// с числом файлов в ней, выбор из нескольких.
    /// </summary>
    /// <remarks>Форм множественного числа словари модулей не знают — число стоит после двоеточия.</remarks>
    [AvaloniaFact]
    public async Task The_question_names_what_goes_and_counts_the_rest()
    {
        using var studio = await Opened();

        var root = studio.Model.Tree.Root!;

        Node Named(string name) => root.Descendants().Single(node => node.Name == name);

        string Ask(Func<CanonicalPath, int?> count, params Node[] nodes) =>
            Editing.Question(EditSelection.Of(nodes), studio.Strings, count);

        Assert.Equal(Format(studio, "project.delete.file", "Program.cs"), Ask(Editing.CountFiles, Named("Program.cs")));
        Assert.Equal(
            Format(studio, "project.delete.file.nested", "MainWindow.axaml", "MainWindow.axaml.cs"),
            Ask(Editing.CountFiles, Named("MainWindow.axaml")));
        Assert.Equal(Format(studio, "project.delete.folder.count", "Views", 2), Ask(Editing.CountFiles, Named("Views")));
        Assert.Equal(Format(studio, "project.delete.folder.count", "Views", $"{Editing.CountLimit}+"), Ask(_ => Editing.CountLimit + 1, Named("Views")));
        Assert.Equal(Format(studio, "project.delete.folder", "Views"), Ask(_ => null, Named("Views")));
        Assert.Equal(Format(studio, "project.delete.files", 3), Ask(Editing.CountFiles, Named("Program.cs"), Named("App.axaml")));
        Assert.Equal(Format(studio, "project.delete.folders", 2), Ask(Editing.CountFiles, Named("Views"), Named("Assets")));
        Assert.Equal(Format(studio, "project.delete.mixed", 1, 1), Ask(Editing.CountFiles, Named("Program.cs"), Named("Views")));

        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        solution.File(app, "Page.xaml", "Page");
        solution.File(app, "Page.xaml.cs", dependentUpon: "Page.xaml");
        solution.File(app, "Page.xaml.cs.map", ProjectItemTypes.None);

        Assert.Equal(
            Format(studio, "project.delete.file.nestedMany", "Page.xaml", 2),
            Editing.Question(EditSelection.Of([solution.Tree().Descendants().Single(node => node.Name == "Page.xaml")]), studio.Strings, _ => 0));
    }

    /// <summary>
    /// F2 открывает имя с выделенной основой, показывает вложенный, который переименуется следом, и
    /// после правки выделение стоит на новом имени.
    /// </summary>
    [AvaloniaFact]
    public async Task F2_renames_a_file_with_its_nested_file_and_stands_on_the_new_name()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        studio.Expand("Views");
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(window: "Main")));

        studio.Press(studio.Item(studio.Select("MainWindow.axaml")), Key.F2);

        var dialog = Dialog<RenameDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");

        Assert.Equal("MainWindow.axaml", field.Text);
        Assert.Equal((0, "MainWindow".Length), (field.SelectionStart, field.SelectionEnd));
        Assert.Equal(Format(studio, "project.rename.prompt", "MainWindow.axaml"), Part<TextBlock>(dialog, "Prompt").Text);

        field.Text = "Main.axaml";

        Assert.Equal(
            ["MainWindow.axaml.cs → Main.axaml.cs"],
            Part<StackPanel>(dialog, "Pairs").Children.OfType<TextBlock>().Select(line => line.Text));

        Enter(dialog);

        await Settled(studio, () => studio.View.Tree.SelectedItem is Row { Name: "Main.axaml" });

        var moves = Assert.Single(files.Moved);

        Assert.Equal(["MainWindow.axaml", "MainWindow.axaml.cs"], moves.Select(move => move.From.FileName));
        Assert.Equal(["Main.axaml", "Main.axaml.cs"], moves.Select(move => move.To.FileName));
        Assert.All(moves, move => Assert.Equal(move.From.Directory, move.To.Directory));
        Assert.Equal(Format(studio, "project.rename.label", "MainWindow.axaml"), Assert.Single(files.Labels));
        Assert.Contains(Format(studio, "project.renamed", "MainWindow.axaml", "Main.axaml"), studio.Status.Said);
        Assert.True(studio.Item(studio.Selected).IsKeyboardFocusWithin, "клавиатура не вернулась на переименованный");
    }

    /// <summary>
    /// Пока имя печатают, диалог говорит, что с ним не так, и кнопка выключена; то же имя — тоже не
    /// правка, но без упрёка.
    /// </summary>
    [AvaloniaFact]
    public async Task The_rename_dialog_says_what_is_wrong_with_the_name()
    {
        using var studio = await Opened();

        studio.Expand("Views");

        var owner = studio.Select("MainWindow.axaml");

        File.WriteAllText(owner.Node.Path.Directory.Combine("About.axaml").Value, string.Empty);
        studio.Press(studio.Item(owner), Key.F2);

        var dialog = Dialog<RenameDialog>(studio);
        var field = Part<AxTextBox>(dialog, "Chosen");
        var problem = Part<TextBlock>(dialog, "Problem");
        var confirm = Part<AxButton>(dialog, "Confirm");

        (string Typed, string Key, string? Subject)[] wrong =
        [
            (string.Empty, "project.rename.empty", null),
            ("Main:Window.axaml", "project.rename.invalid", ":"),
            ("Main.", "project.rename.trailing", null),
            ("nul.axaml", "project.rename.reserved", "nul"),
            ("About.axaml", "project.rename.taken", "About.axaml"),
        ];

        foreach (var (typed, key, subject) in wrong)
        {
            field.Text = typed;

            Assert.False(confirm.IsEnabled, $"«{typed}» можно переименовать");
            Assert.True(problem.IsVisible, $"о «{typed}» диалог молчит");
            Assert.Equal(Format(studio, key, subject ?? string.Empty), problem.Text);
        }

        field.Text = "MainWindow.axaml";

        Assert.False(confirm.IsEnabled, "то же имя можно «переименовать»");
        Assert.False(problem.IsVisible, "то же имя названо ошибкой");

        field.Text = "Shell.axaml";

        Assert.True(confirm.IsEnabled, "годное имя не принято");
        Assert.False(problem.IsVisible);

        dialog.Close();
    }

    /// <summary>Отказ службы показан её словами, а строка состояния об удаче молчит.</summary>
    [AvaloniaFact]
    public async Task A_refusal_of_the_service_is_shown_in_its_own_words()
    {
        var files = new FilesProbe
        {
            Answer = () => ProjectOperationResult.Failed(
                new ProjectDiagnostic("PRJ1008", "Файл занят другой программой", ProjectDiagnosticSeverity.Error)),
        };

        using var studio = await Opened(files);

        studio.Press(studio.Item(studio.Select("Program.cs")), Key.Delete);
        studio.Click(Part<AxButton>(Dialog<DeleteDialog>(studio), "Confirm"));

        await Settled(studio, () => studio.Window.OwnedWindows.OfType<FailureDialog>().Any());

        var failure = Assert.Single(studio.Window.OwnedWindows.OfType<FailureDialog>());

        Assert.Equal("Файл занят другой программой", Part<TextBlock>(failure, "Message").Text);
        Assert.Empty(studio.Status.Said);

        studio.Click(Part<AxButton>(failure, "Dismiss"));

        Assert.Empty(studio.Window.OwnedWindows);
        Assert.Equal("Program.cs", studio.Selected.Name);
    }

    /// <summary>
    /// В две колонки плитку переименовывают и удаляют там же, и выделение остаётся в колонке, а не
    /// уводит её в переименованное.
    /// </summary>
    [AvaloniaFact]
    public async Task Tiles_are_edited_in_the_column_and_the_column_stays()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var pane = studio.Panel.Pane!;

        studio.Select("Views");
        pane.Select(studio.Tile("MainWindow.axaml").Node, focus: true);
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(window: "Main")));

        studio.Press(pane.Shown, Key.F2);
        Part<AxTextBox>(Dialog<RenameDialog>(studio), "Chosen").Text = "Main.axaml";
        Enter(Dialog<RenameDialog>(studio));

        await Settled(studio, () => pane.Selected?.Name == "Main.axaml");

        Assert.Equal("Views", studio.Model.Browser.Current!.Name);

        studio.Select("App");
        pane.Select(studio.Tile("App.axaml").Node, focus: true);

        var at = studio.Model.Browser.Items.IndexOf(studio.Tile("App.axaml"));

        files.After = () => studio.Projects.Publish(
            ProjectWindowStudio.Ready(3, studio.Solution(window: "Main", without: ["App.axaml", "App.axaml.cs"])));

        studio.Press(pane.Shown, Key.Delete);
        studio.Click(Part<AxButton>(Dialog<DeleteDialog>(studio), "Confirm"));

        await Settled(studio, () => studio.Model.Browser.Items.All(tile => tile.Name != "App.axaml") && pane.Selected is not null);

        Assert.Equal(2, Assert.Single(files.Deleted).Count);
        Assert.Equal("App", studio.Model.Browser.Current!.Name);
        Assert.Equal(studio.Model.Browser.Items[at].Name, pane.Selected!.Name);
    }

    /// <summary>
    /// Папка колонки пропала из дерева вместе с удалённым — колонка поднимается к родителю, и
    /// выделение встаёт на плитку, занявшую место пропавшей папки, — а не на первую попавшуюся.
    /// </summary>
    /// <remarks>
    /// Опустевшая папка не пропадает: она стоит на диске, и дерево показывает её пустой. Здесь файлы
    /// остаются на диске, а проект их больше не называет, — так папка пропадает, как пропадает папка,
    /// которую убрали мимо окна. Так нашла живая проверка: колонка, поднявшись, выделяла «Зависимости».
    /// </remarks>
    [AvaloniaFact]
    public async Task When_the_folder_vanishes_its_neighbour_takes_its_place_one_level_up()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var pane = studio.Panel.Pane!;

        studio.Select("App");

        var place = studio.Model.Browser.Items.IndexOf(studio.Tile("Views"));

        studio.Select("Views");
        pane.Select(studio.Tile("MainWindow.axaml").Node, focus: true);
        files.After = () => studio.Projects.Publish(ProjectWindowStudio.Ready(
            2, studio.Solution(without: ["Views/MainWindow.axaml", "Views/MainWindow.axaml.cs"])));

        studio.Press(pane.Shown, Key.Delete);
        studio.Click(Part<AxButton>(Dialog<DeleteDialog>(studio), "Confirm"));

        await Settled(studio, () => studio.Model.Browser.Current?.Name == "App" && pane.Selected is not null);

        Assert.True(place > 0, "папка стояла первой — проверка не отличила бы соседа от первой плитки");
        Assert.Equal(studio.Model.Browser.Items[place].Name, pane.Selected!.Name);
    }

    /// <summary>
    /// Удалив последний файл папки, колонка остаётся в опустевшей папке — она стоит на диске и в
    /// дереве, как у Rider и Unity, — и клавиатура остаётся в колонке.
    /// </summary>
    /// <remarks>
    /// Так нашёл человек: удалив <c>avalonia-logo.ico</c>, он терял <c>Assets</c> и в дереве, и в
    /// колонке — окно брало папки только из элементов проекта. Плитки, на которой стояла клавиатура,
    /// больше нет, а <c>Ctrl+Z</c> и <c>Backspace</c> ждут её в колонке.
    /// </remarks>
    [AvaloniaFact]
    public async Task Deleting_the_last_file_leaves_its_folder_standing_empty()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files, twoColumns: true);

        var pane = studio.Panel.Pane!;

        studio.Select("Assets");

        var logo = studio.Tile("avalonia-logo.ico").Node;

        pane.Select(logo, focus: true);
        files.After = () =>
        {
            File.Delete(logo.Path.Value);
            studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(without: ["Assets/avalonia-logo.ico"])));
        };

        studio.Press(pane.Shown, Key.Delete);
        studio.Click(Part<AxButton>(Dialog<DeleteDialog>(studio), "Confirm"));

        await Settled(studio, () => studio.Model.Browser.Items.Count == 0 && pane.Shown.IsKeyboardFocusWithin);

        Assert.Equal("Assets", studio.Model.Browser.Current!.Name);
        Assert.True(studio.Model.BrowserEmpty, "колонка не сказала, что папка пуста");
        Assert.Contains(studio.Rows, row => row.Name == "Assets");
    }

    /// <summary>
    /// Удалив последний файл папки из дерева, папка остаётся строкой без шеврона, а выделение встаёт
    /// на строку, занявшую место удалённого.
    /// </summary>
    [AvaloniaFact]
    public async Task Deleting_the_last_file_from_the_tree_keeps_its_folder_as_a_leaf()
    {
        var files = new FilesProbe();

        using var studio = await Opened(files);

        studio.Expand("Assets");

        var logo = studio.Row("avalonia-logo.ico");
        var at = studio.Rows.IndexOf(logo);

        files.After = () =>
        {
            File.Delete(logo.Node.Path.Value);
            studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(without: ["Assets/avalonia-logo.ico"])));
        };

        studio.Select("avalonia-logo.ico");
        studio.Press(studio.Item(logo), Key.Delete);
        studio.Click(Part<AxButton>(Dialog<DeleteDialog>(studio), "Confirm"));

        await Settled(studio, () => studio.Rows.All(row => row.Name != "avalonia-logo.ico") && studio.View.Tree.SelectedItem is Row);

        var assets = studio.Row("Assets");

        Assert.False(assets.HasChildren, "у опустевшей папки остался шеврон");
        Assert.Equal(at - 1, studio.Rows.IndexOf(assets));
        Assert.Equal("Models", studio.Selected.Name);
        Assert.Equal(studio.Rows[at].Name, studio.Selected.Name);
    }

    private static Task<ProjectWindowStudio> Opened(FilesProbe? files = null, bool twoColumns = false) =>
        ProjectWindowStudio.OpenedAsync(files, twoColumns);

    /// <summary>«Правка» в меню строки, выбранной одной; пусто — её нет.</summary>
    private static AxMenuItem? Edit(ProjectWindowStudio studio, string name) =>
        studio.EditMenu(studio.Panel.Items(studio.Select(name)));
}
