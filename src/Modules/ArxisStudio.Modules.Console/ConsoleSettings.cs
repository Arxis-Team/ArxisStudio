using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console;

/// <summary>
/// Настройки консоли: то, что о ней помнит студия между запусками.
/// </summary>
/// <remarks>
/// Их ровно две, и обе — предпочтения, а не память панели. Границу проводит
/// сам контракт: «приватную память плагина — последнюю открытую вкладку, кэш,
/// размер панели — в настройки класть не следует».
/// <para>
/// Поэтому уровни, поиск, выбранный источник, свёртка и видимость подробностей
/// сюда не попали: это отбор, а отбор, переживший перезапуск, прятал бы
/// сегодняшние записи молча — человек открыл бы консоль, не увидел ошибки и
/// решил, что её не было.
/// </para>
/// </remarks>
/// <param name="Autoscroll">Следовать ли за хвостом журнала.</param>
/// <param name="Timestamps">Показывать ли столбец времени.</param>
public sealed record ConsoleSettings(bool Autoscroll, bool Timestamps)
{
    /// <summary>Ключ настройки следования за хвостом.</summary>
    public const string AutoscrollKey = "console.autoscroll";

    /// <summary>Ключ настройки показа времени.</summary>
    public const string TimestampsKey = "console.timestamps";

    /// <summary>Настройки, пока человек ничего не менял.</summary>
    public static ConsoleSettings Default { get; } = new(true, true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [AutoscrollKey, TimestampsKey];

    /// <summary>Читает настройки из студии, подставляя умолчания.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ConsoleSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ConsoleSettings(
            settings.Get<bool?>(AutoscrollKey) ?? Default.Autoscroll,
            settings.Get<bool?>(TimestampsKey) ?? Default.Timestamps);
    }

    /// <summary>Записывает настройки в студию.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public void Write(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Set(AutoscrollKey, Autoscroll);
        settings.Set(TimestampsKey, Timestamps);
    }
}
