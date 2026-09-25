using System.Collections.Specialized;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Плоское дерево окна проекта: видимые узлы подряд и одна правка на одну перемену.
/// </summary>
/// <remarks>
/// Список виртуализован и держит столько строк, сколько видно, — если каждая перемена доходит до
/// него одной вставкой или одним удалением. Сброс списка на каждое раскрытие или каждый новый
/// снимок работал бы так же с виду, но терял бы выделение и прокрутку и заводил бы контейнеры
/// заново. Поэтому здесь проверяется не только что видно, но и каким событием оно стало видно.
/// </remarks>
public class ProjectWindowRowsTests
{
    /// <summary>
    /// Решение открывается раскрытым до проектов, как в Rider: видно, из чего оно состоит.
    /// </summary>
    [Fact]
    public void A_solution_opens_expanded_down_to_its_projects()
    {
        var rows = Opened();

        Assert.Equal(
            """
            v Hello
              v src
                v App
                  > Dependencies
                  > Assets
                  . Models
                  > Views
                  > App.axaml
                  . app.manifest
                  . Program.cs
                v Lib
                  . Class1.cs
            """.ReplaceLineEndings("\n"),
            Lines(rows));
    }

    /// <summary>Раскрытие вставляет ветку одной правкой, а прочие строки не трогает.</summary>
    [Fact]
    public void Expanding_inserts_the_branch_in_one_change()
    {
        var rows = Opened();
        var before = rows.Rows.ToList();
        var views = Row(rows, "Views");
        var changes = Record(rows);

        rows.Expand(views);

        var change = Assert.Single(changes);

        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(before.IndexOf(views) + 1, change.NewStartingIndex);
        Assert.Equal(["MainWindow.axaml"], change.NewItems!.Cast<Row>().Select(row => row.Name));
        Assert.True(views.IsExpanded, "раскрытая строка не знает, что раскрыта");
        Assert.All(before, row => Assert.Contains(row, rows.Rows));
    }

    /// <summary>
    /// Свёртка убирает всю видимую ветку одной правкой, и раскрытое внутри неё помнится.
    /// </summary>
    /// <remarks>
    /// Свёрнутый проект, раскрытый снова, показывает папку раскрытой, как её оставили: так ведёт
    /// себя дерево каждой среды, и человек не раскрывает одно и то же дважды.
    /// </remarks>
    [Fact]
    public void Collapsing_removes_the_branch_in_one_change_and_remembers_inside_it()
    {
        var rows = Opened();

        rows.Expand(Row(rows, "Views"));

        var app = Row(rows, "App");
        var changes = Record(rows);

        rows.Collapse(app);

        var change = Assert.Single(changes);

        Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action);
        Assert.Equal(8, change.OldItems!.Count);
        Assert.False(app.IsExpanded, "свёрнутая строка считает себя раскрытой");

        rows.Expand(app);

        Assert.True(Row(rows, "Views").IsExpanded, "свёртка проекта забыла раскрытую в нём папку");
        Assert.Contains(rows.Rows, row => row.Name == "MainWindow.axaml");
    }

    /// <summary>
    /// Новый снимок того же решения меняет только то, что в нём изменилось.
    /// </summary>
    /// <remarks>
    /// Служба проектов перечитывает решение целиком на каждый новый файл. Для списка это должна быть
    /// одна вставка новой строки: прочие строки остаются теми же объектами — с узлами нового снимка,
    /// — и раскрытое остаётся раскрытым.
    /// </remarks>
    [Fact]
    public void A_new_snapshot_changes_only_what_changed_in_it()
    {
        var rows = Opened();

        rows.Expand(Row(rows, "Views"));

        var before = rows.Rows.ToList();
        var changes = Record(rows);
        var next = ProjectWindowSolution.Avalonia(extra: "Views/Settings.axaml").Tree();

        rows.Show(next, forget: false);

        var change = Assert.Single(changes);

        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(["Settings.axaml"], change.NewItems!.Cast<Row>().Select(row => row.Name));
        Assert.Equal(before.IndexOf(Row(rows, "MainWindow.axaml")) + 1, change.NewStartingIndex);
        Assert.All(before, row => Assert.Contains(row, rows.Rows));
        Assert.True(Row(rows, "Views").IsExpanded, "новый снимок свернул раскрытое");

        var nodes = next.Descendants().ToHashSet();

        Assert.All(rows.Rows, row => Assert.Contains(row.Node, nodes));
    }

    /// <summary>Другое решение забывает раскрытое прежнего и открывается как впервые.</summary>
    [Fact]
    public void Another_solution_forgets_what_was_expanded()
    {
        var rows = Opened();
        var fresh = Lines(rows);

        rows.Expand(Row(rows, "Views"));
        rows.Collapse(Row(rows, "Lib"));
        rows.Show(ProjectWindowSolution.Avalonia().Tree(), forget: true);

        Assert.Equal(fresh, Lines(rows));
    }

    /// <summary>
    /// Поиск оставляет найденное и дорогу к нему, раскрытую.
    /// </summary>
    /// <remarks>
    /// Совпадение ищется в имени без учёта регистра, а предки совпадений раскрыты — иначе
    /// найденного не было бы видно, пока человек не раскроет всю дорогу сам.
    /// </remarks>
    [Fact]
    public void Search_keeps_what_it_found_and_the_way_to_it()
    {
        var rows = Opened();

        rows.Filter("main");

        Assert.Equal(
            """
            v Hello
              v src
                v App
                  v Views
                    v MainWindow.axaml
                      . MainWindow.axaml.cs
            """.ReplaceLineEndings("\n"),
            Lines(rows));
        Assert.Equal(2, rows.Found);
        Assert.True(rows.IsFiltered);

        rows.Filter("nothing like this");

        Assert.Empty(rows.Rows);
        Assert.Equal(0, rows.Found);
    }

    /// <summary>Снятый поиск возвращает дерево ровно таким, каким его раскрыли до поиска.</summary>
    [Fact]
    public void Clearing_the_search_brings_the_tree_back_as_it_was()
    {
        var rows = Opened();

        rows.Expand(Row(rows, "Assets"));
        rows.Collapse(Row(rows, "Lib"));

        var before = Lines(rows);

        rows.Filter("axaml");
        rows.Filter(" ");

        Assert.False(rows.IsFiltered);
        Assert.Equal(before, Lines(rows));
    }

    /// <summary>
    /// Снимок, пришедший во время поиска, показывается уже отобранным.
    /// </summary>
    [Fact]
    public void A_snapshot_arriving_during_a_search_is_shown_searched()
    {
        var rows = Opened();

        rows.Filter("settings");

        Assert.Empty(rows.Rows);

        rows.Show(ProjectWindowSolution.Avalonia(extra: "Views/Settings.axaml").Tree(), forget: false);

        Assert.Equal("Settings.axaml", rows.Rows[^1].Name);
        Assert.Equal(1, rows.Found);
    }

    /// <summary>«Свернуть всё» оставляет решение и то, что лежит прямо в нём.</summary>
    [Fact]
    public void Collapsing_everything_leaves_the_solution_and_what_lies_right_in_it()
    {
        var rows = Opened();

        rows.CollapseAll();

        Assert.Equal(
            """
            v Hello
              > src
            """.ReplaceLineEndings("\n"),
            Lines(rows));
    }

    /// <summary>
    /// Ветка раскрывается целиком и сворачивается целиком — вместе со всем, что в ней раскрыто.
    /// </summary>
    [Fact]
    public void A_branch_expands_and_collapses_as_a_whole()
    {
        var rows = Opened();
        var app = Row(rows, "App");

        rows.ExpandBranch(app);

        Assert.Contains(rows.Rows, row => row.Name == "Avalonia");
        Assert.Contains(rows.Rows, row => row.Name == "App.axaml.cs");
        Assert.Contains(rows.Rows, row => row.Name == "MainWindow.axaml.cs");

        rows.CollapseBranch(app);
        rows.Expand(app);

        Assert.All(rows.Rows.Where(row => row.Depth > app.Depth && row.HasChildren), row =>
            Assert.False(row.IsExpanded, $"{row.Name} остался раскрытым после свёртки ветки"));
    }

    /// <summary>Показать узел — раскрыть дорогу до него.</summary>
    [Fact]
    public void Revealing_a_node_expands_the_way_to_it()
    {
        var rows = Opened();

        rows.CollapseAll();

        var code = Named(rows, "App.axaml.cs");

        Assert.Null(rows.Find(code.Key));

        rows.Reveal(code);

        Assert.NotNull(rows.Find(code.Key));
        Assert.Equal("App.axaml", rows.ParentOf(rows.Find(code.Key)!)!.Name);
    }

    /// <summary>
    /// Строка знает глубину, детей и раскрытие; у пустой объявленной папки детей нет.
    /// </summary>
    [Fact]
    public void A_row_knows_its_depth_its_children_and_whether_it_is_open()
    {
        var rows = Opened();
        var models = Row(rows, "Models");
        var views = Row(rows, "Views");

        Assert.Equal(3, models.Depth);
        Assert.False(models.HasChildren, "пустая папка показана с шевроном");
        Assert.True(views.HasChildren);
        Assert.False(views.IsExpanded);
        Assert.Equal("Models", models.ToString());
        Assert.Equal(Path.Combine("src", "App", "Models"), models.Hint);
    }

    /// <summary>Закрытое решение оставляет пустой список.</summary>
    [Fact]
    public void A_closed_solution_leaves_no_rows()
    {
        var rows = Opened();

        rows.Show(null, forget: false);

        Assert.Empty(rows.Rows);
    }

    /// <summary>Плоское дерево обычного решения, открытого впервые.</summary>
    private static RowList Opened()
    {
        var rows = new RowList();

        rows.Show(ProjectWindowSolution.Avalonia().Tree(), forget: true);

        return rows;
    }

    /// <summary>Строка по имени.</summary>
    private static Row Row(RowList rows, string name) => rows.Rows.Single(row => row.Name == name);

    /// <summary>Узел по имени — видим он или нет.</summary>
    private static Node Named(RowList rows, string name) => rows.Root!.Descendants().Single(node => node.Name == name);

    /// <summary>Правки списка, по одной на событие.</summary>
    private static List<NotifyCollectionChangedEventArgs> Record(RowList rows)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();

        rows.Rows.CollectionChanged += (_, change) => changes.Add(change);

        return changes;
    }

    /// <summary>
    /// Строки текстом: отступ — глубина, «v» — раскрыт, «&gt;» — свёрнут, «.» — лист.
    /// </summary>
    private static string Lines(RowList rows) =>
        string.Join('\n', rows.Rows.Select(row =>
            new string(' ', row.Depth * 2) + (!row.HasChildren ? ". " : row.IsExpanded ? "v " : "> ") + row.Name));
}
