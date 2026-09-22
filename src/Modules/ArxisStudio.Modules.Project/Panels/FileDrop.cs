using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.ProjectSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Файлы, которые несут в окно проекта из проводника: куда они лягут, как это видно и что с ними
/// делать, когда их отпустят.
/// </summary>
/// <remarks>
/// <para>
/// <b>Куда.</b> Как у Rider и Unity: на каталог и проект — в них, на файл — в его каталог, в правой
/// колонке на плитку каталога — в него, на файл и на пустое место — в каталог, который колонка
/// показывает. Решение, папка решения, зависимости и пустое место под строками дерева целью не
/// бывают — курсор говорит «нельзя». Правила — <see cref="Dropping"/>; класть — дорогой вставки
/// (<see cref="Editing.DropAsync"/>): копией, с вопросом о занятом имени, одним действием истории.
/// </para>
/// <para>
/// <b>Как видно.</b> Отмечается цель, а не то, над чем курсор: строка каталога в дереве, его плитка и
/// вся колонка, если она его показывает. Над файлом загорается строка его каталога — так видно, куда
/// ляжет принесённое.
/// </para>
/// <para>
/// <b>Дорога к цели.</b> Свёрнутый каталог, над которым держат файлы <see cref="ExpandDelay"/>,
/// раскрывается, как в дереве Rider и в проводнике. У верхнего и нижнего края списка он
/// прокручивается на строку каждые <see cref="ScrollEvery"/>, пока курсор у края: колесо мыши, пока
/// несут файлы, не работает, и без этого до каталога за краем было бы не добраться.
/// </para>
/// <para>
/// Мышь — не единственная дорога: скопированное в проводнике вставляется в окно Ctrl+V той же
/// правкой (WCAG 2.5.7).
/// </para>
/// </remarks>
internal sealed class FileDrop : IDisposable
{
    /// <summary>Сколько держать файлы над свёрнутым каталогом, чтобы он раскрылся.</summary>
    /// <remarks>Короче — ветки раскрывались бы на пролёте курсора по дереву; дольше — ждать приходится заметно.</remarks>
    internal static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>Как часто список прокручивается на строку, пока файлы держат у его края.</summary>
    internal static readonly TimeSpan ScrollEvery = TimeSpan.FromMilliseconds(60);

    private readonly ProjectPanelView _view;
    private readonly ProjectModel _model;
    private readonly Func<bool> _ready;
    private readonly Action<CanonicalPath?> _mark;
    private readonly Action<IReadOnlyList<ClipItem>, CanonicalPath, EditOrigin> _drop;
    private readonly Action<Row> _expand;
    private readonly DispatcherTimer _expanding;
    private readonly DispatcherTimer _scrolling;

    private IDataTransfer? _data;
    private IReadOnlyList<ClipItem> _items = [];
    private Row? _waiting;
    private AxListBox? _edgeList;
    private int _edge;
    private int _generation;

    /// <summary>Подключает перетаскивание к дереву и к обоим видам правой колонки.</summary>
    /// <param name="view">Разметка окна.</param>
    /// <param name="model">Модель окна.</param>
    /// <param name="ready">Можно ли класть сейчас: решение открыто и правка не занята.</param>
    /// <param name="mark">Отмечает каталог назначения; пусто — снимает отметку.</param>
    /// <param name="drop">Кладёт принесённое в каталог; откуда — туда и встанет выделение.</param>
    /// <param name="expand">Раскрывает строку дерева.</param>
    public FileDrop(
        ProjectPanelView view,
        ProjectModel model,
        Func<bool> ready,
        Action<CanonicalPath?> mark,
        Action<IReadOnlyList<ClipItem>, CanonicalPath, EditOrigin> drop,
        Action<Row> expand)
    {
        _view = view;
        _model = model;
        _ready = ready;
        _mark = mark;
        _drop = drop;
        _expand = expand;

        _expanding = new DispatcherTimer { Interval = ExpandDelay };
        _expanding.Tick += OnExpandingTick;
        _scrolling = new DispatcherTimer { Interval = ScrollEvery };
        _scrolling.Tick += OnScrollingTick;

        foreach (var list in Lists)
        {
            DragDrop.SetAllowDrop(list, true);
            list.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
            list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            list.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            list.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private AxListBox[] Lists => [_view.Tree, _view.Tiles, _view.Files];

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var list in Lists)
        {
            DragDrop.SetAllowDrop(list, false);
            list.RemoveHandler(DragDrop.DragEnterEvent, OnDragOver);
            list.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            list.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            list.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }

        Stop();

        _expanding.Tick -= OnExpandingTick;
        _scrolling.Tick -= OnScrollingTick;
    }

    /// <summary>
    /// Раскрывает свёрнутый каталог, над которым держат файлы, — то, что делает таймер по истечении
    /// <see cref="ExpandDelay"/>; тестам — вместо ожидания.
    /// </summary>
    internal void Expand()
    {
        _expanding.Stop();

        if (_waiting is { IsExpanded: false } row)
        {
            _waiting = null;
            _expand(row);
        }
    }

    /// <summary>
    /// Прокручивает список на строку к краю, у которого держат файлы, — шаг таймера; тестам — вместо
    /// ожидания.
    /// </summary>
    internal void Scroll()
    {
        if (_edgeList is not { } list || _edge == 0 || Viewer(list) is not { } viewer)
        {
            _scrolling.Stop();
            return;
        }

        var bottom = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var y = Math.Clamp(viewer.Offset.Y + (_edge * Band(list)), 0, bottom);

        viewer.Offset = viewer.Offset.WithY(y);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not AxListBox list)
            return;

        // Уход, отложенный до конца события, отменён: курсор перешёл на соседний элемент того же окна.
        _generation++;

        Carried(e.DataTransfer);

        var point = e.GetPosition(list);
        var item = ItemAt(list, point);
        var folder = Target(list, item);

        e.DragEffects = folder is not null && (e.DragEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        _mark(e.DragEffects == DragDropEffects.Copy ? folder : null);

        // Нести нечего — ни раскрывать, ни прокручивать незачем: над деревом тащат текст, а не файлы.
        if (_items.Count == 0)
        {
            Wait(null);
            Edge(null, 0);
            return;
        }

        Wait(ReferenceEquals(list, _view.Tree) ? item as Row : null);
        Edge(list, point.Y < Band(list) ? -1 : point.Y > list.Bounds.Height - Band(list) ? 1 : 0);
    }

    /// <remarks>
    /// Переход курсора с одной строки на другую тоже приносит уход — и сразу за ним новый подлёт в
    /// том же событии. Уборка поэтому откладывается до конца события: подлёт, пришедший следом, её
    /// отменяет, и отметка с таймерами не мигают на каждой границе строк. Уход за край окна и отказ
    /// клавишей Esc подлёта за собой не несут — тогда уборка проходит.
    /// </remarks>
    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        var generation = ++_generation;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (generation == _generation)
                    Stop();
            },
            DispatcherPriority.Background);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not AxListBox list)
            return;

        _generation++;

        Carried(e.DataTransfer);

        var items = _items;
        var folder = Target(list, ItemAt(list, e.GetPosition(list)));
        var copy = (e.DragEffects & DragDropEffects.Copy) != 0;

        e.DragEffects = copy && folder is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        Stop();

        if (copy && folder is { } target)
            _drop(items, target, ReferenceEquals(list, _view.Tree) ? EditOrigin.Tree : EditOrigin.Pane);
    }

    /// <summary>Каталог, в который ляжет принесённое, если отпустить над этим; пусто — некуда.</summary>
    private CanonicalPath? Target(AxListBox list, object? item)
    {
        if (!_ready() || _items.Count == 0)
            return null;

        var folder = item switch
        {
            Row row => Pasting.Folder(row.Node),
            Tile tile => Pasting.Folder(tile.Node),
            null when !ReferenceEquals(list, _view.Tree) && _model.IsBrowsing && _model.Browser.Current is { } current => Pasting.Folder(current),
            _ => null,
        };

        return folder is { } target && Dropping.Fits(_items, target) ? target : null;
    }

    /// <summary>Читает принесённое один раз на перетаскивание: подлёт приходит на каждое движение мыши.</summary>
    private void Carried(IDataTransfer data)
    {
        if (ReferenceEquals(data, _data))
            return;

        _data = data;
        _items = data.TryGetFiles() is { Length: > 0 } files
            ? Dropping.Items(files.Select(file => file.TryGetLocalPath()).OfType<string>())
            : [];
    }

    /// <summary>Строка или плитка под точкой списка; пусто — под точкой пустое место.</summary>
    private static object? ItemAt(AxListBox list, Point point) =>
        (list.InputHitTest(point) as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext;

    /// <summary>Заводит ожидание раскрытия для свёрнутой строки с детьми; другая строка — ожидание заново.</summary>
    private void Wait(Row? row)
    {
        var waiting = row is { HasChildren: true, IsExpanded: false } ? row : null;

        if (ReferenceEquals(waiting, _waiting))
            return;

        _waiting = waiting;
        _expanding.Stop();

        if (waiting is not null)
            _expanding.Start();
    }

    /// <summary>Заводит прокрутку у края списка или снимает её: −1 — вверх, 1 — вниз, 0 — не у края.</summary>
    private void Edge(AxListBox? list, int edge)
    {
        _edgeList = edge == 0 ? null : list;
        _edge = edge;

        if (edge == 0)
            _scrolling.Stop();
        else if (!_scrolling.IsEnabled)
            _scrolling.Start();
    }

    /// <summary>Снимает отметку, ожидание и прокрутку и забывает принесённое.</summary>
    private void Stop()
    {
        _expanding.Stop();
        _scrolling.Stop();
        _waiting = null;
        _edgeList = null;
        _edge = 0;
        _data = null;
        _items = [];
        _mark(null);
    }

    private void OnExpandingTick(object? sender, EventArgs e) => Expand();

    private void OnScrollingTick(object? sender, EventArgs e) => Scroll();

    /// <summary>
    /// Полоса у края, где держат файлы, чтобы список ехал, — она же шаг прокрутки: высота строки
    /// списка, какой её задала плотность.
    /// </summary>
    private static double Band(Control list) =>
        list.TryFindResource("AxRowHeight", list.ActualThemeVariant, out var value) && value is double height ? height : 0;

    private static ScrollViewer? Viewer(AxListBox list) => list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
}
