using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;

namespace ArxisStudio.Services;

/// <summary>
/// Что расширение объявило манифестом: кнопки и меню полосы, сочетания команд, значки.
/// </summary>
/// <remarks>
/// Всё это студия ставит, не загружая сборки: спящий плагин получает кнопки и сочетания здесь и
/// только здесь, а щелчок или нажатие будят его через реестр команд. Замечания о записях манифеста
/// звучат тут же — один раз на чтение, а не на каждую перестройку меню.
/// <para>
/// Вынесено из <see cref="StudioPlugins"/>: служба решает, когда объявленное ставить — на старте и
/// после каскада, — а как, решается здесь.
/// </para>
/// </remarks>
/// <param name="log">Журнал студии.</param>
/// <param name="toolbar">Полоса студии.</param>
/// <param name="shortcuts">Реестр сочетаний; null — сочетания манифестов не раздаются.</param>
internal sealed class DeclaredContributions(IStudioLog log, StudioToolBar toolbar, StudioShortcuts? shortcuts)
{
    private readonly IStudioLog _log = log;
    private readonly StudioToolBar _toolbar = toolbar;
    private readonly StudioShortcuts? _shortcuts = shortcuts;

    /// <summary>
    /// Ставит в полосу всё, что объявлено манифестами, — не поднимая никого.
    /// </summary>
    /// <remarks>
    /// Кнопка и меню сборки не требуют: студия рисует их сама, а щелчок будит
    /// хозяина через реестр команд. Свой контрол здесь только занимает место —
    /// придёт он, когда плагин поднимут.
    /// </remarks>
    /// <param name="plugins">Чьи манифесты читать; выключенные и сломанные пропускаются.</param>
    public void Mount(IEnumerable<InstalledPlugin> plugins)
    {
        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
        {
            foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
                _toolbar.Add(plugin, declared);

            foreach (var declared in plugin.Manifest.Contributions.Commands)
                Glyph(plugin, $"команда {declared.Id}", declared.Icon);

            foreach (var declared in plugin.Manifest.Contributions.ToolWindows)
                Glyph(plugin, $"панель {declared.Id}", declared.Icon);

            // Пункты «Добавить ▸» собираются на каждом открытии меню, и о том, что в них не
            // читается, говорится здесь — тем же правилом, что о значках.
            foreach (var complaint in StudioNewItems.Complaints(plugin))
                _log.Write(StudioLogLevel.Warning, "Plugins", $"{plugin.DisplayName}: {complaint}");
        }
    }

    /// <summary>
    /// Заявляет сочетания, объявленные манифестом расширения.
    /// </summary>
    /// <param name="plugin">Чей манифест.</param>
    /// <remarks>
    /// Отдельно от кнопок, потому что живут они по-разному. Кнопку ставят до подъёма и снимают с
    /// экрана; сочетание записано на хозяина, и уход хозяина стирает его вместе с остальными его
    /// записями. Заявка — ровно одна на жизнь хозяина: повторную реестр считает спором за занятое
    /// и отказывает хозяину в его же сочетании. Поэтому дорог две и они не пересекаются — старт
    /// для всех объявивших и каскад для поднятых заново.
    /// </remarks>
    public void Claim(InstalledPlugin plugin)
    {
        foreach (var declared in plugin.Manifest?.Contributions.Commands ?? [])
            Give(plugin, declared);
    }

    /// <summary>
    /// Говорит в журнал о значке панели или команды, который не разобрался.
    /// </summary>
    /// <param name="plugin">Чей манифест.</param>
    /// <param name="what">Чей значок — словами, для журнала.</param>
    /// <param name="icon">Запись из манифеста.</param>
    /// <remarks>
    /// Здесь, а не там, где значок рисуют. Рисуют его на каждой перестройке — меню и палитра
    /// собираются на каждом открытии, вкладка встаёт при каждом подъёме, — и замечание звучало бы
    /// столько же раз. Манифест же читается здесь, и сказать о нём один раз на чтение честнее.
    /// Значок, который не разобрался, ничего не отменяет: пункт, строка и вкладка встают без него.
    /// </remarks>
    private void Glyph(InstalledPlugin plugin, string what, string? icon)
    {
        ManifestIcons.Resolve(icon, out var problem);

        if (problem is not null)
            _log.Write(StudioLogLevel.Warning, "Plugins", $"{plugin.DisplayName}: {what} — {problem}");
    }

    /// <summary>
    /// Отдаёт команде сочетание, объявленное манифестом.
    /// </summary>
    /// <param name="plugin">Чья это команда.</param>
    /// <param name="declared">Объявление команды.</param>
    /// <remarks>
    /// Отказ не молчит: занятое сочетание второму не достаётся, и проигравший
    /// обязан узнать имя победителя — иначе «моё сочетание не работает» не
    /// имеет ответа. Сама команда при этом остаётся доступна из палитры.
    /// </remarks>
    private void Give(InstalledPlugin plugin, PluginCommand declared)
    {
        if (_shortcuts is not { } keys || declared.Key is not { Length: > 0 } gesture)
            return;

        if (keys.Bind(gesture, declared.Id, plugin.Id))
            return;

        var winner = keys.Refused
            .LastOrDefault(refusal => string.Equals(refusal.CommandId, declared.Id, StringComparison.Ordinal))
            ?.Winner;

        _log.Write(StudioLogLevel.Warning, "Keys", winner is null
            ? $"{plugin.DisplayName}: сочетание «{gesture}» не разобралось — команда {declared.Id} осталась без него"
            : $"{plugin.DisplayName}: сочетание «{gesture}» занято командой {winner} — {declared.Id} осталась без него");
    }
}
