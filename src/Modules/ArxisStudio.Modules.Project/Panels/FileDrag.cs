using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Файлы и каталоги, которые несут мышью внутри окна проекта: с чего тяга начинается, что несут, как
/// это видно и чем она кончается.
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
/// <b>Куда и чем.</b> Цель, отметку, раскрытие и прокрутку считает <see cref="FileDrop"/> — те же, что у
/// файлов из проводника: строки, плитки и сегменты крошек над колонкой — плитку несут и на уровень
/// выше, как на адресную строку проводника. Отпущенное переносится, с Ctrl — копируется, как в
/// проводнике и Rider; Ctrl, нажатый или отпущенный на месте, меняет это сразу. Курсор говорит, что
/// будет: перенос, копия или «нельзя». Esc и потеря захвата тягу бросают.
/// </para>
/// <para>
/// <b>Что несут</b> — подпись у курсора (<see cref="DragGhost"/>): значок и имя того, за что взялись, и
/// сколько ещё.
/// </para>
/// <para>
/// Тяга своя, на захвате указателя, как у вкладок докинга, а не системная: несут только внутри окна,
/// и мышь, которую изображают инструменты студии и тесты, ведёт её так же, как настоящая. Дорога без
/// мыши — вырезать и вставить (WCAG 2.5.7).
/// </para>
/// </remarks>
internal sealed class FileDrag : IDisposable
{
    /// <summary>Сколько пройти мышью с нажатой кнопкой, чтобы это было тягой, а не щелчком.</summary>
    internal const double Threshold = 6;

    private static readonly Cursor Moving = new(StandardCursorType.DragMove);
    private static readonly Cursor Copying = new(StandardCursorType.DragCopy);
    private static readonly Cursor Refusing = new(StandardCursorType.No);

    private readonly ProjectPanelView _view;
    private readonly FileDrop _drop;
    private readonly IStudioStrings _strings;
    private readonly Action<FileClip, CanonicalPath, EditOrigin> _carry;

    private Press? _pressed;
    private AxListBox? _source;
    private IPointer? _pointer;
    private Cursor? _cursor;
    private IReadOnlyList<ClipItem> _items = [];
    private Point _at;
    private ClipMode _mode;
    private Control? _over;
    private Point _overAt;

    /// <summary>Подключает тягу к дереву и к обоим видам правой колонки.</summary>
    /// <param name="view">Разметка окна.</param>
    /// <param name="drop">Разбор цели — общий с файлами из проводника.</param>
    /// <param name="strings">Словари модуля: подпись у курсора.</param>
    /// <param name="carry">Переносит или копирует несомое в каталог; откуда — туда встанет выделение.</param>
    /// <remarks>
    /// Можно ли класть сейчас — открыто ли решение и свободна ли правка, — решает разбор цели: пока
    /// правка занята, целью не становится ничто, и курсор говорит «нельзя».
    /// </remarks>
    public FileDrag(ProjectPanelView view, FileDrop drop, IStudioStrings strings, Action<FileClip, CanonicalPath, EditOrigin> carry)
    {
        _view = view;
        _drop = drop;
        _strings = strings;
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

    /// <summary>Что сделает отпускание сейчас: перенос, копия или ничего; тестам.</summary>
    internal DragDropEffects Effect { get; private set; }

    /// <summary>Подпись у курсора, пока несут; тестам.</summary>
    internal DragGhost? Ghost { get; private set; }

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
                Follow(e.GetPosition(top), ModeOf(e.KeyModifiers));

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
                Follow(e.GetPosition(top), ModeOf(e.KeyModifiers));

            var over = _over;
            var items = _items;
            var mode = _mode;

            // Сперва — куда легло, потом — отпустить захват: отпускание синхронно поднимает «захват
            // потерян», а тот тягу бросает, и спрашивать «куда отпустили» было бы уже не у кого.
            var folder = over is null ? null : _drop.Place(over, _overAt, items, mode);

            if (over is null)
                _drop.Clear();

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
            Follow(_at, ClipMode.Copy);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_source is not null && e.Key is Key.LeftCtrl or Key.RightCtrl)
            Follow(_at, ClipMode.Cut);
    }

    /// <summary>Начинает тягу: берёт выбор, захватывает указатель и показывает подпись у курсора.</summary>
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
        _cursor = list.Cursor;
        _items = FileClip.Of(ClipMode.Cut, selection).Items;

        Ghost = new DragGhost
        {
            DataContext = new Carried(
                first,
                more == 0 ? first.Name : string.Format(CultureInfo.CurrentCulture, _strings["project.drag.many"], first.Name, more)),
        };

        OverlayLayer.GetOverlayLayer(list)?.Children.Add(Ghost);

        e.Pointer.Capture(list);
        Follow(e.GetPosition(top), ModeOf(e.KeyModifiers));
    }

    /// <summary>Ведёт несомое к точке окна: цель, курсор и подпись у курсора.</summary>
    private void Follow(Point at, ClipMode mode)
    {
        if (_source is not { } source)
            return;

        _at = at;
        _mode = mode;
        (_over, _overAt) = SurfaceAt(source, at);

        if (_over is { } over)
        {
            Effect = _drop.Hover(over, _overAt, _items, mode);
        }
        else
        {
            Effect = DragDropEffects.None;
            _drop.Clear();
        }

        source.Cursor = Effect switch
        {
            DragDropEffects.Move => Moving,
            DragDropEffects.Copy => Copying,
            _ => Refusing,
        };

        if (Ghost is { } ghost
            && OverlayLayer.GetOverlayLayer(source) is { } layer
            && TopLevel.GetTopLevel(source) is { } top
            && top.TranslatePoint(at, layer) is { } place)
        {
            var offset = source.TryFindResource("AxSpaceLoose", source.ActualThemeVariant, out var value) && value is double space ? space : 0;

            Canvas.SetLeft(ghost, place.X + offset);
            Canvas.SetTop(ghost, place.Y + offset);
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

    /// <summary>Кончает тягу: убирает подпись, возвращает курсор и отпускает захват.</summary>
    private void End()
    {
        if (_source is not { } source)
            return;

        var pointer = _pointer;

        // Тяга кончается раньше, чем отпускается захват: «захват потерян», поднятый отпусканием, должен
        // застать её законченной и ничего больше не бросать.
        _source = null;
        _pointer = null;
        _items = [];
        _over = null;
        Effect = DragDropEffects.None;

        if (Ghost is { Parent: Panel host } ghost)
            host.Children.Remove(ghost);

        Ghost = null;
        source.Cursor = _cursor;
        _cursor = null;

        pointer?.Capture(null);
    }

    /// <summary>
    /// Наш список или крошки под точкой окна и точка в их координатах; пусто — мимо того, куда несут.
    /// </summary>
    /// <remarks>
    /// Раскрытое меню спрятанных уровней крошек — отдельное окно поверх колонки: над ним несут к
    /// крошкам, а не к тому, что под ним, и попадание окна источника его не видит. Поэтому меню
    /// спрашивается первым, по точке экрана. Дальше попадание обычное, верхним элементом: меню,
    /// раскрытое тягой, пропускает ввод к окну и того, что под ним, не заслоняет.
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

        if ((top.InputHitTest(at) as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
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
