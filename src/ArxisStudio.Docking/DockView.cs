using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ArxisStudio.Docking;

/// <summary>
/// Потянутая граница: чьё это деление и какими стали доли.
/// </summary>
/// <param name="Path">Путь к делению от корня — номера детей сверху вниз.</param>
/// <param name="Weights">Новые доли по числу детей.</param>
/// <remarks>
/// Деление адресуется путём, а не именем: имя есть только у группы. Заводить
/// имя и делению значило бы придумывать его на каждое перетаскивание и хранить
/// в файле то, на что никто не ссылается.
/// </remarks>
public sealed record DockResize(IReadOnlyList<int> Path, IReadOnlyList<double> Weights);

/// <summary>
/// Вкладка в пути или отпущенная: что несут и где сейчас курсор.
/// </summary>
/// <param name="Item">Имя панели.</param>
/// <param name="At">Точка на экране.</param>
/// <remarks>
/// Точка в пикселях экрана, а не окна, и в этом всё перетаскивание между
/// окнами: пока кнопка нажата, движения приходят окну, начавшему тягу, даже
/// когда курсор давно над чужим. Своё дерево оно разберёт само, а какое из
/// окон под курсором — знает лишь тот, у кого этих окон несколько.
/// </remarks>
public sealed record DockDrag(string Item, PixelPoint At);

/// <summary>
/// Дерево раскладки на экране.
/// </summary>
/// <remarks>
/// Вид ничего не решает: он показывает дерево, которое ему дали, и сообщает о
/// том, что сделал человек. Правку дерева делает владелец — так одно и то же
/// дерево одинаково ведёт себя в главном окне, в оторванном окне и в тесте, где
/// окна нет вовсе.
/// </remarks>
public class DockView : Decorator
{
    /// <summary>
    /// Сколько пикселей надо пройти, чтобы это считалось тягой, а не щелчком.
    /// </summary>
    private const double Threshold = 6;

    /// <summary>
    /// Насколько глубоко от края тянется зона «раздели».
    /// </summary>
    /// <remarks>
    /// Треть — как в Unity; там это промерено проходом курсора, и цифра сошлась
    /// с расчётом до пикселя. Глубина считается в долях своей стороны, поэтому
    /// у широкой низкой области верхняя зона выходит шире боковой — иначе угол
    /// доставался бы не тому краю.
    /// </remarks>
    private const double Third = 1.0 / 3;

    /// <summary>Дерево, которое показываем.</summary>
    public static readonly StyledProperty<DockNode?> RootProperty =
        AvaloniaProperty.Register<DockView, DockNode?>(nameof(Root));

    /// <summary>Где брать живые панели.</summary>
    public static readonly StyledProperty<DockItems?> ItemsProperty =
        AvaloniaProperty.Register<DockView, DockItems?>(nameof(Items));

    /// <summary>
    /// Что показать там, где показывать нечего.
    /// </summary>
    /// <remarks>
    /// Достаётся одной названной группе, а не всем пустым: родитель у контрола
    /// ровно один, и одна и та же заставка в двух местах кончилась бы
    /// исключением.
    /// </remarks>
    public static readonly StyledProperty<object?> EmptyProperty =
        AvaloniaProperty.Register<DockView, object?>(nameof(Empty));

    /// <summary>
    /// Имя группы, которая показывается даже пустой.
    /// </summary>
    /// <remarks>
    /// Остальные пустые группы не показываются вовсе — но и не пропадают из
    /// дерева. Разница видна на выключенном плагине: имена его панелей остаются
    /// на своих местах, места на экране не занимают, и стоит плагин включить,
    /// как панель возвращается туда же, где стояла, той же ширины.
    /// </remarks>
    public static readonly StyledProperty<string?> EmptyGroupProperty =
        AvaloniaProperty.Register<DockView, string?>(nameof(EmptyGroup));

    /// <summary>
    /// Виды групп по именам — их переносим, а не создаём заново.
    /// </summary>
    /// <remarks>
    /// Пересоздание вида группы стоило бы дороже, чем кажется: панель внутри
    /// потеряла бы прокрутку, выделение и всё, что контрол помнит о себе сам.
    /// </remarks>
    private readonly Dictionary<string, DockGroupView> _groups = new(StringComparer.Ordinal);

    /// <summary>Вкладка, на которой нажали, и где нажали.</summary>
    private (string Item, Point At)? _pressed;

    /// <summary>Что тащат прямо сейчас.</summary>
    private string? _dragged;

    /// <summary>Подсказка: место, которое займёт брошенная вкладка.</summary>
    private Border? _hint;

    /// <summary>
    /// Что положить в правый край шапки угловой группы; null — ничего.
    /// </summary>
    /// <remarks>
    /// Сюда оторванное окно кладёт свои кнопки: полоса вкладок и есть его
    /// заголовок. Отдельная полоса поверх неё стояла бы пустой и съедала бы
    /// четверть невысокого окна ради трёх кнопок.
    /// <para>
    /// Не контрол, а способ его сделать. Родитель у контрола Avalonia ровно
    /// один, а угол переезжает: группу могло не стать на экране и завести
    /// заново. Один и тот же контрол попросили бы тогда в две шапки разом, и
    /// это исключение.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<Func<Control>?> ActionsProperty =
        AvaloniaProperty.Register<DockView, Func<Control>?>(nameof(Actions));

    /// <summary>
    /// Прячется ли панель этого дерева кнопкой в шапке своей группы.
    /// </summary>
    /// <remarks>
    /// У оторванного окна такой кнопки нет, и это не значит, что его панели не
    /// спрятать: «скрыть» у него стоит в шапке самого окна и убирает всё, что в
    /// нём лежит. Две кнопки рядом, делающие одно, — не выбор для человека, а
    /// недосмотр.
    /// <para>
    /// Флаг живёт на виде, а не выводится из окна: одно и то же дерево обязано
    /// вести себя одинаково в главном окне, в оторванном и в тесте, где окна
    /// нет вовсе, — а спросить <see cref="DockFloat"/> значило бы это правило
    /// нарушить.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<bool> HideableProperty =
        AvaloniaProperty.Register<DockView, bool>(nameof(Hideable), defaultValue: true);

    /// <summary>
    /// Подпись кнопки «вернуть в главное окно».
    /// </summary>
    /// <remarks>
    /// Нужна одному <see cref="DockFloat"/>, а живёт здесь, рядом с
    /// <see cref="HideTitleProperty"/>: подписи кнопок дока приходят из студии,
    /// и место у них одно — иначе оболочке пришлось бы помнить, какие из них
    /// ставить виду, а какие окну.
    /// </remarks>
    public static readonly StyledProperty<string?> DockTitleProperty =
        AvaloniaProperty.Register<DockView, string?>(nameof(DockTitle));

    /// <summary>
    /// Подпись кнопки «скрыть панель».
    /// </summary>
    /// <remarks>
    /// Одна на две кнопки: в шапке группы и в шапке оторванного окна. Дело у
    /// них одно, и разными словами называть его незачем.
    /// <para>
    /// Текст приходит снаружи: движок докинга не знает ни о языках студии, ни
    /// о её словарях, и знать не должен — он живёт отдельной библиотекой.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<string?> HideTitleProperty =
        AvaloniaProperty.Register<DockView, string?>(nameof(HideTitle));

    /// <summary>
    /// Имена панелей, которые убраны с глаз.
    /// </summary>
    /// <remarks>
    /// Убранная панель ведёт себя как панель выключенного плагина: места на
    /// экране не занимает, а имя её остаётся в дереве — там же, где стояло, и с
    /// той же долей. Оттуда она и возвращается: показать её снова значит убрать
    /// имя из этого набора, а не искать ей новый дом.
    /// <para>
    /// Набор, а не признак у панели: убранность — часть раскладки студии, она
    /// переживает перезапуск и лежит в её файле. Движку докинга принадлежит
    /// показ, а не решение о том, что человек спрятал.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<IReadOnlySet<string>?> HiddenProperty =
        AvaloniaProperty.Register<DockView, IReadOnlySet<string>?>(nameof(Hidden));

    /// <summary>
    /// Имена, которые скрытию не подлежат.
    /// </summary>
    /// <remarks>
    /// У студии это открытые документы: за ними стоят файлы, и закрывать их —
    /// дело того, кто их открыл. Движку докинга про файлы знать нечего, поэтому
    /// имена он получает списком, как и убранные.
    /// <para>
    /// Нужно это одной кнопке: группе, в которой скрывать нечего, кнопка
    /// «скрыть» не достаётся. Иначе она стояла бы в шапке и не делала ничего —
    /// а кнопка, которая не работает, хуже отсутствующей.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<IReadOnlySet<string>?> FixedProperty =
        AvaloniaProperty.Register<DockView, IReadOnlySet<string>?>(nameof(Fixed));

    /// <summary>Черта в полосе вкладок: у неё вкладка и встанет.</summary>
    private Border? _caret;

    static DockView()
    {
        RootProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        HideableProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        HiddenProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        FixedProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ItemsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyGroupProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ActionsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Hang());

        // Подписи раздаются значением, а не привязкой: привязка на каждую
        // перекладку оставляла бы за собой подписку на вид группы, а живой вид
        // держит контрол панели — и плагин, которого выключили, не выгрузился
        // бы никогда.
        HideTitleProperty.Changed.AddClassHandler<DockView>((view, _) => view.Retitle());
    }

    /// <summary>Заводит вид и подписывается на мышь.</summary>
    /// <remarks>
    /// Обработчики перехватывающие: вкладка забирает нажатие себе — ей надо
    /// стать выбранной, — и до всплытия дело не дойдёт. Само нажатие мы при
    /// этом не помечаем разобранным: щелчок обязан работать как щелчок, пока
    /// человек не потянул.
    /// <para>
    /// Только перехватывающие, не оба маршрута разом: вид лежит и на пути
    /// вниз, и на пути вверх, и подписка на оба поднимала бы каждое движение
    /// дважды. Пока за движением ничего тяжёлого не стояло, это было незаметно;
    /// с предпросмотром — уже нет.
    /// </para>
    /// </remarks>
    public DockView()
    {
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>
    /// Человек взялся за пустое место шапки; в поле — само нажатие.
    /// </summary>
    /// <remarks>
    /// Там, где у окна ручка. Оторванному окну она нужна: своей полосы
    /// заголовка у него нет, и не будь этой, окно нельзя было бы ни подвинуть,
    /// ни развернуть. Главное окно на это событие не подписано — его двигают за
    /// собственную полосу.
    /// </remarks>
    public event EventHandler<PointerPressedEventArgs>? Grabbed;

    /// <summary>Человек потянул границу.</summary>
    public event EventHandler<DockResize>? Resized;

    /// <summary>Вкладку несут; в поле — она и точка экрана под курсором.</summary>
    public event EventHandler<DockDrag>? Dragging;

    /// <summary>Вкладку отпустили; в поле — она и точка экрана.</summary>
    /// <remarks>
    /// Куда она попадёт, вид не решает: он видит одно своё дерево, а окон у
    /// студии несколько, и брошенная мимо всех — это отрыв в новое окно.
    /// </remarks>
    public event EventHandler<DockDrag>? Dropped;

    /// <summary>Человек попросил скрыть группу; в поле — её имя.</summary>
    /// <remarks>
    /// Как и с размером, вид только просит: какие панели лежат в группе и куда
    /// они уходят, знает студия — она же держит список скрытых и она же его
    /// сохраняет. Имя группы, а не панели: кнопка стоит в шапке группы, и
    /// человек, нажавший её, убирает то, на что смотрит.
    /// </remarks>
    public event EventHandler<string>? Hiding;

    /// <summary>Человек попросил закрыть панель; в поле — её имя.</summary>
    public event EventHandler<string>? Closing;

    /// <summary>Тяга кончилась ничем: захват потерян или вид ушёл с экрана.</summary>
    /// <remarks>
    /// Про бросок говорит <see cref="Dropped"/>, а это — про оборванную тягу.
    /// Своё показанное вид убирает сам, но показывал не он один: призрак окна
    /// живёт отдельным окном, и убрать его некому, кроме того, кто его завёл.
    /// </remarks>
    public event EventHandler? Stopped;

    /// <inheritdoc cref="RootProperty"/>
    public DockNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    /// <inheritdoc cref="ItemsProperty"/>
    public DockItems? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <inheritdoc cref="EmptyProperty"/>
    public object? Empty
    {
        get => GetValue(EmptyProperty);
        set => SetValue(EmptyProperty, value);
    }

    /// <inheritdoc cref="ActionsProperty"/>
    public Func<Control>? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <inheritdoc cref="DockTitleProperty"/>
    public string? DockTitle
    {
        get => GetValue(DockTitleProperty);
        set => SetValue(DockTitleProperty, value);
    }

    /// <inheritdoc cref="HideTitleProperty"/>
    public string? HideTitle
    {
        get => GetValue(HideTitleProperty);
        set => SetValue(HideTitleProperty, value);
    }

    /// <inheritdoc cref="FixedProperty"/>
    public IReadOnlySet<string>? Fixed
    {
        get => GetValue(FixedProperty);
        set => SetValue(FixedProperty, value);
    }

    /// <inheritdoc cref="HiddenProperty"/>
    public IReadOnlySet<string>? Hidden
    {
        get => GetValue(HiddenProperty);
        set => SetValue(HiddenProperty, value);
    }

    /// <inheritdoc cref="HideableProperty"/>
    public bool Hideable
    {
        get => GetValue(HideableProperty);
        set => SetValue(HideableProperty, value);
    }

    /// <inheritdoc cref="EmptyGroupProperty"/>
    public string? EmptyGroup
    {
        get => GetValue(EmptyGroupProperty);
        set => SetValue(EmptyGroupProperty, value);
    }

    /// <summary>Показанный вид группы; null — такой на экране нет.</summary>
    /// <param name="groupId">Имя группы.</param>
    public DockGroupView? View(string groupId) =>
        _groups.TryGetValue(groupId, out var view) ? view : null;

    /// <summary>
    /// Строит экран заново по тому же дереву.
    /// </summary>
    /// <remarks>
    /// Нужно, когда изменилось не дерево, а то, что стоит за именами: панель
    /// выключенного плагина вернулась на своё место, и дерево этого не заметило
    /// — оно и не менялось.
    /// </remarks>
    public void Refresh() => Rebuild();

    /// <summary>
    /// Отнятый захват заканчивает тягу.
    /// </summary>
    /// <remarks>
    /// Захват отнимают чужое окно, Alt+Tab, всплывшее модальное окно. Без этого
    /// тяга осталась бы взведённой: подсветка висела бы на экране, а следующее
    /// движение мыши таскало бы вкладку с отпущенной кнопкой.
    /// <para>
    /// Проверять, кто именно потерял захват, не нужно: событие направленное и
    /// приходит только тому, у кого захват и был. Отделять чужую потерю от
    /// своей было бы охраной от того, чего не бывает.
    /// </para>
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _pressed = null;

        Stop();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Подсветка живёт не в нас, а в слое поверх окна: уходя, забираем её с
        // собой, иначе она осталась бы висеть над пустым местом.
        Stop();
    }

    /// <summary>Запоминает вкладку, на которой нажали.</summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if ((e.Source as Visual)?.FindAncestorOfType<AxTabItem>() is { } tab)
        {
            if (tab.FindAncestorOfType<DockGroupView>()?.Item(tab) is { } item)
                _pressed = (item, e.GetPosition(this));

            return;
        }

        // Пустое место шапки — ручка окна. Кнопки из неё исключены: щелчок по
        // кнопке обязан работать как щелчок по кнопке, а не двигать окно.
        if (e.Source is Visual source
            && source.FindAncestorOfType<Button>() is null
            && source.FindAncestorOfType<DockGroupView>() is { } group
            && e.GetPosition(group).Y < group.HeaderHeight)
        {
            Grabbed?.Invoke(this, e);
        }
    }

    /// <summary>Начинает тягу, когда её уже не спутать со щелчком, и ведёт её.</summary>
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);

        if (_dragged is null && _pressed is { } pressed)
        {
            // Порог нужен, чтобы дрогнувшая рука не растаскивала раскладку:
            // щелчок по вкладке почти всегда сдвигает мышь на пиксель-другой.
            if (Math.Abs(point.X - pressed.At.X) < Threshold
                && Math.Abs(point.Y - pressed.At.Y) < Threshold)
            {
                return;
            }

            _dragged = pressed.Item;
            _pressed = null;

            e.Pointer.Capture(this);
        }

        if (_dragged is { } carried && Screen(point) is { } at)
            Dragging?.Invoke(this, new DockDrag(carried, at));
    }

    /// <summary>Сообщает, что вкладку отпустили и где.</summary>
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragged;
        var at = Screen(e.GetPosition(this));

        _pressed = null;
        _dragged = null;

        if (dragged is null)
            return;

        // Сперва о броске, потом отпустить захват. Отпускание синхронно
        // поднимает «захват потерян», а тот заканчивает тягу и забывает
        // разметку прицела — сделай мы это раньше, спрашивать «куда бросили»
        // было бы уже не по чему, и вкладка улетала бы в своё окно.
        if (at is { } where)
            Dropped?.Invoke(this, new DockDrag(dragged, where));

        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Точка в пикселях экрана; null — вид не на экране.
    /// </summary>
    /// <remarks>
    /// Окно должно быть не просто найдено, а показано: закрытое окно у вида
    /// ещё числится, но экранных пикселей у него уже нет, и перевод в них
    /// кончается исключением. Закрываются же окна прямо посреди тяги —
    /// опустевшее оторванное закрывает себя само.
    /// </remarks>
    private PixelPoint? Screen(Point point) =>
        TopLevel.GetTopLevel(this) is { IsVisible: true } top && this.TranslatePoint(point, top) is { } local
            ? top.PointToScreen(local)
            : null;

    /// <summary>Точка экрана в координатах этого вида; null — вид не на экране.</summary>
    private Point? Local(PixelPoint at) =>
        TopLevel.GetTopLevel(this) is { IsVisible: true } top
            ? top.TranslatePoint(top.PointToClient(at), this)
            : null;

    /// <summary>
    /// Куда попадёт вкладка, брошенная в эту точку экрана; null — мимо этого дерева.
    /// </summary>
    /// <param name="at">Точка на экране.</param>
    /// <param name="item">Какую панель несут.</param>
    /// <remarks>
    /// Спрашивают об этом каждое дерево по очереди — и то, в котором тягу
    /// начали, и деревья остальных окон. Курсор в каждый миг над одним из них,
    /// поэтому ответ есть не больше чем у одного.
    /// <para>
    /// Ответ зависит от того, что именно несут: место в полосе вкладок
    /// считается среди <b>остальных</b> вкладок, иначе перестановка внутри
    /// полосы промахивалась бы на единицу.
    /// </para>
    /// </remarks>
    public DockAim? Aim(PixelPoint at, string item)
    {
        if (TopLevel.GetTopLevel(this) is not { IsVisible: true } top)
            return null;

        var inside = top.PointToClient(at);

        // Мимо окна — это дерево не отвечает вовсе: пусть отвечает то, над
        // которым курсор, а не ответил никто — вкладка уходит в своё окно.
        if (!new Rect(top.ClientSize).Contains(inside))
            return null;

        if (top.TranslatePoint(inside, this) is not { } point)
            return null;

        // Внутри дерева спрашивают его области, вне — всё дерево целиком.
        // Случаи не пересекаются, и это видно здесь, а не прячется внутри.
        return new Rect(Bounds.Size).Contains(point) ? Target(point, item) : Beyond(point);
    }

    /// <summary>
    /// Показывает, где окажется вкладка, брошенная в эту точку экрана.
    /// </summary>
    /// <param name="at">Точка на экране.</param>
    /// <param name="item">Какую панель несут.</param>
    /// <remarks>
    /// Показывает <b>ровно то место</b>, которое панель займёт: доли, по которым
    /// оно считается, живут в <see cref="DockTree"/> и берутся оттуда, а не
    /// повторяются здесь своей цифрой. Обещание поэтому не расходится с тем,
    /// что человек получит.
    /// <para>
    /// Настоящих областей при этом не двигают. Перекладка на каждую границу зон
    /// заставляет раскладку щёлкать под курсором, а целиться — в то, что уже
    /// уехало. Место рисуется поверх, окно стоит на месте, и переход между
    /// зонами виден одним движением подсказки.
    /// </para>
    /// </remarks>
    public void Show(PixelPoint at, string item)
    {
        var aim = Aim(at, item);
        var title = Items?.Find(item)?.Title ?? item;

        if (aim is DockAim.Tab tab && View(tab.Group) is { } joined && Place(joined) is { } strip)
        {
            var (_, edge) = joined.Slot(Local(at) is { } point ? (point - strip.Position).X : 0, item);

            // Подсвечивается сама полоса, а не вся область: вкладка встаёт в
            // полосу, и накрывать ради этого всю панель — кричать не по делу.
            // Подпись здесь тоже лишняя: имя человек и так несёт под курсором,
            // а поверх чужих вкладок оно легло бы кашей.
            Paint(new Rect(strip.X, strip.Y, strip.Width, joined.HeaderHeight), null);
            Mark(new Rect(strip.X + edge - 1, strip.Y, 2, joined.HeaderHeight));

            return;
        }

        if (aim is DockAim.Split split && View(split.Group) is { } divided && Place(divided) is { } area)
        {
            Paint(Slice(area, split.Side, DockTree.SplitShare), title);
            Mark(null);

            return;
        }

        if (aim is DockAim.Frame frame)
        {
            Paint(Slice(new Rect(Bounds.Size), frame.Side, DockTree.FrameShare), title);
            Mark(null);

            return;
        }

        Clear();
    }

    /// <summary>Убирает подсказку этого дерева.</summary>
    /// <remarks>
    /// Тяги не касается: пока курсор идёт над чужим окном, своему показывать
    /// нечего, а вкладку несёт по-прежнему оно.
    /// </remarks>
    public void Clear()
    {
        Paint(null, null);
        Mark(null);
    }

    /// <summary>Куда попадёт брошенная вкладка; null — мимо всего.</summary>
    private DockAim? Target(Point point, string item)
    {
        if (Group(point) is not { } group || Place(group) is not { } area)
            return null;

        var size = area.Size;

        if (size.Width <= 0 || size.Height <= 0)
            return null;

        var local = point - area.Position;

        // Полоса вкладок сильнее всего: она и есть «встань рядом», и место в
        // ней человек выбирает тем же движением.
        if (local.Y < group.HeaderHeight)
            return new DockAim.Tab(group.Id, group.Slot(local.X, item).At);

        var across = local.X / size.Width;
        var down = local.Y / size.Height;

        (double Share, DockSide Side)[] edges =
        [
            (across, DockSide.Left),
            (1 - across, DockSide.Right),
            (down, DockSide.Top),
            (1 - down, DockSide.Bottom),
        ];

        var near = edges.MinBy(edge => edge.Share);

        // Дальше трети от каждого края — это середина, а середина значит
        // «оторви в своё окно»: так человеку не нужен свободный рабочий стол.
        return near.Share < Third
            ? new DockAim.Split(group.Id, near.Side)
            : new DockAim.Float();
    }

    /// <summary>
    /// Стыковка ко всему дереву; null — мерить нечего.
    /// </summary>
    /// <remarks>
    /// Внутри окна, но вне дерева — это полосы, которые деревом не заняты: у
    /// главного окна тулбар сверху и строка состояния снизу, у оторванного —
    /// его заголовок. Брошенная туда панель ложится полосой поперёк всего
    /// дерева, а не внутрь чьей-то колонки: консоль во всю ширину окна иначе
    /// собрать нечем. Так же устроено и у Unity, и по той же причине — слева и
    /// справа таких полос нет, потому что дерево доходит до самой рамки.
    /// </remarks>
    private DockAim? Beyond(Point point)
    {
        var size = Bounds.Size;

        if (size.Width <= 0 || size.Height <= 0)
            return null;

        // Сторона — та, за которую вышли дальше всего.
        (double Away, DockSide Side)[] edges =
        [
            (-point.X, DockSide.Left),
            (point.X - size.Width, DockSide.Right),
            (-point.Y, DockSide.Top),
            (point.Y - size.Height, DockSide.Bottom),
        ];

        return new DockAim.Frame(edges.MaxBy(edge => edge.Away).Side);
    }

    /// <summary>Группа под указателем; null — там её нет.</summary>
    private DockGroupView? Group(Point point) =>
        _groups.Values.FirstOrDefault(group => Place(group)?.Contains(point) == true);

    /// <summary>Место группы в координатах вида; null — её там нет.</summary>
    private Rect? Place(DockGroupView group) =>
        group.TranslatePoint(default, this) is { } origin
            ? new Rect(origin, group.Bounds.Size)
            : null;

    /// <summary>Полоса указанной доли у названного края.</summary>
    private static Rect Slice(Rect area, DockSide side, double share) => side switch
    {
        DockSide.Left => new Rect(area.X, area.Y, area.Width * share, area.Height),
        DockSide.Right => new Rect(
            area.X + (area.Width * (1 - share)), area.Y, area.Width * share, area.Height),
        DockSide.Top => new Rect(area.X, area.Y, area.Width, area.Height * share),

        // Осталась только нижняя: сторон четыре, три уже разобраны.
        _ => new Rect(area.X, area.Y + (area.Height * (1 - share)), area.Width, area.Height * share),
    };

    /// <summary>
    /// Кладёт подсказку на указанное место; null — снимает её.
    /// </summary>
    /// <remarks>
    /// Подсказка не ловит мышь: поймай она её — под указателем всегда была бы
    /// она сама, и цель перестала бы меняться.
    /// </remarks>
    private void Paint(Rect? area, string? title)
    {
        if (area is not { } place
            || OverlayLayer.GetOverlayLayer(this) is not { } layer
            || this.TranslatePoint(place.Position, layer) is not { } corner)
        {
            if (_hint is { Parent: Panel host })
                host.Children.Remove(_hint);

            _hint = null;

            return;
        }

        _hint ??= new Border
        {
            Classes = { "dock-hint" },
            IsHitTestVisible = false,
            Child = new TextBlock(),
        };

        if (_hint.Child is TextBlock label)
            label.Text = title;

        if (!layer.Children.Contains(_hint))
            layer.Children.Add(_hint);

        Canvas.SetLeft(_hint, corner.X);
        Canvas.SetTop(_hint, corner.Y);
        _hint.Width = place.Width;
        _hint.Height = place.Height;
    }

    /// <summary>Ставит черту, у которой встанет вкладка; null — снимает её.</summary>
    private void Mark(Rect? area)
    {
        if (area is not { } place
            || OverlayLayer.GetOverlayLayer(this) is not { } layer
            || this.TranslatePoint(place.Position, layer) is not { } corner)
        {
            if (_caret is { Parent: Panel host })
                host.Children.Remove(_caret);

            _caret = null;

            return;
        }

        _caret ??= new Border { Classes = { "dock-caret" }, IsHitTestVisible = false };

        if (!layer.Children.Contains(_caret))
            layer.Children.Add(_caret);

        Canvas.SetLeft(_caret, corner.X);
        Canvas.SetTop(_caret, corner.Y);
        _caret.Width = place.Width;
        _caret.Height = place.Height;
    }

    /// <summary>
    /// Заканчивает тягу и убирает всё, что она показывала.
    /// </summary>
    /// <remarks>
    /// Снять показанное и забыть, что вкладку несут, — разные дела, и путать их
    /// нельзя: пока курсор идёт над чужим окном, своему показывать нечего, а
    /// тяга всё ещё его. Смешай их — и тяга обрывалась бы на выходе из окна.
    /// </remarks>
    private void Stop()
    {
        var dragged = _dragged is not null;

        _dragged = null;

        Clear();

        if (dragged)
            Stopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Строит экран заново по нынешнему дереву.</summary>
    private void Rebuild()
    {
        // Сперва отпускаем всё и только потом строим. Родитель у контрола
        // Avalonia ровно один, и панель, переехавшая в соседнюю группу,
        // встала бы на новое место исключением, не уйдя со старого.
        foreach (var group in _groups.Values)
        {
            (group.Parent as Panel)?.Children.Remove(group);
            group.Release();
        }

        Child = null;

        var alive = new HashSet<string>(StringComparer.Ordinal);

        if (Root is { } root && Items is { } items)
            Child = Build(root, [], items, alive);

        foreach (var id in _groups.Keys.Where(id => !alive.Contains(id)).ToList())
            _groups.Remove(id);

        Hang();
    }

    /// <summary>Раздаёт группам нынешнюю подпись кнопки скрытия.</summary>
    private void Retitle()
    {
        foreach (var view in _groups.Values)
        {
            view.HideTitle = HideTitle;
        }
    }

    /// <summary>Вешает кнопки в шапку угловой группы, у остальных снимает.</summary>
    /// <remarks>
    /// Угол считается среди показанных групп: у панели выключенного плагина имя
    /// в дереве осталось, а места на экране нет — кнопки уехали бы в пустоту.
    /// </remarks>
    private void Hang()
    {
        var corner = Root is { } root
            ? DockTree.Corner(root, _groups.Keys.ToHashSet(StringComparer.Ordinal))
            : null;

        foreach (var (id, view) in _groups)
        {
            var wanted = string.Equals(id, corner, StringComparison.Ordinal);

            // Делаем один раз на группу: сделай мы контрол на каждую перекладку,
            // прежний уходил бы из шапки на следующем проходе — уже после того,
            // как новый попросился в неё же.
            if (wanted && view.Actions is null && Actions is { } make)
                view.Actions = make();
            else if (!wanted && view.Actions is not null)
                view.Actions = null;
        }
    }

    /// <summary>
    /// Строит узел: группу — видом, деление — сеткой со сплиттерами.
    /// </summary>
    /// <returns>Контрол либо null — показывать тут нечего.</returns>
    private Control? Build(DockNode node, IReadOnlyList<int> path, DockItems items, HashSet<string> alive)
    {
        if (node is DockGroup group)
        {
            var named = string.Equals(group.Id, EmptyGroup, StringComparison.Ordinal);

            // Группа, в которой нечего показать — плагин выключен или панели
            // убраны с глаз, — места не занимает. Из дерева она при этом
            // остаётся: там её имена и её доля, и по ним панель вернётся сюда
            // же и такой же ширины.
            if (!named && !group.Items.Any(id => Onscreen(id, items)))
                return null;

            alive.Add(group.Id);

            if (!_groups.TryGetValue(group.Id, out var view))
            {
                view = new DockGroupView();
                view.Chosen += (_, id) => Chosen?.Invoke(this, id);
                view.Closing += (_, id) => Closing?.Invoke(this, id);

                // Имя группы берётся у самого вида, а не из замыкания: та же
                // группа переживает перекладку, а её узел в дереве — нет.
                view.HideRequested += (sender, _) =>
                    Hiding?.Invoke(this, ((DockGroupView)sender!).Id);

                _groups[group.Id] = view;
            }

            // Названная группа — пол рабочей области: она остаётся на месте,
            // даже опустев, и красится в цвет оболочки, а не панели.
            view.Standing = named;

            // Прятать пол рабочей области не дают: документы не скрывают. Не
            // дают и группе, в которой скрывать нечего, — там кнопка стояла бы
            // и не делала ничего. А одинокой группе дают: её имя остаётся в
            // дереве вместе с местом и долей, и меню «Панели» вернёт панель
            // туда же.
            view.CanHide = Hideable && !named && group.Items.Any(id => Hidable(id, items));
            view.HideTitle = HideTitle;
            view.Hidden = Hidden;
            view.Update(group, items, named ? Empty : null);

            return view;
        }

        var split = (DockSplit)node;
        var down = split.Orientation == DockOrientation.Vertical;
        var shares = DockTree.Shares(split);
        var shown = new List<(Control Control, int At)>();

        for (var at = 0; at < split.Children.Count; at++)
        {
            if (Build(split.Children[at], [.. path, at], items, alive) is { } control)
                shown.Add((control, at));
        }

        if (shown.Count == 0)
            return null;

        if (shown.Count == 1)
            return shown[0].Control;

        var grid = new Grid();

        // Полосы, которые делят место между собой. Группы выключенного плагина
        // и скрытой панели здесь нет вовсе: они не попали в показанные, а их
        // доли ждут в дереве нетронутыми. Освободившееся место на экране берёт
        // пол рабочей области — иначе звёздочные доли отдали бы его всем
        // показанным поровну, и боковая панель, которой никто не касался,
        // становилась бы шире.
        var sized = new List<(int At, int Row)>();
        var floor = Floor(split, shown);
        var room = Onscreen(shares, [.. shown.Select(item => item.At)], floor);

        for (var number = 0; number < shown.Count; number++)
        {
            var (control, at) = shown[number];

            if (number > 0)
                Line(grid, down, path, shares, sized, floor);

            var row = Row(grid, down, new GridLength(room[number], GridUnitType.Star));

            Put(grid, down, control, row);
            sized.Add((at, row));
        }

        return grid;
    }

    /// <summary>
    /// Ставит между соседями границу, за которую можно взяться.
    /// </summary>
    /// <param name="grid">Сетка деления.</param>
    /// <param name="down">Деление идёт сверху вниз.</param>
    /// <param name="path">Путь к делению от корня.</param>
    /// <param name="shares">Доли всех детей — и показанных, и нет.</param>
    /// <param name="sized">Кто делит место: номер ребёнка и его полоса в сетке.</param>
    /// <param name="floor">Кто из показанных несёт пол рабочей области; -1 — никто.</param>
    /// <remarks>
    /// Замороженных границ здесь не бывает: на экране остаются только те, кто
    /// делит место долями. Группа, которой на экране нет, в сетку не попадает
    /// вовсе — иначе сплиттер, дотянувшись до неё, молча выдавал бы ей пиксели
    /// вместо доли.
    /// </remarks>
    private void Line(
        Grid grid,
        bool down,
        IReadOnlyList<int> path,
        IReadOnlyList<double> shares,
        IReadOnlyList<(int At, int Row)> sized,
        int floor)
    {
        // Разделитель — контрол набора: линия в пиксель, полоса захвата вокруг
        // и подсветка под курсором приходят вместе с ним. Свой шаблон движок
        // держал, пока такого контрола не было; двух одинаковых границ в одном
        // окне быть не должно.
        var splitter = new AxSplitter
        {
            Orientation = down ? Orientation.Horizontal : Orientation.Vertical,
        };

        splitter.DragCompleted += (_, _) => Resized?.Invoke(this, new DockResize(
            path,
            Spread(
                shares,
                [.. sized.Select(item => item.At)],
                Shares(grid, down, [.. sized.Select(item => item.Row)]),
                floor)));

        Put(grid, down, splitter, Row(grid, down, new GridLength(1)));
    }

    /// <summary>
    /// Раскладывает померенные доли по местам, не трогая спрятанных.
    /// </summary>
    /// <remarks>
    /// На экране могли стоять не все дети: у соседа выключили плагин, и его
    /// группа ничего не показывает. Отдать в дерево доли одних лишь видимых
    /// значило бы отобрать место у спрятанного — и панель, вернувшись, встала
    /// бы шириной в ноль. Поэтому видимые делят между собой ровно то место,
    /// которое им и принадлежало.
    /// </remarks>
    private static IReadOnlyList<double> Spread(
        IReadOnlyList<double> all,
        IReadOnlyList<int> visible,
        IReadOnlyList<double> measured,
        int floor)
    {
        if (measured.Count != visible.Count)
            return all;

        var next = all.ToList();
        var mine = Seat(visible, floor);

        // Некому было отдавать место — значит и снимать нечего: видимые делят
        // между собой ровно то, что им принадлежало.
        if (mine < 0)
        {
            var room = visible.Sum(at => all[at]);

            for (var number = 0; number < visible.Count; number++)
                next[visible[number]] = measured[number] * room;

            return DockTree.Normalize(next);
        }

        // Пол показан шире своей доли ровно на то, что причитается
        // отсутствующим. Снимаем добавку — иначе первое же перетаскивание
        // съело бы их доли, и панель, вернувшись, встала бы шириной в ноль.
        var slack = 1 - visible.Sum(at => all[at]);

        // Уже того места, что пол держит за отсутствующих, его не утянуть:
        // такому экрану нет соответствия в дереве. Доля пола вышла бы
        // отрицательной, Normalize молча обратила бы её в ноль — и стоило
        // вернуть скрытую панель, как область документов раскладывалась бы
        // шириной в ноль. Отказываем: граница отскакивает к пределу, и человек
        // видит, что дальше некуда, — так же ведёт себя и минимальный размер.
        if (measured[mine] <= slack)
            return all;

        for (var number = 0; number < visible.Count; number++)
            next[visible[number]] = measured[number];

        next[visible[mine]] = measured[mine] - slack;

        return DockTree.Normalize(next);
    }

    /// <summary>
    /// Доли для показа: пол рабочей области берёт место отсутствующих.
    /// </summary>
    /// <param name="all">Доли всех детей деления.</param>
    /// <param name="visible">Номера тех, кто попал на экран.</param>
    /// <param name="floor">Номер ребёнка, несущего пол, в дереве; -1 — никто.</param>
    /// <returns>Доли по числу показанных, в сумме единица.</returns>
    /// <remarks>
    /// Обратна <see cref="Spread"/>, и это не совпадение: что показали, то
    /// перетаскивание и обязано вернуть в дерево. Проверяется парой напрямую —
    /// тянем границу и сверяем записанное с показанным.
    /// </remarks>
    private static IReadOnlyList<double> Onscreen(
        IReadOnlyList<double> all,
        IReadOnlyList<int> visible,
        int floor)
    {
        var room = visible.Select(at => all[at]).ToList();
        var mine = Seat(visible, floor);

        if (mine < 0)
            return DockTree.Normalize(room);

        room[mine] += 1 - room.Sum();

        return DockTree.Normalize(room);
    }

    /// <summary>
    /// Какое место среди показанных занимает названный ребёнок дерева.
    /// </summary>
    /// <param name="visible">Номера показанных детей, по порядку на экране.</param>
    /// <param name="floor">Номер ребёнка в дереве; -1 — никакой.</param>
    /// <returns>Место в списке показанных; -1 — его там нет.</returns>
    private static int Seat(IReadOnlyList<int> visible, int floor)
    {
        if (floor < 0)
            return -1;

        for (var number = 0; number < visible.Count; number++)
        {
            if (visible[number] == floor)
                return number;
        }

        return -1;
    }

    /// <summary>
    /// Есть ли этой панели что показать прямо сейчас.
    /// </summary>
    /// <param name="id">Имя панели.</param>
    /// <param name="items">Живые панели по именам.</param>
    /// <returns>true — панель жива и не убрана с глаз.</returns>
    /// <remarks>
    /// Две причины не показывать панель, и обе оставляют её имя в дереве:
    /// плагин выключили, и живого контрола за именем нет; человек убрал панель
    /// с глаз, и контрол есть, но показывать его не просили. Место и доля в
    /// обоих случаях ждут в дереве.
    /// </remarks>
    private bool Onscreen(string id, DockItems items) =>
        items.Find(id) is not null && Hidden?.Contains(id) != true;

    /// <summary>
    /// Можно ли эту панель убрать с глаз.
    /// </summary>
    /// <param name="id">Имя панели.</param>
    /// <param name="items">Живые панели по именам.</param>
    /// <returns>true — панель показана и скрытию подлежит.</returns>
    /// <remarks>
    /// Показанную и незапретную: уже убранную убирать нечего, а документ
    /// убирать нельзя. Спрашивает об этом одна кнопка — та, что в шапке группы.
    /// </remarks>
    private bool Hidable(string id, DockItems items) =>
        Onscreen(id, items) && Fixed?.Contains(id) != true;

    /// <summary>
    /// Кто из показанных детей несёт в себе пол рабочей области.
    /// </summary>
    /// <param name="split">Деление, чьих детей показываем.</param>
    /// <param name="shown">Показанные: контрол и номер ребёнка.</param>
    /// <returns>Номер ребёнка в дереве; -1 — пола среди показанных нет.</returns>
    /// <remarks>
    /// Не прямым ребёнком, а «несёт в себе»: обычная раскладка студии — ряд из
    /// панелей и документов сверху, полоса снизу. Уходит полоса, и место должен
    /// взять ряд, потому что документы лежат внутри него.
    /// <para>
    /// Номер именно в дереве, а не среди показанных. Нумераций здесь три —
    /// дети деления, показанные и померенные полосы сетки, — и совпадают они
    /// лишь потому, что их наполняет один цикл. Пол ходит от постройки к
    /// перетаскиванию, и назвать его номером дерева — единственный способ не
    /// зависеть от порядка этого цикла.
    /// </para>
    /// </remarks>
    private int Floor(DockSplit split, IReadOnlyList<(Control Control, int At)> shown)
    {
        if (EmptyGroup is not { Length: > 0 } home)
            return -1;

        foreach (var (_, at) in shown)
        {
            if (DockTree.Group(split.Children[at], home) is not null)
                return at;
        }

        return -1;
    }

    /// <summary>Заводит очередную полосу сетки и возвращает её номер.</summary>
    private static int Row(Grid grid, bool down, GridLength size)
    {
        if (down)
            grid.RowDefinitions.Add(new RowDefinition(size));
        else
            grid.ColumnDefinitions.Add(new ColumnDefinition(size));

        return (down ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count) - 1;
    }

    /// <summary>Ставит контрол в полосу с указанным номером.</summary>
    private static void Put(Grid grid, bool down, Control control, int at)
    {
        if (down)
            Grid.SetRow(control, at);
        else
            Grid.SetColumn(control, at);

        grid.Children.Add(control);
    }

    /// <summary>Снимает доли с названных полос сетки.</summary>
    /// <param name="grid">Сетка деления.</param>
    /// <param name="down">Деление идёт сверху вниз.</param>
    /// <param name="rows">Полосы, делящие место, по порядку детей.</param>
    /// <remarks>
    /// Берём объявленные длины, а не занятое место: сплиттер переписывает
    /// длины сразу, а место обновится только на следующем проходе раскладки, и
    /// доли вышли бы вчерашними — граница возвращалась бы на место, едва её
    /// отпустили.
    /// <para>
    /// Полосы названы поимённо, а не отобраны по чётности: между содержимым
    /// стоят линии, и своей доли у них нет — к дележу места они отношения не
    /// имеют.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<double> Shares(Grid grid, bool down, IReadOnlyList<int> rows)
    {
        var sizes = rows
            .Select(at => down ? grid.RowDefinitions[at].Height.Value : grid.ColumnDefinitions[at].Width.Value)
            .ToList();

        return DockTree.Normalize(sizes);
    }
}
