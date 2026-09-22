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
/// показывает. Сегмент крошек над колонкой — такой же каталог: отпущенное на нём ложится на этот
/// уровень пути, как на адресную строку проводника и Finder. Решение, папка решения, зависимости,
/// пустое место под строками дерева и между крошками целью не бывают — курсор говорит «нельзя».
/// Правила — <see cref="Dropping"/>; класть — дорогой вставки (<see cref="Editing.DropAsync"/>):
/// копией, с вопросом о занятом имени, одним действием истории.
/// </para>
/// <para>
/// <b>Как видно.</b> Отмечается цель, а не то, над чем курсор: строка каталога в дереве, его плитка,
/// его сегмент крошек и вся колонка, если она его показывает. Над файлом загорается строка его
/// каталога — так видно, куда ляжет принесённое.
/// </para>
/// <para>
/// <b>Дорога к цели.</b> Свёрнутый каталог, над которым держат файлы <see cref="ExpandDelay"/>,
/// раскрывается, как в дереве Rider и в проводнике. У верхнего и нижнего края списка он
/// прокручивается на строку каждые <see cref="ScrollEvery"/>, пока курсор у края: колесо мыши, пока
/// несут файлы, не работает, и без этого до каталога за краем было бы не добраться. «…» крошек, над
/// которой держат столько же, раскрывает меню спрятанных уровней, как выпадающий список адресной
/// строки проводника, и его пункты — такие же цели, как сегменты. Закрывается меню, когда несут в
/// другое место, при сбросе и когда несомое ушло из окна и не вернулось: уход из окна в само меню —
/// тоже уход, и настоящий от него отличает возвращение, которое приходит следом (<see cref="FoldDelay"/>).
/// </para>
/// <para>
/// Мышь — не единственная дорога: скопированное в проводнике вставляется в окно Ctrl+V той же
/// правкой (WCAG 2.5.7).
/// </para>
/// <para>
/// <b>Изнутри окна</b> несёт <see cref="FileDrag"/> и спрашивает здесь же — <see cref="Hover"/> и
/// <see cref="Place"/>: цель, отметка, раскрытие и прокрутка у обоих источников одни. Разница только в
/// том, чем кладут: принесённое из проводника копируется, перенесённое внутри окна переносится.
/// </para>
/// </remarks>
internal sealed class FileDrop : IDisposable
{
    /// <summary>Сколько держать файлы над свёрнутым каталогом, чтобы он раскрылся.</summary>
    /// <remarks>Короче — ветки раскрывались бы на пролёте курсора по дереву; дольше — ждать приходится заметно.</remarks>
    internal static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>Как часто список прокручивается на строку, пока файлы держат у его края.</summary>
    internal static readonly TimeSpan ScrollEvery = TimeSpan.FromMilliseconds(60);

    /// <summary>Сколько ждать несомое, ушедшее из окна, прежде чем закрыть меню спрятанных уровней.</summary>
    /// <remarks>
    /// Уход из окна в само меню — тоже уход: меню лежит в своём окне, и событие от него приходит
    /// следом, через считаные миллисекунды. Закрывать сразу значило бы закрывать меню под курсором;
    /// не закрывать вовсе — оставлять его висеть за брошенной тягой.
    /// </remarks>
    internal static readonly TimeSpan FoldDelay = TimeSpan.FromMilliseconds(250);

    private readonly ProjectPanelView _view;
    private readonly ProjectModel _model;
    private readonly Func<bool> _ready;
    private readonly Action<CanonicalPath?> _mark;
    private readonly Action<IReadOnlyList<ClipItem>, CanonicalPath, EditOrigin> _drop;
    private readonly Action<Row> _expand;
    private readonly DispatcherTimer _expanding;
    private readonly DispatcherTimer _scrolling;
    private readonly DispatcherTimer _unfolding;
    private readonly DispatcherTimer _folding;

    private IDataTransfer? _data;
    private IReadOnlyList<ClipItem> _items = [];
    private Row? _waiting;
    private bool _lingering;
    private AxListBox? _edgeList;
    private int _edge;
    private int _generation;

    /// <summary>Подключает перетаскивание к дереву, к обоим видам правой колонки и к её крошкам.</summary>
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
        _unfolding = new DispatcherTimer { Interval = ExpandDelay };
        _unfolding.Tick += OnUnfoldingTick;
        _folding = new DispatcherTimer { Interval = FoldDelay };
        _folding.Tick += OnFoldingTick;

        foreach (var surface in Surfaces)
        {
            DragDrop.SetAllowDrop(surface, true);
            surface.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
            surface.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            surface.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            surface.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    /// <summary>Куда несут: дерево, оба вида правой колонки и крошки над ней.</summary>
    internal Control[] Surfaces => [_view.Tree, _view.Tiles, _view.Files, _view.Path];

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var surface in Surfaces)
        {
            DragDrop.SetAllowDrop(surface, false);
            surface.RemoveHandler(DragDrop.DragEnterEvent, OnDragOver);
            surface.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            surface.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            surface.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }

        Stop();
        Shut();

        _expanding.Tick -= OnExpandingTick;
        _scrolling.Tick -= OnScrollingTick;
        _unfolding.Tick -= OnUnfoldingTick;
        _folding.Tick -= OnFoldingTick;
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
    /// Раскрывает меню спрятанных уровней крошек, над «…» которых держат несомое, — то, что делает
    /// таймер по истечении <see cref="ExpandDelay"/>; тестам — вместо ожидания.
    /// </summary>
    internal void Unfold()
    {
        _unfolding.Stop();

        if (_lingering)
        {
            _lingering = false;
            _view.Path.OpenOverflow();
        }
    }

    /// <summary>
    /// Закрывает меню спрятанных уровней за несомым, ушедшим из окна, — то, что делает таймер по
    /// истечении <see cref="FoldDelay"/>; тестам — вместо ожидания.
    /// </summary>
    /// <remarks>Несомое вернулось — закрывать нечего: подлёт таймер снял.</remarks>
    internal void Fold()
    {
        if (_folding.IsEnabled)
            Shut();
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

    /// <summary>
    /// Несомое над точкой поверхности: отмечает, куда оно ляжет, заводит раскрытие и прокрутку и
    /// говорит, что сделает отпускание.
    /// </summary>
    /// <param name="surface">Список или крошки под курсором — одна из <see cref="Surfaces"/>.</param>
    /// <param name="point">Точка в координатах поверхности.</param>
    /// <param name="items">Несомое; пусто — нести нечего.</param>
    /// <param name="mode">Перенос или копия.</param>
    /// <returns>Перенос, копия или ничего — чем ответить курсору.</returns>
    internal DragDropEffects Hover(Control surface, Point point, IReadOnlyList<ClipItem> items, ClipMode mode)
    {
        // Уход, отложенный до конца события, отменён: курсор перешёл на соседний элемент того же окна.
        // Несомое здесь же, и ожидание закрытия меню снято: уход был не из окна, а в само меню.
        _generation++;
        _folding.Stop();

        var crumbs = ReferenceEquals(surface, _view.Path);

        // Меню спрятанных уровней — часть крошек: понесли в другое место — оно закрывается.
        if (!crumbs)
        {
            Linger(false);
            Shut();
        }

        var item = ItemAt(surface, point);
        var folder = Target(surface, item, items, mode);

        _mark(folder);

        // Нести нечего — ни раскрывать, ни прокручивать незачем: над деревом тащат текст, а не файлы.
        // Крошкам и то и другое не нужно: каталоги пути раскрыты, и ряд не листается, — зато «…»
        // раскрывает меню спрятанных уровней.
        if (items.Count == 0 || surface is not AxListBox list)
        {
            Wait(null);
            Edge(null, 0);
            Linger(items.Count > 0 && !_view.Path.IsOverflowOpen && _view.Path.IsOverflowAt(surface.PointToScreen(point)));
        }
        else
        {
            Wait(ReferenceEquals(list, _view.Tree) ? item as Row : null);
            Edge(list, point.Y < Band(list) ? -1 : point.Y > list.Bounds.Height - Band(list) ? 1 : 0);
        }

        return folder is null ? DragDropEffects.None : mode == ClipMode.Cut ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    /// <summary>Отпускание несомого над точкой поверхности: куда оно ляжет; отметка и таймеры снимаются.</summary>
    /// <param name="surface">Список или крошки под курсором.</param>
    /// <param name="point">Точка в координатах поверхности.</param>
    /// <param name="items">Несомое.</param>
    /// <param name="mode">Перенос или копия.</param>
    /// <returns>Каталог назначения; пусто — отпустили там, где класть некуда.</returns>
    internal CanonicalPath? Place(Control surface, Point point, IReadOnlyList<ClipItem> items, ClipMode mode)
    {
        _generation++;

        var folder = Target(surface, ItemAt(surface, point), items, mode);

        Stop();
        Shut();

        return folder;
    }

    /// <summary>
    /// Тягу изнутри окна бросили или отпустили мимо: отметка, раскрытие и прокрутка снимаются, меню
    /// спрятанных уровней закрывается.
    /// </summary>
    internal void Clear()
    {
        Stop();
        Shut();
    }

    /// <summary>Откуда пришёл сброс — туда встанет выделение после правки.</summary>
    /// <param name="surface">Список или крошки, над которыми отпустили: крошки — это колонка.</param>
    internal EditOrigin Origin(Control surface) => ReferenceEquals(surface, _view.Tree) ? EditOrigin.Tree : EditOrigin.Pane;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control surface)
            return;

        Carried(e.DataTransfer);

        // Из проводника окно только копирует: источник, копии не разрешивший, не несёт ничего.
        e.DragEffects = Hover(surface, e.GetPosition(surface), Offered(e), ClipMode.Copy);
        e.Handled = true;
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

        // Меню живёт в своём окне, и уход к нему приходит сюда же: его закрывает не уборка, а
        // ожидание — несомое, которое не вернулось.
        if (_view.Path.IsOverflowOpen && !_folding.IsEnabled)
            _folding.Start();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control surface)
            return;

        Carried(e.DataTransfer);

        var items = Offered(e);
        var folder = Place(surface, e.GetPosition(surface), items, ClipMode.Copy);

        e.DragEffects = folder is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;

        if (folder is { } target)
            _drop(items, target, Origin(surface));
    }

    /// <summary>Принесённое, если источник разрешил копию; иначе — ничего.</summary>
    private IReadOnlyList<ClipItem> Offered(DragEventArgs e) => (e.DragEffects & DragDropEffects.Copy) != 0 ? _items : [];

    /// <summary>Каталог, в который ляжет несомое, если отпустить над этим; пусто — некуда.</summary>
    /// <remarks>
    /// Пустое место колонки — её каталог; пустое место дерева и ряда крошек — ничей: между сегментами
    /// пути каталога нет.
    /// </remarks>
    private CanonicalPath? Target(Control surface, object? item, IReadOnlyList<ClipItem> items, ClipMode mode)
    {
        if (!_ready() || items.Count == 0)
            return null;

        var column = ReferenceEquals(surface, _view.Tiles) || ReferenceEquals(surface, _view.Files);
        var folder = item switch
        {
            Row row => Pasting.Folder(row.Node),
            Tile tile => Pasting.Folder(tile.Node),
            Segment segment => Pasting.Folder(segment.Node),
            null when column && _model.IsBrowsing && _model.Browser.Current is { } current => Pasting.Folder(current),
            _ => null,
        };

        return folder is { } target && Dropping.Fits(items, target, mode) ? target : null;
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

    /// <summary>Строка, плитка или сегмент крошек под точкой поверхности; пусто — под точкой пустое место.</summary>
    /// <remarks>
    /// Сегмент крошки называют сами, по точке экрана: он бывает и спрятанным, и тогда под курсором —
    /// его пункт в раскрытом меню, отдельном окне. Кнопка переполнения сегментом не бывает.
    /// </remarks>
    private static object? ItemAt(Control surface, Point point) => surface is AxBreadcrumb crumbs
        ? crumbs.SegmentAt(crumbs.PointToScreen(point))?.DataContext
        : (surface.InputHitTest(point) as Visual)?.GetSelfAndVisualAncestors()
            .FirstOrDefault(visual => visual is ListBoxItem) is StyledElement { DataContext: var data } ? data : null;

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

    /// <summary>Заводит раскрытие меню спрятанных уровней над «…» или снимает его.</summary>
    private void Linger(bool over)
    {
        if (over == _lingering)
            return;

        _lingering = over;
        _unfolding.Stop();

        if (over)
            _unfolding.Start();
    }

    /// <summary>Закрывает меню спрятанных уровней и снимает ожидание его закрытия.</summary>
    private void Shut()
    {
        _folding.Stop();
        _view.Path.CloseOverflow();
    }

    /// <summary>Снимает отметку, ожидание и прокрутку и забывает принесённое.</summary>
    /// <remarks>
    /// Меню спрятанных уровней остаётся: уход из окна в него — тоже уход, и закрывает меню ожидание
    /// (<see cref="Fold"/>), а не уборка.
    /// </remarks>
    private void Stop()
    {
        _expanding.Stop();
        _scrolling.Stop();
        Linger(false);
        _waiting = null;
        _edgeList = null;
        _edge = 0;
        _data = null;
        _items = [];
        _mark(null);
    }

    private void OnExpandingTick(object? sender, EventArgs e) => Expand();

    private void OnScrollingTick(object? sender, EventArgs e) => Scroll();

    private void OnUnfoldingTick(object? sender, EventArgs e) => Unfold();

    private void OnFoldingTick(object? sender, EventArgs e) => Fold();

    /// <summary>
    /// Полоса у края, где держат файлы, чтобы список ехал, — она же шаг прокрутки: высота строки
    /// списка, какой её задала плотность.
    /// </summary>
    private static double Band(Control list) =>
        list.TryFindResource("AxRowHeight", list.ActualThemeVariant, out var value) && value is double height ? height : 0;

    private static ScrollViewer? Viewer(AxListBox list) => list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
}
