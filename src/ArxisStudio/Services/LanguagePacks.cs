using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Services;

/// <summary>
/// Отдаёт студии языки, принесённые установленными плагинами.
/// </summary>
/// <remarks>
/// Место одно на всю студию, и зовут его двое: запуск — выбранный язык может быть языком пакета —
/// и окно настроек перед каждым показом. Язык выбирают там, а пакет могли поставить, включить,
/// выключить или удалить менеджером минуту назад. Звал его прежде Welcome, и настройки, открытые
/// из студии, показывали список, каким он был на запуске.
/// </remarks>
internal static class LanguagePacks
{
    /// <summary>
    /// Пересобирает языки плагинов и ставит их студии.
    /// </summary>
    /// <param name="catalog">Каталог установленных плагинов.</param>
    /// <param name="log">Журнал студии; null — молча.</param>
    /// <remarks>
    /// О занятом коде и потерянном словаре говорится в журнал: пакет
    /// установлен, языка в списке нет — и без такой записи человеку неоткуда
    /// узнать, почему.
    /// </remarks>
    public static void Apply(PluginCatalog catalog, IStudioLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var packs = new PluginLanguages(catalog.Scan());

        foreach (var problem in packs.Problems)
            log?.Write(StudioLogLevel.Warning, "Languages", problem);

        Localizer.Instance.UsePacks(packs);
        PluginStrings.UseTranslations(packs);
    }
}
