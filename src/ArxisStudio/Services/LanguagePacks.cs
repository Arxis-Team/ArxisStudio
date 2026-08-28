using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Services;

/// <summary>
/// Отдаёт студии языки, принесённые установленными плагинами.
/// </summary>
/// <remarks>
/// Место одно на всю студию: языки пересобираются и при запуске, и после
/// всякого действия менеджера плагинов — установили пакет, включили,
/// выключили, удалили. Разведи это по вызовам, и рано или поздно один из них
/// забудут: язык остался бы в списке после удаления пакета.
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
    }
}
