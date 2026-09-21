using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правила правки файлов в окне проекта без окна: что берётся из выбранного и каким станет имя.
/// </summary>
public class ProjectEditRulesTests
{
    /// <summary>
    /// Файл уносит вложенный в него, а вложенный, выбранный без владельца, идёт один — как у Rider.
    /// </summary>
    [Fact]
    public void A_file_takes_its_nested_file_along_and_a_nested_file_goes_alone()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();
        var owner = Named(tree, "MainWindow.axaml");
        var nested = Named(tree, "MainWindow.axaml.cs");

        var both = EditSelection.Of([owner]);

        Assert.Equal([owner.Path, nested.Path], both.Paths);
        Assert.Equal(2, both.Files);
        Assert.True(both.CanRename, "владелец с вложенным — одно переименование");

        var alone = EditSelection.Of([nested]);

        Assert.Equal([nested.Path], alone.Paths);
        Assert.Equal(1, alone.Files);

        Assert.Equal([owner], EditSelection.Of([nested, owner]).Roots);
    }

    /// <summary>Лежащее в выбранной папке уходит вместе с ней и отдельно не считается.</summary>
    [Fact]
    public void What_lies_in_a_chosen_folder_goes_with_the_folder()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();
        var views = Named(tree, "Views");

        var selection = EditSelection.Of([Named(tree, "MainWindow.axaml"), views, Named(tree, "Program.cs")]);

        Assert.Equal(["Program.cs", "Views"], selection.Roots.Select(root => root.Name).Order(StringComparer.Ordinal));
        Assert.Equal((1, 1), (selection.Files, selection.Folders));
        Assert.False(selection.CanRename, "переименовать можно одно");
    }

    /// <summary>
    /// Выбор, в котором есть что-то кроме файлов и папок, правке не отдаётся вовсе.
    /// </summary>
    /// <remarks>Ctrl+A выделяет и решение с проектами — удалить из него «что получится» нельзя.</remarks>
    [Fact]
    public void A_choice_with_anything_but_files_and_folders_is_not_edited()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();

        Assert.True(EditSelection.Of([Named(tree, "Program.cs"), Named(tree, "App")]).IsEmpty);
        Assert.True(EditSelection.Of(tree.Descendants()).IsEmpty);
        Assert.True(EditSelection.Of([tree]).IsEmpty);
        Assert.True(EditSelection.Of([]).IsEmpty);
    }

    /// <summary>
    /// Вложенный получает имя владельца — целиком или по основе, — а чужой по имени своё сохраняет.
    /// </summary>
    [Fact]
    public void Nested_files_follow_the_name_of_their_owner()
    {
        Assert.Equal("Main.axaml.cs", Renaming.Companion("MainWindow.axaml", "Main.axaml", "MainWindow.axaml.cs"));

        // Код за разметкой идёт за её полным именем, и сменённое расширение уносит с собой.
        Assert.Equal("MainWindow.xaml.cs", Renaming.Companion("MainWindow.axaml", "MainWindow.xaml", "MainWindow.axaml.cs"));
        Assert.Equal("Strings.Designer.cs", Renaming.Companion("Resources.resx", "Strings.resx", "Resources.Designer.cs"));
        Assert.Null(Renaming.Companion("App.axaml", "Shell.axaml", "Theme.cs"));
        Assert.Null(Renaming.Companion("MainWindow.axaml", "Main.axaml", "MainWindowHelper.cs"));

        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        solution.File(app, "Resources.resx", ProjectItemTypes.EmbeddedResource);
        solution.File(app, "Resources.Designer.cs", dependentUpon: "Resources.resx");
        solution.File(app, "Page.xaml", "Page");
        solution.File(app, "Page.xaml.cs", dependentUpon: "Page.xaml");
        solution.File(app, "Page.xaml.cs.map", ProjectItemTypes.None);

        var tree = solution.Tree();

        Assert.Equal(
            ["Strings.resx", "Strings.Designer.cs"],
            Renaming.Plan(Named(tree, "Resources.resx"), "Strings.resx").Select(step => step.Name));

        // Звено за звеном: каждое тянет следующее за собой.
        Assert.Equal(
            ["Screen.xaml", "Screen.xaml.cs", "Screen.xaml.cs.map"],
            Renaming.Plan(Named(tree, "Page.xaml"), "Screen.xaml").Select(step => step.Name));
    }

    /// <summary>Поле выделяет имя без последнего расширения, а у папки и у имени с точки — всё.</summary>
    [Fact]
    public void The_field_selects_the_name_without_its_extension()
    {
        Assert.Equal("MainWindow".Length, Renaming.Selected("MainWindow.axaml", folder: false));
        Assert.Equal("MainWindow.axaml".Length, Renaming.Selected("MainWindow.axaml.cs", folder: false));
        Assert.Equal(".gitignore".Length, Renaming.Selected(".gitignore", folder: false));
        Assert.Equal("Views.old".Length, Renaming.Selected("Views.old", folder: true));
    }

    /// <summary>Негодное имя названо раньше, чем уйдёт службе, — и тем, что с ним не так.</summary>
    [Fact]
    public void A_bad_name_is_named_before_it_reaches_the_service()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();
        var owner = Named(tree, "MainWindow.axaml");
        var folder = owner.Path.Directory;
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            owner.Path.Value,
            Named(tree, "MainWindow.axaml.cs").Path.Value,
            folder.Combine("About.axaml").Value,
            folder.Combine("Main.axaml.cs").Value,
        };

        NameCheck Check(string typed) => Renaming.Check(typed, owner, taken.Contains);

        Assert.Equal(NameProblem.Empty, Check("  ").Problem);
        Assert.Equal(NameProblem.Unchanged, Check("MainWindow.axaml").Problem);
        Assert.Equal(new NameCheck(NameProblem.Invalid, ":"), Check("Main:Window.axaml"));
        Assert.Equal(new NameCheck(NameProblem.Invalid, "/"), Check("Views/Main.axaml"));
        Assert.Equal(NameProblem.Trailing, Check("Main.axaml.").Problem);
        Assert.Equal(new NameCheck(NameProblem.Reserved, "con"), Check("con.axaml"));
        Assert.Equal(new NameCheck(NameProblem.Reserved, "LPT1"), Check("LPT1"));
        Assert.Equal(new NameCheck(NameProblem.Taken, "About.axaml"), Check("About.axaml"));

        // Свободно имя владельца, занято имя, которое получит вложенный.
        Assert.Equal(new NameCheck(NameProblem.Taken, "Main.axaml.cs"), Check("Main.axaml"));
        Assert.True(Check("Shell.axaml").IsFine);
    }

    /// <summary>Смена одного регистра — не занятое имя: на месте лежит сам переименовываемый.</summary>
    [Fact]
    public void A_change_of_case_is_not_a_taken_name()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();
        var owner = Named(tree, "MainWindow.axaml");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            owner.Path.Value,
            Named(tree, "MainWindow.axaml.cs").Path.Value,
        };

        Assert.True(Renaming.Check("mainwindow.axaml", owner, taken.Contains).IsFine, "смену регистра приняли за занятое имя");
    }

    private static Node Named(Node tree, string name) => tree.Descendants().Single(node => node.Name == name);
}
