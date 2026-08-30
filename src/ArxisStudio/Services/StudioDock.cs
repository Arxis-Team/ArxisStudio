using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Раскладка студии: дерево доков и живые панели в нём.
/// </summary>
/// <remarks>
/// Движок докинга про плагины не знает и знать не должен — вся склейка здесь.
/// Отсюда же и правило владения: контрол приходит вместе с именем хозяина, и по
/// этому имени его снимут, когда хозяин уйдёт. Дерево при этом помнит только
/// имена панелей, поэтому выключенный плагин возвращается на своё место, а его
/// контекст загрузки тем временем выгружается.
/// </remarks>
public sealed class StudioDock
{
    /// <summary>Имя группы, куда открываются документы по умолчанию.</summary>
    public const string Documents = "documents";

    /// <summary>
    /// Сколько ждать перед записью раскладки.
    /// </summary>
    /// <remarks>
    /// Правок много и они частые: тянут границу — десятки за секунду, щёлкают по
    /// вкладкам — каждый щелчок. Писать файл на каждую значит стучать по диску
    /// весь день; ждать конца сеанса — потерять раскладку при жёстком закрытии.
    /// Пауза даёт человеку договорить движение и записывает уже итог.
    /// </remarks>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly DockView _view;
    private readonly DockLayoutStore? _store;
    private readonly DispatcherTimer? _writer;

    /// <summary>
    /// Группы, которые не сносятся, даже опустев.
    /// </summary>
    /// <remarks>
    /// Область документов человек видит и узнаёт. Исчезни она вместе с
    /// последней закрытой вкладкой — следующий документ появился бы неизвестно
    /// где, а на её месте было бы пусто без объяснений.
    /// </remarks>
    private HashSet<string> _standing = new([Documents], StringComparer.Ordinal);

    /// <summary>
    /// Кто куда просился, по порядку появления.
    /// </summary>
    /// <remarks>
    /// Нужно сбросу и переключению наборов: и то и другое обязано разложить
    /// панели так же, как при первом запуске. Порядок важен — от него зависит,
    /// кто с кем окажется вкладками в одной группе, и кто кого застанет на
    /// экране, попросившись «рядом с той панелью».
    /// </remarks>
    private readonly List<(string Id, PluginPlacement Where)> _asked = [];

    /// <summary>
    /// Наборы раскладки по именам — кроме показанного.
    /// </summary>
    /// <remarks>
    /// Показанный набор здесь не лежит и лежать не может: он живёт в дереве
    /// вида и меняется с каждым движением мыши. Копия рядом с ним неминуемо
    /// разошлась бы с ним же, и вопрос «где правда» получил бы два ответа.
    /// </remarks>
    private Dictionary<string, DockWorkspace> _saved = new(StringComparer.Ordinal);

    private string _active = DockLayout.DefaultName;
    private string _home = Documents;
    private bool _dirty;

    /// <summary>Заводит раскладку над видом.</summary>
    /// <param name="view">Вид, который её показывает.</param>
    /// <param name="store">Куда записывать раскладку; null — никуда.</param>
    public StudioDock(DockView view, DockLayoutStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
        _store = store;
        _view.Items = Items;
        _view.EmptyGroup = _home;
        _view.Root = Skeleton();

        if (store is not null)
            _writer = new DispatcherTimer(Pause, DispatcherPriority.Background, (_, _) => Flush());

        // Выбор вкладки и потянутая граница — это правки дерева, а не состояние
        // контрола: они переживают перезапуск студии. Вид о них только
        // сообщает; записывает их сюда владелец дерева.
        _view.Chosen += (_, id) =>
        {
            Edit(root => DockTree.Select(root, id));
            Chosen?.Invoke(this, id);
        };

        _view.Resized += (_, resize) => Edit(root => DockTree.Resize(root, resize.Path, resize.Weights));
        _view.Dropped += (_, drop) => Move(drop);
        _view.Closing += (_, id) => Closing?.Invoke(this, id);
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели или документа.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Студии есть что сказать человеку о файле раскладки.</summary>
    public event EventHandler<string>? Complained;

    /// <summary>Человек попросил закрыть панель или документ; в поле — имя.</summary>
    public event EventHandler<string>? Closing;

    /// <summary>Живые панели по именам.</summary>
    public DockItems Items { get; } = new();

    /// <summary>Имя показанного набора раскладки.</summary>
    public string Layout => _active;

    /// <summary>
    /// Имена всех наборов по алфавиту — показанный в их числе.
    /// </summary>
    /// <remarks>
    /// По алфавиту, а не по времени: список читают глазами в меню, и порядок,
    /// меняющийся от того, куда человек переключался вчера, там ни к чему.
    /// </remarks>
    public IReadOnlyList<string> Layouts =>
        [.. _saved.Keys.Append(_active).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Что выбрано в области документов; null — ничего или не документ.</summary>
    public string? Showing => _view.Root is { } root ? DockTree.Group(root, _home)?.Selected : null;

    /// <summary>
    /// Поднимает сохранённую раскладку, если она есть и читается.
    /// </summary>
    /// <remarks>
    /// Зовётся до того, как встанут панели: иначе они успели бы разойтись по
    /// стандартным местам, а прочитанное дерево тут же смело бы их оттуда.
    /// <para>
    /// Имена панелей из файла не отсеиваются по живым. Плагин мог быть выключен
    /// или ещё не поднят — а место за ним числится, и он вернётся именно туда.
    /// </para>
    /// </remarks>
    public void Restore()
    {
        if (_store is not { } store)
            return;

        var layout = store.Load(out var complaint);

        if (complaint is not null)
            Complained?.Invoke(this, complaint);

        if (layout?.Current is not { } workspace)
            return;

        _saved = new Dictionary<string, DockWorkspace>(layout.Layouts, StringComparer.Ordinal);
        _active = _saved.ContainsKey(layout.Active) ? layout.Active : _saved.Keys.First();

        // Показанный набор в _saved не остаётся: правда о нём — дерево вида.
        _saved.Remove(_active);

        Apply(workspace);
    }

    /// <summary>
    /// Сохраняет показанную раскладку под новым именем и переходит в неё.
    /// </summary>
    /// <param name="name">Имя набора; пустое имя ничего не делает.</param>
    /// <remarks>
    /// Прежний набор при этом ничего не теряет: показанная раскладка и была
    /// им — студия пишет её туда после каждой правки. Поэтому «сохранить как» —
    /// это копия под новым именем и переход в неё, а не перенос.
    /// </remarks>
    public void SaveAs(string name)
    {
        var chosen = name?.Trim();

        if (string.IsNullOrEmpty(chosen) || _view.Root is not { } root)
            return;

        _saved[_active] = new DockWorkspace { Root = root, DocumentHome = _home };
        _saved.Remove(chosen);
        _active = chosen;
        _dirty = true;

        Flush();
    }

    /// <summary>
    /// Показывает другой набор; незнакомое имя ничего не меняет.
    /// </summary>
    /// <param name="name">Имя набора.</param>
    /// <remarks>
    /// Уходя, показанную раскладку запоминаем: человек её не сохранял, но и не
    /// отменял — он всего лишь ушёл посмотреть другую, и вернуться обязан к
    /// своей, а не к той, что была при последнем переключении.
    /// </remarks>
    public void Switch(string name)
    {
        if (string.Equals(name, _active, StringComparison.Ordinal)
            || !_saved.TryGetValue(name, out var workspace)
            || _view.Root is not { } root)
        {
            return;
        }

        _saved[_active] = new DockWorkspace { Root = root, DocumentHome = _home };
        _saved.Remove(name);
        _active = name;
        _dirty = true;

        Apply(workspace);
        Flush();
    }

    /// <summary>
    /// Забывает показанный набор и возвращается к стандартному.
    /// </summary>
    /// <remarks>
    /// Стандартный набор не забывается: он — то, куда возвращаются, и студия
    /// без него осталась бы без единого имени. Удалять же чужой набор, не
    /// глядя на него, человеку незачем — сперва переключись, потом решай.
    /// </remarks>
    public void Forget()
    {
        if (string.Equals(_active, DockLayout.DefaultName, StringComparison.Ordinal))
            return;

        _active = DockLayout.DefaultName;
        _dirty = true;

        if (_saved.Remove(DockLayout.DefaultName, out var standard))
            Apply(standard);
        else
            Reset();

        Flush();
    }

    /// <summary>
    /// Собирает раскладку заново — такой, какой она бывает при первом запуске.
    /// </summary>
    /// <remarks>
    /// Без этого перетаскивание — дверь в одну сторону: перекроить раскладку
    /// можно, а вернуть как было нечем, и человеку остаётся собирать её мышью
    /// обратно, вспоминая, где что стояло.
    /// <para>
    /// Панели раскладываются по объявленным местам и в том же порядке, в каком
    /// вставали при подъёме, — иначе «как было при первом запуске» означало бы
    /// каждый раз что-то своё.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        _home = Documents;
        _standing = new HashSet<string>([_home], StringComparer.Ordinal);
        _view.EmptyGroup = _home;

        var root = Skeleton();

        foreach (var (id, where) in _asked)
            root = Place(root, id, where);

        _view.Root = root;
        _dirty = true;

        Flush();
    }

    /// <summary>
    /// Записывает раскладку, не дожидаясь паузы.
    /// </summary>
    /// <remarks>
    /// Нужно при закрытии окна: отложенная запись до него просто не доживёт.
    /// </remarks>
    public void Flush()
    {
        _writer?.Stop();

        if (!_dirty || _store is null || _view.Root is not { } root)
            return;

        _dirty = false;

        if (_store.Save(Snapshot(root)) is { } complaint)
            Complained?.Invoke(this, complaint);
    }

    /// <summary>Ставит панель плагина в объявленное им место.</summary>
    /// <param name="owner">Чья панель — по нему её потом и снимут.</param>
    /// <param name="id">Имя панели, уникальное на всю студию.</param>
    /// <param name="where">Пожелание из манифеста: сторона, доля, соседство.</param>
    /// <param name="title">Заголовок из манифеста; ключ вида <c>%panel.main%</c> переводится.</param>
    /// <param name="strings">Словари плагина, которому принадлежит панель.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    /// <remarks>
    /// Место из манифеста — пожелание для незнакомого имени, а не приказ на
    /// каждый запуск. У панели, которая в дереве уже есть, место своё: её увели
    /// в другой угол, или плагин просто перезагрузили — манифест об этом не
    /// спрашивают, иначе каждый подъём затаскивал бы панели обратно.
    /// </remarks>
    public void Add(
        string owner, string id, PluginPlacement where, string title, PluginStrings strings, Control content)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(where);

        var item = new DockItem(id, content);

        // Заголовок — единственный текст панели, который показывает не её автор,
        // а студия, поэтому и переводить его при смене языка — забота студии.
        if (PluginStrings.IsKey(title, out var key))
            item.Bind(DockItem.TitleProperty, strings.Text(key));
        else
            item.Title = title;

        Items.Add(owner, item);

        if (!_asked.Any(asked => string.Equals(asked.Id, id, StringComparison.Ordinal)))
            _asked.Add((id, where));

        Edit(root => DockTree.Holder(root, id) is not null ? root : Place(root, id, where));
    }

    /// <summary>Открывает документ вкладкой в области документов.</summary>
    /// <param name="owner">Чей редактор его построил.</param>
    /// <param name="id">Имя документа.</param>
    /// <param name="title">Подпись вкладки.</param>
    /// <param name="content">Содержимое, построенное редактором.</param>
    public void Open(string owner, string id, string title, Control content)
    {
        Items.Add(owner, new DockItem(id, content) { Title = title, CanClose = true });

        if (!_asked.Any(asked => string.Equals(asked.Id, id, StringComparison.Ordinal)))
            _asked.Add((id, new PluginPlacement { Side = Documents }));

        Edit(root => DockTree.Insert(root, Home(root), DockSide.Tab, id, Documents));
    }

    /// <summary>Убирает одну панель или документ.</summary>
    /// <param name="id">Имя того, что убираем.</param>
    public void Remove(string id)
    {
        Items.Remove(id);
        _asked.RemoveAll(asked => string.Equals(asked.Id, id, StringComparison.Ordinal));
        Edit(root => DockTree.Remove(root, id, _standing));
    }

    /// <summary>
    /// Снимает с экрана всё, что поставил этот хозяин.
    /// </summary>
    /// <param name="owner">Чьи панели убираем.</param>
    /// <remarks>
    /// Из дерева имена при этом не уходят. Выключенный плагин обязан вернуться
    /// на своё место — той же ширины и в том же соседстве, — а место помнит
    /// именно дерево. Экрану пустая группа не мешает: вид её не показывает.
    /// </remarks>
    public void RemoveOwnedBy(string owner)
    {
        if (Items.RemoveOwnedBy(owner).Count > 0)
            _view.Refresh();
    }

    /// <summary>Достаёт панель или документ на видное место.</summary>
    /// <param name="id">Имя того, что показываем.</param>
    public void Show(string id)
    {
        Edit(root => DockTree.Select(root, id));
        Chosen?.Invoke(this, id);
    }

    /// <summary>
    /// Раскладка, с которой студия начинает.
    /// </summary>
    /// <remarks>
    /// Доли взяты с прежней оболочки, где они были зашиты в шаблон: 262 и 302
    /// пикселя по краям от полутора тысяч ширины и 212 снизу. Стороны заведены
    /// заранее и пустыми: пока в них никто не встал, вид их не показывает, зато
    /// пришедшая панель попадает в место с готовым размером, а не делит пополам
    /// область документов.
    /// </remarks>
    private static DockNode Skeleton() => new DockSplit
    {
        Orientation = DockOrientation.Horizontal,
        Weights = [0.18, 0.60, 0.22],
        Children =
        [
            new DockGroup { Id = "left" },
            new DockSplit
            {
                Orientation = DockOrientation.Vertical,
                Weights = [0.74, 0.26],
                Children = [new DockGroup { Id = Documents }, new DockGroup { Id = "bottom" }],
            },
            new DockGroup { Id = "right" },
        ],
    };

    /// <summary>
    /// Переносит панель туда, куда её бросили.
    /// </summary>
    /// <remarks>
    /// Снять и поставить — две правки, и между ними группа, куда бросали, может
    /// исчезнуть: снятие прибирает опустевшую, а человек мог унести из неё же
    /// последнюю вкладку. Ставить тогда некуда, и правка отменяется целиком —
    /// иначе панель просто пропала бы с экрана.
    /// </remarks>
    private void Move(DockDrop drop) => Edit(root =>
    {
        var without = DockTree.Remove(root, drop.Item, _standing);

        return DockTree.Group(without, drop.Group) is null
            ? root
            : DockTree.Insert(without, drop.Group, drop.Side, drop.Item, Fresh(without));
    });

    /// <summary>
    /// Имя для новой группы, которого в дереве ещё нет.
    /// </summary>
    /// <remarks>
    /// Имя попадёт в файл раскладки и переживёт перезапуск, поэтому оно должно
    /// быть своим у каждой группы. Считаем от единицы и берём первое свободное:
    /// так имена не растут без конца, когда области заводят и сносят по кругу.
    /// </remarks>
    private static string Fresh(DockNode root)
    {
        var taken = root.Groups().Select(group => group.Id).ToHashSet(StringComparer.Ordinal);

        for (var number = 1; ; number++)
        {
            if (taken.Add($"group{number}"))
                return $"group{number}";
        }
    }

    /// <summary>
    /// Показывает набор, доставая в него панели, которых он не знает.
    /// </summary>
    /// <remarks>
    /// Набор мог быть сохранён до того, как плагин поставили, — тогда его
    /// панели в дереве нет. Без этого она просто пропала бы с экрана, и человек
    /// решил бы, что плагин сломался, хотя дело в возрасте набора.
    /// </remarks>
    private void Apply(DockWorkspace workspace)
    {
        _home = string.IsNullOrEmpty(workspace.DocumentHome) ? Documents : workspace.DocumentHome;
        _standing = new HashSet<string>([_home], StringComparer.Ordinal);
        _view.EmptyGroup = _home;

        var root = workspace.Root;

        foreach (var (id, where) in _asked)
        {
            if (DockTree.Holder(root, id) is null)
                root = Place(root, id, where);
        }

        _view.Root = root;
    }

    /// <summary>
    /// Ставит панель туда, куда она просилась.
    /// </summary>
    /// <remarks>
    /// Соседство сильнее стороны: «встань рядом с деревом решения» — пожелание
    /// точное, и спрашивать после него про сторону незачем. Названного соседа
    /// может не быть на экране вовсе — плагин не поставили или выключили, —
    /// и тогда работает сторона.
    /// <para>
    /// Долю слушают только у первой панели на пустой стороне. У занятой размер
    /// уже есть — его дал сосед или мышь человека, — и отбирать его новичок не
    /// вправе.
    /// </para>
    /// </remarks>
    private DockNode Place(DockNode root, string id, PluginPlacement where)
    {
        if (where.Near is { Length: > 0 } near && DockTree.Holder(root, near) is { } neighbour)
            return DockTree.Insert(root, neighbour.Id, DockSide.Tab, id, neighbour.Id);

        var side = where.Side.ToLowerInvariant();

        if (DockTree.Group(root, side) is not { } waiting)
            return DockTree.Widen(DockTree.Insert(root, Home(root), Side(side), id, side), side, where.Size);

        var next = DockTree.Insert(root, side, DockSide.Tab, id, side);

        return waiting.Items.Count == 0 ? DockTree.Widen(next, side, where.Size) : next;
    }

    /// <summary>Сторона по названию; незнакомое слово уводит вправо.</summary>
    private static DockSide Side(string side) => side switch
    {
        "left" => DockSide.Left,
        "top" => DockSide.Top,
        "bottom" => DockSide.Bottom,
        _ => DockSide.Right,
    };

    /// <summary>
    /// Раскладка в том виде, в каком она ложится в файл.
    /// </summary>
    /// <remarks>
    /// Показанный набор пишется из дерева вида, остальные — как лежали. Так
    /// набор, в который человек не заходил, переживает и правки соседей, и
    /// перезапуск студии, ни разу не побывав на экране.
    /// </remarks>
    private DockLayout Snapshot(DockNode root) => new()
    {
        Active = _active,
        Layouts = new Dictionary<string, DockWorkspace>(_saved, StringComparer.Ordinal)
        {
            [_active] = new() { Root = root, DocumentHome = _home },
        },
    };

    /// <summary>От какой группы отмерять место для новой.</summary>
    private string Home(DockNode root) =>
        DockTree.Group(root, _home)?.Id ?? root.Groups().First().Id;

    /// <summary>
    /// Правит дерево и показывает, что вышло.
    /// </summary>
    /// <remarks>
    /// Правка, ничего не изменившая в дереве, всё равно требует перекладки:
    /// панель у неё уже была своё место, и поменялось не дерево, а то, что за
    /// именем стоит. Присвоить то же самое дерево мало — свойство сравнит
    /// ссылки и промолчит, а панель так и не появится.
    /// </remarks>
    private void Edit(Func<DockNode, DockNode> change)
    {
        if (_view.Root is not { } root)
            return;

        var next = change(root);

        if (ReferenceEquals(next, root))
        {
            _view.Refresh();
            return;
        }

        _view.Root = next;
        _dirty = true;

        // Отсчёт начинается заново с каждой правкой: пока границу тянут, писать
        // нечего — итог станет известен, когда её отпустят.
        _writer?.Stop();
        _writer?.Start();
    }
}
