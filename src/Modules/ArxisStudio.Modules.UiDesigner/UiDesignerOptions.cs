namespace ArxisStudio.Modules.UiDesigner;

/// <summary>
/// Шов дизайнера: часы и паузы, если не продуктовые.
/// </summary>
/// <remarks>
/// Модуль поднимает студия конструктором без аргументов, поэтому подменить их можно только через
/// контекст: редактор спрашивает этот тип у <c>IStudioContext.GetService</c>, а тест кладёт его в
/// словарь служб. Тип внутренний: плагин его не назовёт, и в продукте ответ всегда null — берутся
/// умолчания.
/// </remarks>
internal sealed class UiDesignerOptions
{
    /// <summary>Продуктовые умолчания.</summary>
    public static UiDesignerOptions Default { get; } = new();

    /// <summary>Часы паузы автосохранения.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Сколько форма ждёт после последней правки, прежде чем сохраниться сама;
    /// <see cref="Timeout.InfiniteTimeSpan"/> — по паузе не сохраняется.
    /// </summary>
    /// <remarks>
    /// Пять секунд, как у IntelliJ: дольше — шире окно, в котором другой редактор того же файла
    /// наткнётся на несохранённое здесь; короче — сохранение посреди серии правок мышью.
    /// </remarks>
    public TimeSpan AutoSaveDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Папка снимков форм; null — по переменной среды <c>ARXIS_PREVIEWS</c> или в машинной папке студии,
    /// <c>0</c> — форм не снимать.
    /// </summary>
    public string? SnapshotsFolder { get; init; }
}
