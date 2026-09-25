using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правая колонка окна проекта: содержимое контейнера, путь до него и поиск по решению.
/// </summary>
/// <remarks>
/// Колонка — модель без окна: дерево на входе, предметы и сегменты пути на выходе. Проверяется то,
/// что видит человек, — что лежит в колонке, в каком порядке и куда колонка уходит, когда решение
/// перезагрузилось без того, на что она смотрела.
/// </remarks>
public class ProjectWindowBrowserTests
{
    /// <summary>Колонка открывается на корне решения.</summary>
    [Fact]
    public void The_column_opens_at_the_solution()
    {
        var browser = Shown();

        Assert.Equal("Hello", browser.Current!.Name);
        Assert.Equal(["src"], Names(browser));
        Assert.Equal(["Hello"], browser.Segments.Select(segment => segment.ToString()));
    }

    /// <summary>
    /// Контейнер показывает своих детей, а вложенный файл стоит сразу за владельцем — плоско.
    /// </summary>
    [Fact]
    public void A_container_shows_its_children_with_nested_files_right_after_their_owners()
    {
        var browser = Shown();

        Assert.True(browser.Go(Node(browser, "App")));
        Assert.Equal(
            ["Dependencies", "Assets", "Models", "Views", "App.axaml", "App.axaml.cs", "app.manifest", "Program.cs"],
            Names(browser));
        Assert.Equal(["App.axaml.cs"], browser.Items.Where(tile => tile.IsNested).Select(tile => tile.Name));
    }

    /// <summary>Путь идёт от решения до текущего контейнера.</summary>
    [Fact]
    public void The_path_runs_from_the_solution_to_the_current_container()
    {
        var browser = Shown();

        browser.Go(Node(browser, "Views"));

        Assert.Equal(["Hello", "src", "App", "Views"], browser.Segments.Select(segment => segment.Node.Name));
    }

    /// <summary>Подъём ведёт к родителю и останавливается у корня.</summary>
    [Fact]
    public void Climbing_leads_to_the_parent_and_stops_at_the_root()
    {
        var browser = Shown();

        browser.Go(Node(browser, "Views"));

        Assert.True(browser.Up());
        Assert.Equal("App", browser.Current!.Name);
        Assert.True(browser.Up());
        Assert.True(browser.Up());
        Assert.Equal("Hello", browser.Current!.Name);
        Assert.False(browser.Up(), "колонка поднялась выше корня");
    }

    /// <summary>Лист — не место: в файл колонка не уходит.</summary>
    [Fact]
    public void A_leaf_is_not_a_place_to_go()
    {
        var browser = Shown();

        browser.Go(Node(browser, "App"));

        Assert.False(browser.Go(Node(browser, "Program.cs")));
        Assert.Equal("App", browser.Current!.Name);
    }

    /// <summary>
    /// Новый снимок оставляет колонку в том же контейнере, а без него — в ближайшем уцелевшем предке.
    /// </summary>
    /// <remarks>
    /// Служба проектов перечитывает решение на каждый новый файл; колонка, прыгающая от этого в
    /// корень, выбрасывала бы человека из папки, в которой он работает.
    /// </remarks>
    [Fact]
    public void A_new_snapshot_keeps_the_container_or_falls_back_to_its_nearest_ancestor()
    {
        var browser = Shown();

        browser.Go(Node(browser, "Views"));

        var next = ProjectWindowSolution.Avalonia(extra: "Views/Settings.axaml").Tree();

        browser.Show(next);

        Assert.Same(next.Descendants().Single(node => node.Name == "Views"), browser.Current);
        Assert.Contains("Settings.axaml", Names(browser));

        var without = new ProjectWindowSolution();
        var app = without.Project("App");

        without.File(app, "Program.cs");
        without.SolutionFolder("/src/", app);
        browser.Show(without.Tree());

        Assert.Equal("App", browser.Current!.Name);
    }

    /// <summary>Плитки переживают снимок теми же объектами — и выделение вместе с ними.</summary>
    [Fact]
    public void Tiles_stay_the_same_objects_across_a_snapshot()
    {
        var browser = Shown();

        browser.Go(Node(browser, "App"));

        var before = browser.Items.ToList();

        browser.Show(ProjectWindowSolution.Avalonia(extra: "Extra.cs").Tree());

        Assert.All(before, tile => Assert.Contains(tile, browser.Items));
        Assert.Contains("Extra.cs", Names(browser));
    }

    /// <summary>
    /// Поиск ищет по всему решению и говорит, сколько нашлось; путь при этом не показан.
    /// </summary>
    [Fact]
    public void Search_looks_through_the_whole_solution()
    {
        var browser = Shown();

        browser.Search("axaml");

        Assert.True(browser.IsSearching);
        Assert.Equal(4, browser.Found);
        Assert.Equal(["MainWindow.axaml", "MainWindow.axaml.cs", "App.axaml", "App.axaml.cs"], Names(browser));
        Assert.Empty(browser.Segments);

        browser.Search(" ");

        Assert.False(browser.IsSearching);
        Assert.Equal(["src"], Names(browser));
    }

    /// <summary>
    /// Поиск показывает не больше потолка, а нашедшееся считает всё.
    /// </summary>
    /// <remarks>
    /// Плитки раскладываются без виртуализации, и тысяча совпадений на каждую букву запроса — это
    /// колонка, которая перестаёт отвечать.
    /// </remarks>
    [Fact]
    public void Search_shows_no_more_than_its_cap_and_counts_everything()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        for (var index = 0; index < Browser.Cap + 100; index++)
            solution.File(app, $"Generated/File{index:D4}.cs");

        var browser = new Browser();

        browser.Show(solution.Tree());
        browser.Search("file");

        Assert.Equal(Browser.Cap + 100, browser.Found);
        Assert.Equal(Browser.Cap, browser.Items.Count);
    }

    /// <summary>Переход в контейнер снимает поиск: колонка показывает место.</summary>
    [Fact]
    public void Going_somewhere_ends_the_search()
    {
        var browser = Shown();

        browser.Search("axaml");
        browser.Go(Node(browser, "App"));

        Assert.False(browser.IsSearching);
        Assert.Contains("Program.cs", Names(browser));
    }

    /// <summary>
    /// Что на диске — силуэт, что в модели — подложка.
    /// </summary>
    [Fact]
    public void What_lies_on_disk_is_a_silhouette_and_what_lives_in_the_model_is_a_plate()
    {
        var browser = Shown();

        browser.Go(Node(browser, "App"));

        Tile Tile(string name) => browser.Items.Single(tile => tile.Name == name);

        Assert.Equal(TileLook.Folder, Tile("Views").Look);
        Assert.Equal(TileLook.Document, Tile("Program.cs").Look);
        Assert.Equal(TileLook.Plate, Tile("Dependencies").Look);
        Assert.True(Tile("Views").IsContainer);
        Assert.False(Tile("Program.cs").IsContainer);
        Assert.Equal(Path.Combine("src", "App", "Program.cs"), Tile("Program.cs").Hint);

        browser.Go(Node(browser, "Packages"));

        Assert.All(browser.Items, tile => Assert.Equal(TileLook.Plate, tile.Look));
        Assert.Equal("Avalonia 12.1.2", browser.Items.First().Hint);
    }

    /// <summary>Закрытое решение оставляет колонку пустой и без пути.</summary>
    [Fact]
    public void A_closed_solution_leaves_the_column_empty()
    {
        var browser = Shown();

        browser.Show(null);

        Assert.Null(browser.Current);
        Assert.Empty(browser.Items);
        Assert.Empty(browser.Segments);
    }

    private static Browser Shown() => Shown(ProjectWindowSolution.Avalonia().Tree());

    private static Browser Shown(Node tree)
    {
        var browser = new Browser();

        browser.Show(tree);

        return browser;
    }

    /// <summary>Узел по имени — в дереве, которое показывает колонка.</summary>
    private static Node Node(Browser browser, string name) =>
        browser.Current!.Ancestors().Append(browser.Current).Last(node => node.Parent is null)
            .Descendants()
            .Single(node => node.Name == name);

    private static List<string> Names(Browser browser) => [.. browser.Items.Select(tile => tile.Name)];
}
