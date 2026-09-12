using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Точка входа службы проектов: публикует службы и заявляет команды.
/// </summary>
/// <remarks>
/// Интерфейса у модуля нет, и подъём его дешёвый: MSBuild при нём не трогается. Движок
/// регистрируется при первом открытии и не раньше — регистрация меняет окружение всего процесса,
/// а модули поднимаются задолго до того, как человек что-то открыл.
/// <para>
/// Служб две, и обе публикуются экспортом, как у плагина: модель и сборка разведены, потому что
/// подписчику снимков события сборки не нужны, а тому, кто рисует окно сборки, не нужен снимок.
/// Другой дороги к ним у соседей нет, и у самой студии тоже не будет — работа с проектами
/// проверяется тем путём, каким её получит чужой плагин.
/// </para>
/// </remarks>
public sealed class ProjectsModule : StudioPlugin
{
    /// <summary>Перечитать открытое.</summary>
    public const string ReloadCommand = "projects.reload";

    /// <summary>Закрыть открытое.</summary>
    public const string CloseCommand = "projects.close";

    /// <summary>Восстановить пакеты открытого.</summary>
    public const string RestoreCommand = "projects.restore";

    /// <summary>Собрать открытое.</summary>
    public const string BuildCommand = "projects.build";

    /// <summary>Пересобрать открытое.</summary>
    public const string RebuildCommand = "projects.rebuild";

    /// <summary>Очистить то, что собрано.</summary>
    public const string CleanCommand = "projects.clean";

    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Projects";

    private ProjectsHost? _host;

    /// <summary>Служба, пока модуль поднят.</summary>
    internal ProjectsHost? Host => _host;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var host = new ProjectsHost(context, context.GetService<ProjectsHostOptions>() ?? ProjectsHostOptions.Default);

        _host = host;

        Publish(context, host);

        context.Commands.Register(ReloadCommand, host.ReloadFromCommand);
        context.Commands.Register(CloseCommand, host.CloseFromCommand);
        context.Commands.Register(RestoreCommand, () => host.RunFromCommand(ProjectOperationKind.Restore));
        context.Commands.Register(BuildCommand, () => host.RunFromCommand(ProjectOperationKind.Build));
        context.Commands.Register(RebuildCommand, () => host.RunFromCommand(ProjectOperationKind.Rebuild));
        context.Commands.Register(CleanCommand, () => host.RunFromCommand(ProjectOperationKind.Clean));
    }

    /// <summary>
    /// Модуль выключают — студию закрывают.
    /// </summary>
    /// <remarks>
    /// Остановка не ждёт движка: MSBuild дочитывает начатый проект сколько потребуется, а закрытие
    /// студии ждать его не обязано. Открытое закрывается сразу, движок отпускается очередью.
    /// </remarks>
    public override void Deactivate()
    {
        _host?.Stop();
        _host = null;
    }

    /// <summary>Отдаёт службы соседям.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="host">Служба.</param>
    private static void Publish(IStudioContext context, ProjectsHost host)
    {
        if (context.GetService<IStudioExports>() is not { } exports)
        {
            context.Log.Write(StudioLogLevel.Warning, LogSource,
                "У студии нет обмена реализациями: службы проектов не увидит ни один плагин");

            return;
        }

        if (!exports.Publish<IStudioProjects>(host))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Служба проектов не опубликована: её тип уже занят");

        if (!exports.Publish<IStudioBuild>(host))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Служба сборки не опубликована: её тип уже занят");
    }
}
