using ArxisStudio.Modules.Project.Looks;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Дерево решения окна проекта: из снимка — так, как решение показывает Rider.
/// </summary>
/// <remarks>
/// Построитель чистый — снимок, ответ диска и подписи на входе, дерево на выходе, — и проверяется
/// без окна. Снимок — вычисление MSBuild, а не содержимое папки: элементов в нём больше,
/// чем файлов, и главная работа построителя — не показать лишнего.
/// </remarks>
public class ProjectWindowTreeTests
{
    /// <summary>
    /// Дерево читается как Solution в Rider.
    /// </summary>
    /// <remarks>
    /// Корень — решение и счёт проектов, под ним папка решения и проекты. У проекта первыми
    /// «Зависимости» с платформой, группы в своём порядке, пакеты с версиями; затем папки, затем
    /// файлы; код за разметкой стоит под разметкой. Пустая объявленная папка есть и пуста.
    /// </remarks>
    [Fact]
    public void The_tree_reads_like_the_solution_view_of_rider()
    {
        var tree = ProjectWindowSolution.Avalonia().Tree();

        Assert.Equal(
            """
            Hello · 2 projects
              src
                App
                  Dependencies net10.0
                    Frameworks
                      Microsoft.NETCore.App
                    Packages
                      Avalonia 12.1.2
                      CommunityToolkit.Mvvm 8.4.0
                    Projects
                      Lib
                  Assets
                    avalonia-logo.ico
                  Models
                  Views
                    MainWindow.axaml
                      MainWindow.axaml.cs
                  App.axaml
                    App.axaml.cs
                  app.manifest
                  Program.cs
                Lib
                  Class1.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(tree));
    }

    /// <summary>Один проект — счёт своими словами, а не шаблоном с числом.</summary>
    [Fact]
    public void A_single_project_is_counted_in_its_own_words()
    {
        var solution = new ProjectWindowSolution();

        solution.Project("App");

        Assert.Equal("· 1 project", solution.Tree().Detail);
    }

    /// <summary>
    /// Папки решения вкладываются по пути, а проект вне папок стоит у корня.
    /// </summary>
    /// <remarks>
    /// Вложенность папок решения записана только путём — «/src/Libs/» лежит в «/src/», — и
    /// родитель ищется по нему, в каком бы порядке папки ни пришли.
    /// </remarks>
    [Fact]
    public void Solution_folders_nest_by_their_path()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");
        var core = solution.Project("Core", "src/Libs");

        solution.Project("Tool", "tools");
        solution.SolutionFolder("/src/Libs/", core);
        solution.SolutionFolder("/src/", app);

        Assert.Equal(
            """
            Hello · 3 projects
              src
                Libs
                  Core
                App
              Tool
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(solution.Tree()));
    }

    /// <summary>
    /// Дерево показывает только то, что лежит в папке проекта и есть на диске.
    /// </summary>
    /// <remarks>
    /// Фантомы — кандидаты вроде <c>PotentialEditorConfigFiles</c>, которых на диске нет, — не
    /// проходят. Выход сборки не проходит, даже если он есть: и <c>bin</c>, <c>obj</c>, и папка,
    /// которую проект назвал своим <c>OutputPath</c>. Папки с точкой служебные; файл с точкой —
    /// обычный файл проекта. Файл вне папки проекта без ссылки ни в одной папке проекта не лежит.
    /// Элемент с подчёркиванием в типе — внутренний элемент целей, даже если одноимённое что-то
    /// лежит на диске, а <c>Visible="false"</c> прячет элемент, как прячут его VS и Rider.
    /// </remarks>
    [Fact]
    public void Only_what_lies_in_the_project_folder_and_on_disk_is_shown()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        app.Properties["OutputPath"] = "build\\Debug\\";
        app.Properties["BaseIntermediateOutputPath"] = Path.Combine(app.ProjectFilePath.Directory.Value, "artifacts", "obj") + Path.DirectorySeparatorChar;

        solution.File(app, "Program.cs");
        solution.File(app, "Missing.cs", onDisk: false);
        solution.File(app, "artifacts/obj/App.AssemblyInfo.cs");
        solution.File(app, ".editorconfig", "PotentialEditorConfigFiles", onDisk: false);
        solution.File(app, ".globalconfig", ProjectItemTypes.None);
        solution.File(app, "bin/Debug/App.dll", ProjectItemTypes.None);
        solution.File(app, "obj/App.AssemblyInfo.cs");
        solution.File(app, "build/Debug/App.dll", ProjectItemTypes.None);
        solution.File(app, ".vs/state.json", ProjectItemTypes.None);
        solution.Folder(app, "Gone/", onDisk: false);
        solution.Linked(app, solution.Home.Combine("Shared.cs"), link: null);
        solution.File(app, "win-x64", "_KnownRuntimeIdentiferPlatforms");
        solution.File(app, "Generated.cs", visible: false);

        Assert.Equal(
            """
            Hello · 1 project
              App
                .globalconfig
                Program.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(solution.Tree()));
    }

    /// <summary>Файл, названный проектом дважды — под двумя типами, — стоит один раз.</summary>
    [Fact]
    public void A_file_listed_twice_stands_once()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        solution.File(app, "appsettings.json", ProjectItemTypes.None);
        solution.File(app, "appsettings.json", ProjectItemTypes.Content);

        var project = Assert.Single(solution.Tree().Children);

        Assert.Equal("appsettings.json", Assert.Single(project.Children).Name);
    }

    /// <summary>
    /// Файл со ссылкой стоит там, куда его поставила ссылка, а не там, где он лежит.
    /// </summary>
    [Fact]
    public void A_linked_file_stands_where_its_link_puts_it()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");
        var shared = solution.Linked(app, solution.Home.Combine("shared/Version.cs"), link: "Properties\\Version.cs");

        var project = Assert.Single(solution.Tree().Children);
        var folder = Assert.Single(project.Children);
        var file = Assert.Single(folder.Children);

        Assert.Equal("Properties", folder.Name);
        Assert.Equal("Version.cs", file.Name);
        Assert.Equal(shared, file.Path);
        Assert.Equal(Path.Combine("shared", "Version.cs"), file.Relative);
    }

    /// <summary>
    /// Зависимый файл стоит под владельцем — по метаданным, а без них по имени, и только в своей папке.
    /// </summary>
    /// <remarks>
    /// Метаданные главнее имени: <c>Strings.Designer.cs</c> принадлежит <c>Strings.resx</c>, хотя по
    /// имени владельца у него нет. Без метаданных решает имя: <c>Card.axaml.cs</c> — это
    /// <c>Card.axaml</c> с ещё одним расширением. Файл в соседней папке с похожим именем чужой. Круг
    /// из двух файлов, назначивших друг друга владельцами, обрывается, и ни один из них не теряется.
    /// </remarks>
    [Fact]
    public void A_dependent_file_stands_under_its_owner_within_its_folder()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        solution.File(app, "Strings.resx", ProjectItemTypes.EmbeddedResource);
        solution.File(app, "Strings.Designer.cs", dependentUpon: "Strings.resx");
        solution.File(app, "Main.axaml", "AvaloniaXaml");
        solution.File(app, "Views/Main.axaml.cs");
        solution.File(app, "Views/Card.axaml", "AvaloniaXaml");
        solution.File(app, "Views/Card.axaml.cs");
        solution.File(app, "Ping.cs", dependentUpon: "Ping.cs.cs");
        solution.File(app, "Ping.cs.cs");

        Assert.Equal(
            """
            Hello · 1 project
              App
                Views
                  Card.axaml
                    Card.axaml.cs
                  Main.axaml.cs
                Main.axaml
                Ping.cs.cs
                  Ping.cs
                Strings.resx
                  Strings.Designer.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(solution.Tree()));
    }

    /// <summary>
    /// Папки первыми, затем файлы, по имени без учёта регистра.
    /// </summary>
    /// <remarks>
    /// При равных без регистра именах порядок побайтный — чтобы он не зависел от того, в каком
    /// порядке элементы пришли из MSBuild.
    /// </remarks>
    [Fact]
    public void Folders_come_first_then_files_by_name_regardless_of_case()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        solution.File(app, "b.cs");
        solution.File(app, "zeta/Z.cs");
        solution.File(app, "A.cs");
        solution.File(app, "Alpha/a.cs");
        solution.File(app, "readme.md", ProjectItemTypes.None);
        solution.File(app, "README.txt", ProjectItemTypes.None);

        var project = Assert.Single(solution.Tree().Children);

        Assert.Equal(["Alpha", "zeta", "A.cs", "b.cs", "readme.md", "README.txt"], project.Children.Select(node => node.Name));
    }

    /// <summary>
    /// Проект, который не загрузился, говорит почему и ничего под собой не показывает.
    /// </summary>
    /// <remarks>
    /// Проект, загрузившийся с ошибками, — другое дело: элементы у него есть, и они показаны.
    /// </remarks>
    [Fact]
    public void A_project_that_did_not_load_says_why_and_shows_nothing_under_it()
    {
        var solution = new ProjectWindowSolution();
        var broken = solution.Project("Broken");
        var partial = solution.Project("Partial");

        broken.Diagnostics.Add(ProjectDiagnostic.ForProject(
            "MSB4025", "The project file could not be loaded.", ProjectDiagnosticSeverity.Error, broken.Identity));
        partial.Diagnostics.Add(ProjectDiagnostic.ForProject(
            "NU1101", "Unable to find package Nope.", ProjectDiagnosticSeverity.Error, partial.Identity));
        solution.File(partial, "Program.cs");

        var tree = solution.Tree();
        var failed = tree.Children.Single(node => node.Name == "Broken");
        var loaded = tree.Children.Single(node => node.Name == "Partial");

        Assert.True(failed.IsBroken, "проект без единого элемента и с ошибкой не назван упавшим");
        Assert.Equal("not loaded", failed.Detail);
        Assert.Equal("The project file could not be loaded.", failed.Problem);
        Assert.Empty(failed.Children);

        Assert.False(loaded.IsBroken, "проект, загрузившийся с ошибкой, назван незагруженным");
        Assert.Null(loaded.Problem);
        Assert.Contains(loaded.Children, node => node.Name == "Program.cs");
    }

    /// <summary>
    /// Ключи узлов — от путей: новый сеанс того же решения даёт те же ключи.
    /// </summary>
    /// <remarks>
    /// Сеанс перевыдаёт идентичности проектов, а раскрытое и выделенное обязаны пережить
    /// перезагрузку. И наоборот: одна и та же папка в двух проектах — два разных узла.
    /// </remarks>
    [Fact]
    public void Keys_come_from_paths_and_survive_a_new_session()
    {
        var first = ProjectWindowSolution.Avalonia().Tree();
        var second = ProjectWindowSolution.Avalonia().Tree();

        Assert.Equal(first.Descendants().Select(node => node.Key), second.Descendants().Select(node => node.Key));

        var keys = first.Descendants().Select(node => node.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");
        var lib = solution.Project("Lib");

        solution.File(app, "Views/A.cs");
        solution.File(lib, "Views/B.cs");

        var views = solution.Tree().Descendants().Where(node => node.Name == "Views").ToList();

        Assert.Equal(2, views.Count);
        Assert.NotEqual(views[0].Key, views[1].Key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Узел знает свой проект, путь на диске и путь от решения — для меню и подсказки.
    /// </summary>
    [Fact]
    public void A_node_knows_its_project_its_path_and_its_place_in_the_solution()
    {
        var solution = ProjectWindowSolution.Avalonia();
        var tree = solution.Tree();
        var view = tree.Descendants().Single(node => node.Name == "MainWindow.axaml.cs");
        var folder = tree.Descendants().Single(node => node.Name == "Views");
        var app = tree.Descendants().Single(node => node.Name == "App" && node.Kind == NodeKind.Project);
        var package = tree.Descendants().Single(node => node.Name == "Avalonia");

        Assert.Equal(solution.Home.Combine("src/App/Views/MainWindow.axaml.cs"), view.Path);
        Assert.Equal(Path.Combine("src", "App", "Views", "MainWindow.axaml.cs"), view.Relative);
        Assert.Equal(app.Path, view.Project);
        Assert.Equal(FileKind.CSharp, view.FileKind);

        Assert.Equal(solution.Home.Combine("src/App/Views"), folder.Path);
        Assert.Equal(NodeKind.Folder, folder.Kind);

        Assert.Equal(NodeKind.Dependency, package.Kind);
        Assert.Equal(DependencyKind.Packages, package.Dependency);
        Assert.True(package.Path.IsEmpty, "у пакета появился путь на диске");
        Assert.False(package.IsContainer, "зависимость назвалась контейнером");
        Assert.True(folder.IsContainer, "папка не назвалась контейнером");
    }

    /// <summary>
    /// Цвет значка — по виду файла, как велит дизайн-система.
    /// </summary>
    /// <remarks>
    /// Код зелёный, разметка синяя, XML и JSON оранжевые, картинки фиолетовые, прочее — цветом
    /// подписи. Проект зелёный, как код, на котором написан; папки и зависимости своего цвета не
    /// имеют.
    /// </remarks>
    [Fact]
    public void Icons_are_tinted_by_the_kind_of_file()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        foreach (var name in new[] { "A.cs", "B.axaml", "C.xaml", "D.csproj", "E.json", "F.png", "G.md", "H.bin", "Folder/I.cs" })
            solution.File(app, name, ProjectItemTypes.None);

        var tree = solution.Tree();

        string? Tint(string name) => Glyphs.TintOf(tree.Descendants().Single(node => node.Name == name));

        Assert.Equal("AxTintGreenBrush", Tint("A.cs"));
        Assert.Equal("AxTintBlueBrush", Tint("B.axaml"));
        Assert.Equal("AxTintBlueBrush", Tint("C.xaml"));
        Assert.Equal("AxTintOrangeBrush", Tint("D.csproj"));
        Assert.Equal("AxTintOrangeBrush", Tint("E.json"));
        Assert.Equal("AxTintPurpleBrush", Tint("F.png"));
        Assert.Null(Tint("G.md"));
        Assert.Null(Tint("H.bin"));
        Assert.Equal("AxTintGreenBrush", Tint("App"));
        Assert.Null(Tint("Folder"));
    }

    /// <summary>
    /// Проверка диска спрашивает ровно о том, что дерево показало бы.
    /// </summary>
    /// <remarks>
    /// Файл выхода сборки лежит на диске, но спрашивать о нём незачем — дерево его всё равно не
    /// покажет, и в ответе его нет. Явная папка проверяется как папка, файл — как файл.
    /// </remarks>
    [Fact]
    public void The_disk_probe_answers_only_about_what_the_tree_would_show()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-disk-{Guid.NewGuid():N}");

        try
        {
            var solution = new ProjectWindowSolution();
            var project = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            var app = new ProjectSnapshotBuilder
            {
                Identity = ProjectIdentity.Create(solution.Request.Workspace, CanonicalPath.Create(Path.Combine(project, "App.csproj"))),
                ProjectFilePath = CanonicalPath.Create(Path.Combine(project, "App.csproj")),
                Name = "App",
            };

            Directory.CreateDirectory(Path.Combine(project, "Models"));
            Directory.CreateDirectory(Path.Combine(project, "bin"));
            File.WriteAllText(Path.Combine(project, "Program.cs"), "// код");
            File.WriteAllText(Path.Combine(project, "Models.cs"), "// файл, а не папка");
            File.WriteAllText(Path.Combine(project, "bin", "App.dll"), "выход");

            string[] names = ["Program.cs", "Missing.cs", "bin/App.dll"];

            foreach (var name in names)
                app.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Compile, Include = name, FullPath = app.ProjectFilePath.Directory.Combine(name) });

            app.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Folder, Include = "Models/", FullPath = app.ProjectFilePath.Directory.Combine("Models") });
            app.Items.Add(new ProjectItem { ItemType = ProjectItemTypes.Folder, Include = "Models.cs/", FullPath = app.ProjectFilePath.Directory.Combine("Models.cs") });

            var snapshot = new SolutionSnapshotBuilder
            {
                Workspace = solution.Request.Workspace,
                Solution = SolutionIdentity.Create(solution.Request.Workspace, solution.Entry),
                Name = "Disk",
                Request = solution.Request,
            };

            snapshot.Projects.Add(app.ToSnapshot());

            var present = DiskProbe.Probe(snapshot.ToSnapshot(), TestContext.Current.CancellationToken).Present;

            Assert.Equal(
                new[] { "Models", "Program.cs" },
                present.Select(path => path.FileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Отменённая проверка диска останавливается, а не досчитывает ненужный ответ.</summary>
    [Fact]
    public void A_cancelled_disk_probe_stops()
    {
        var snapshot = ProjectWindowSolution.Avalonia().ToSnapshot();

        Assert.Throws<OperationCanceledException>(() => DiskProbe.Probe(snapshot, new CancellationToken(canceled: true)));
    }

    /// <summary>
    /// Папка, опустевшая после удаления последнего файла, стоит в дереве листом — как у Rider.
    /// </summary>
    /// <remarks>
    /// Так нашёл человек: удалив <c>avalonia-logo.ico</c>, он терял и <c>Assets</c> — дерево брало
    /// папки только из элементов проекта, а у опустевшей их нет. Папка стоит среди папок, по имени, и
    /// путь у неё настоящий: её можно удалить, переименовать и вставить в неё.
    /// </remarks>
    [Fact]
    public void A_folder_emptied_of_its_last_file_stays_as_a_leaf()
    {
        var solution = ProjectWindowSolution.Avalonia(without: ["Assets/avalonia-logo.ico"]);
        var assets = solution.Home.Combine(Path.Combine("src", "App", "Assets"));

        solution.Empty.Add(assets);

        var tree = solution.Tree();

        Assert.Equal(
            """
            Hello · 2 projects
              src
                App
                  Dependencies net10.0
                  Assets
                  Models
                  Views
                    MainWindow.axaml
                      MainWindow.axaml.cs
                  App.axaml
                    App.axaml.cs
                  app.manifest
                  Program.cs
                Lib
                  Class1.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(tree, dependencies: false));

        var folder = tree.Descendants().Single(node => node.Name == "Assets");

        Assert.Equal(NodeKind.Folder, folder.Kind);
        Assert.Equal(assets, folder.Path);
        Assert.Empty(folder.Children);
    }

    /// <summary>
    /// Пустая папка встаёт только под тем, что дерево показывает, — служебная и выход сборки не
    /// встают и пустыми.
    /// </summary>
    /// <remarks>
    /// Пустая в показанной папке встаёт в неё, пустая в пустой — цепочкой. Пустая внутри папки,
    /// которую проект прячет, не встаёт: иначе она вытащила бы спрятанную на свет.
    /// </remarks>
    [Fact]
    public void An_empty_folder_stands_only_under_what_the_tree_shows()
    {
        var solution = new ProjectWindowSolution();
        var app = solution.Project("App");

        app.Properties["OutputPath"] = "build\\Debug\\";

        solution.File(app, "Program.cs");
        solution.File(app, "Views/Main.axaml", "AvaloniaXaml");

        // Вложенная идёт раньше родителя: в каком порядке ответил диск, дереву всё равно.
        foreach (var folder in (string[])["Fonts/Mono", "Fonts", "Views/Empty", "Hidden/Empty", ".idea", "Views/.cache", "bin", "build"])
            solution.Bare(app, folder);

        Assert.Equal(
            """
            Hello · 1 project
              App
                Fonts
                  Mono
                Views
                  Empty
                  Main.axaml
                Program.cs
            """.ReplaceLineEndings("\n"),
            ProjectWindowSolution.Shape(solution.Tree()));
    }

    /// <summary>
    /// Проверка диска находит пустые папки там, где дерево их показало бы, — и только пустые.
    /// </summary>
    /// <remarks>
    /// Пустая — без единого файла ни в ней, ни глубже: папка с одними папками пуста, и её папки тоже,
    /// кроме служебных. Папка с файлом, которого проект не называет, не пуста — проект его исключил, и
    /// она спрятана со всем, что в ней. Служебная папка и выход сборки не ищутся вовсе.
    /// </remarks>
    [Fact]
    public void The_disk_probe_finds_empty_folders_where_the_tree_would_show_them()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-empty-{Guid.NewGuid():N}");

        try
        {
            var solution = new ProjectWindowSolution(root: root);
            var app = solution.Project("App");

            app.Properties["OutputPath"] = "build\\Debug\\";

            solution.File(app, "Program.cs");
            solution.File(app, "Views/Main.axaml", "AvaloniaXaml");
            solution.File(app, "Pages/Admin/Panel.axaml", "AvaloniaXaml");
            solution.OnDisk();

            var home = app.ProjectFilePath.Directory;

            foreach (var folder in (string[])
                     [
                         "Assets/Fonts", "Views/Empty", "Pages/Empty", "Hollow/Deep/.cache", "Secret/Empty", "Stuffed/Inner",
                         ".idea", "bin", "obj", "build",
                     ])
            {
                Directory.CreateDirectory(home.Combine(folder).Value);
            }

            File.WriteAllText(home.Combine("Secret/key.txt").Value, "проект его исключил");
            File.WriteAllText(home.Combine("Stuffed/Inner/note.txt").Value, "глубже, но файл");

            var empty = DiskProbe.Probe(solution.ToSnapshot(), TestContext.Current.CancellationToken).Empty;

            // Pages показана одной вложенной папкой, а её пустая соседка всё равно находится.
            Assert.Equal(
                ["Assets", "Assets/Fonts", "Hollow", "Hollow/Deep", "Pages/Empty", "Views/Empty"],
                empty.Select(path => SolutionTree.RelativeTo(home, path).Replace('\\', '/')).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Пустая папка вложенного проекта стоит в нём, а не во внешнем: папку вложенного внешний не
    /// показывает.
    /// </summary>
    /// <remarks>
    /// Так лежат тесты внутри папки приложения. Их файлы приложение не называет, и папка тестов в нём
    /// спрятана — пустая папка в ней не вытаскивает её на свет, а стоит там, где её проект.
    /// </remarks>
    [Fact]
    public void An_empty_folder_of_a_nested_project_stands_in_that_project()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-nested-{Guid.NewGuid():N}");

        try
        {
            var solution = new ProjectWindowSolution(root: root);
            var app = solution.Project("App");
            var tests = solution.Project("Tests", "src/App");

            solution.File(app, "Program.cs");
            solution.File(tests, "Checks.cs");
            solution.OnDisk();

            File.WriteAllText(tests.ProjectFilePath.Value, "<Project />");
            Directory.CreateDirectory(tests.ProjectFilePath.Directory.Combine("Empty").Value);

            var snapshot = solution.ToSnapshot();
            var tree = SolutionTree.Build(snapshot, DiskProbe.Probe(snapshot, TestContext.Current.CancellationToken), Words.English);

            Assert.Equal(
                """
                Hello · 2 projects
                  App
                    Program.cs
                  Tests
                    Empty
                    Checks.cs
                """.ReplaceLineEndings("\n"),
                ProjectWindowSolution.Shape(tree));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
