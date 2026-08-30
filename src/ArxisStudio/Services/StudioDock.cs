using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using Avalonia;
using Avalonia.Controls;

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
    /// <summary>Имя группы, куда открываются документы.</summary>
    public const string Documents = "documents";

    /// <summary>
    /// Группы, которые не сносятся, даже опустев.
    /// </summary>
    /// <remarks>
    /// Область документов человек видит и узнаёт. Исчезни она вместе с
    /// последней закрытой вкладкой — следующий документ появился бы неизвестно
    /// где, а на её месте было бы пусто без объяснений.
    /// </remarks>
    private static readonly IReadOnlySet<string> Standing =
        new HashSet<string>([Documents], StringComparer.Ordinal);

    private readonly DockView _view;

    /// <summary>Заводит раскладку над видом.</summary>
    /// <param name="view">Вид, который её показывает.</param>
    public StudioDock(DockView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
        _view.Items = Items;
        _view.EmptyGroup = Documents;
        _view.Root = Skeleton();

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

    /// <summary>Живые панели по именам.</summary>
    public DockItems Items { get; } = new();

    /// <summary>Что выбрано в области документов; null — ничего или не документ.</summary>
    public string? Showing => _view.Root is { } root ? DockTree.Group(root, Documents)?.Selected : null;

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
        Edit(root => DockTree.Remove(root, id, Standing));
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

    /// <summary>От какой группы отмерять место для новой.</summary>
    private static string Home(DockNode root) =>
        DockTree.Group(root, Documents)?.Id ?? root.Groups().First().Id;

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
            _view.Refresh();
        else
            _view.Root = next;
    }
}
