using ArxisStudio.Projects;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Projects;

/// <summary>
/// Точка входа службы проектов: публикует службу и заявляет команды.
/// </summary>
/// <remarks>
/// Интерфейса у модуля нет, и подъём его дешёвый: MSBuild при нём не трогается. Движок
/// регистрируется при первом открытии и не раньше — регистрация меняет окружение всего процесса,
/// а модули поднимаются задолго до того, как человек что-то открыл.
/// <para>
/// Служба публикуется экспортом, как у плагина: другой дороги к ней у соседей нет, и у самой
/// студии тоже не будет — работа с проектами проверяется тем путём, каким её получит чужой плагин.
/// </para>
/// </remarks>
public sealed class ProjectsModule : StudioPlugin
{
    /// <summary>Перечитать открытое.</summary>
    public const string ReloadCommand = "projects.reload";

    /// <summary>Закрыть открытое.</summary>
    public const string CloseCommand = "projects.close";

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

        if (context.GetService<IStudioExports>() is not { } exports)
        {
            context.Log.Write(StudioLogLevel.Warning, LogSource,
                "У студии нет обмена реализациями: службу проектов не увидит ни один плагин");
        }
        else if (!exports.Publish<IStudioProjects>(host))
        {
            context.Log.Write(StudioLogLevel.Error, LogSource,
                "Служба проектов не опубликована: её тип уже занят");
        }

        context.Commands.Register(ReloadCommand, host.ReloadFromCommand);
        context.Commands.Register(CloseCommand, host.CloseFromCommand);
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
}
