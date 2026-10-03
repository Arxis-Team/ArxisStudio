using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Looks;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Файлы и каталоги, которые несут мышью из окна проекта: с чего тяга начинается, что несут, куда и чем
/// она кончается.
/// </summary>
/// <remarks>
/// <para>
/// <b>Тяга.</b> Как у Rider и Unity: нажать на строку или плитку файла или каталога и повести дальше
/// <see cref="Threshold"/> — порога вкладок докинга: щелчок почти всегда сдвигает мышь на пиксель-другой.
/// Несут то, что взяла бы правка (<see cref="EditSelection"/>): весь выбор, вложенные — с владельцем.
/// Решение, папки решения, проекты и зависимости не несут: правке они не отдаются.
/// </para>
/// <para>
/// <b>Выбор.</b> Список сбрасывает множественный выбор уже на нажатии, и группу было бы не утащить.
/// Нажатие по выбранному внутри группы поэтому выбора не трогает, как в проводнике, а щелчок без тяги
/// сводит его к одной строке при отпускании.
/// </para>
/// <para>
/// <b>Внутри окна.</b> Цель, отметку, раскрытие и прокрутку считает <see cref="FileDrop"/> — те же, что у
/// файлов из проводника: строки, плитки и сегменты крошек над колонкой — плитку несут и на уровень
/// выше, как на адресную строку проводника. Отпущенное переносится, с Ctrl — копируется, как в
/// проводнике и Rider; Ctrl, нажатый или отпущенный на месте, меняет это сразу.
/// </para>
/// <para>
/// <b>За край окна.</b> Над чужим несомое становится тягой студии (<see cref="IStudioDragSession"/>):
/// цель — чужая панель, объявившая себя целью, например доска дизайнера, — получает пути выбора
/// (<see cref="StudioDataFormats.Files"/>) и отвечает сама. Чужим разрешены копия и ссылка, но не
/// перенос: перенос значил бы, что окно удалит свои файлы, когда цель их заберёт, а это правка
/// решения мимо его истории. Отпущенное над чужим окну проекта ничего не делает — сделала цель.
/// </para>
/// <para>
/// <b>Как видно.</b> Подсказку у курсора и курсор ведёт студия — над своим и над чужим одинаково, и над
/// оторванным окном тоже: значок и имя того, за что взялись, и сколько ещё, а под ними ответ чужой цели.
/// Курсор говорит, что сделает отпускание: перенос, копия, ссылка или «нельзя». Esc и потеря захвата
/// тягу бросают.
/// </para>
/// <para>
/// Тяга своя, на захвате указателя, как у вкладок докинга, а не системная: несут только между окнами
/// студии, и мышь, которую изображают инструменты студии и тесты, ведёт её так же, как настоящая. Дорога
/// без мыши — вырезать и вставить (WCAG 2.5.7).
/// </para>
/// </remarks>
internal sealed class FileDrag : IDisposable
{
    /// <summary>Сколько пройти мышью с нажатой кнопкой, чтобы это было тягой, а не щелчком.</summary>
    internal const double Threshold = StudioDragDrop.Threshold;

    /// <summary>Что окно разрешает чужим целям: взять копию или сослаться, но не забрать.</summary>
    internal const DragDropEffects Outside = DragDropEffects.Copy | DragDropEffects.Link;

    private readonly ProjectPanelView _view;
    private readonly FileDrop _drop;
    private readonly IStudioStrings _strings;
    private readonly IStudioDragDrop? _drags;
    private readonly Action<FileClip, CanonicalPath, EditOrigin> _carry;

    private Press? _pressed;
    private AxListBox? _source;
    private IPointer? _pointer;
    private IStudioDragSession? _session;
    private IReadOnlyList<ClipItem> _items = [];
    private Point _at;
    private KeyModifiers _keys;
    private Control? _over;
    private Point _overAt;

    /// <summary>Подключает тягу к дереву и к обоим видам правой колонки.</summary>
    /// <param name="view">Разметка окна.</param>
    /// <param name="drop">Разбор цели — общий с файлами из проводника.</param>
    /// <param name="strings">Словари модуля: подпись у курсора.</param>
    /// <param name="drags">Тяга студии: подсказка, курсор и чужие цели; пусто — несут только внутри, и не видно что.</param>
    /// <param name="carry">Переносит или копирует несомое в каталог; откуда — туда встанет выделение.</param>
    /// <remarks>
    /// Можно ли класть сейчас — открыто ли решение и свободна ли правка, — решает разбор цели: пока
    /// правка занята, своей целью не становится ничто, и курсор над окном говорит «нельзя».
    /// </remarks>
    public FileDrag(
        ProjectPanelView view,
        FileDrop drop,
        IStudioStrings strings,
        IStudioDragDrop? drags,
        Action<FileClip, CanonicalPath, EditOrigin> carry)
    {
        _view = view;
        _drop = drop;
        _strings = strings;
        _drags = drags;
        _carry = carry;

        foreach (var list in Lists)
        {
            list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        }

        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary>Что сделает отпускание сейчас: перенос, копия, ссылка или ничего; тестам.</summary>
    internal DragDropEffects Effect { get; private set; }

    private AxListBox[] Lists => [_view.Tree, _view.Tiles, _view.Files];

    /// <inheritdoc/>
    public void Dispose()
    {
        Cancel();

        foreach (var list in Lists)
        {
            list.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            list.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            list.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            list.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        }

        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _view.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;

        if (sender is not AxListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: { } item } container
            || NodeOf(item) is not { } node
            || !EditSelection.IsEditable(node))
        {
            return;
        }

        var plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0;
        var collapse = plain && container.IsSelected && list.SelectedItems is { Count: > 1 };

        if (collapse)
        {
            e.Handled = true;
            container.Focus(NavigationMethod.Pointer);
        }

        _pressed = new Press(list, e.GetPosition(list), item, collapse);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_source is { } source)
        {
            if (TopLevel.GetTopLevel(source) is { } top)
                Follow(e.GetPosition(top), e.KeyModifiers);

            return;
        }

        if (_pressed is not { } pressed || !ReferenceEquals(sender, pressed.List))
            return;

        if (!e.GetCurrentPoint(pressed.List).Properties.IsLeftButtonPressed)
        {
            _pressed = null;
            return;
        }

        var point = e.GetPosition(pressed.List);

        if (Math.Abs(point.X - pressed.At.X) < Threshold && Math.Abs(point.Y - pressed.At.Y) < Threshold)
            return;

        Start(pressed, e);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_source is { } source)
        {
            if (TopLevel.GetTopLevel(source) is { } top)
                Follow(e.GetPosition(top), e.KeyModifiers);

            var over = _over;
            var items = _items;
            var mode = ModeOf(_keys);

            // Сперва — куда легло, потом — отпустить захват: отпускание синхронно поднимает «захват
            // потерян», а тот тягу бросает, и спрашивать «куда отпустили» было бы уже не у кого. Над
            // чужим кладёт цель — тем же отпусканием, пока сеанс открыт.
            var folder = over is null ? null : _drop.Place(over, _overAt, items, mode);

            if (over is null)
                _session?.Drop();

            End();
            e.Handled = true;

            if (over is not null && folder is { } target)
                _carry(new FileClip(mode, items), target, _drop.Origin(over));

            return;
        }

        // Щелчок без тяги по выбранному внутри группы — щелчок: выбор сводится к этой строке.
        if (_pressed is { Collapse: true } pressed && ReferenceEquals(sender, pressed.List))
            pressed.List.SelectedItem = pressed.Item;

        _pressed = null;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_source is not null)
            Cancel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_source is null)
            return;

        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            Follow(_at, _keys | KeyModifiers.Control);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_source is not null && e.Key is Key.LeftCtrl or Key.RightCtrl)
            Follow(_at, _keys & ~KeyModifiers.Control);
    }

    /// <summary>Начинает тягу: берёт выбор, захватывает указатель и открывает сеанс тяги студии.</summary>
    private void Start(Press pressed, PointerEventArgs e)
    {
        _pressed = null;

        var list = pressed.List;
        var selection = EditSelection.Of(list.SelectedItems?.Cast<object?>().Select(NodeOf) ?? []);

        // Взялись за то, что выбором уже не является (Ctrl снял его на нажатии), или выбор правке не
        // отдаётся — нести нечего.
        if (selection.IsEmpty || list.SelectedItems?.Contains(pressed.Item) != true || TopLevel.GetTopLevel(list) is not { } top)
            return;

        var first = NodeOf(pressed.Item) is { } grabbed && selection.Roots.Contains(grabbed) ? grabbed : selection.Roots[0];
        var more = selection.Roots.Count - 1;

        _source = list;
        _pointer = e.Pointer;
        _items = FileClip.Of(ClipMode.Cut, selection).Items;

        // Первым несут то, за что взялись, — как в подписи у курсора: цель, ставящая несомое рядами от
        // точки отпускания, ставит под курсор его, а не первое по порядку выбора.
        _session = _drags?.Begin(
            list,
            StudioDragData.FromFiles(_items.Select(item => item.Root).OrderBy(path => path == first.Path ? 0 : 1).Select(path => path.Value)),
            Outside,
            new StudioDragVisual(more == 0 ? first.Name : _strings.Format("project.drag.many", first.Name, more))
            {
                Icon = Glyphs.Of(first, expanded: false),
                IconBrush = Glyphs.TintOf(first) is { } key && list.TryFindResource(key, list.ActualThemeVariant, out var brush)
                    ? brush as IBrush
                    : null,
            });

        e.Pointer.Capture(list);
        Follow(e.GetPosition(top), e.KeyModifiers);
    }

    /// <summary>
    /// Ведёт несомое к точке окна: своя цель отвечает здесь, чужую спрашивает сеанс студии.
    /// </summary>
    private void Follow(Point at, KeyModifiers keys)
    {
        if (_source is not { } source || TopLevel.GetTopLevel(source) is not { } top)
            return;

        _at = at;
        _keys = keys;
        (_over, _overAt) = SurfaceAt(source, at);

        var position = top.TranslatePoint(at, source) ?? at;

        if (_over is { } over)
        {
            Effect = _drop.Hover(over, _overAt, _items, ModeOf(keys));
            _session?.OverOwn(position, Effect);
        }
        else
        {
            _drop.Clear();
            Effect = _session?.Over(position, keys) ?? DragDropEffects.None;
        }
    }

    /// <summary>Бросает тягу: ничего не кладёт, снимает отметку.</summary>
    private void Cancel()
    {
        if (_source is null)
            return;

        _drop.Clear();
        End();
    }

    /// <summary>Кончает тягу: закрывает сеанс — подсказку и курсор — и отпускает захват.</summary>
    private void End()
    {
        if (_source is null)
            return;

        var pointer = _pointer;
        var session = _session;

        // Тяга кончается раньше, чем отпускается захват: «захват потерян», поднятый отпусканием, должен
        // застать её законченной и ничего больше не бросать.
        _source = null;
        _pointer = null;
        _session = null;
        _items = [];
        _over = null;
        Effect = DragDropEffects.None;

        session?.Dispose();
        pointer?.Capture(null);
    }

    /// <summary>
    /// Наш список или крошки под точкой окна и точка в их координатах; пусто — мимо своего.
    /// </summary>
    /// <remarks>
    /// Раскрытое меню спрятанных уровней крошек — отдельное окно поверх колонки: над ним несут к
    /// крошкам, а не к тому, что под ним, и попадание окна источника его не видит. Поэтому меню
    /// спрашивается первым, по точке экрана. Дальше попадание верхним элементом — у сеанса студии: он
    /// знает окна сверху вниз, и оторванное окно поверх колонки заслоняет её, как заслоняет на экране.
    /// Меню, раскрытое тягой, пропускает ввод к окну и того, что под ним, не заслоняет.
    /// </remarks>
    private (Control? Surface, Point At) SurfaceAt(Visual source, Point at)
    {
        var surfaces = _drop.Surfaces;

        if (TopLevel.GetTopLevel(source) is not { } top)
            return (null, default);

        var crumbs = _view.Path;
        var screen = top.PointToScreen(at);

        if (crumbs.IsOverflowOpen && crumbs.IsOverflowAt(screen))
            return (crumbs, crumbs.PointToClient(screen));

        var hit = _session is { } session
            ? session.ElementAt(top.TranslatePoint(at, source) ?? at)
            : top.InputHitTest(at) as Visual;

        if (hit?.GetSelfAndVisualAncestors().OfType<Control>()
                .FirstOrDefault(control => Array.IndexOf(surfaces, control) >= 0) is not { } surface
            || top.TranslatePoint(at, surface) is not { } point)
        {
            return (null, default);
        }

        return (surface, point);
    }

    private static ClipMode ModeOf(KeyModifiers keys) => (keys & KeyModifiers.Control) != 0 ? ClipMode.Copy : ClipMode.Cut;

    private static Node? NodeOf(object? item) => item switch
    {
        Row row => row.Node,
        Tile tile => tile.Node,
        _ => null,
    };

    /// <summary>Нажатие, которое может стать тягой.</summary>
    /// <param name="List">Список, в котором нажали.</param>
    /// <param name="At">Точка нажатия в координатах списка.</param>
    /// <param name="Item">Строка или плитка под нажатием.</param>
    /// <param name="Collapse">Нажали по выбранному внутри группы: щелчок без тяги сведёт выбор к нему.</param>
    private sealed record Press(AxListBox List, Point At, object Item, bool Collapse);
}
