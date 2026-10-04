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

    /// <summary>
    /// Сколько тихо должно быть после последней просьбы о превью, прежде чем снимать форму в фоне.
    /// </summary>
    /// <remarks>
    /// Просьбы идут, пока человек листает плитки, а снимок занимает поток интерфейса на время постройки
    /// формы — десятки и сотни миллисекунд. Снимать посреди прокрутки значило бы дёргать её; полсекунды
    /// тишины — прокрутка кончилась.
    /// </remarks>
    public TimeSpan SnapshotQuiet { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Сколько фоновый снимок ждёт корня формы после того, как служба показала документ.
    /// </summary>
    /// <remarks>
    /// Корень приходит, когда поколение типов проекта живое; первое поднимается сборкой дизайна, и это
    /// секунды, — но не минута: дольше значит, что поколение не встанет, и форма остаётся значком.
    /// </remarks>
    public TimeSpan SnapshotRootWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Сколько фоновый снимок ждёт приложения формы после её корня.
    /// </summary>
    /// <remarks>
    /// Приложение служба строит следом за корнем, обычно за доли секунды. Приложения может и не быть — тогда
    /// служба молчит, — поэтому ждут его не дольше этого: форма без своего приложения снимается после паузы.
    /// </remarks>
    public TimeSpan SnapshotApplicationWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Что ждёт фоновый снимок, взяв показ формы, — до раскладки; null — ничего. Тестам: застать снимок
    /// с показом в руках.
    /// </summary>
    public Func<CancellationToken, Task>? SnapshotShown { get; init; }
}
