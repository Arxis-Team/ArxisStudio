using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Inspector;

/// <summary>
/// Точка входа инспектора: служба показа, каталог вкладов и команда «Показать инспектор».
/// </summary>
/// <remarks>
/// <para>
/// Служба публикуется экспортом, как у плагина: дизайнер показывает в ней выбранное на холсте той же дорогой, какой
/// плагин покажет свой объект. Каталог собирает вклады — редакторы, разделы, метаданные, пункты меню — у всех
/// поднятых расширений, и у этого модуля тоже: редакторы по умолчанию и таблица Avalonia — такие же вклады.
/// </para>
/// <para>
/// Подъём дешёвый: каталог спрашивает таблицы метаданных при первом показе цели, а панель строит строки, когда
/// её открыли.
/// </para>
/// </remarks>
public sealed class InspectorModule : StudioPlugin
{
    /// <summary>Команда «Показать инспектор» — F4, как окно свойств Visual Studio.</summary>
    public const string ShowCommand = "inspector.show";

    /// <summary>Панель инспектора.</summary>
    public const string PanelId = "inspector.panel";

    /// <summary>Имя источника в журнале.</summary>
    public const string LogSource = "Inspector";

    private IStudioContext? _context;

    /// <summary>Служба, пока модуль поднят.</summary>
    internal StudioInspector? Service { get; private set; }

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        var contributions = context.GetService<IStudioContributions>();

        Service = new StudioInspector(contributions, new InspectorCatalog(contributions, id => context.Strings["category." + id]));

        context.Commands.Register(ShowCommand, Show);

        if (context.GetService<IStudioExports>() is not { } exports)
        {
            context.Log.Write(StudioLogLevel.Warning, LogSource, "У студии нет обмена реализациями: инспектор не увидит ни один источник");
            return;
        }

        if (!exports.Publish<IStudioInspector>(Service))
            context.Log.Write(StudioLogLevel.Error, LogSource, "Инспектор не опубликован: его тип уже занят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        Service?.Catalog?.Dispose();
        Service = null;
        _context = null;
    }

    private void Show()
    {
        if (_context is null)
            return;

        _context.GetService<IStudioToolWindows>()?.Show(PanelId);
        _context.GetService<IStudioFocus>()?.Focus(PanelId);
    }
}
