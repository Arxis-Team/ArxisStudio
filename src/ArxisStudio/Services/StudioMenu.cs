using ArxisStudio.Extensibility;

namespace ArxisStudio.Services;

/// <summary>Пункт меню студии.</summary>
/// <param name="Title">Что написано в пункте.</param>
/// <param name="PluginId">Плагин, которому принадлежит команда; null у ветки.</param>
/// <param name="CommandId">Команда, которую вызывает пункт; null у ветки.</param>
public sealed record StudioMenuItem(string Title, string? PluginId = null, string? CommandId = null)
{
    /// <summary>Вложенные пункты.</summary>
    public List<StudioMenuItem> Children { get; } = [];

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

        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
        {
            foreach (var declared in plugin.Manifest!.Contributions.Menus)
            {
                // Путь режется до перевода, а переводится посегментно: ключ
                // разделителя не содержит, а переведённая строка вполне может —
                // и «Файл/Открыть», пришедшее из словаря, развалило бы путь.
                var segments = declared.Path
                    .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(plugin.Strings.Resolve)
                    .ToArray();

                if (segments.Length == 0)
                    continue;

                Insert(roots, segments, plugin.Id, declared.Command);
            }
        }

        return roots;
    }

    private static void Insert(List<StudioMenuItem> level, string[] segments, string pluginId, string commandId)
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
                    ? new StudioMenuItem(title, pluginId, commandId)
                    : new StudioMenuItem(title);

                level.Add(existing);
            }

            level = existing.Children;
        }
    }
}
