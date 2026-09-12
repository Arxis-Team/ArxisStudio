using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects.Reporting;

/// <summary>
/// Находки загрузки и операций — в журнал студии.
/// </summary>
/// <remarks>
/// <para>
/// Пишутся сразу за итоговой строкой того, что их нашло: сначала «что вышло», следом «что
/// сказано». Журнал — летопись, а не состояние: находка остаётся записанной и после того, как её
/// исправили, и снимать её незачем — время рядом с ней говорит, когда это было правдой.
/// </para>
/// <para>
/// Одно и то же, сказанное дважды, пишется один раз: один импорт, сломанный у двух проектов, —
/// одна находка, а не две одинаковых строки подряд.
/// </para>
/// <para>
/// Потолок — на случай сборки, у которой предупреждений сотни: журнал общий, и утопить в них всё
/// остальное было бы хуже, чем не показать хвост. Сколько осталось несказанным, пишется числом.
/// </para>
/// </remarks>
internal static class FindingsLog
{
    /// <summary>Сколько находок пишется, прежде чем остальные сойдутся в одну строку.</summary>
    internal const int Ceiling = 20;

    /// <summary>Пишет находки в журнал.</summary>
    /// <param name="log">Журнал студии.</param>
    /// <param name="diagnostics">Что сказали загрузка или операция.</param>
    internal static void Write(IStudioLog log, IEnumerable<ProjectDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var said = new HashSet<string>(StringComparer.Ordinal);
        var written = 0;
        var hidden = 0;

        foreach (var diagnostic in diagnostics)
        {
            var line = Line(diagnostic);

            if (!said.Add(line))
                continue;

            if (written == Ceiling)
            {
                hidden++;
                continue;
            }

            log.Write(Level(diagnostic.Severity), ProjectsModule.LogSource, line);
            written++;
        }

        if (hidden > 0)
            log.Write(StudioLogLevel.Info, ProjectsModule.LogSource, $"…ещё находок: {hidden}");
    }

    /// <summary>Находка одной строкой: код, объяснение и место.</summary>
    /// <param name="diagnostic">Находка.</param>
    private static string Line(ProjectDiagnostic diagnostic) =>
        Where(diagnostic) is { Length: > 0 } where
            ? $"{diagnostic.Code}: {diagnostic.Message} — {where}"
            : $"{diagnostic.Code}: {diagnostic.Message}";

    /// <summary>
    /// Место находки: имя файла и строка.
    /// </summary>
    /// <param name="diagnostic">Находка.</param>
    /// <remarks>
    /// Файла у находки может не быть — тогда берётся файл её проекта: так у неё есть хотя бы то
    /// место, где её искать. Полный путь в строку журнала не идёт: строк много, а папка у них одна
    /// и та же, и от неё в глазах рябит.
    /// </remarks>
    private static string Where(ProjectDiagnostic diagnostic)
    {
        var path = !diagnostic.FilePath.IsEmpty ? diagnostic.FilePath
            : !diagnostic.Project.IsEmpty ? diagnostic.Project.ProjectFilePath
            : CanonicalPath.None;

        if (path.IsEmpty)
            return string.Empty;

        return diagnostic.Span.StartLine > 0
            ? $"{path.FileName}:{diagnostic.Span.StartLine}"
            : path.FileName;
    }

    /// <summary>Уровень записи по серьёзности находки.</summary>
    /// <param name="severity">Серьёзность.</param>
    private static StudioLogLevel Level(ProjectDiagnosticSeverity severity) => severity switch
    {
        ProjectDiagnosticSeverity.Error => StudioLogLevel.Error,
        ProjectDiagnosticSeverity.Warning => StudioLogLevel.Warning,
        _ => StudioLogLevel.Info,
    };
}
