using ArxisStudio.Extensibility;
using ArxisStudio.Shell;
using Avalonia.Media;

namespace ArxisStudio.Services;

/// <summary>Пункт меню студии.</summary>
/// <param name="Title">Что написано в пункте.</param>
/// <param name="PluginId">Плагин, которому принадлежит команда; null у ветки.</param>
/// <param name="CommandId">Команда, которую вызывает пункт; null у ветки.</param>
public sealed record StudioMenuItem(string Title, string? PluginId = null, string? CommandId = null)
{
    /// <summary>Вложенные пункты.</summary>
    public List<StudioMenuItem> Children { get; } = [];

    /// <summary>Значок команды из её объявления; null — у ветки и у команды без значка.</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Пункт вызывает команду, а не раскрывает подменю.</summary>
    public bool IsCommand => CommandId is not null;
}

/// <summary>
/// Собирает меню студии из манифестов установленных плагинов.
/// </summary>
/// <remarks>
/// Меню строится по манифестам, а не по поднятым плагинам: сборка плагина может
/// быть ещё не загружена, и требовать её загрузки ради строчки в меню — значит
/// поднимать при старте всё, что установлено, то есть отменить смысл событий
/// активации.
/// <para>
/// Названия пунктов переводятся здесь же, и ветки сходятся по переведённому
/// тексту: два плагина, назвавшие ветку каждый своим ключом, должны оказаться в
/// одном «Инструменты», а не в двух одинаковых с виду.
/// </para>
/// <para>
/// Встроенные модули идут первыми: своё выше принесённого. Иначе порядок
/// пунктов зависел бы от того, что человек успел установить, и знакомое меню
/// перестраивалось бы после каждой установки.
/// </para>
/// <para>
/// Дерево собирается заново на каждом открытии: меню полосы показывает его, а палитра команд и
/// страница клавиш берут из него свои строки. Список вкладывающихся меняется от подъёма к подъёму,
/// и дерево, запомненное однажды, показывало бы уже выключенных.
/// </para>
/// </remarks>
public static class StudioMenu
{
    /// <summary>
    /// Собирает дерево меню.
    /// </summary>
    /// <param name="plugins">Установленные плагины.</param>
    /// <returns>Ветки верхнего уровня; пусто, если никто ничего не добавил.</returns>
    public static IReadOnlyList<StudioMenuItem> Build(IEnumerable<InstalledPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var roots = new List<StudioMenuItem>();

        var contributing = plugins
            .Where(candidate => candidate is { IsEnabled: true, IsValid: true })
            .OrderByDescending(candidate => candidate.IsBuiltIn)
            .ToList();

        foreach (var plugin in contributing)
        {
            foreach (var declared in plugin.Manifest!.Contributions.Menus)
            {
                var segments = Segments(declared.Path, plugin.Strings);

                if (segments.Length == 0)
                    continue;

                // Значок — у объявления команды, а объявить её мог и сосед: пункт
                // зовёт команду по имени, и реестр у студии один. Своё объявление
                // при этом старше соседского.
                var icon = IconOf(plugin, declared.Command)
                    ?? contributing
                        .Select(other => IconOf(other, declared.Command))
                        .FirstOrDefault(found => found is not null);

                Insert(roots, segments, plugin.Id, declared.Command, icon);
            }
        }

        return roots;
    }

    /// <summary>
    /// Путь меню по сегментам, переведённый словарями хозяина.
    /// </summary>
    /// <param name="path">Путь через косую — как его пишет манифест; null — пустой.</param>
    /// <param name="strings">Словари хозяина пути.</param>
    /// <remarks>
    /// Путь режется до перевода, а переводится посегментно: ключ разделителя не содержит, а
    /// переведённая строка вполне может — и «Файл/Открыть», пришедшее из словаря, развалило бы путь.
    /// Правило одно у меню, у меню кнопки полосы и у пунктов «Добавить ▸».
    /// </remarks>
    internal static string[] Segments(string? path, PluginStrings strings) =>
    [
        .. (path ?? string.Empty)
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(strings.Resolve),
    ];

    /// <summary>
    /// Значок команды — по её объявлению в манифесте этого расширения.
    /// </summary>
    /// <param name="plugin">Чей манифест; null — элемент самой студии, объявлений у неё нет.</param>
    /// <param name="commandId">Какая команда.</param>
    /// <returns>Геометрия глифа; null — команда здесь не объявлена, значка у неё нет или он не разобрался.</returns>
    /// <remarks>
    /// Молча: о значке, который не разобрался, журнал слышит один раз — когда манифест читают. Здесь
    /// же спрашивают на каждом открытии меню.
    /// </remarks>
    internal static Geometry? IconOf(InstalledPlugin? plugin, string? commandId) =>
        plugin?.Manifest?.Contributions.Commands
            .FirstOrDefault(command => string.Equals(command.Id, commandId, StringComparison.Ordinal))
            ?.Icon is { } icon
            ? ManifestIcons.Resolve(icon, out _)
            : null;

    private static void Insert(
        List<StudioMenuItem> level, string[] segments, string pluginId, string commandId, Geometry? icon)
    {
        for (var depth = 0; depth < segments.Length; depth++)
        {
            var last = depth == segments.Length - 1;
            var title = segments[depth];

            // Ветку с таким названием переиспользуем: два плагина, добавивших
            // «Tools/…», должны оказаться в одном «Tools», а не в двух.
            var existing = level.FirstOrDefault(item => item.Title == title && item.IsCommand == last);

            if (existing is null)
            {
                existing = last
                    ? new StudioMenuItem(title, pluginId, commandId) { Icon = icon }
                    : new StudioMenuItem(title);

                level.Add(existing);
            }

            level = existing.Children;
        }
    }
}
