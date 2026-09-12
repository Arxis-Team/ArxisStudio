using ArxisStudio.Modules.Projects;
using ArxisStudio.Modules.Projects.Reporting;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Находки службы проектов в журнале студии.
/// </summary>
/// <remarks>
/// Панели под них у студии нет, и журнал — единственное место, где человек видит сказанное
/// провайдером. Поэтому строка обязана называть всё: код, объяснение и место, — а поток записей
/// обязан оставаться читаемым, сколько бы их ни нашлось.
/// </remarks>
public class FindingsLogTests
{
    /// <summary>Находка пишется строкой: код, объяснение и место.</summary>
    [Fact]
    public void A_finding_is_one_line_with_its_code_its_message_and_its_place()
    {
        var log = new StudioLog();

        FindingsLog.Write(log, [ProjectDiagnostic.ForFile(
            "APS2002",
            "проект не прочитался",
            ProjectDiagnosticSeverity.Error,
            CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Lib.csproj")),
            FileSpan.At(12))]);

        var record = Assert.Single(Written(log));

        Assert.Equal(StudioLogLevel.Error, record.Level);
        Assert.Equal("APS2002: проект не прочитался — Lib.csproj:12", record.Message);
    }

    /// <summary>Уровень записи — серьёзность находки.</summary>
    [Fact]
    public void The_level_of_the_record_is_the_severity_of_the_finding()
    {
        var log = new StudioLog();

        FindingsLog.Write(log,
        [
            new ProjectDiagnostic("E", "раз", ProjectDiagnosticSeverity.Error),
            new ProjectDiagnostic("W", "два", ProjectDiagnosticSeverity.Warning),
            new ProjectDiagnostic("I", "три", ProjectDiagnosticSeverity.Info),
        ]);

        Assert.Equal(
            [StudioLogLevel.Error, StudioLogLevel.Warning, StudioLogLevel.Info],
            Written(log).Select(record => record.Level));
    }

    /// <summary>
    /// Место берётся у проекта, когда своего файла у находки нет.
    /// </summary>
    /// <remarks>
    /// Так у неё есть хотя бы то место, где её искать. Совсем без места находка пишется одним
    /// объяснением: тире, за которым ничего, обещало бы адрес, которого нет.
    /// </remarks>
    [Fact]
    public void A_finding_without_a_file_borrows_the_file_of_its_project()
    {
        var log = new StudioLog();
        var project = ProjectIdentity.Create(
            WorkspaceIdentity.New(),
            CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "App.csproj")));

        FindingsLog.Write(log,
        [
            new ProjectDiagnostic("APS3001", "не собралось", ProjectDiagnosticSeverity.Error) { Project = project },
            new ProjectDiagnostic("APS1001", "негде", ProjectDiagnosticSeverity.Info),
        ]);

        Assert.Equal(
            ["APS3001: не собралось — App.csproj", "APS1001: негде"],
            Written(log).Select(record => record.Message));
    }

    /// <summary>Одно и то же, сказанное дважды, пишется один раз.</summary>
    /// <remarks>
    /// Один сломанный импорт виден каждому проекту, который его тянет, и провайдер честно говорит
    /// о нём столько раз, сколько их. Человеку это одна находка.
    /// </remarks>
    [Fact]
    public void The_same_finding_said_twice_is_written_once()
    {
        var log = new StudioLog();
        var said = ProjectDiagnostic.ForFile(
            "APS2002",
            "импорт не найден",
            ProjectDiagnosticSeverity.Warning,
            CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Shared.props")));

        FindingsLog.Write(log, [said, said, said]);

        Assert.Single(Written(log));
    }

    /// <summary>За потолком находки сходятся в одну строку с их числом.</summary>
    /// <remarks>
    /// Журнал общий: сборка с сотней предупреждений утопила бы в них всё остальное, что студия
    /// пишет. Сколько осталось несказанным, человек видит числом и находит в выводе сборки.
    /// </remarks>
    [Fact]
    public void Beyond_the_ceiling_the_rest_are_counted_in_one_line()
    {
        var log = new StudioLog();
        var extra = 5;

        FindingsLog.Write(log,
        [
            .. Enumerable.Range(0, FindingsLog.Ceiling + extra).Select(number =>
                new ProjectDiagnostic($"CS{number:0000}", "раз", ProjectDiagnosticSeverity.Warning)),
        ]);

        var written = Written(log).ToList();

        Assert.Equal(FindingsLog.Ceiling + 1, written.Count);
        Assert.Equal($"…ещё находок: {extra}", written[^1].Message);
        Assert.Equal(StudioLogLevel.Info, written[^1].Level);
    }

    /// <summary>Сказать нечего — и в журнале ничего.</summary>
    [Fact]
    public void Nothing_found_is_nothing_written()
    {
        var log = new StudioLog();

        FindingsLog.Write(log, []);

        Assert.Empty(Written(log));
    }

    /// <summary>Что написала служба проектов.</summary>
    /// <param name="log">Журнал.</param>
    private static IEnumerable<StudioLogRecord> Written(StudioLog log) =>
        log.Records.Where(record => record.Source == ProjectsModule.LogSource);
}
