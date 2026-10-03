using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml;

/// <summary>
/// Точка входа службы XAML: публикует службы.
/// </summary>
/// <remarks>
/// Подъём дешёвый: ни профиля дизайна, ни поколения типов при нём нет. Они поднимаются первым
/// открытым документом решения — оценка и загрузка стоят секунд, а модули поднимаются задолго до того,
/// как человек что-то открыл, и многие так и не откроют ни одной формы.
/// <para>
/// Служб три, и публикуются они экспортом, как у плагина: документы, поколение и типы для формы. Другой
/// дороги к ним у соседей нет — дизайнер форм берёт их тем путём, каким их получит чужой плагин.
/// </para>
/// </remarks>
public sealed class XamlModule : StudioPlugin
{
    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Xaml";

    private XamlService? _service;

    /// <summary>Служба, пока модуль поднят.</summary>
    internal XamlService? Service => _service;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = new XamlService(context, context.GetService<XamlServiceOptions>() ?? XamlServiceOptions.Default);

        _service = service;

        Publish(context, service);
    }

    /// <summary>
    /// Модуль выключают — студию закрывают.
    /// </summary>
    /// <remarks>
    /// Остановка не ждёт поколения: выгрузка контекста и сборка, начатая дизайнером, закончатся сами,
    /// а закрытие студии ждать их не обязано. Аренды документов, которые ещё держат вкладки, узнают о
    /// закрытии и отвечают последним состоянием.
    /// </remarks>
    public override void Deactivate()
    {
        _service?.Stop();
        _service = null;
    }

    /// <summary>Отдаёт службы соседям.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="service">Служба.</param>
    private static void Publish(IStudioContext context, XamlService service)
    {
        if (context.GetService<IStudioExports>() is not { } exports)
        {
            context.Log.Write(StudioLogLevel.Warning, LogSource,
                "У студии нет обмена реализациями: службы XAML не увидит ни один плагин");

            return;
        }

        if (!exports.Publish<IStudioXamlDocuments>(service))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Служба документов XAML не опубликована: её тип уже занят");

        if (!exports.Publish<IStudioXamlDesign>(service))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Служба поколения XAML не опубликована: её тип уже занят");

        if (!exports.Publish<IStudioXamlTypes>(service))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Служба типов XAML не опубликована: её тип уже занят");
    }
}
