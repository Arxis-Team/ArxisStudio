namespace ArxisStudio.Sdk;

/// <summary>
/// Превью файлов: картинка, которой файл показывается на плитке, — от расширения, которое этот вид
/// файлов знает.
/// </summary>
/// <remarks>
/// Берётся через <see cref="IStudioContext.GetService{T}"/>, и выдаётся каждому своя: поставщик
/// записывается на того, кто его поставил, и снимается вместе с выгрузкой своего плагина. Показывает
/// превью тот, у кого плитки, — окно проекта студии или панель плагина; рисует его тот, кто понимает
/// файл, — дизайнер форм отдаёт снимок формы, плагин шрифтов мог бы отдать образец начертания. Так же
/// разделены обработчики эскизов проводника Windows и <c>AssetPreview</c> у Unity: показывающий не знает
/// форматов, а поставщик — плиток.
/// <para>
/// <b>Картинка — закодированная</b>, байтами формата, который читает декодер студии: PNG, JPEG, BMP,
/// ICO, WebP. Растр уменьшает и держит показывающий: у него плитки, их размер и память под них, и
/// растр, отданный поставщиком, жил бы по чужим правилам, — а выгружаемый плагин удерживал бы им чужую
/// плитку.
/// </para>
/// <para>
/// <b>Потоки.</b> Студия зовёт поставщика в потоке интерфейса и через шов сбоев: упавший отвечает
/// пустотой, и падение засчитывается ему. Чтение и кодирование поставщик уводит в фон сам —
/// показывающий просит превью для каждой видимой плитки. <see cref="Changed"/> приходит в потоке
/// интерфейса, откуда бы ни позвали <see cref="Invalidate"/>. Появилось в SDK 7.17.
/// </para>
/// </remarks>
public interface IStudioFilePreviews
{
    /// <summary>
    /// Есть ли превью у такого файла — по расширению, без вызова поставщика.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <remarks>
    /// Нужно показывающему до вопроса: плитку без поставщика он даже не ставит в очередь. Что превью
    /// есть у самого файла, не обещает: у формы, которую ни разу не открывали, снимка ещё нет.
    /// </remarks>
    bool CanPreview(string filePath);

    /// <summary>Превью файла не крупнее <paramref name="pixels"/> по длинной стороне.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="pixels">Длинная сторона в точках экрана, которую покажут; поставщик вправе отдать крупнее.</param>
    /// <param name="cancellationToken">Отмена: плитка ушла из вида.</param>
    /// <returns>Превью; null — поставщика нет, превью у файла нет или поставщик упал.</returns>
    /// <remarks>Отмена — не сбой: поставщику её не засчитывают.</remarks>
    Task<FilePreview?> GetAsync(string filePath, int pixels, CancellationToken cancellationToken = default);

    /// <summary>Ставит поставщика превью для его расширений.</summary>
    /// <param name="provider">Поставщик.</param>
    /// <returns>Запись; <see cref="IDisposable.Dispose"/> снимает поставщика.</returns>
    /// <remarks>
    /// У расширения один поставщик: занятое другим плагином не отдаётся, и студия говорит об этом в
    /// журнал, — как у команд. Своё плагин переставляет и не снимая: новый поставщик вытесняет его
    /// прежнего, а снятие прежнего нового не трогает. С выгрузкой плагина его поставщики и подписки
    /// снимаются сами.
    /// </remarks>
    IDisposable Register(IFilePreviewProvider provider);

    /// <summary>Говорит показывающим, что превью файла сменилось: его надо спросить заново.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <remarks>Зовёт поставщик, когда у него новая картинка: снят новый снимок, файл перечитан.</remarks>
    void Invalidate(string filePath);

    /// <summary>Превью файла сменилось.</summary>
    event EventHandler<FilePreviewChangedEventArgs>? Changed;
}

/// <summary>
/// Поставщик превью: картинка для файлов своих расширений.
/// </summary>
/// <remarks>Ставится через <see cref="IStudioFilePreviews.Register"/>. Появилось в SDK 7.17.</remarks>
public interface IFilePreviewProvider
{
    /// <summary>Расширения файлов, которые он показывает, с точкой: <c>.axaml</c>.</summary>
    /// <remarks>Читаются один раз, при постановке, и сравниваются без учёта регистра.</remarks>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Превью файла.</summary>
    /// <param name="filePath">Путь к файлу одного из <see cref="Extensions"/>.</param>
    /// <param name="pixels">Длинная сторона, которую покажут; отдать можно и крупнее — уменьшит показывающий.</param>
    /// <param name="cancellationToken">Отмена: плитка ушла из вида.</param>
    /// <returns>Превью; null — показать нечего, и плитка остаётся значком.</returns>
    /// <remarks>Зовётся в потоке интерфейса; чтение и кодирование — дело поставщика, и в фоне.</remarks>
    Task<FilePreview?> GetPreviewAsync(string filePath, int pixels, CancellationToken cancellationToken);
}

/// <summary>Превью файла: закодированная картинка и то, отвечает ли она нынешнему файлу.</summary>
/// <param name="Image">Картинка байтами формата, который читает декодер студии: PNG, JPEG, BMP, ICO, WebP.</param>
/// <remarks>Появилось в SDK 7.17.</remarks>
public sealed record FilePreview(ReadOnlyMemory<byte> Image)
{
    /// <summary>
    /// Картинка старше файла: файл менялся после неё. Показывающий показывает её с отметкой, а не прячет —
    /// узнаваемое старое лучше значка.
    /// </summary>
    public bool IsStale { get; init; }
}

/// <summary>Сведения о сменившемся превью.</summary>
/// <param name="filePath">Путь к файлу.</param>
/// <remarks>Появилось в SDK 7.17.</remarks>
public sealed class FilePreviewChangedEventArgs(string filePath) : EventArgs
{
    /// <summary>Путь к файлу, чьё превью сменилось.</summary>
    public string FilePath { get; } = filePath;
}
