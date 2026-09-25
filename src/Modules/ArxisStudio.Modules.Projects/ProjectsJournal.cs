using System.Collections.Immutable;
using ArxisStudio.Modules.Projects.Engine;
using ArxisStudio.Modules.Projects.Reporting;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using ArxisStudio.ProjectSystem.NuGet;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Что служба проектов пишет в журнал студии: итог загрузки и итог операции, по строке на каждый.
/// </summary>
/// <remarks>
/// Журнальная часть жила внутри хоста, среди очередей и замков, и занимала в нём полторы сотни строк,
/// которым ни очередь, ни замок не нужны. Слова журнала — русские и внутренние: это разговор для
/// автора и для того, кто разбирает сбой, а не для строки состояния.
/// </remarks>
/// <param name="log">Журнал студии.</param>
internal sealed class ProjectsJournal(IStudioLog log)
{
    private int _announced;

    /// <summary>Итог загрузки — в журнал, одной строкой, с причиной и временем.</summary>
    /// <param name="reason">Почему загружали.</param>
    /// <param name="request">Что просили.</param>
    /// <param name="causes">Какие файлы вызвали перезагрузку.</param>
    /// <param name="result">Что вышло.</param>
    /// <param name="elapsed">Сколько заняло.</param>
    public void Loaded(
        ProjectsLoadReason reason,
        WorkspaceLoadRequest request,
        ImmutableArray<CanonicalPath> causes,
        WorkspaceLoadResult result,
        TimeSpan elapsed)
    {
        // Какой MSBuild нашёлся — один раз за службу: регистрация одна на процесс, и первая
        // загрузка — первое место, где об этом можно сказать правду.
        if (MSBuildEnvironment.Current is { } registration && Interlocked.Exchange(ref _announced, 1) == 0)
            log.Write(StudioLogLevel.Info, ProjectsModule.LogSource, $"MSBuild: {registration}");

        var why = reason switch
        {
            ProjectsLoadReason.Open => "открыто",
            ProjectsLoadReason.Reload => "перезагружено",
            ProjectsLoadReason.Configuration => $"перезагружено под конфигурацию {request.Configuration ?? "проекта"}",
            _ => $"перезагружено, изменилось: {Causes(causes)}",
        };

        ProjectDiagnostic? failure = null;

        if (result.Snapshot is { } snapshot)
        {
            var errors = result.Diagnostics.Count(diagnostic => diagnostic.IsError);

            log.Write(
                errors > 0 ? StudioLogLevel.Warning : StudioLogLevel.Info,
                ProjectsModule.LogSource,
                $"{snapshot.Name}: {why} — проектов {snapshot.Projects.Length}, ошибок {errors}, {elapsed.TotalMilliseconds:F0} мс");
        }
        else
        {
            failure = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

            log.Write(
                StudioLogLevel.Error,
                ProjectsModule.LogSource,
                $"{request.EntryPointPath.FileName}: не {(reason == ProjectsLoadReason.Open ? "открылось" : "перезагрузилось")} — {failure?.Code} {failure?.Message}");
        }

        // Сами находки — следом за итогом: он говорит, что вышло, они — что сказано. Ту, которой
        // провал уже назвался, повторять сразу под собой незачем.
        FindingsLog.Write(
            log,
            failure is null ? result.Diagnostics : result.Diagnostics.Where(diagnostic => diagnostic != failure));
    }

    /// <summary>Итог операции — в журнал, одной строкой.</summary>
    /// <param name="item">Операция.</param>
    /// <param name="result">Итог; null — отменена или сорвалась.</param>
    /// <param name="error">Сбой самой службы; null — его не было.</param>
    /// <param name="elapsed">Сколько заняла.</param>
    public void Finished(OperationItem item, ProjectOperationResult? result, Exception? error, TimeSpan elapsed)
    {
        var operation = item.Operation;
        var what = item.Edit is { } edit ? $"{Name(edit.Kind)} {edit.PackageId}" : Name(operation.Kind);
        var file = operation.EntryPoint.FileName;

        if (result is null)
        {
            log.Write(
                error is null ? StudioLogLevel.Info : StudioLogLevel.Error,
                ProjectsModule.LogSource,
                error is null ? $"{file}: {what} — отменено" : $"{file}: {what} — сорвалось: {error.Message}");

            return;
        }

        var errors = result.Diagnostics.Count(diagnostic => diagnostic.IsError);

        log.Write(
            result.HasErrors ? StudioLogLevel.Error : StudioLogLevel.Info,
            ProjectsModule.LogSource,
            $"{file}: {what} — {(result.HasErrors ? "не удалось" : "готово")}, ошибок {errors}, {elapsed.TotalMilliseconds:F0} мс");

        FindingsLog.Write(log, result.Diagnostics);
    }

    /// <summary>Как операция называется в журнале.</summary>
    /// <param name="kind">Что делали.</param>
    private static string Name(ProjectOperationKind kind) => kind switch
    {
        ProjectOperationKind.Restore => "восстановление",
        ProjectOperationKind.Build => "сборка",
        ProjectOperationKind.Rebuild => "пересборка",
        _ => "очистка",
    };

    /// <summary>Как правка пакетов называется в журнале.</summary>
    /// <param name="kind">Что делали.</param>
    private static string Name(PackageEditKind kind) => kind switch
    {
        PackageEditKind.Install => "установка",
        PackageEditKind.Update => "обновление",
        _ => "удаление",
    };

    private static string Causes(ImmutableArray<CanonicalPath> causes) => causes.Length <= 3
        ? string.Join(", ", causes.Select(cause => cause.FileName))
        : $"{string.Join(", ", causes.Take(3).Select(cause => cause.FileName))} и ещё {causes.Length - 3}";
}
