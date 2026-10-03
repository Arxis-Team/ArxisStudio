using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ArxisStudio.Sdk;

/// <summary>
/// Вид того, что несут мышью: имя и тип значения.
/// </summary>
/// <typeparam name="T">Тип значения.</typeparam>
/// <remarks>
/// Формат — ключ, по которому источник кладёт значение в <see cref="StudioDragData"/>, а цель его
/// достаёт. Имя — с приставкой хозяина, как у команд: <c>figma.frame</c>, а не <c>frame</c>. Форматы
/// самой студии собраны в <see cref="StudioDataFormats"/>.
/// <para>
/// Значение видят обе стороны, а живут они в разных контекстах загрузки, поэтому тип значения должен
/// быть общим: из платформы, из SDK или из контракта, который плагин объявил в
/// <c>provides.contracts</c>. Закрытый тип плагина соседу не назвать, и
/// <see cref="StudioDragData.TryGet{T}"/> его соседу не отдаст.
/// </para>
/// </remarks>
public sealed class StudioDataFormat<T>
    where T : notnull
{
    /// <summary>Заводит формат.</summary>
    /// <param name="id">Имя формата — с приставкой хозяина.</param>
    public StudioDataFormat(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
    }

    /// <summary>Имя формата.</summary>
    public string Id { get; }

    /// <inheritdoc/>
    public override string ToString() => Id;
}

/// <summary>Форматы, которые несёт сама студия.</summary>
public static class StudioDataFormats
{
    /// <summary>
    /// Файлы и каталоги — абсолютными путями, первым — тот, за который взялись.
    /// </summary>
    /// <remarks>
    /// Так несёт окно проекта: всё, что взяла бы его правка, — вложенное вместе с владельцем, — и первым
    /// тот, за который взялись, как в подписи у курсора: цель, раскладывающая несомое от точки
    /// отпускания, ставит под курсор его. Каталог приходит своим путём, а не перечнем файлов внутри: что с
    /// ним делать, решает цель.
    /// </remarks>
    public static StudioDataFormat<IReadOnlyList<string>> Files { get; } = new("arxis.files");
}

/// <summary>
/// Что несут мышью: значения по форматам.
/// </summary>
/// <remarks>
/// Данные неизменяемы: каждая цель видит то, что положил источник, и ни одна не поправит их для
/// следующей. Значений бывает несколько — один и тот же выбор можно нести и путями, и своим видом для
/// соседа, — и цель берёт тот формат, который понимает.
/// </remarks>
public sealed class StudioDragData
{
    private readonly Dictionary<string, object> _values;

    /// <summary>Пустые данные; значения кладёт <see cref="With{T}"/>.</summary>
    public StudioDragData()
        : this(new Dictionary<string, object>(StringComparer.Ordinal))
    {
    }

    private StudioDragData(Dictionary<string, object> values) => _values = values;

    /// <summary>Имена форматов, значения которых есть в данных.</summary>
    public IReadOnlyCollection<string> Formats => _values.Keys;

    /// <summary>Файлы и каталоги (<see cref="StudioDataFormats.Files"/>); их не несут — пусто.</summary>
    public IReadOnlyList<string> Files => TryGet(StudioDataFormats.Files, out var files) ? files : [];

    /// <summary>
    /// Данные с файлами и каталогами.
    /// </summary>
    /// <param name="paths">Абсолютные пути в порядке выбора.</param>
    /// <exception cref="ArgumentException">Путь пустой или не абсолютный.</exception>
    /// <remarks>
    /// Путь от текущей папки процесса значил бы у цели не то, что у источника, поэтому относительный
    /// отвергается сразу, а не превращается у цели в чужой файл.
    /// </remarks>
    public static StudioDragData FromFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var list = paths.ToArray();

        foreach (var path in list)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException($"путь должен быть абсолютным: «{path}»", nameof(paths));
        }

        return new StudioDragData().With<IReadOnlyList<string>>(StudioDataFormats.Files, Array.AsReadOnly(list));
    }

    /// <summary>
    /// Те же данные и ещё одно значение.
    /// </summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="format">Формат значения.</param>
    /// <param name="value">Значение.</param>
    /// <returns>Новые данные; эти остаются как были. Значение того же формата заменяется.</returns>
    public StudioDragData With<T>(StudioDataFormat<T> format, T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(value);

        return new StudioDragData(new Dictionary<string, object>(_values, StringComparer.Ordinal) { [format.Id] = value });
    }

    /// <summary>Есть ли значение этого формата нужного типа.</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="format">Формат.</param>
    public bool Contains<T>(StudioDataFormat<T> format)
        where T : notnull => TryGet(format, out _);

    /// <summary>
    /// Достаёт значение формата.
    /// </summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="format">Формат.</param>
    /// <param name="value">Значение; не нашлось — значение по умолчанию.</param>
    /// <returns>Нашлось ли значение этого формата нужного типа.</returns>
    public bool TryGet<T>(StudioDataFormat<T> format, [MaybeNullWhen(false)] out T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(format);

        if (_values.TryGetValue(format.Id, out var stored) && stored is T typed)
        {
            value = typed;

            return true;
        }

        value = default;

        return false;
    }
}

/// <summary>
/// Что видно у курсора, пока несут: значок и подпись.
/// </summary>
/// <remarks>
/// Рисует студия — одной подсказкой у всех источников и над любым её окном, а не только над тем, где
/// тягу начали. Подпись — то, за что взялись: имя первого и сколько ещё, как у окна проекта. Под ней
/// студия пишет подсказку цели (<see cref="StudioDragEventArgs.Hint"/>) — что сделает отпускание.
/// </remarks>
public sealed class StudioDragVisual
{
    /// <summary>Заводит вид несомого.</summary>
    /// <param name="label">Подпись у курсора.</param>
    public StudioDragVisual(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = label;
    }

    /// <summary>Подпись у курсора.</summary>
    public string Label { get; }

    /// <summary>Значок перед подписью — контур набора <c>AxIcons</c>; пусто — без значка.</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Кисть значка; пусто — вторичный цвет подписи, как у значка строки.</summary>
    public IBrush? IconBrush { get; init; }
}

/// <summary>
/// Событие цели перетаскивания: что несут, что разрешил источник и что цель ответит.
/// </summary>
/// <remarks>
/// <see cref="Effect"/> и <see cref="Hint"/> — ответ цели. В <see cref="StudioDragDrop.DragEnterEvent"/>
/// и <see cref="StudioDragDrop.DragOverEvent"/> он начинается заново, с отказа: цель, которая забыла
/// ответить, отказывает, а не принимает всё подряд. В <see cref="StudioDragDrop.DropEvent"/> он
/// начинается с последнего ответа <c>DragOver</c>, и итог <c>Drop</c> источник получает ответом на тягу.
/// <para>
/// Эффект — ровно один из разрешённых (<see cref="AllowedEffects"/>) или <see cref="DragDropEffects.None"/>.
/// Неразрешённый или составной студия считает отказом и пишет об этом в журнал.
/// </para>
/// </remarks>
public sealed class StudioDragEventArgs : RoutedEventArgs
{
    private readonly Visual _root;
    private readonly Point _position;

    /// <summary>Заводит событие цели: студии — для маршрута, автору цели — для её тестов.</summary>
    /// <param name="routedEvent">Какое событие: одно из событий <see cref="StudioDragDrop"/>.</param>
    /// <param name="data">Что несут.</param>
    /// <param name="allowedEffects">Что разрешил источник.</param>
    /// <param name="root">Окно, над которым курсор.</param>
    /// <param name="position">Курсор в координатах окна.</param>
    /// <param name="keyModifiers">Зажатые клавиши.</param>
    public StudioDragEventArgs(
        RoutedEvent<StudioDragEventArgs> routedEvent,
        StudioDragData data,
        DragDropEffects allowedEffects,
        Visual root,
        Point position,
        KeyModifiers keyModifiers)
        : base(routedEvent)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(root);

        Data = data;
        AllowedEffects = allowedEffects;
        KeyModifiers = keyModifiers;
        _root = root;
        _position = position;
    }

    /// <summary>Что несут.</summary>
    public StudioDragData Data { get; }

    /// <summary>
    /// Что разрешил источник.
    /// </summary>
    /// <remarks>
    /// Окно проекта разрешает копию и ссылку, но не перенос: перенос значил бы, что источник удалит
    /// свой файл, когда цель его заберёт, а файлы решения правит только служба файлов.
    /// </remarks>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>Зажатые клавиши: Ctrl, Shift, Alt.</summary>
    public KeyModifiers KeyModifiers { get; }

    /// <summary>Что сделает отпускание над этой целью; <see cref="DragDropEffects.None"/> — отказ.</summary>
    public DragDropEffects Effect { get; set; }

    /// <summary>
    /// Подсказка у курсора: что сделает отпускание — или почему здесь нельзя.
    /// </summary>
    /// <remarks>
    /// Короткая фраза словами человека: «Вернуть «Main.axaml» на доску», «Только формы .axaml». Без
    /// неё курсор говорит «можно» или «нельзя», но не говорит, что именно будет и почему нет.
    /// </remarks>
    public string? Hint { get; set; }

    /// <summary>
    /// Курсор в координатах элемента.
    /// </summary>
    /// <param name="relativeTo">Элемент того же окна.</param>
    /// <exception cref="ArgumentException">Элемент не в окне, над которым курсор.</exception>
    public Point GetPosition(Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);

        return _root.TranslatePoint(_position, relativeTo)
            ?? throw new ArgumentException("элемент не в том окне, над которым курсор", nameof(relativeTo));
    }
}

/// <summary>
/// Цель перетаскивания внутри студии: свойство, которым элемент её объявляет, и события, которыми она
/// отвечает.
/// </summary>
/// <remarks>
/// <para>
/// <b>Цель.</b> Элемент с <see cref="AllowDropProperty"/> — и только он: свойство не наследуется, и
/// цель — ближайший к курсору элемент, объявивший себя целью, а не его дети. Переход курсора между
/// детьми одной цели поэтому не дёргает её <c>DragLeave</c> и <c>DragEnter</c>. События поднимаются
/// на самой цели и всплывают, как у <see cref="DragDrop"/> Avalonia; обработчик, ответивший за цель,
/// ставит <see cref="RoutedEventArgs.Handled"/>, и внешняя цель его ответ не перепишет.
/// </para>
/// <para>
/// <b>Порядок.</b> Курсор пришёл на цель — <c>DragEnter</c> и сразу <c>DragOver</c>; движется —
/// <c>DragOver</c>; ушёл, тягу бросили или отпустили — <c>DragLeave</c>. Отпустили над целью —
/// <c>Drop</c>, а за ним тоже <c>DragLeave</c>: отметку и заготовку цель убирает в одном месте.
/// Отвечает цель в <c>DragOver</c> — каждый раз заново (<see cref="StudioDragEventArgs"/>), —
/// <c>DragEnter</c> нужен, чтобы разобрать данные один раз.
/// </para>
/// <para>
/// <b>Это не системная тяга.</b> Тягу между панелями ведёт студия — захватом указателя, как вкладки
/// докинга, — а не <see cref="DragDrop"/> платформы: несут только между окнами студии, и мышь, которой
/// водят инструменты студии и тесты, ведёт её так же, как настоящая. Файлы из проводника приходят
/// событиями <see cref="DragDrop"/>, как и прежде.
/// </para>
/// <para>
/// Падение обработчика цели студию не роняет: цель выпадает из этой тяги, а плагин получает сбой так
/// же, как за всякое необработанное исключение.
/// </para>
/// </remarks>
public static class StudioDragDrop
{
    /// <summary>
    /// Сколько пройти мышью с нажатой кнопкой, чтобы это было тягой, а не щелчком.
    /// </summary>
    /// <remarks>Порог вкладок докинга и окна проекта: щелчок почти всегда сдвигает руку на пиксель-другой.</remarks>
    public const double Threshold = 6;

    /// <summary>Объявляет элемент целью перетаскивания внутри студии; не наследуется.</summary>
    public static readonly AttachedProperty<bool> AllowDropProperty =
        AvaloniaProperty.RegisterAttached<Interactive, bool>("AllowDrop", typeof(StudioDragDrop));

    /// <summary>Курсор с несомым пришёл на цель.</summary>
    public static readonly RoutedEvent<StudioDragEventArgs> DragEnterEvent =
        RoutedEvent.Register<StudioDragEventArgs>("DragEnter", RoutingStrategies.Bubble, typeof(StudioDragDrop));

    /// <summary>Курсор с несомым движется над целью или сменились зажатые клавиши.</summary>
    public static readonly RoutedEvent<StudioDragEventArgs> DragOverEvent =
        RoutedEvent.Register<StudioDragEventArgs>("DragOver", RoutingStrategies.Bubble, typeof(StudioDragDrop));

    /// <summary>Цель больше не под курсором: ушли, бросили или уже отпустили.</summary>
    public static readonly RoutedEvent<StudioDragEventArgs> DragLeaveEvent =
        RoutedEvent.Register<StudioDragEventArgs>("DragLeave", RoutingStrategies.Bubble, typeof(StudioDragDrop));

    /// <summary>Несомое отпустили над целью.</summary>
    public static readonly RoutedEvent<StudioDragEventArgs> DropEvent =
        RoutedEvent.Register<StudioDragEventArgs>("Drop", RoutingStrategies.Bubble, typeof(StudioDragDrop));

    /// <summary>Цель ли элемент.</summary>
    /// <param name="element">Элемент.</param>
    public static bool GetAllowDrop(Interactive element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.GetValue(AllowDropProperty);
    }

    /// <summary>Объявляет элемент целью или снимает объявление.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="value">Цель ли он.</param>
    public static void SetAllowDrop(Interactive element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(AllowDropProperty, value);
    }

    /// <summary>Подписывает на <see cref="DragEnterEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void AddDragEnterHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Subscribe(element, DragEnterEvent, handler);

    /// <summary>Отписывает от <see cref="DragEnterEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void RemoveDragEnterHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Unsubscribe(element, DragEnterEvent, handler);

    /// <summary>Подписывает на <see cref="DragOverEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void AddDragOverHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Subscribe(element, DragOverEvent, handler);

    /// <summary>Отписывает от <see cref="DragOverEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void RemoveDragOverHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Unsubscribe(element, DragOverEvent, handler);

    /// <summary>Подписывает на <see cref="DragLeaveEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void AddDragLeaveHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Subscribe(element, DragLeaveEvent, handler);

    /// <summary>Отписывает от <see cref="DragLeaveEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void RemoveDragLeaveHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Unsubscribe(element, DragLeaveEvent, handler);

    /// <summary>Подписывает на <see cref="DropEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void AddDropHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Subscribe(element, DropEvent, handler);

    /// <summary>Отписывает от <see cref="DropEvent"/>.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="handler">Обработчик.</param>
    public static void RemoveDropHandler(Interactive element, EventHandler<StudioDragEventArgs> handler) =>
        Unsubscribe(element, DropEvent, handler);

    private static void Subscribe(Interactive element, RoutedEvent<StudioDragEventArgs> routedEvent, EventHandler<StudioDragEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);

        element.AddHandler(routedEvent, handler);
    }

    private static void Unsubscribe(Interactive element, RoutedEvent<StudioDragEventArgs> routedEvent, EventHandler<StudioDragEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);

        element.RemoveHandler(routedEvent, handler);
    }
}

/// <summary>
/// Перетаскивание между панелями студии — со стороны того, кто несёт.
/// </summary>
/// <remarks>
/// <para>
/// Достаётся через <see cref="IStudioContext.GetService{T}"/>. Тягу ведёт студия: подсказка у курсора
/// над любым её окном, курсор по ответу цели, Esc и потеря захвата бросают тягу. Источнику остаются
/// жест и ответ на него: начать тягу, когда мышь с нажатой кнопкой ушла дальше
/// <see cref="StudioDragDrop.Threshold"/>, и сделать своё по итогу — например, удалить перенесённое.
/// </para>
/// <para>
/// Дорог две. <see cref="DragAsync"/> — студия ведёт указатель сама, от первого движения до отпускания;
/// это дорога почти любого источника. <see cref="Begin"/> — сеанс для источника, у которого свой цикл
/// указателя и свои цели внутри: окно проекта носит файлы между своими каталогами само, а за его край —
/// через сеанс.
/// </para>
/// <para>
/// Тяга одна на студию: новая бросает прежнюю, как бросило бы её отпускание кнопки.
/// </para>
/// </remarks>
public interface IStudioDragDrop
{
    /// <summary>
    /// Несёт данные от элемента до отпускания.
    /// </summary>
    /// <param name="source">Элемент, за который взялись: он держит указатель, пока несут.</param>
    /// <param name="trigger">Движение указателя, на котором жест стал тягой.</param>
    /// <param name="data">Что несут.</param>
    /// <param name="allowedEffects">Что источник разрешает цели.</param>
    /// <param name="visual">Что видно у курсора.</param>
    /// <returns>Ответ цели на отпускание; бросили или отказали — <see cref="DragDropEffects.None"/>.</returns>
    /// <remarks>
    /// Зовётся из обработчика движения указателя. Задача кончается, когда кнопку отпустили, нажали Esc
    /// или захват потерян.
    /// </remarks>
    Task<DragDropEffects> DragAsync(
        Control source,
        PointerEventArgs trigger,
        StudioDragData data,
        DragDropEffects allowedEffects,
        StudioDragVisual visual);

    /// <summary>
    /// Открывает сеанс для источника со своим циклом указателя.
    /// </summary>
    /// <param name="source">Элемент, держащий указатель: курсор тяги ставится ему.</param>
    /// <param name="data">Что несут.</param>
    /// <param name="allowedEffects">Что источник разрешает чужим целям.</param>
    /// <param name="visual">Что видно у курсора.</param>
    /// <returns>Сеанс; закрывает его <see cref="IStudioDragSession.Drop"/> или <see cref="IDisposable.Dispose"/>.</returns>
    /// <remarks>Захват указателя, Esc и движение — дело источника: сеанс только отвечает, что под курсором.</remarks>
    IStudioDragSession Begin(Control source, StudioDragData data, DragDropEffects allowedEffects, StudioDragVisual visual);
}

/// <summary>
/// Сеанс перетаскивания у источника со своим циклом указателя.
/// </summary>
/// <remarks>
/// Источник сообщает каждое движение: над чужим — <see cref="Over"/>, и сеанс находит цель и спрашивает
/// её; над своим — <see cref="OverOwn"/>, и источник отвечает сам, а сеанс только ведёт подсказку и
/// курсор. Отпустили над чужой целью — <see cref="Drop"/>; над своей или бросили — <see cref="IDisposable.Dispose"/>.
/// Закрытый сеанс на движения не отвечает и возвращает отказ.
/// </remarks>
public interface IStudioDragSession : IDisposable
{
    /// <summary>Что несут.</summary>
    StudioDragData Data { get; }

    /// <summary>Что источник разрешил чужим целям.</summary>
    DragDropEffects AllowedEffects { get; }

    /// <summary>Что сделает отпускание сейчас.</summary>
    DragDropEffects Effect { get; }

    /// <summary>Подсказка у курсора сейчас; пусто — её нет.</summary>
    string? Hint { get; }

    /// <summary>Сеанс открыт: его не отпустили, не бросили и не сменила новая тяга.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Что под курсором: верхний элемент, ловящий мышь, в верхнем окне студии.
    /// </summary>
    /// <param name="position">Курсор в координатах источника.</param>
    /// <returns>Элемент; курсор вне окон студии — пусто.</returns>
    /// <remarks>
    /// По этому ответу источник решает, над своим ли он: попадание в собственное окно не видит
    /// оторванного окна поверх него и нашло бы свою цель под чужой панелью.
    /// </remarks>
    Visual? ElementAt(Point position);

    /// <summary>
    /// Курсор над чужим: найти цель и спросить её.
    /// </summary>
    /// <param name="position">Курсор в координатах источника.</param>
    /// <param name="keyModifiers">Зажатые клавиши.</param>
    /// <returns>Ответ цели; цели нет или она отказала — <see cref="DragDropEffects.None"/>.</returns>
    DragDropEffects Over(Point position, KeyModifiers keyModifiers);

    /// <summary>
    /// Курсор над своим: отвечает источник, а прежняя чужая цель получает <c>DragLeave</c>.
    /// </summary>
    /// <param name="position">Курсор в координатах источника.</param>
    /// <param name="effect">Что сделает отпускание — решение источника, а не разрешение чужим.</param>
    /// <param name="hint">Подсказка у курсора; пусто — без неё.</param>
    void OverOwn(Point position, DragDropEffects effect, string? hint = null);

    /// <summary>
    /// Отпускает несомое над целью последнего <see cref="Over"/> и закрывает сеанс.
    /// </summary>
    /// <returns>
    /// Ответ цели на отпускание; цели не было, последним был <see cref="OverOwn"/> или цель отказала —
    /// <see cref="DragDropEffects.None"/>.
    /// </returns>
    DragDropEffects Drop();
}
