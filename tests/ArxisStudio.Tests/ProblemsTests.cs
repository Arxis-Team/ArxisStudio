using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панель «Проблемы»: находки от разных источников в одном списке.
/// </summary>
public class ProblemsTests
{
    [Fact]
    public void A_source_replaces_everything_it_said_before()
    {
        var problems = new StudioProblems();

        problems.Report("project", [Error("APS1001", "первая")]);
        problems.Report("project", [Error("APS1002", "вторая")]);

        // Не сложение, а замена: иначе исправленную находку пришлось бы снимать
        // поимённо, а её ещё нужно вспомнить.
        var single = Assert.Single(problems.All);

        Assert.Equal("APS1002", single.Code);
    }

    [Fact]
    public void An_empty_report_takes_the_source_off_the_list()
    {
        var problems = new StudioProblems();

        problems.Report("designer:MainWindow.axaml", [Error("AXD0001", "не разобралось")]);
        problems.Report("designer:MainWindow.axaml", []);

        Assert.Empty(problems.All);
    }

    [Fact]
    public void Sources_do_not_take_each_other_off_the_list()
    {
        var problems = new StudioProblems();

        problems.Report("designer:A.axaml", [Error("AXD0001", "про A")]);
        problems.Report("designer:B.axaml", [Error("AXD0001", "про B")]);
        problems.Report("designer:A.axaml", []);

        var single = Assert.Single(problems.All);

        Assert.Equal("про B", single.Message);
    }

    [Fact]
    public void Errors_come_before_warnings()
    {
        var problems = new StudioProblems();

        problems.Report("project",
        [
            new StudioProblem(StudioProblemSeverity.Warning, "W", "предупреждение"),
            new StudioProblem(StudioProblemSeverity.Info, "I", "к сведению"),
            new StudioProblem(StudioProblemSeverity.Error, "E", "ошибка"),
        ]);

        Assert.Equal(["E", "W", "I"], problems.All.Select(problem => problem.Code));
    }

    [Fact]
    public void Every_change_is_announced()
    {
        var problems = new StudioProblems();
        var announced = 0;

        problems.Changed += (_, _) => announced++;

        problems.Report("project", [Error("APS1001", "раз")]);
        problems.Report("project", []);

        // Снятие несуществующей находки менять нечего, и объявлять нечего.
        problems.Report("project", []);

        Assert.Equal(2, announced);
    }

    [Fact]
    public void A_problem_without_a_file_has_nowhere_to_go()
    {
        var problem = new StudioProblem(StudioProblemSeverity.Warning, "W", "без файла");

        Assert.Equal(string.Empty, problem.Where);
    }

    /// <summary>
    /// Расширение не может снять чужие находки, назвавшись чужим именем.
    /// </summary>
    /// <remarks>
    /// Имя источника сочиняет сам источник, а <c>Report</c> заменяет всё, что
    /// источник говорил прежде. Без хозяина впереди одно расширение стирало бы
    /// находки другого пустым списком — и назвать чужое имя ему ничего не
    /// стоило: у обоих проверка зовётся «build».
    /// </remarks>
    [Fact]
    public void One_extension_cannot_take_another_off_the_list()
    {
        var problems = new StudioProblems();

        new PluginProblems(problems, "arxis.one").Report("build", [Error("A1", "у первого")]);
        new PluginProblems(problems, "arxis.two").Report("build", [Error("B1", "у второго")]);

        Assert.Equal(2, problems.All.Count);

        // Второй снимает свои — и только свои, хотя назвал ту же проверку.
        new PluginProblems(problems, "arxis.two").Report("build", []);

        var left = Assert.Single(problems.All);

        Assert.Equal("A1", left.Code);
    }

    /// <summary>
    /// Находки ушедшего расширения снимает студия.
    /// </summary>
    /// <remarks>
    /// Само оно этого уже не сделает: выгружено. Прежде его находки висели бы
    /// в панели до конца сеанса — исправить некому, перепроверить некому,
    /// убрать нечем.
    /// </remarks>
    [Fact]
    public void The_findings_of_a_departed_extension_are_taken_off()
    {
        var problems = new StudioProblems();

        new PluginProblems(problems, "arxis.one").Report("build", [Error("A1", "раз")]);
        new PluginProblems(problems, "arxis.one").Report("lint", [Error("A2", "два")]);
        new PluginProblems(problems, "arxis.two").Report("build", [Error("B1", "три")]);

        var announced = 0;

        problems.Changed += (_, _) => announced++;

        problems.RemoveOwnedBy("arxis.one");

        var left = Assert.Single(problems.All);

        Assert.Equal("B1", left.Code);
        Assert.Equal(1, announced);

        // Уходящему, который ничего не находил, объявлять нечего.
        problems.RemoveOwnedBy("arxis.three");

        Assert.Equal(1, announced);
    }

    /// <summary>
    /// Расширению выдают именные находки, а не общие.
    /// </summary>
    /// <remarks>
    /// Проверка проводки, а не поведения: приставку ставит фасад, но получить
    /// его расширение должно от того, кто выдал контекст. Отдай студия общий
    /// экземпляр — все проверки выше остались бы зелёными, а расширение всё
    /// равно писало бы без хозяина.
    /// </remarks>
    [Fact]
    public void An_extension_is_handed_problems_of_its_own()
    {
        var problems = new StudioProblems();

        var factory = new StudioContextFactory(
            new StudioLog(),
            new StudioCommands(),
            projectPath: null,
            services: new Dictionary<Type, object> { [typeof(IStudioProblems)] = problems });

        var manifest = new Sdk.Plugins.PluginManifest { Id = "arxis.one", Name = "Один" };
        var context = factory.Create(new InstalledPlugin(AppContext.BaseDirectory, manifest, null, IsEnabled: true, IsBuiltIn: true));

        context.GetService<IStudioProblems>()!.Report("build", [Error("A1", "раз")]);

        // Снимается по имени с хозяином — значит под ним и записано.
        problems.Report(StudioProblems.Owned("arxis.one", "build"), []);

        Assert.Empty(problems.All);
    }

    /// <summary>Имя источника у расширения начинается с его идентификатора.</summary>
    /// <remarks>
    /// По этой приставке студия и узнаёт, чьи находки снимать. Проверяется она
    /// здесь прямо: остальные проверки этого файла говорят о поведении, а имя
    /// — договор между фасадом и уборкой.
    /// </remarks>
    [Fact]
    public void An_extension_reports_under_its_own_name()
    {
        var problems = new StudioProblems();

        new PluginProblems(problems, "arxis.one").Report("build", [Error("A1", "раз")]);
        problems.Report(StudioProblems.Owned("arxis.one", "build"), []);

        Assert.Empty(problems.All);
    }

    private static StudioProblem Error(string code, string message) =>
        new(StudioProblemSeverity.Error, code, message);
}
