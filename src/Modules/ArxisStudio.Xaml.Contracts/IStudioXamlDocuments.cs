using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Xaml;

/// <summary>
/// Документы разметки открытого решения — живые: у каждого своя история правок, свой текст и объекты,
/// построенные из текста в поколении типов проекта.
/// </summary>
/// <remarks>
/// <para>
/// <b>Документ один на файл.</b> <see cref="OpenAsync"/> отдаёт аренду, и документ живёт, пока не
/// отпущена последняя: вкладка формы, иерархия и плагин, открывшие один файл, правят один текст с
/// одной историей и одной отметкой «не сохранено». Отпущенный последним документ закрывается, и
/// несохранённое уходит вместе с ним — сохраняет тот, кто решает о закрытии.
/// </para>
/// <para>
/// <b>Пишет служба файлов.</b> Сохранение идёт через <c>IStudioFiles.WriteAsync</c> со сверкой
/// ожидаемого: файл, переписанный мимо документа, не затирается, а запись попадает в локальную историю
/// действием сохранения. Чужая запись на диск приходит в документ шагом его истории; поверх
/// несохранённого — вопросом (<see cref="IXamlDocumentHandle.ExternalConflict"/>).
/// </para>
/// <para>
/// <b>Потоки.</b> Методы зовутся из потока интерфейса, события приходят туда же.
/// </para>
/// </remarks>
public interface IStudioXamlDocuments
{
    /// <summary>
    /// Открывает документ — или берёт ещё одну аренду уже открытого.
    /// </summary>
    /// <param name="path">Файл разметки, который проект открытого решения объявляет своим.</param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>Аренда; отпускают её <c>DisposeAsync</c>.</returns>
    /// <remarks>
    /// Первое открытие в решении поднимает поколение его типов: оценку дизайна, сборку того, что
    /// устарело, и загрузку собранного, — поэтому оно долгое, а следующие нет. Состояние поколения
    /// говорит <see cref="IStudioXamlDesign"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">Путь пуст, это не разметка, или его не объявляет ни один проект решения.</exception>
    /// <exception cref="InvalidOperationException">
    /// Решение не открыто, служба остановлена, или поколение не поднялось — причину называет сообщение и
    /// <see cref="IStudioXamlDesign.StateReason"/>.
    /// </exception>
    /// <exception cref="IOException">Файл не прочёлся.</exception>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    Task<IXamlDocumentHandle> OpenAsync(CanonicalPath path, CancellationToken cancellationToken = default);

    /// <summary>Что за разметка в файле — по его корню, без сборки и без сессии.</summary>
    /// <param name="path">Файл.</param>
    /// <returns>
    /// Вид; у открытого документа — по его тексту, а не по файлу. Файл, которого нет, который не
    /// читается или не XML, — <see cref="XamlFileKind.Unknown"/>.
    /// </returns>
    /// <remarks>
    /// Читает один корневой элемент, но читает с диска: много файлов подряд спрашивают не из потока
    /// интерфейса.
    /// </remarks>
    XamlFileKind Classify(CanonicalPath path);
}

/// <summary>Что объявляет корень файла разметки.</summary>
/// <remarks>
/// Узнаётся по имени корня, а не по типу: тип живёт в сборке проекта, которую для ответа никто не
/// грузит. Поэтому свой наследник <c>Window</c> с другим именем — <see cref="Control"/>, а не окно.
/// </remarks>
public enum XamlFileKind
{
    /// <summary>Не разметка: файла нет, он не читается или не XML.</summary>
    Unknown,

    /// <summary>Окно: корень называется <c>…Window</c>.</summary>
    Window,

    /// <summary>Пользовательский элемент: корень <c>…UserControl</c>.</summary>
    UserControl,

    /// <summary>Другой контрол в корне — шаблонный, панель.</summary>
    Control,

    /// <summary>Приложение: корень <c>Application</c>.</summary>
    Application,

    /// <summary>Стили: корень <c>Styles</c> или <c>Style</c>.</summary>
    Styles,

    /// <summary>Словарь ресурсов: корень <c>ResourceDictionary</c>.</summary>
    Resources,
}
