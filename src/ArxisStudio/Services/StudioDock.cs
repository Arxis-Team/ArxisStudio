using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
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
    }

    /// <summary>Человек выбрал вкладку; в поле — имя панели или документа.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Студии есть что сказать человеку о файле раскладки.</summary>
    public event EventHandler<string>? Complained;

    /// <summary>Живые панели по именам.</summary>
    public DockItems Items { get; } = new();

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

        _home = string.IsNullOrEmpty(workspace.DocumentHome) ? Documents : workspace.DocumentHome;
        _standing = new HashSet<string>([_home], StringComparer.Ordinal);
        _view.EmptyGroup = _home;
        _view.Root = workspace.Root;
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
    /// <param name="zone">Пожелание из манифеста: left, right или bottom.</param>
    /// <param name="title">Заголовок из манифеста; ключ вида <c>%panel.main%</c> переводится.</param>
    /// <param name="strings">Словари плагина, которому принадлежит панель.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    /// <remarks>
    /// Место из манифеста — пожелание для незнакомого имени, а не приказ на
    /// каждый запуск. У панели, которая в дереве уже есть, место своё: её увели
    /// в другой угол, или плагин просто перезагрузили — манифест об этом не
    /// спрашивают, иначе каждый подъём затаскивал бы панели обратно.
    /// </remarks>
    public void Add(string owner, string id, string zone, string title, PluginStrings strings, Control content)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var item = new DockItem(id, content);

        // Заголовок — единственный текст панели, который показывает не её автор,
        // а студия, поэтому и переводить его при смене языка — забота студии.
        if (PluginStrings.IsKey(title, out var key))
            item.Bind(DockItem.TitleProperty, strings.Text(key));
        else
            item.Title = title;

        Items.Add(owner, item);

        var group = zone.ToLowerInvariant();

        Edit(root => DockTree.Holder(root, id) is not null
            ? root
            : DockTree.Group(root, group) is not null
                ? DockTree.Insert(root, group, DockSide.Tab, id, group)
                : DockTree.Insert(root, Home(root), Side(group), id, group));
    }

    /// <summary>Открывает документ вкладкой в области документов.</summary>
    /// <param name="owner">Чей редактор его построил.</param>
    /// <param name="id">Имя документа.</param>
    /// <param name="title">Подпись вкладки.</param>
    /// <param name="content">Содержимое, построенное редактором.</param>
    public void Open(string owner, string id, string title, Control content)
    {
        Items.Add(owner, new DockItem(id, content) { Title = title });

        Edit(root => DockTree.Insert(root, Home(root), DockSide.Tab, id, Documents));
    }

    /// <summary>Убирает одну панель или документ.</summary>
    /// <param name="id">Имя того, что убираем.</param>
    public void Remove(string id)
    {
        Items.Remove(id);
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

    /// <summary>Сторона по названию зоны; незнакомое слово уводит вправо.</summary>
    private static DockSide Side(string zone) => zone switch
    {
        "left" => DockSide.Left,
        "bottom" => DockSide.Bottom,
        _ => DockSide.Right,
    };

    /// <summary>
    /// Раскладка в том виде, в каком она ложится в файл.
    /// </summary>
    /// <remarks>
    /// Набор пока один — именованные наборы будут отдельным шагом, — но поле
    /// под них в формате есть с самого начала: дописать его потом значило бы
    /// поднимать версию файла ради того, что было известно заранее.
    /// </remarks>
    private DockLayout Snapshot(DockNode root) => new()
    {
        Active = DockLayout.DefaultName,
        Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
        {
            [DockLayout.DefaultName] = new() { Root = root, DocumentHome = _home },
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
