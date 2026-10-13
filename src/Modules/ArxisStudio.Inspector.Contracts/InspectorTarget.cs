namespace ArxisStudio.Inspector;

/// <summary>Что сменилось у цели.</summary>
[Flags]
public enum InspectorChanges
{
    /// <summary>Ничего.</summary>
    None = 0,

    /// <summary>Значения свойств: правка, отмена, файл переписали снаружи.</summary>
    Values = 1,

    /// <summary>Состав свойств: загрузились типы проекта, сменился родитель.</summary>
    Properties = 2,

    /// <summary>Шапка: имя, значок, заметка.</summary>
    Header = 4,

    /// <summary>Цель закрыта: показывать больше нечего.</summary>
    Closed = 8,
}

/// <summary>Что сменилось у цели и у каких свойств.</summary>
/// <param name="changes">Что сменилось.</param>
/// <param name="ids">Свойства, чьи значения сменились; пусто — все.</param>
public sealed class InspectorChangedEventArgs(InspectorChanges changes, IReadOnlyCollection<string>? ids = null) : EventArgs
{
    /// <summary>Что сменилось.</summary>
    public InspectorChanges Changes { get; } = changes;

    /// <summary>Свойства, чьи значения сменились; пусто — все.</summary>
    public IReadOnlyCollection<string>? Ids { get; } = ids;

    /// <summary>Сменилось значение этого свойства.</summary>
    /// <param name="id">Свойство.</param>
    public bool Touches(string id) =>
        Changes.HasFlag(InspectorChanges.Values) && (Ids is null || Ids.Contains(id));
}

/// <summary>
/// Предпросмотр значения: оно видно на цели, а написано ещё не было — протяжка числа, тяга по спектру цвета.
/// </summary>
/// <remarks>
/// На каждом движении зовётся <see cref="Show"/>, при отпускании — <see cref="CommitAsync"/>, и это одна правка, один
/// шаг истории. <see cref="IDisposable.Dispose"/> без фиксации — отмена: цель возвращается к тому, что было, а
/// написанное не тронуто.
/// </remarks>
public interface IInspectorPreview : IDisposable
{
    /// <summary>Показывает значение на цели.</summary>
    /// <param name="text">Значение текстом.</param>
    /// <returns>Прочлось ли значение; непрочтённое не показывается.</returns>
    InspectorCheck Show(string text);

    /// <summary>Пишет значение одной правкой и кончает предпросмотр.</summary>
    /// <param name="text">Значение текстом.</param>
    /// <returns>Записалось ли.</returns>
    Task<bool> CommitAsync(string text);
}

/// <summary>
/// Цель инспектора: то, что показывают, — элемент разметки, несколько выбранных элементов, объект плагина.
/// </summary>
/// <remarks>
/// <para>
/// Источник — как <c>SerializedObject</c> и <c>Editor</c> Unity вместе: он знает свойства цели, их значения и
/// откуда они пришли, и пишет правки. Инспектор только спрашивает и показывает: объект цели он не держит и
/// сам ничего не меняет. Поэтому цель свободна жить где угодно — в поколении типов проекта, которое заменят,
/// в чужом плагине, в другом процессе: инспектор знает её свойства идентификаторами и типы именами.
/// </para>
/// <para>
/// <b>Одна правка — один шаг истории.</b> <see cref="WriteAsync"/> принимает пачку записей: «сбросить все
/// заданные», вставка нескольких свойств, правка у нескольких выбранных — всё это один шаг, и Ctrl+Z его
/// отменяет целиком. Протяжку инспектор показывает предпросмотром (<see cref="BeginPreview"/>), а пишет
/// одним значением при отпускании.
/// </para>
/// <para>
/// Перемены цель объявляет сама — <see cref="Changed"/>: значения после правки или отмены, состав свойств,
/// шапку, закрытие. Инспектор переспрашивает только показанные строки. Всё зовётся в потоке интерфейса.
/// </para>
/// </remarks>
public abstract class InspectorTarget
{
    /// <summary>Шапка: имя, тип, флажок «включён».</summary>
    public abstract InspectorHeader Header { get; }

    /// <summary>Тип цели — по нему выбираются разделы и метаданные; у нескольких — общий.</summary>
    public abstract InspectorTypeInfo Type { get; }

    /// <summary>Сколько объектов показано: у нескольких значения могут расходиться.</summary>
    public virtual int Count => 1;

    /// <summary>Свойства цели — у нескольких выбранных только общие.</summary>
    public abstract IReadOnlyList<InspectorProperty> Properties { get; }

    /// <summary>
    /// Свойства, заданные у самой цели, — без вопроса о значениях: у элемента разметки это его атрибуты, у
    /// нескольких выбранных — заданные хотя бы у одного.
    /// </summary>
    /// <remarks>
    /// Инспектор считает по ним заданное в заголовках разделов и отбирает «Только заданные», не спрашивая значения
    /// у сотни строк, которых не видно. Пусто — цель так не умеет: отбор спросит значения, а заголовки промолчат.
    /// </remarks>
    public virtual IReadOnlyCollection<string>? WrittenIds => null;

    /// <summary>Закрыта ли цель: показывать больше нечего.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>У цели что-то сменилось.</summary>
    public event EventHandler<InspectorChangedEventArgs>? Changed;

    /// <summary>Значение свойства.</summary>
    /// <param name="id">Свойство.</param>
    public abstract InspectorValue ValueOf(string id);

    /// <summary>Прочтётся ли текст значением свойства — до записи.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="text">Текст.</param>
    public virtual InspectorCheck Check(string id, string text) => InspectorCheck.Valid;

    /// <summary>Пишет значения — одной правкой, одним шагом истории.</summary>
    /// <param name="writes">Записи; пустой текст — сброс к значению по умолчанию.</param>
    /// <param name="label">Как назвать шаг в истории; пусто — назовёт источник.</param>
    /// <returns>Записалось ли.</returns>
    public abstract Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null);

    /// <summary>Начинает предпросмотр значения свойства.</summary>
    /// <param name="id">Свойство.</param>
    /// <returns>Предпросмотр; пусто — показать на ходу нельзя, и протяжка пишет только при отпускании.</returns>
    public virtual IInspectorPreview? BeginPreview(string id) => null;

    /// <summary>Держит цель, пока в поле свойства набрано своё: замена поколения ждёт.</summary>
    /// <param name="id">Свойство.</param>
    /// <returns>Отпускание; пусто — держать нечего.</returns>
    public virtual IDisposable? Hold(string id) => null;

    /// <summary>Шаг истории хозяина: Ctrl+Z и Ctrl+Y в инспекторе, когда в поле ничего не набрано.</summary>
    /// <param name="back">Назад — отменить; вперёд — вернуть.</param>
    /// <returns>Сделан ли шаг.</returns>
    public virtual Task<bool> StepAsync(bool back) => Task.FromResult(false);

    /// <summary>
    /// Просит показывать и свойства, которых нет в обычном составе: метаданные назвали <c>ToolTip.Tip</c> всегда
    /// видимым — цель добавит его, если умеет.
    /// </summary>
    /// <param name="ids">Свойства.</param>
    public virtual void Include(IReadOnlyCollection<string> ids)
    {
    }

    /// <summary>Подсказки к набору: имена ресурсов, шрифты, значения.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="typed">Набранное.</param>
    public virtual IReadOnlyList<InspectorChoice> Suggest(string id, string typed) => [];

    /// <summary>Дополняет меню свойства — или меню всей цели, если свойства нет.</summary>
    /// <param name="menu">Меню.</param>
    public virtual void FillMenu(InspectorMenuContext menu)
    {
    }

    /// <summary>
    /// Какими выражениями свойство задают в панели под строкой: у привязки — «Привязать…» в меню строки, а Enter, F2 и
    /// двойной щелчок по жетону такого выражения открывают панель, а не текст.
    /// </summary>
    /// <param name="id">Свойство.</param>
    /// <returns>Виды выражений; пусто — панели нет, и выражение правят текстом.</returns>
    /// <remarks>Появилось в контракте 1.4.</remarks>
    public virtual IReadOnlyList<InspectorExpressionKind> ExpressionKinds(string id) => [];

    /// <summary>
    /// Строит редактор выражения — содержимое панели под строкой, как окно привязки у Unity. Пишет он сам, своей
    /// правкой, когда панель просит (<see cref="InspectorExpressionEditor.CommitAsync"/>).
    /// </summary>
    /// <param name="id">Свойство.</param>
    /// <param name="kind">Какое выражение правят.</param>
    /// <param name="context">Дорога к панели: о чём сказать под полем, записать, закрыть.</param>
    /// <returns>Редактор; пусто — такого выражения у свойства не правят.</returns>
    /// <remarks>Появилось в контракте 1.4.</remarks>
    public virtual InspectorExpressionEditor? CreateExpressionEditor(string id, InspectorExpressionKind kind, InspectorExpressionContext context) =>
        null;

    /// <summary>Объявляет перемену.</summary>
    /// <param name="changes">Что сменилось.</param>
    /// <param name="ids">Свойства, чьи значения сменились; пусто — все.</param>
    protected void Raise(InspectorChanges changes, IReadOnlyCollection<string>? ids = null)
    {
        if (IsClosed || changes == InspectorChanges.None)
            return;

        Changed?.Invoke(this, new InspectorChangedEventArgs(changes, ids));
    }

    /// <summary>Закрывает цель: инспектор уберёт её и вернётся к прежней.</summary>
    protected void Close()
    {
        if (IsClosed)
            return;

        IsClosed = true;
        Changed?.Invoke(this, new InspectorChangedEventArgs(InspectorChanges.Closed));
    }
}
