using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Настройки окна проекта: то, что о нём помнит студия между запусками.
/// </summary>
/// <remarks>
/// Их три, и все — предпочтения, а не память окна: сколько колонок, какого размера плитки и показывают
/// ли плитки картинок саму картинку. Раскрытое, выделенное, текущая папка, поиск и положение
/// разделителя — память, и живут они сеанс: контракт прямо говорит, что приватную память плагина в
/// настройки класть не следует.
/// <para>
/// Размер плиток хранится точками силуэта, а не номером ступени: ступеней теперь лестница из темы, и
/// номер съезжал бы с каждой её правкой, а размер встаёт на ближайшую ступень. На ступень его ставит
/// окно — лестницу знает тема, а не настройки. Пусто — человек размера не выбирал, и окно берёт
/// обычную ступень темы.
/// </para>
/// </remarks>
/// <param name="TwoColumns">Две колонки, как в Unity, — или одно дерево.</param>
/// <param name="IconSize">Размер силуэта плиток в точках, ноль — список; пусто — обычная ступень.</param>
/// <param name="Previews">Плитка картинки показывает саму картинку, а не силуэт документа.</param>
public sealed record ProjectSettings(bool TwoColumns, double? IconSize, bool Previews)
{
    /// <summary>Ключ настройки раскладки.</summary>
    public const string TwoColumnsKey = "project.twoColumns";

    /// <summary>Ключ настройки размера плиток.</summary>
    public const string IconSizeKey = "project.iconSize";

    /// <summary>Ключ выключателя превью картинок.</summary>
    public const string PreviewsKey = "project.previews";

    /// <summary>Размер «список»: правая колонка строками, а не плитками.</summary>
    public const double List = 0;

    /// <summary>Настройки, пока человек ничего не менял: две колонки, обычные плитки, превью включены.</summary>
    /// <remarks>
    /// Превью включены, как в Unity: картинку в папке ищут глазами, и силуэт на её месте — это
    /// лишнее открытие файла. Умолчание повторено в манифесте: без него окно настроек показало бы
    /// выключатель выключенным, а окно вело бы себя как включённое.
    /// </remarks>
    public static ProjectSettings Default { get; } = new(true, null, true);

    /// <summary>Все ключи, которые модуль объявляет в манифесте.</summary>
    public static IReadOnlyList<string> Keys { get; } = [TwoColumnsKey, IconSizeKey, PreviewsKey];

    /// <summary>Правая колонка — строками.</summary>
    public bool ShowsList => IconSize is <= List;

    /// <summary>Читает настройки из студии, подставляя умолчания.</summary>
    /// <param name="settings">Настройки модуля.</param>
    public static ProjectSettings Read(IStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var size = settings.Get<double?>(IconSizeKey) is { } number && double.IsFinite(number) ? number : Default.IconSize;

        return new ProjectSettings(
            settings.Get<bool?>(TwoColumnsKey) ?? Default.TwoColumns,
            size,
            settings.Get<bool?>(PreviewsKey) ?? Default.Previews);
    }
}
