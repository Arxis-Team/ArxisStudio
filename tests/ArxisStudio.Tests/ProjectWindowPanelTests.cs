using ArxisStudio.Modules.Project;
using ArxisStudio.Modules.Project.Panels;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно проекта на живом дереве контролов.
/// </summary>
/// <remarks>
/// Служба проектов подделана — ей тест велит, что открыто, — а всё остальное настоящее: модуль
/// поднимает хост студии, словари — модуля, мышь и клавиши идут дорогой ввода, а файлы решения
/// лежат на диске, который окно и спрашивает. Дерево строится вне потока интерфейса, и тест ждёт
/// постройку, а не время.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowPanelTests
{
    /// <summary>Без службы проектов окно так и говорит, а не показывает пустоту.</summary>
    [AvaloniaFact]
    public void Without_the_projects_service_the_window_says_so()
    {
        using var studio = new ProjectWindowStudio(service: false);

        Assert.Equal(ProjectState.NoService, studio.Model.State);
        Assert.True(studio.Shown(studio.Strings["project.state.noService"]), "о том, что службы нет, окно промолчало");
        Assert.False(studio.View.Tree.IsEffectivelyVisible, "службы нет, а дерево показано");
    }

    /// <summary>
    /// Решение не открыто — окно предлагает открыть и отдаёт выбранное службе проектов.
    /// </summary>
    [AvaloniaFact]
    public void A_closed_window_offers_to_open_a_solution()
    {
        using var studio = new ProjectWindowStudio();

        var chosen = studio.Avalonia().Entry;

        Assert.Equal(ProjectState.Closed, studio.Model.State);
        Assert.True(studio.Shown(studio.Strings["project.state.closed"]), "окно не сказало, что решение не открыто");

        SolutionPicker.Override = () => Task.FromResult<string?>(null);
        studio.Click(studio.View.OpenSolution);

        Assert.Empty(studio.Projects.Opened);

        SolutionPicker.Override = () => Task.FromResult<string?>(chosen.Value);
        studio.Click(studio.View.OpenSolution);

        Assert.Equal([chosen], studio.Projects.Opened);
    }

    /// <summary>
    /// Окно выбора файлов, которое не открылось, окна проекта не роняет — о нём сказано в журнале.
    /// </summary>
    [AvaloniaFact]
    public void A_picker_that_fails_is_told_to_the_journal()
    {
        using var studio = new ProjectWindowStudio();

        SolutionPicker.Override = () => throw new InvalidOperationException("окна выбора файлов нет");
        studio.Click(studio.View.OpenSolution);

        Assert.Contains(
            studio.Log.Records,
            record => record.Source == ProjectModule.LogSource && record.Message.Contains("окна выбора файлов нет", StringComparison.Ordinal));
        Assert.Empty(studio.Projects.Opened);
        Assert.Equal(ProjectState.Closed, studio.Model.State);
    }

    /// <summary>Решение открывается — окно называет его, пока оно открывается.</summary>
    [AvaloniaFact]
    public void An_opening_solution_is_named_while_it_opens()
    {
        using var studio = new ProjectWindowStudio();

        studio.Projects.Publish(new ProjectsStatus
        {
            Sequence = 1,
            Session = 1,
            State = ProjectsState.Opening,
            EntryPoint = studio.Avalonia().Entry,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ProjectState.Opening, studio.Model.State);
        Assert.True(
            studio.Shown(string.Format(studio.Strings["project.state.opening"], "Hello.slnx")),
            "окно не назвало то, что открывается");
    }

    /// <summary>
    /// Решение не открылось — окно говорит почему и повторяет попытку по кнопке.
    /// </summary>
    [AvaloniaFact]
    public void A_solution_that_did_not_open_says_why_and_tries_again()
    {
        using var studio = new ProjectWindowStudio();

        var entry = studio.Avalonia().Entry;
        var failure = WorkspaceLoadResult.Failure(ProjectDiagnostic.ForFile(
            "MSB1009", "Project file does not exist.", ProjectDiagnosticSeverity.Error, entry));

        studio.Projects.Publish(new ProjectsStatus
        {
            Sequence = 1,
            Session = 1,
            State = ProjectsState.Failed,
            EntryPoint = entry,
            LastLoad = new ProjectsLoad { Reason = ProjectsLoadReason.Open, Result = failure },
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ProjectState.Failed, studio.Model.State);
        Assert.True(studio.Shown("Project file does not exist."), "окно не сказало, почему решение не открылось");

        studio.Click(studio.View.Retry);

        Assert.Equal(1, studio.Projects.Reloads);
    }

    /// <summary>
    /// Открытое решение — дерево, и строки читаются диктором по имени, а путь — подсказкой.
    /// </summary>
    [AvaloniaFact]
    public async Task An_open_solution_is_shown_as_its_tree()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        Assert.True(studio.View.Tree.IsEffectivelyVisible, "решение открыто, а дерева не видно");
        Assert.Equal("Hello", studio.Rows[0].Name);
        Assert.True(studio.Shown(string.Format(studio.Strings["project.count"], 2)), "у корня нет счёта проектов");

        var row = studio.Row("Program.cs");
        var item = studio.Item(row);

        Assert.Equal("Program.cs", AutomationProperties.GetName(item));
        Assert.Equal(row.Hint, AutomationProperties.GetHelpText(item));
    }

    /// <summary>
    /// Значок без своего цвета идёт вторичным цветом подписи, как в дереве темы, а код — зелёным.
    /// </summary>
    [AvaloniaFact]
    public async Task Icons_take_the_colors_the_theme_tree_gives_them()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        Assert.Same(studio.Resource("AxTextSecondaryBrush"), studio.Glyph("Views").Foreground);
        Assert.Same(studio.Resource("AxTextSecondaryBrush"), studio.Glyph("Hello").Foreground);
        Assert.Same(studio.Resource("AxTintGreenBrush"), studio.Glyph("Program.cs").Foreground);
        Assert.Same(studio.Resource("AxTintBlueBrush"), studio.Glyph("App.axaml").Foreground);
    }

    /// <summary>
    /// Длинное имя в узком окне сжимается с многоточием, а вторая подпись остаётся целой.
    /// </summary>
    [AvaloniaFact]
    public async Task A_long_name_gives_way_to_its_detail_in_a_narrow_window()
    {
        const string name = "SolutionWithANameFarTooLongForANarrowWindow";

        using var studio = new ProjectWindowStudio(width: 260);

        await studio.Open(studio.Solution(name));

        var root = studio.Rows[0];
        var item = studio.Item(root);
        var texts = item.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
        var label = texts.Single(text => text.Text == name);
        var detail = texts.Single(text => text.Text == root.Detail);

        Assert.True(label.TextLayout.TextLines.Any(line => line.HasCollapsed), "длинное имя не сжалось с многоточием");
        Assert.False(detail.TextLayout.TextLines.Any(line => line.HasCollapsed), "вторая подпись сжалась вместо имени");
        Assert.True(
            detail.TranslatePoint(new Point(detail.Bounds.Width, 0), item)!.Value.X <= item.Bounds.Width,
            "вторая подпись ушла за край строки");
    }

    /// <summary>Решение, открытое раньше окна, окно застаёт.</summary>
    [AvaloniaFact]
    public async Task The_window_finds_a_solution_opened_before_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-project-window-{Guid.NewGuid():N}");
        var projects = new ProjectsProbe();

        try
        {
            projects.Publish(ProjectWindowStudio.Ready(1, ProjectWindowSolution.Avalonia(root: root).OnDisk().ToSnapshot()));

            using var studio = new ProjectWindowStudio(projects: projects);

            await studio.Built();

            Assert.Equal(ProjectState.Ready, studio.Model.State);
            Assert.Contains(studio.Rows, row => row.Name == "Program.cs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Событие не новее прочитанного отбрасывается: оно о том, что окно уже учло.
    /// </summary>
    /// <remarks>
    /// Окно подписывается и читает состояние; событие, стоявшее в очереди до чтения, приходит
    /// после — и откатило бы дерево к прежнему снимку.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_event_no_newer_than_what_was_read_is_dropped()
    {
        using var studio = new ProjectWindowStudio();

        studio.Projects.Publish(ProjectWindowStudio.Ready(5, studio.Solution()));
        await studio.Built();

        studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution(extra: "Stale.cs")));
        await studio.Built();

        Assert.DoesNotContain(studio.Rows, row => row.Name == "Stale.cs");
    }

    /// <summary>
    /// Перезагрузка того же решения оставляет раскрытое раскрытым и выделенное выделенным.
    /// </summary>
    /// <remarks>
    /// Служба проектов перечитывает решение на каждый новый файл; окно, сворачивающееся от этого,
    /// было бы непригодно ровно тогда, когда в нём работают.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_reload_keeps_what_was_expanded_and_selected()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Select("Views");
        studio.Press(studio.View.Tree, Key.Right);

        var selected = studio.Select("MainWindow.axaml");

        studio.Projects.Publish(ProjectWindowStudio.Ready(2, studio.Solution(extra: "Views/Settings.axaml")));
        await studio.Built();

        Assert.Contains(studio.Rows, row => row.Name == "Settings.axaml");
        Assert.True(studio.Row("Views").IsExpanded, "перезагрузка свернула раскрытую папку");
        Assert.Same(selected, studio.View.Tree.SelectedItem);
    }

    /// <summary>
    /// Другое решение открывается как впервые, а то же, открытое заново, — каким его оставили.
    /// </summary>
    [AvaloniaFact]
    public async Task Another_solution_opens_fresh_and_the_same_one_opens_as_it_was_left()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Select("Views");
        studio.Press(studio.View.Tree, Key.Right);

        studio.Projects.Publish(ProjectsStatus.Closed with { Sequence = 2, Session = 1 });
        studio.Projects.Publish(ProjectWindowStudio.Ready(3, studio.Solution(), session: 2));
        await studio.Built();

        Assert.True(studio.Row("Views").IsExpanded, "то же решение, открытое заново, забыло раскрытое");

        studio.Projects.Publish(ProjectWindowStudio.Ready(4, studio.Solution("Other"), session: 3));
        await studio.Built();

        Assert.Equal("Other", studio.Rows[0].Name);
        Assert.True(studio.Row("App").IsExpanded, "другое решение открылось не раскрытым до проектов");
        Assert.False(studio.Row("Views").IsExpanded, "другое решение унаследовало раскрытое прежнего");
    }

    /// <summary>
    /// Щелчок по шеврону раскрывает узел, но выделения не двигает — как в Rider.
    /// </summary>
    [AvaloniaFact]
    public async Task A_click_on_the_chevron_toggles_without_moving_the_selection()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        var program = studio.Select("Program.cs");
        var views = studio.Row("Views");

        studio.Press(studio.Chevron(views));

        Assert.True(views.IsExpanded, "щелчок по шеврону не раскрыл папку");
        Assert.Same(program, studio.View.Tree.SelectedItem);

        studio.Press(studio.Chevron(views));

        Assert.False(views.IsExpanded, "второй щелчок по шеврону не свернул папку");
    }

    /// <summary>Двойной щелчок открывает файл, а папку раскрывает.</summary>
    [AvaloniaFact]
    public async Task A_double_click_opens_a_file_and_opens_up_a_folder()
    {
        using var studio = new ProjectWindowStudio();

        var solution = studio.Avalonia();

        await studio.Open(solution.ToSnapshot());

        studio.DoubleClick(studio.Label("Program.cs"));

        Assert.Equal([solution.Home.Combine("src/App/Program.cs").Value], studio.Documents.Opened);

        // Щелчок в стороне: время в безголовом прогоне стоит, и следующий щелчок иначе сложился бы
        // с прежними.
        studio.Press(studio.View.Query);
        studio.DoubleClick(studio.Label("Views"));

        Assert.True(studio.Row("Views").IsExpanded, "двойной щелчок не раскрыл папку");
        Assert.Single(studio.Documents.Opened);
    }

    /// <summary>
    /// Клавиши ходят по дереву, как в Rider.
    /// </summary>
    /// <remarks>
    /// Right раскрывает, а на раскрытом шагает к первому ребёнку; Left сворачивает, а на свёрнутом
    /// шагает к родителю; «*» раскрывает ветку целиком, «−» и «+» сворачивают и раскрывают; Enter
    /// открывает файл.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_keys_walk_the_tree_like_rider()
    {
        using var studio = new ProjectWindowStudio();

        var tree = studio.View.Tree;

        await studio.Open();

        var views = studio.Select("Views");

        studio.Press(tree, Key.Right);
        Assert.True(views.IsExpanded, "Right не раскрыл папку");

        studio.Press(tree, Key.Right);
        Assert.Equal("MainWindow.axaml", studio.Selected.Name);

        studio.Press(tree, Key.Left);
        Assert.Same(views, studio.Selected);

        studio.Press(tree, Key.Left);
        Assert.False(views.IsExpanded, "Left не свернул папку");

        studio.Press(tree, Key.Multiply);
        Assert.True(studio.Row("MainWindow.axaml").IsExpanded, "«*» не раскрыл ветку целиком");

        studio.Press(tree, Key.Subtract);
        Assert.False(views.IsExpanded, "«−» не свернул папку");

        studio.Press(tree, Key.Add);
        Assert.True(views.IsExpanded, "«+» не раскрыл папку");

        studio.Select("Program.cs");
        studio.Press(tree, Key.Enter);

        Assert.Equal("Program.cs", Path.GetFileName(Assert.Single(studio.Documents.Opened)));
    }

    /// <summary>
    /// Свёртка, унёсшая выделенную строку, переносит выделение на свёрнутый узел.
    /// </summary>
    [AvaloniaFact]
    public async Task Collapsing_over_the_selection_moves_it_to_the_collapsed_node()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Select("Program.cs");

        var app = studio.Row("App");

        studio.Press(studio.Chevron(app));

        Assert.Same(app, studio.View.Tree.SelectedItem);
    }

    /// <summary>
    /// Ctrl+F ставит каретку в поиск, запрос сужает дерево, стрелка вниз возвращает в дерево, Esc
    /// снимает поиск.
    /// </summary>
    [AvaloniaFact]
    public async Task Search_narrows_the_tree_and_lets_go_of_it()
    {
        using var studio = new ProjectWindowStudio();

        var view = studio.View;

        await studio.Open();

        var everything = studio.Rows.Count;

        studio.Press(view.Tree, Key.F, KeyModifiers.Control);
        Assert.True(view.Query.IsKeyboardFocusWithin, "Ctrl+F не поставил каретку в поиск");

        view.Query.Text = "main";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Hello", "src", "App", "Views", "MainWindow.axaml", "MainWindow.axaml.cs"], studio.Rows.Select(row => row.Name));

        studio.Press(view.Query, Key.Down);
        Assert.True(view.Tree.IsKeyboardFocusWithin, "стрелка вниз не вернула в дерево");

        view.Query.Text = "nothing like this";
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.Model.NothingFound);
        Assert.True(studio.Shown(studio.Strings["project.search.none"]), "окно не сказало, что ничего не нашлось");

        view.Query.Focus();
        studio.Press(view.Query, Key.Escape);
        Dispatcher.UIThread.RunJobs();

        Assert.True(string.IsNullOrEmpty(view.Query.Text), "Esc не снял поиск");
        Assert.Equal(everything, studio.Rows.Count);
    }

    /// <summary>«Свернуть всё» оставляет решение и то, что лежит прямо в нём.</summary>
    [AvaloniaFact]
    public async Task Collapse_all_leaves_the_solution_and_what_lies_right_in_it()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        studio.Click(studio.View.CollapseAll);

        Assert.Equal(["Hello", "src"], studio.Rows.Select(row => row.Name));
    }

    /// <summary>
    /// Меню строки предлагает то, что подходит узлу, и делает то, что предложило.
    /// </summary>
    /// <remarks>
    /// Файл открывают и показывают в проводнике; папку показывают и раскрывают веткой; у пакета
    /// копируют имя, а у решения нет «пути от решения». Проводник подменён записью — окно не вправе
    /// открывать его в прогоне тестов.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_row_menu_offers_what_fits_the_node()
    {
        using var studio = new ProjectWindowStudio();

        var solution = studio.Avalonia();
        var revealed = new List<string>();
        var words = studio.Strings;

        Reveal.Override = revealed.Add;
        await studio.Open(solution.ToSnapshot());

        var menu = studio.Panel.Menu!;
        var program = menu.Items(studio.Row("Program.cs"));

        Assert.Equal(
            [words["project.menu.open"], words[Reveal.Words], words["project.menu.copyPath"], words["project.menu.copyRelative"]],
            program.Select(item => item.Header));

        studio.Click(program, words["project.menu.open"]);
        studio.Click(program, words[Reveal.Words]);

        var path = solution.Home.Combine("src/App/Program.cs").Value;

        Assert.Equal([path], studio.Documents.Opened);
        Assert.Equal([path], revealed);

        var views = menu.Items(studio.Row("Views"));

        Assert.DoesNotContain(views, item => Equals(item.Header, words["project.menu.open"]));

        studio.Click(views, words["project.menu.expand"]);

        Assert.True(studio.Row("MainWindow.axaml").IsExpanded, "«Раскрыть ветку» не раскрыла ветку");

        studio.Select("Dependencies");
        studio.Press(studio.View.Tree, Key.Multiply);

        Assert.Equal([words["project.menu.copyName"]], menu.Items(studio.Row("Avalonia")).Select(item => item.Header));
        Assert.DoesNotContain(menu.Items(studio.Rows[0]), item => Equals(item.Header, words["project.menu.copyRelative"]));
    }

    /// <summary>
    /// Предупреждение о неудачной перезагрузке закрывается, а следующая неудача говорит о себе снова.
    /// </summary>
    /// <remarks>
    /// Дерево при неудачной перезагрузке показывает последнее загруженное — и говорит об этом над
    /// собой. Закрытое предупреждение не возвращается от всякого события, пока речь о той же
    /// неудаче, но новая неудача — новое сообщение.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_failed_reload_warns_once_per_failure()
    {
        using var studio = new ProjectWindowStudio();

        var snapshot = studio.Solution();
        var stale = studio.View.Stale;

        await studio.Open(snapshot);

        Assert.False(stale.IsEffectivelyVisible);

        studio.Projects.Publish(Failed(2, snapshot));
        Dispatcher.UIThread.RunJobs();

        Assert.True(stale.IsEffectivelyVisible, "неудачная перезагрузка прошла молча");
        Assert.Contains(studio.Rows, row => row.Name == "Program.cs");

        studio.Click(stale.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Close"));

        Assert.False(stale.IsEffectivelyVisible, "крестик не закрыл предупреждение");

        studio.Projects.Publish(studio.Projects.Status with { Sequence = 3, IsLoading = true });
        Dispatcher.UIThread.RunJobs();

        Assert.False(stale.IsEffectivelyVisible, "закрытое предупреждение вернулось без новой неудачи");
        Assert.True(studio.View.Loading.IsEffectivelyVisible, "перезагрузка идёт, а спиннера нет");

        studio.Projects.Publish(Failed(4, snapshot));
        Dispatcher.UIThread.RunJobs();

        Assert.True(stale.IsEffectivelyVisible, "новая неудача не сказала о себе");
    }

    /// <summary>
    /// Подписи дерева идут за языком студии.
    /// </summary>
    /// <remarks>
    /// «Зависимости» и счёт проектов строятся вместе с деревом, и смена языка без перестройки
    /// оставила бы их прежними.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_tree_speaks_the_language_of_the_studio()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        try
        {
            using var studio = new ProjectWindowStudio();

            await studio.Open();

            Assert.Contains(studio.Rows, row => row.Name == "Dependencies");

            Localizer.Instance.SetLanguage("ru");
            await studio.Built();

            Assert.Contains(studio.Rows, row => row.Name == "Зависимости");
            Assert.Equal("· проектов: 2", studio.Rows[0].Detail);
        }
        finally
        {
            Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        }
    }

    /// <summary>Прощаясь, окно отпускает службу проектов — и прощается сколько угодно раз.</summary>
    [AvaloniaFact]
    public async Task A_released_window_lets_go_of_the_projects_service()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        Assert.Equal(1, studio.Projects.Listeners);

        studio.Panel.Release();
        studio.Panel.Release();

        Assert.Equal(0, studio.Projects.Listeners);
    }

    /// <summary>
    /// Экранный диктор слышит окно деревом: дерево, узлы с именами, «свёрнуто», «развёрнуто» и лист.
    /// </summary>
    /// <remarks>
    /// Раскладке и клавиатуре дерево — список, и прежде диктор так его и читал: «элемент списка», без
    /// раскрытия. Роль и раскрытие спрашиваются у пиров — у тех, кого спрашивает мост платформы.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_screen_reader_hears_a_tree_of_nodes_that_open()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        var tree = ControlAutomationPeer.CreatePeerForElement(studio.View.Tree);
        var views = Peer(studio, "Views");
        var program = Peer(studio, "Program.cs");

        Assert.Equal(AutomationControlType.Tree, tree.GetAutomationControlType());
        Assert.Equal(AutomationControlType.TreeItem, views.GetAutomationControlType());
        Assert.Equal("Views", views.GetName());
        Assert.Equal(ExpandCollapseState.Collapsed, Expander(views).ExpandCollapseState);
        Assert.Equal(ExpandCollapseState.Expanded, Expander(Peer(studio, "App")).ExpandCollapseState);
        Assert.Equal(ExpandCollapseState.LeafNode, Expander(program).ExpandCollapseState);
        Assert.IsAssignableFrom<ISelectionItemProvider>(program.GetProvider<ISelectionItemProvider>());
    }

    /// <summary>
    /// Диктор раскрывает и сворачивает узел той же дорогой, что стрелки, и слышит, что вышло.
    /// </summary>
    /// <remarks>
    /// Дорога одна — и выделение с кареткой, унесённые свёрткой, так же переходят к свёрнутому узлу.
    /// Выделение подхватил бы и запасной путь перестройки, а каретку — нет: она ушла бы вместе со
    /// строкой, и диктор потерял бы место. Смену раскрытия пир объявляет сам: без этого диктор узнал
    /// бы о ней, только вернувшись к строке.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_screen_reader_opens_and_closes_a_node_the_way_the_arrows_do()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        var views = studio.Row("Views");
        var peer = Peer(studio, "Views");
        var heard = new List<(ExpandCollapseState Before, ExpandCollapseState After)>();

        peer.PropertyChanged += (_, e) =>
        {
            if (e.Property == ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty)
                heard.Add(((ExpandCollapseState)e.OldValue!, (ExpandCollapseState)e.NewValue!));
        };

        Expander(peer).Expand();
        Dispatcher.UIThread.RunJobs();

        Assert.True(views.IsExpanded, "просьба диктора не раскрыла папку");
        Assert.Contains(studio.Rows, row => row.Name == "MainWindow.axaml");
        Assert.Equal(ExpandCollapseState.Expanded, Expander(peer).ExpandCollapseState);

        studio.Select("MainWindow.axaml");
        Expander(peer).Collapse();
        Dispatcher.UIThread.RunJobs();

        Assert.False(views.IsExpanded, "просьба диктора не свернула папку");
        Assert.Same(views, studio.Selected);
        Assert.Same(views, Assert.IsAssignableFrom<Control>(studio.Window.FocusManager?.GetFocusedElement()).DataContext);
        Assert.Equal(
            [(ExpandCollapseState.Collapsed, ExpandCollapseState.Expanded), (ExpandCollapseState.Expanded, ExpandCollapseState.Collapsed)],
            heard);

        var leaf = Expander(Peer(studio, "Program.cs"));

        leaf.Expand();

        Assert.Equal(ExpandCollapseState.LeafNode, leaf.ExpandCollapseState);
    }

    private static AutomationPeer Peer(ProjectWindowStudio studio, string name) =>
        ControlAutomationPeer.CreatePeerForElement(studio.Item(studio.Row(name)));

    private static IExpandCollapseProvider Expander(AutomationPeer peer) =>
        Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetProvider<IExpandCollapseProvider>());

    /// <summary>Перезагрузка не удалась, а прежний снимок остался.</summary>
    private static ProjectsStatus Failed(long sequence, SolutionSnapshot snapshot) => new()
    {
        Sequence = sequence,
        Session = 1,
        State = ProjectsState.Failed,
        EntryPoint = snapshot.EntryPoint.Path,
        Snapshot = snapshot,
        LastLoad = new ProjectsLoad
        {
            Reason = ProjectsLoadReason.Reload,
            Result = WorkspaceLoadResult.Failure(
                new ProjectDiagnostic("MSB4025", "The project file could not be loaded.", ProjectDiagnosticSeverity.Error)),
        },
    };
}
