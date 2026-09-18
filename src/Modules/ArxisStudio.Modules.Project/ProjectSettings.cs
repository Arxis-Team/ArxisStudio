using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Настройки окна проекта: то, что о нём помнит студия между запусками.
/// </summary>
/// <remarks>
/// Их две, и обе — предпочтения, а не память окна: сколько колонок и какого размера плитки. Раскрытое,
/// выделенное, текущая папка, поиск и положение разделителя — память, и живут они сеанс: контракт
/// прямо говорит, что приватную память плагина в настройки класть не следует.
/// <para>
/// Размер плиток хранится номером ступени, а не пикселями: пиксели — ключи темы, и число в настройках
/// разошлось бы с ними при первой правке темы. Чужое число — из файла, руками — зажимается в ступени.
/// </para>
/// </remarks>
/// <param name="TwoColumns">Две колонки, как в Unity, — или одно дерево.</param>
/// <param name="IconSize">Ступень правой колонки: 0 — список, 1 — плитки, 2 — крупные плитки.</param>
public sealed record ProjectSettings(bool TwoColumns, int IconSize)
{
    /// <summary>Ключ настройки раскладки.</summary>
    public const string TwoColumnsKey = "project.twoColumns";

    /// <summary>Ключ настройки размера плиток.</summary>
    public const string IconSizeKey = "project.iconSize";

    /// <summary>Ступень «список».</summary>
    public const int List = 0;

    /// <summary>Ступень «плитки».</summary>
    public const int Tiles = 1;

    /// <summary>Ступень «крупные плитки».</summary>
    public const int LargeTiles = 2;

    /// <summary>Настройки, пока человек ничего не менял: две колонки, обычные плитки.</summary>
    public static ProjectSettings Default { get; } = new(true, Tiles);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [TwoColumnsKey, IconSizeKey];

    /// <summary>Читает настройки из студии, подставляя умолчания и зажимая ступень.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ProjectSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var size = settings.Get<double?>(IconSizeKey) is { } number && double.IsFinite(number)
            ? (int)Math.Round(number)
            : Default.IconSize;

        return new ProjectSettings(
            settings.Get<bool?>(TwoColumnsKey) ?? Default.TwoColumns,
            Math.Clamp(size, List, LargeTiles));
    }
}
