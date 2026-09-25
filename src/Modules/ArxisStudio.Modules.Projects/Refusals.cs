using ArxisStudio.Modules.Projects.Files;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Отказы службы проектов: провал с кодом службы и словами для человека, а не исключение.
/// </summary>
/// <remarks>
/// Отказ собирали по месту — хост, служба файлов, история, отмена, — и одна форма стояла в восьми
/// местах, каждое своими словами. Путь, если назван, отмечает, чего отказ касается: окно проекта
/// ставит по нему выделение.
/// </remarks>
internal static class Refusals
{
    /// <summary>Находка службы; по умолчанию — отказ.</summary>
    /// <param name="code">Код службы.</param>
    /// <param name="message">Что сказать человеку.</param>
    /// <param name="path">Чего находка касается; null — ничего отдельного.</param>
    /// <param name="severity">Строгость; отказ — ошибка.</param>
    public static ProjectDiagnostic Diagnostic(
        string code,
        string message,
        string? path = null,
        ProjectDiagnosticSeverity severity = ProjectDiagnosticSeverity.Error) =>
        new(code, message, severity)
        {
            FilePath = CanonicalPath.TryCreate(path, out var canonical) ? canonical : CanonicalPath.None,
        };

    /// <summary>Отказ операции.</summary>
    /// <param name="code">Код службы.</param>
    /// <param name="message">Что сказать человеку.</param>
    /// <param name="path">Чего отказ касается; null — ничего отдельного.</param>
    public static ProjectOperationResult Of(string code, string message, string? path = null) =>
        ProjectOperationResult.Failed(Diagnostic(code, message, path));

    /// <summary>Отказ правки файлов: итог без перемен на диске.</summary>
    /// <param name="code">Код службы.</param>
    /// <param name="message">Что сказать человеку.</param>
    /// <param name="path">Чего отказ касается; null — ничего отдельного.</param>
    public static FileWorkResult Work(string code, string message, string? path = null) =>
        new(Of(code, message, path), null);
}
