using System.Collections.Immutable;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Xaml;

/// <summary>
/// Аренда документа разметки: текст, история, сохранение и показ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Текст — правда.</b> Правка записывается в текст и в историю раньше, чем её покажут: объекты,
/// которые её не смогли показать, остаются при последнем показанном тексте
/// (<see cref="XamlDocumentState.Behind"/>), а правка всё равно в документе. Правят через
/// <see cref="EditAsync"/> — редактором синтаксиса Markup, — а не через объекты: объект — показ текста,
/// и написанное в него не попадёт ни в файл, ни в историю.
/// </para>
/// <para>
/// <b>Аренда, а не документ.</b> Аренд одного документа может быть сколько угодно, и все видят одно и
/// то же. Отпущенная аренда больше ничего не делает; отпущенная последней закрывает документ.
/// Документ, который служба закрыла сама — решение закрыли, служба остановилась, —
/// <see cref="IsClosed"/>: аренда отвечает последним состоянием и правок не принимает.
/// </para>
/// <para>
/// <b>Потоки.</b> Методы зовутся из потока интерфейса, события приходят туда же.
/// </para>
/// </remarks>
public interface IXamlDocumentHandle : IAsyncDisposable
{
    /// <summary>Файл документа; переносят файл — документ идёт за ним.</summary>
    CanonicalPath Path { get; }

    /// <summary>Документ, как он читается сейчас: неизменяемое синтаксическое дерево с текстом.</summary>
    XamlDocument Syntax { get; }

    /// <summary>Что объекты показывают из текста.</summary>
    XamlDocumentState State { get; }

    /// <summary>Что сказали разбор и показ текста.</summary>
    ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

    /// <summary>В документе есть правки, которых нет в файле.</summary>
    bool IsModified { get; }

    /// <summary>
    /// Файл переписали мимо документа поверх его несохранённых правок, и ответа ещё нет —
    /// <see cref="ResolveConflictAsync"/>.
    /// </summary>
    bool HasConflict { get; }

    /// <summary>Файл документа удалён — сам или вместе с папкой; сохранение вернуло бы его на место.</summary>
    bool IsDeleted { get; }

    /// <summary>Документ закрыт службой или аренда отпущена: ни правок, ни сохранения.</summary>
    bool IsClosed { get; }

    /// <summary>Есть что отменить.</summary>
    bool CanUndo { get; }

    /// <summary>Есть что вернуть.</summary>
    bool CanRedo { get; }

    /// <summary>Что отменит отмена — имя шага истории; null — нечего.</summary>
    string? UndoLabel { get; }

    /// <summary>Что вернёт возврат; null — нечего.</summary>
    string? RedoLabel { get; }

    /// <summary>Что-то из сказанного выше сдвинулось — правкой, отменой, записью снаружи, службой.</summary>
    event EventHandler<XamlDocumentChangesEventArgs>? Changed;

    /// <summary>
    /// Файл переписали мимо документа поверх его несохранённых правок. Ничего не принято: решает
    /// человек — <see cref="ResolveConflictAsync"/>.
    /// </summary>
    event EventHandler<XamlExternalConflictEventArgs>? ExternalConflict;

    /// <summary>Правит документ одним шагом истории.</summary>
    /// <param name="label">Имя шага — действие, а не механизм: «Ширина кнопки», а не «SetAttribute».</param>
    /// <param name="edit">
    /// Записывает правку. Зовётся в очереди документа, против текста, каким его оставили операции
    /// до неё, и не обязательно в этом потоке: только записывает и ничего больше не трогает. Элементы
    /// берут у <see cref="XamlDocumentEditor.Document"/> — элемент прежнего разбора редактор отвергнет.
    /// </param>
    /// <param name="cancellationToken">Отмена ожидания очереди; записанная правка показывается всегда.</param>
    /// <returns>Что правка сделала с текстом и с тем, что его показывает. Ничего не записала — текст не двинулся.</returns>
    /// <exception cref="ArgumentException">Имя шага пусто.</exception>
    /// <exception cref="ArgumentNullException">Правки нет.</exception>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task<XamlEditOutcome> EditAsync(string label, Action<XamlDocumentEditor> edit, CancellationToken cancellationToken = default);

    /// <summary>Отменяет последний шаг истории.</summary>
    /// <param name="cancellationToken">Отмена ожидания очереди.</param>
    /// <returns>Что сделала отмена; нечего отменять — текст не двинулся.</returns>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task<XamlEditOutcome> UndoAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает отменённый шаг.</summary>
    /// <param name="cancellationToken">Отмена ожидания очереди.</param>
    /// <returns>Что сделал возврат; нечего возвращать — текст не двинулся.</returns>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task<XamlEditOutcome> RedoAsync(CancellationToken cancellationToken = default);

    /// <summary>Пишет несохранённое в файл — в кодировке, в какой файл прочитан.</summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>Задача, кончающаяся, когда файл записан и документ это знает. Сохранять нечего — сразу.</returns>
    /// <remarks>
    /// Запись сверяется с тем, что документ считает содержимым файла: переписанный мимо него файл не
    /// затирается, а сохранение отказывает. Отказ — исключение, и документ остаётся несохранённым.
    /// </remarks>
    /// <exception cref="IOException">Файл не записан: диск отказал или файл переписан мимо документа.</exception>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Отвечает на <see cref="ExternalConflict"/>.</summary>
    /// <param name="choice">Взять файл или оставить свои правки.</param>
    /// <param name="cancellationToken">Отмена ожидания очереди.</param>
    /// <returns>Задача, кончающаяся, когда ответ принят. Вопроса нет — сразу.</returns>
    /// <remarks>
    /// Взятый файл — шаг истории: отмена вернёт правки. Оставленные правки читаются изменёнными против
    /// нового содержимого файла, и сохранение запишет их поверх него.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task ResolveConflictAsync(XamlConflictChoice choice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Показывает документ: отдаёт корень, построенный из текста в живом поколении, и знает, что за
    /// объект перед ним.
    /// </summary>
    /// <param name="lender">
    /// Тот, кто одалживает части корня, чтобы его показать, — рамка формы, вынимающая содержимое окна, —
    /// и возвращает их на время записи; null — корень не одалживают.
    /// </param>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>Показ; отпускают его <c>Dispose</c>.</returns>
    /// <remarks>
    /// Показ у документа один: корень — один контрол, и стоять в двух деревьях он не может. Пока
    /// прежний показ не отпущен, второй отказывает.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Документ уже показан.</exception>
    /// <exception cref="ObjectDisposedException">Аренда отпущена или документ закрыт.</exception>
    Task<IXamlDesignView> ShowAsync(IXamlRootLender? lender, CancellationToken cancellationToken = default);
}

/// <summary>Что объекты документа показывают из его текста.</summary>
public enum XamlDocumentState
{
    /// <summary>
    /// Ничего не построено: поколения типов нет — его заменяют, или нужен перезапуск, — или документ
    /// никто не показывает.
    /// </summary>
    Detached,

    /// <summary>Объекты показывают текст, каким он читается сейчас.</summary>
    Live,

    /// <summary>
    /// Объекты показывают прежний текст: нынешний не показался — не разбирается, называет тип, которого
    /// нет, — и <see cref="IXamlDocumentHandle.Diagnostics"/> говорит почему.
    /// </summary>
    Behind,

    /// <summary>Ничто не показывает документ: он не загрузился, и прежнего показа, которому можно верить, нет.</summary>
    Broken,
}

/// <summary>Что сдвинулось в документе одной переменой.</summary>
[Flags]
public enum XamlDocumentChanges
{
    /// <summary>Ничего.</summary>
    None = 0,

    /// <summary>Текст читается иначе — правкой, отменой, возвратом или записью снаружи, — а с ним и история.</summary>
    Text = 1,

    /// <summary>Сдвинулось сохранённое или то, отличается ли от него документ.</summary>
    Saved = 2,

    /// <summary>Сдвинулись состояние показа или диагностики.</summary>
    State = 4,

    /// <summary>Файл перенесли, и документ пошёл за ним.</summary>
    Path = 8,

    /// <summary>
    /// Объекты построены заново на месте при неподвижном тексте: кто держал прежний, держит то, что
    /// больше не показано.
    /// </summary>
    Objects = 16,

    /// <summary>Файл документа удалён.</summary>
    Deleted = 32,

    /// <summary>Появился вопрос о чужой записи или снят.</summary>
    Conflict = 64,

    /// <summary>Документ закрыт: решение закрыли или служба остановилась.</summary>
    Closed = 128,
}

/// <summary>Документ сдвинулся.</summary>
/// <param name="changes">Что сдвинулось.</param>
public sealed class XamlDocumentChangesEventArgs(XamlDocumentChanges changes) : EventArgs
{
    /// <summary>Что сдвинулось.</summary>
    public XamlDocumentChanges Changes { get; } = changes;
}

/// <summary>Файл переписали мимо документа поверх его несохранённых правок.</summary>
/// <param name="path">Файл.</param>
/// <param name="diskText">Что в файле теперь.</param>
public sealed class XamlExternalConflictEventArgs(CanonicalPath path, string diskText) : EventArgs
{
    /// <summary>Файл.</summary>
    public CanonicalPath Path { get; } = path;

    /// <summary>Что в файле теперь.</summary>
    public string DiskText { get; } = diskText;
}

/// <summary>Ответ на запись снаружи поверх несохранённого.</summary>
public enum XamlConflictChoice
{
    /// <summary>Взять файл: его текст — шаг истории, отмена вернёт правки.</summary>
    TakeTheirs,

    /// <summary>Оставить свои правки: сохранение запишет их поверх файла.</summary>
    KeepMine,
}

/// <summary>Что операция сделала с текстом документа и с тем, что его показывает.</summary>
/// <param name="TextChanged">Текст сдвинулся — а с ним и история.</param>
/// <param name="State">Что объекты показывают теперь.</param>
/// <param name="Diagnostics">Что говорят разбор и показ теперь.</param>
public sealed record XamlEditOutcome(bool TextChanged, XamlDocumentState State, ImmutableArray<MarkupDiagnostic> Diagnostics);

/// <summary>
/// Тот, кто одалживает части корня, чтобы его показать, и возвращает их, пока сессия пишет в корень.
/// </summary>
/// <remarks>
/// Окно нельзя поставить никуда — верхний уровень ничьим содержимым не бывает. Рамка формы показывает
/// вместо него его содержимое, ресурсы и стили, вынутые из окна; пока они вынуты, окно пусто, и правка,
/// записанная в него, ушла бы в окно, которого никто не видит. Поэтому на время каждой записи корень
/// получает своё обратно, а после неё рамка одалживает заново — уже то, что запись построила.
/// Зовётся в потоке интерфейса и никогда — пока открыта прежняя аренда.
/// </remarks>
public interface IXamlRootLender
{
    /// <summary>Возвращает корню всё одолженное.</summary>
    /// <param name="root">Корень сессии.</param>
    /// <returns>Аренда; её закрытие позволяет одолжить снова.</returns>
    IDisposable Lend(object root);
}
