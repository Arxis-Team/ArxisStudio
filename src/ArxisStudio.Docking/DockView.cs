using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>Черта в полосе вкладок: у неё вкладка и встанет.</summary>
    private Border? _caret;

    static DockView()
    {
        RootProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ItemsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        EmptyGroupProperty.Changed.AddClassHandler<DockView>((view, _) => view.Rebuild());
        ActionsProperty.Changed.AddClassHandler<DockView>((view, _) => view.Hang());
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

            // Группа без единой живой панели места не занимает — но из дерева
            // не уходит: там остаются имена, и по ним панель вернётся сюда же.
            if (!named && !group.Items.Any(id => items.Find(id) is not null))
                return null;

            alive.Add(group.Id);

            if (!_groups.TryGetValue(group.Id, out var view))
            {
                view = new DockGroupView();
                view.Chosen += (_, id) => Chosen?.Invoke(this, id);
                view.Closing += (_, id) => Closing?.Invoke(this, id);
                _groups[group.Id] = view;
            }

            // Названная группа — пол рабочей области: она остаётся на месте,
            // даже опустев, и красится в цвет оболочки, а не панели.
            view.Standing = named;
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
        var visible = shown.Select(child => child.At).ToList();

        for (var number = 0; number < shown.Count; number++)
        {
            if (number > 0)
                Line(grid, down, path, shares, visible);

            Put(grid, down, shown[number].Control,
                Row(grid, down, new GridLength(shares[shown[number].At], GridUnitType.Star)));
        }

        return grid;
    }

    /// <summary>Ставит между соседями границу, за которую можно взяться.</summary>
    private void Line(
        Grid grid,
        bool down,
        IReadOnlyList<int> path,
        IReadOnlyList<double> shares,
        IReadOnlyList<int> visible)
    {
        var splitter = new GridSplitter
        {
            Classes = { down ? "dock-h" : "dock-v" },
            ResizeDirection = down ? GridResizeDirection.Rows : GridResizeDirection.Columns,
        };

        splitter.DragCompleted += (_, _) =>
            Resized?.Invoke(this, new DockResize(path, Spread(shares, visible, Shares(grid, down))));

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
        IReadOnlyList<double> measured)
    {
        if (measured.Count != visible.Count)
            return all;

        var room = visible.Sum(at => all[at]);
        var next = all.ToList();

        for (var number = 0; number < visible.Count; number++)
            next[visible[number]] = measured[number] * room;

        return DockTree.Normalize(next);
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

    /// <summary>Снимает доли с сетки.</summary>
    /// <remarks>
    /// Берём объявленные длины, а не занятое место: сплиттер переписывает
    /// длины сразу, а место обновится только на следующем проходе раскладки, и
    /// доли вышли бы вчерашними — граница возвращалась бы на место, едва её
    /// отпустили.
    /// <para>
    /// Содержимое стоит по чётным полосам, между ними линии; единица измерения
    /// для отбора не годится — сплиттер вправе переписать доли в пиксели, и
    /// тогда «звёздочка» перестала бы отличать одно от другого.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<double> Shares(Grid grid, bool down)
    {
        var count = down ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
        var sizes = new List<double>();

        for (var at = 0; at < count; at += 2)
            sizes.Add(down ? grid.RowDefinitions[at].Height.Value : grid.ColumnDefinitions[at].Width.Value);

        return DockTree.Normalize(sizes);
    }
}
