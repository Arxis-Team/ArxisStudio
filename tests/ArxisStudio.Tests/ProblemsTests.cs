using ArxisStudio.ProjectSystem;
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
    public void A_project_diagnostic_keeps_its_place_in_the_file()
    {
        var diagnostic = ProjectDiagnostic.ForFile(
            "APS1002",
            "проект не открылся",
            ProjectDiagnosticSeverity.Error,
            CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Sample.csproj")),
            FileSpan.At(12, 5));

        var problem = StudioProblems.From(diagnostic);

        Assert.Equal(StudioProblemSeverity.Error, problem.Severity);
        Assert.Equal("APS1002", problem.Code);
        Assert.Equal(12, problem.Line);
        Assert.Equal(5, problem.Column);
        Assert.Equal("Sample.csproj:12", problem.Where);
    }

    [Fact]
    public void A_problem_without_a_file_has_nowhere_to_go()
    {
        var problem = new StudioProblem(StudioProblemSeverity.Warning, "W", "без файла");

        Assert.Equal(string.Empty, problem.Where);
    }

    private static StudioProblem Error(string code, string message) =>
        new(StudioProblemSeverity.Error, code, message);
}
