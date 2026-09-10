using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.Reporting;

/// <summary>
/// Находки загрузки — в панель «Проблемы».
/// </summary>
/// <remarks>
/// <para>
/// Показывается итог последней загрузки, и только он. Провалившаяся перезагрузка оставляет прежний
/// снимок, но его диагностики уже не правда: сейчас не так то, что сказал провал. Удачная загрузка
/// приносит всё, что собрано в итоге, — диагностики решения и каждого проекта.
/// </para>
/// <para>
/// Отвечает на доставленное состояние, в потоке интерфейса и раньше подписчиков: доставка склеена и
/// упорядочена, поэтому панель не покажет итог загрузки, которую уже сменило закрытие, а подписчик,
/// читающий находки в своём обработчике, увидит их согласными с событием.
/// </para>
/// </remarks>
/// <param name="problems">Находки студии; null — показывать некуда.</param>
internal sealed class ProblemsReporter(IStudioProblems? problems)
{
    /// <summary>Имя источника находок; хозяина впереди ставит студия.</summary>
    public const string Source = "load";

    /// <summary>Показывает итог доставленного состояния.</summary>
    /// <param name="change">Доставленная перемена.</param>
    public void Show(ProjectsChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (problems is null || (change.Changes & (ProjectsChanges.LastLoad | ProjectsChanges.Session)) == 0)
            return;

        problems.Report(Source, change.Current.LastLoad is { } load ? Translate(load.Result.Diagnostics) : []);
    }

    /// <summary>Диагностики ядра находками студии, без повторов.</summary>
    /// <param name="diagnostics">Диагностики.</param>
    internal static IReadOnlyList<StudioProblem> Translate(IEnumerable<ProjectDiagnostic> diagnostics) =>
        [.. diagnostics.Select(Translate).Distinct()];

    private static StudioProblem Translate(ProjectDiagnostic diagnostic) => new(
        diagnostic.Severity switch
        {
            ProjectDiagnosticSeverity.Error => StudioProblemSeverity.Error,
            ProjectDiagnosticSeverity.Warning => StudioProblemSeverity.Warning,
            _ => StudioProblemSeverity.Info,
        },
        diagnostic.Code,
        diagnostic.Message,
        Where(diagnostic),
        diagnostic.Span.StartLine,
        diagnostic.Span.StartColumn);

    /// <summary>Файл находки, а без него — файл её проекта: так её хотя бы есть где открыть.</summary>
    private static string? Where(ProjectDiagnostic diagnostic) =>
        !diagnostic.FilePath.IsEmpty ? diagnostic.FilePath.Value
        : !diagnostic.Project.IsEmpty ? diagnostic.Project.ProjectFilePath.Value
        : null;
}
