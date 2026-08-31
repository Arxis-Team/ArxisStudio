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
    /// В оторванном окне намертво не стоит ничего.
    /// </summary>
    /// <remarks>
    /// Область документов есть только в главном окне: там пустое место человек
    /// видит и узнаёт. Оторванное окно без вкладок — просто пустая рамка, и
    /// держать её незачем.
    /// </remarks>
    private static readonly IReadOnlySet<string> Nothing =
        new HashSet<string>(StringComparer.Ordinal);

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

    /// <summary>
    /// Оторванные окна.
    /// </summary>
    /// <remarks>
    /// Живые панели у них общие с главным окном, а деревья свои. Имя панели
    /// лежит ровно в одном дереве: у контрола Avalonia один родитель, и панель,
    /// числящаяся в двух местах, встала бы во второе исключением.
    /// </remarks>
    private readonly List<DockFloat> _floats = [];

    /// <summary>
    /// Идёт разбор оторванных окон — их закрытие не возвращает панели домой.
    /// </summary>
    /// <remarks>
    /// Человек, закрывший окно, ждёт панель обратно. Смена набора раскладок —
    /// не закрытие: панели тут же разложит новый набор, и вернуть их сперва
    /// домой значило бы поставить их дважды.
    /// </remarks>
    private bool _sweeping;

    /// <summary>
    /// Студия попрощалась — раскладка записана и больше не пишется.
    /// </summary>
    /// <remarks>
    /// Оторванные окна закрываются вместе с главным, и каждое из них при этом
    /// возвращает панели домой — правка, которую нельзя допустить до файла:
    /// в нём осталась бы раскладка без единого оторванного окна.
    /// </remarks>
    private bool _farewell;


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
        _view.Closing += (_, id) => Closing?.Invoke(this, id);

        Follow(_view);
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели или документа.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Студии есть что сказать человеку о файле раскладки.</summary>
    public event EventHandler<string>? Complained;

    /// <summary>Человек попросил закрыть панель или документ; в поле — имя.</summary>
    public event EventHandler<string>? Closing;

    /// <summary>Живые панели по именам.</summary>
    public DockItems Items { get; } = new();

    /// <summary>
    /// Оторванные окна — как они есть сейчас.
    /// </summary>
    /// <remarks>
    /// Список читают, а не правят: заводит и закрывает окна сама раскладка,
    /// иначе имя панели оказалось бы в двух деревьях разом.
    /// </remarks>
    public IReadOnlyList<DockFloat> Floating => _floats;

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

        _saved[_active] = Current(root);
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

        _saved[_active] = Current(root);
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

        // Оторванных окон при первом запуске нет, а сброс возвращает раскладку
        // именно к нему. Оставь мы окно — панель оказалась бы разом и в нём, и
        // в главном дереве, а родитель у контрола Avalonia ровно один.
        Sweep();

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

        if (_farewell || !_dirty || _store is null || _view.Root is not { } root)
            return;

        _dirty = false;

        if (_store.Save(Snapshot(root)) is { } complaint)
            Complained?.Invoke(this, complaint);
    }

    /// <summary>
    /// Записывает раскладку в последний раз: студия закрывается.
    /// </summary>
    /// <remarks>
    /// Зовётся до того, как закроется хоть одно окно. Дальше раскладка не
    /// пишется вовсе: закрывающиеся оторванные окна вернут панели домой, и эта
    /// правка, дойдя до файла, стёрла бы из него сами окна.
    /// </remarks>
    public void Farewell()
    {
        Flush();

        _farewell = true;
        _writer?.Stop();
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

        // Панель, которая в каком-то дереве уже есть, туда и возвращается: её
        // могли увести в другой угол или в своё окно, а плагин просто
        // перезагрузили — манифест об этом не спрашивают.
        if (Tree(id) is { } known)
        {
            known.Refresh();
            Rehang();

            return;
        }

        Edit(root => Place(root, id, where));
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

        // Документ, уже стоящий в каком-то дереве, туда и достаётся: он мог
        // уехать в своё окно. Поставить его вторично значило бы попросить один
        // контрол в два места, а это исключение.
        if (Tree(id) is { } known)
        {
            known.Refresh();
            Rehang();
            Show(id);

            return;
        }

        Edit(root => DockTree.Attach(root, Home(root), id));
    }

    /// <summary>Убирает одну панель или документ.</summary>
    /// <param name="id">Имя того, что убираем.</param>
    public void Remove(string id)
    {
        Items.Remove(id);
        _asked.RemoveAll(asked => string.Equals(asked.Id, id, StringComparison.Ordinal));
        Edit(root => DockTree.Remove(root, id, _standing));

        foreach (var window in _floats.ToList())
            Change(window, root => DockTree.Remove(root, id, Nothing));
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
        if (Items.RemoveOwnedBy(owner).Count == 0)
            return;

        _view.Refresh();

        foreach (var window in _floats)
            window.View.Refresh();

        Rehang();
    }

    /// <summary>Достаёт панель или документ на видное место.</summary>
    /// <param name="id">Имя того, что показываем.</param>
    public void Show(string id)
    {
        // Панель могла уехать в своё окно, и выбор в главном дереве её там не
        // достанет. Показать — значит показать: окно ещё и поднимается, иначе
        // человек ищет панель, которая всё это время была за спиной у студии.
        if (Torn(id) is { } torn)
        {
            Change(torn, root => DockTree.Select(root, id));

            if (torn.IsVisible)
                torn.Activate();
        }
        else
        {
            Edit(root => DockTree.Select(root, id));
        }

        Chosen?.Invoke(this, id);
    }

    /// <summary>
    /// Выносит панель в своё окно.
    /// </summary>
    /// <remarks>
    /// Окно встаёт там, где вкладку отпустили, и берёт заголовок у неё же:
    /// другого имени у окна с одной панелью нет. Уносить можно и из
    /// оторванного окна — тогда оно опустеет и закроется, а панель поедет
    /// дальше в новом.
    /// </remarks>
    private void TearOff(DockView source, DockDrag drag)
    {
        if (Items.Find(drag.Item) is null)
            return;

        Edit(source, root => DockTree.Remove(root, drag.Item, Standing(source)));

        var window = Float();

        window.View.Root = new DockGroup
        {
            Id = Fresh(),
            Items = [drag.Item],
            Selected = drag.Item,
        };

        window.Position = drag.At;
        Rehang();
    }

    /// <summary>Заводит оторванное окно и подписывается на всё, что в нём делают.</summary>
    private DockFloat Float()
    {
        var window = new DockFloat();

        window.View.Items = Items;

        window.View.Chosen += (_, id) =>
        {
            Change(window, root => DockTree.Select(root, id));
            Chosen?.Invoke(this, id);
        };

        window.View.Resized += (_, resize) =>
            Change(window, root => DockTree.Resize(root, resize.Path, resize.Weights));

        window.View.Closing += (_, id) => Closing?.Invoke(this, id);

        Follow(window.View);

        window.Closed += (_, _) => Sank(window);
        window.PositionChanged += (_, _) => Note();

        _floats.Add(window);

        return window;
    }

    /// <summary>
    /// Возвращает панели закрытого окна домой.
    /// </summary>
    /// <remarks>
    /// Закрыть окно — не значит выбросить панель: другого пути назад у человека
    /// пока нет, и панель, пропавшая вместе с окном, выглядела бы потерей.
    /// </remarks>
    private void Sank(DockFloat window)
    {
        if (_sweeping || !_floats.Remove(window))
            return;

        var items = window.View.Root?.Groups().SelectMany(group => group.Items).ToList() ?? [];

        // Сперва отпускаем контролы: родитель у контрола один, и панель встала
        // бы на новое место исключением, не уйдя со старого.
        window.View.Root = null;

        Edit(root =>
        {
            foreach (var id in items)
                root = Place(root, id, Asked(id));

            return root;
        });
    }

    /// <summary>Показывает оторванные окна, в которых есть чему быть, и прячет пустые.</summary>
    /// <remarks>
    /// Плагин выключили — его окно опустело, но имя панели в дереве осталось,
    /// и окно обязано вернуться, когда плагин включат обратно. Пустое окно на
    /// экране при этом человеку не нужно.
    /// </remarks>
    private void Rehang()
    {
        var owner = TopLevel.GetTopLevel(_view) as Window;

        foreach (var window in _floats.ToList())
        {
            var alive = window.View.Root?.Groups()
                .SelectMany(group => group.Items)
                .Any(id => Items.Find(id) is not null) == true;

            if (!alive)
            {
                window.Hide();
                continue;
            }

            window.Retitle();

            if (window.IsVisible)
                continue;

            if (owner is { IsVisible: true })
                window.Show(owner);
            else
                window.Show();
        }
    }

    /// <summary>Место, о котором панель просила; неизвестная просится вправо.</summary>
    private PluginPlacement Asked(string id) =>
        _asked.FirstOrDefault(asked => string.Equals(asked.Id, id, StringComparison.Ordinal)).Where
        ?? new PluginPlacement();

    /// <summary>
    /// Все деревья студии: сперва оторванных окон, потом главное.
    /// </summary>
    /// <remarks>
    /// Порядок не для красоты. Оторванное окно лежит поверх главного и вполне
    /// может его закрывать; спроси главное первым — и вкладка, брошенная на
    /// видимое поверх всего окно, уехала бы в то, что под ним.
    /// </remarks>
    private IEnumerable<DockView> Views => _floats.Select(window => window.View).Append(_view);

    /// <summary>Что в этом дереве не сносится, даже опустев.</summary>
    /// <remarks>
    /// Область документов есть только в главном окне: там пустое место человек
    /// видит и узнаёт. В оторванном окне пустая рамка не нужна никому.
    /// </remarks>
    private IReadOnlySet<string> Standing(DockView view) =>
        ReferenceEquals(view, _view) ? _standing : Nothing;

    /// <summary>Правит дерево названного вида — главного или оторванного окна.</summary>
    private void Edit(DockView view, Func<DockNode, DockNode> change)
    {
        if (ReferenceEquals(view, _view))
        {
            Edit(change);
            return;
        }

        if (_floats.FirstOrDefault(window => ReferenceEquals(window.View, view)) is { } found)
            Change(found, change);
    }

    /// <summary>Дерево, в котором числится панель; null — нигде.</summary>
    private DockView? Tree(string id) =>
        _view.Root is { } root && DockTree.Holder(root, id) is not null ? _view : Torn(id)?.View;

    /// <summary>Оторванное окно, держащее это имя; null — имя не в окнах.</summary>
    private DockFloat? Torn(string id) =>
        _floats.FirstOrDefault(window =>
            window.View.Root is { } tree && DockTree.Holder(tree, id) is not null);

    /// <summary>Правит дерево оторванного окна; опустевшее окно закрывается само.</summary>
    private void Change(DockFloat window, Func<DockNode, DockNode> change)
    {
        if (window.View.Root is not { } root)
            return;

        var next = change(root);

        if (!ReferenceEquals(next, root))
            window.View.Root = next;

        window.Retitle();
        Note();

        // Окно без единой вкладки закрывается: держать пустую рамку незачем, а
        // имён, которые стоило бы помнить, в нём уже нет.
        if (next.Groups().All(group => group.Items.Count == 0))
            window.Close();
    }

    /// <summary>Помечает раскладку изменившейся и заводит отсчёт до записи.</summary>
    private void Note()
    {
        _dirty = true;
        _writer?.Stop();
        _writer?.Start();
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

    /// <summary>Слушает тягу в этом дереве: вести её и бросать — дело общее.</summary>
    private void Follow(DockView view)
    {
        view.Dragging += (_, drag) => Lead(drag);
        view.Dropped += (source, drag) => Land((DockView)source!, drag);
    }

    /// <summary>
    /// Ведёт вкладку: подсвечивает то дерево, над которым сейчас курсор.
    /// </summary>
    /// <remarks>
    /// Спрашивают все деревья, а подсвечивает одно: курсор в каждый миг над
    /// одним окном. Дерево, начавшее тягу, ничем не выделено — вкладка уже на
    /// полпути в чужое окно, и подсказка обязана быть там же, где курсор.
    /// </remarks>
    private void Lead(DockDrag drag)
    {
        // Показывает одно дерево, и то же самое, которое потом и примет вкладку:
        // окна перекрываются, и под курсором их вполне может быть два.
        var target = Views.FirstOrDefault(view => view.Aim(drag.At, drag.Item) is not null);

        foreach (var view in Views)
        {
            if (ReferenceEquals(view, target))
                view.Show(drag.At, drag.Item);
            else
                view.Clear();
        }
    }

    /// <summary>
    /// Кладёт вкладку туда, где её отпустили.
    /// </summary>
    /// <remarks>
    /// Отпущенная мимо всех деревьев уходит в своё окно: за их пределами
    /// ничего нет, и отпустить там вкладку человек может только нарочно.
    /// </remarks>
    private void Land(DockView source, DockDrag drag)
    {
        // Сперва спрашиваем, потом убираем показанное: вопрос задаётся по той же
        // разметке, что человек и видел, а снятие предпросмотра перекладывает
        // области заново, и мерить по ним до нового прохода нечего.
        var landing = Views
            .Select(view => (View: view, Aim: view.Aim(drag.At, drag.Item)))
            .FirstOrDefault(found => found.Aim is not null);

        foreach (var view in Views)
            view.Clear();

        // Мимо всех деревьев и середина области значат одно: отдельное окно.
        // Разница только в том, где человек отпустил, — а просит он то же самое.
        if (landing.Aim is not { } aim || aim is DockAim.Float)
        {
            TearOff(source, drag);
            return;
        }

        if (ReferenceEquals(landing.View, source))
            Rearrange(source, drag.Item, aim);
        else
            Hand(source, landing.View, drag.Item, aim);
    }

    /// <summary>
    /// Перекладывает вкладку внутри одного дерева.
    /// </summary>
    /// <remarks>
    /// Снять и поставить — две правки, и между ними группа, куда бросали, может
    /// исчезнуть: снятие прибирает опустевшую, а человек мог унести из неё же
    /// последнюю вкладку. Ставить тогда некуда, и правка отменяется целиком —
    /// иначе панель просто пропала бы с экрана.
    /// </remarks>
    private void Rearrange(DockView view, string item, DockAim aim) =>
        Edit(view, root => Landing(view, view, item, aim) ?? root);

    /// <summary>
    /// Передаёт вкладку из одного дерева в другое.
    /// </summary>
    /// <remarks>
    /// Сперва панель уходит из своего дерева и только потом встаёт в чужое:
    /// родитель у контрола Avalonia один, и она встала бы на новое место
    /// исключением, не уйдя со старого. Опустевшее окно при этом закроется —
    /// и правильно сделает: вкладок в нём больше нет.
    /// </remarks>
    private void Hand(DockView from, DockView to, string item, DockAim aim)
    {
        Edit(from, root => DockTree.Remove(root, item, Standing(from)));
        Edit(to, root => Landing(to, from, item, aim) ?? root);

        Rehang();
    }

    /// <summary>
    /// Дерево, каким оно станет, если бросить вкладку сюда; null — ставить некуда.
    /// </summary>
    /// <remarks>
    /// Одна дверь и для предпросмотра, и для настоящей правки: пока их было
    /// две, показанное человеку и полученное им были разными вычислениями — и
    /// расходились.
    /// <para>
    /// Из своего же дерева панель сперва уходит: место ей ищут уже без неё.
    /// Группа, куда целились, при этом может исчезнуть — человек унёс из неё
    /// последнюю вкладку, — и тогда ставить некуда, а правка отменяется целиком.
    /// </para>
    /// </remarks>
    private DockNode? Landing(DockView view, DockView source, string item, DockAim aim)
    {
        if (view.Root is not { } root)
            return null;

        var without = ReferenceEquals(view, source)
            ? DockTree.Remove(root, item, Standing(view))
            : root;

        if (aim is DockAim.Tab tab && DockTree.Group(without, tab.Group) is null)
            return null;

        if (aim is DockAim.Split split && DockTree.Group(without, split.Group) is null)
            return null;

        return DockTree.Apply(without, aim, item, Fresh());
    }

    /// <summary>
    /// Имя для новой группы, которого в дереве ещё нет.
    /// </summary>
    /// <remarks>
    /// Имя попадёт в файл раскладки и переживёт перезапуск, поэтому оно должно
    /// быть своим у каждой группы — и не только в своём окне: вкладки ходят
    /// между окнами, и совпавшие имена сошлись бы в одном дереве. Считаем от
    /// единицы и берём первое свободное: так имена не растут без конца, когда
    /// области заводят и сносят по кругу.
    /// </remarks>
    private string Fresh()
    {
        var taken = Views
            .Select(view => view.Root)
            .OfType<DockNode>()
            .SelectMany(root => root.Groups())
            .Select(group => group.Id)
            .ToHashSet(StringComparer.Ordinal);

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

        Sweep();

        var root = workspace.Root;
        var floating = new List<DockFloat>();

        foreach (var window in workspace.Floating)
        {
            var torn = Float();

            torn.Restore(window);
            floating.Add(torn);
        }

        // Одно имя — одно дерево. В испорченном файле панель могла оказаться и
        // в главном окне, и в оторванном; кто нашёл первым, тот и держит, у
        // остальных имя вычёркивается — иначе контрол попросят в два места.
        var taken = new HashSet<string>(
            root.Groups().SelectMany(group => group.Items), StringComparer.Ordinal);

        foreach (var window in floating)
        {
            if (window.View.Root is not { } tree)
                continue;

            foreach (var id in tree.Groups().SelectMany(group => group.Items).ToList())
            {
                if (!taken.Add(id))
                    tree = DockTree.Remove(tree, id, Nothing);
            }

            window.View.Root = tree;
        }

        foreach (var (id, where) in _asked)
        {
            if (taken.Contains(id))
                continue;

            root = Place(root, id, where);
        }

        _view.Root = root;

        Rehang();
    }

    /// <summary>
    /// Разбирает оторванные окна молча — не возвращая панели домой.
    /// </summary>
    /// <remarks>
    /// Не возвращая потому, что зовущий тут же разложит их сам: и сброс, и смена
    /// набора строят раскладку заново. Вернуть их сперва домой значило бы
    /// поставить их дважды.
    /// <para>
    /// Дерево окна обнуляется до закрытия: так контролы панелей отпускаются, и
    /// новая раскладка вольна взять их себе. Панель, оставшаяся разом в окне и в
    /// главном дереве, кончается исключением — родитель у контрола Avalonia
    /// ровно один.
    /// </para>
    /// </remarks>
    private void Sweep()
    {
        _sweeping = true;

        foreach (var window in _floats.ToList())
        {
            window.View.Root = null;
            window.Close();
        }

        _floats.Clear();
        _sweeping = false;
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
            return DockTree.Attach(root, neighbour.Id, id);

        var side = where.Side.ToLowerInvariant();

        if (DockTree.Group(root, side) is not { } waiting)
            return DockTree.Widen(DockTree.Insert(root, Home(root), Side(side), id, side), side, where.Size);

        var next = DockTree.Attach(root, side, id);

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
            [_active] = Current(root),
        },
    };

    /// <summary>
    /// Показанная раскладка в том виде, в каком её кладут в набор или в файл.
    /// </summary>
    /// <remarks>
    /// Оторванные окна — её часть, и забыть их значит потерять: человек уходит
    /// посмотреть соседний набор, а вернувшись, не находит расставленных окон.
    /// Место у этой сборки одно, чтобы забыть их было негде.
    /// </remarks>
    private DockWorkspace Current(DockNode root) => new()
    {
        Root = root,
        DocumentHome = _home,
        Floating = [.. _floats.Select(window => window.Snapshot())],
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
