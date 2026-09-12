namespace ArxisStudio.Sdk;

/// <summary>
/// Версия контракта, по которой студия и плагин узнают друг друга.
/// </summary>
/// <remarks>
/// Плагин объявляет в манифесте, какая версия ему нужна (<c>sdk.min</c>), и
/// студия сверяет её с этой — до того, как что-нибудь загрузит. Плагин, которому
/// нужен SDK новее, поднимать нельзя: он будет звать то, чего в этой студии
/// ещё нет, и падать не там, где ошибся автор, а там, куда он не заглядывал.
/// <para>
/// Старший номер меняется, когда контракт перестаёт быть совместимым:
/// убранный метод, изменившаяся сигнатура. Младший — когда в него что-то
/// добавили: плагин, написанный под 1.0, работает в студии с SDK 1.3, обратное
/// неверно.
/// </para>
/// </remarks>
public static class StudioSdk
{
    /// <summary>Версия контракта этой студии.</summary>
    /// <remarks>
    /// 4.2 — <see cref="IStudioContext.ProjectPath"/> стал живым: он отвечает на
    /// открытие и закрытие проекта, а не остаётся тем, чем был при подъёме плагина. Младший номер,
    /// потому что обещание добавлено, а не снято: плагин, читавший путь один раз, в этой студии
    /// работает как работал, просто теперь его значение бывает верным дольше.
    /// <para>
    /// 4.1 — добавлены теги манифеста (<c>tags</c>).
    /// </para>
    /// </remarks>
    public const string Version = "4.2";

    /// <summary>
    /// Довольна ли эта версия SDK тем, что просит плагин.
    /// </summary>
    /// <param name="required">Что объявлено в манифесте; пусто — «любая».</param>
    /// <returns><c>true</c>, если плагин можно поднимать.</returns>
    /// <remarks>
    /// Неразобранная строка считается подходящей: манифест пишет человек, и
    /// отказать ему из-за опечатки в номере значило бы не запустить рабочий
    /// плагин по ничтожному поводу. О самой опечатке скажет проверка манифеста.
    /// </remarks>
    public static bool Satisfies(string? required)
    {
        if (!TryParse(required, out var wanted))
            return true;

        TryParse(Version, out var have);

        // Сравниваем по старшему, а при равенстве — по младшему: плагин,
        // написанный под 1.0, работает в студии с 1.3, обратное неверно.
        return have.Major != wanted.Major
            ? have.Major > wanted.Major
            : have.Minor >= wanted.Minor;
    }

    private static bool TryParse(string? version, out (int Major, int Minor) parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || !int.TryParse(parts[0], out var major))
            return false;

        var minor = parts.Length > 1 && int.TryParse(parts[1], out var found) ? found : 0;

        parsed = (major, minor);
        return true;
    }
}
