using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Правая колонка окна проекта: плитки, список, крошки и ползунок — их указатель, клавиатура и меню.
/// </summary>
/// <remarks>
/// Двойной щелчок и Enter по контейнеру ведут в него: колонка показывает его содержимое, а дерево
/// слева выделяет его, раскрыв дорогу, — так ходит правая колонка Unity. По файлу — открывают файл.
/// Backspace и Alt+Up поднимаются к родителю, как в проводнике; крошка ведёт на свой уровень.
/// <para>
/// Ползунок ступени пишет её в настройки, а применяет окно — из настройки, одной дорогой с правкой в
/// окне настроек студии: иначе ползунок и настройка разошлись бы при первой правке с другой стороны.
/// Колесо с Ctrl и Ctrl с плюсом, минусом и нулём двигают тот же ползунок.
/// </para>
/// </remarks>
internal sealed class BrowserPane : IDisposable
{
    private readonly ProjectPanelView _view;
    private readonly ProjectModel _model;
    private readonly ProjectMenu _menu;
    private readonly Action<Node> _located;
    private readonly Action<double> _resized;
    private readonly Action<string> _copy;
    private bool _sizing;

    /// <summary>Доля щелчка колеса, накопленная тачпадом.</summary>
    private double _wheel;

    /// <summary>Плитка, которую держать в виду, когда список переложится по новой ступени.</summary>
    private Tile? _anchor;

    /// <summary>
    /// Как вернуть клавиатуру в колонку после смены ступени; пусто — её там не было.
    /// </summary>
    private NavigationMethod? _refocus;

    /// <summary>Связывает колонку с разметкой.</summary>
    /// <param name="view">Разметка окна.</param>
    /// <param name="model">Модель окна.</param>
    /// <param name="menu">Меню узла — то же, что у дерева.</param>
    /// <param name="located">Колонка ушла в контейнер — дерево слева встаёт на него.</param>
    /// <param name="resized">Человек сменил ступень — её размер уходит в настройки.</param>
    /// <param name="copy">Положить текст в буфер обмена.</param>
    public BrowserPane(
        ProjectPanelView view, ProjectModel model, ProjectMenu menu, Action<Node> located, Action<double> resized, Action<string> copy)
    {
        _view = view;
        _model = model;
        _menu = menu;
        _located = located;
        _resized = resized;
        _copy = copy;

        foreach (var list in Lists)
        {
            list.DoubleTapped += OnDoubleTapped;
            list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            list.SelectionChanged += OnSelectionChanged;
            list.ContextRequested += OnContextRequested;
        }

        view.Path.Navigated += OnNavigated;
        view.Size.PropertyChanged += OnSizeChanged;
        view.Browser.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>Список, который сейчас показан: плитки или строки.</summary>
    public AxListBox Shown => _model.ShowsList ? _view.Files : _view.Tiles;

    /// <summary>Выбранная плитка показанного списка.</summary>
    public Tile? Selected => Shown.SelectedItem as Tile;

    private AxListBox[] Lists => [_view.Tiles, _view.Files];

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var list in Lists)
        {
            list.DoubleTapped -= OnDoubleTapped;
            list.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            list.SelectionChanged -= OnSelectionChanged;
            list.ContextRequested -= OnContextRequested;
        }

        _view.Path.Navigated -= OnNavigated;
        _view.Size.PropertyChanged -= OnSizeChanged;
        _view.Browser.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
    }

    /// <summary>Ставит ползунок и плитки на размер из настроек, не записывая его обратно.</summary>
    /// <param name="size">Размер силуэта в точках, ноль — список; пусто — обычная ступень.</param>
    public void Show(double? size)
    {
        var ladder = TileLadder.Of(_view);
        var position = ladder.Position(size);

        _sizing = true;
        _view.Size.Maximum = ladder.Last;
        _view.Size.Value = position;
        _sizing = false;

        if (position > 0)
            TileMetrics.Apply(_view.Tiles, ladder, ladder.Size(position));

        Keep();
    }

    /// <summary>
    /// Сдвигает ступень на <paramref name="steps"/>: вверх — крупнее, вниз — мельче, мельче малой —
    /// список. Выбранная плитка, а без неё та, что под мышью, остаётся в виду.
    /// </summary>
    /// <param name="steps">Сколько ступеней и куда.</param>
    /// <param name="under">Плитка под мышью; пусто — ступень сменили с клавиатуры.</param>
    /// <param name="method">Чем сменили: клавиатурой колонка вернёт себе и кольцо фокуса.</param>
    public void Step(int steps, Tile? under = null, NavigationMethod method = NavigationMethod.Directional) =>
        Stand((int)Math.Round(_view.Size.Value) + steps, under, method);

    /// <summary>Выделяет плитку узла в показанном списке.</summary>
    /// <param name="node">Узел.</param>
    /// <param name="focus">Отдать плитке и клавиатуру — после правки, начатой в колонке.</param>
    /// <returns>Была ли плитка в колонке.</returns>
    public bool Select(Node node, bool focus = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        var tile = _model.Browser.Items.FirstOrDefault(item => string.Equals(item.Key, node.Key, StringComparison.OrdinalIgnoreCase));

        if (tile is null)
            return false;

        Stand(tile, focus);

        return true;
    }

    /// <summary>Место первой выделенной плитки; −1 — выделения нет.</summary>
    public int FirstSelected() =>
        Shown.SelectedItems is { Count: > 0 } selected
            ? selected.OfType<Tile>().Select(tile => _model.Browser.Items.IndexOf(tile)).Where(at => at >= 0).DefaultIfEmpty(-1).Min()
            : -1;

    /// <summary>Выделяет плитку на месте — ту, что заняла место удалённой, — и отдаёт ей клавиатуру.</summary>
    /// <param name="at">Место; за концом — последняя плитка.</param>
    public void SelectAt(int at)
    {
        var items = _model.Browser.Items;

        if (items.Count > 0 && at >= 0)
            Stand(items[Math.Min(at, items.Count - 1)], focus: true);
    }

    /// <summary>Что выбрано в колонке — так, как его возьмёт правка.</summary>
    internal EditSelection Selection() => EditSelection.Of(Shown.SelectedItems?.OfType<Tile>().Select(tile => tile.Node) ?? []);

    /// <summary>Пункты меню плитки — тестам: попап — отдельное окно, которого у безголового прогона нет.</summary>
    /// <param name="tile">Плитка.</param>
    internal IReadOnlyList<AxMenuItem> Items(Tile tile) =>
        _menu.Items(tile, _model.IsSearching ? () => ShowInFolder(tile.Node) : null, Selection());

    /// <summary>Контейнер открывается в колонке, файл — в редакторе.</summary>
    /// <param name="tile">Плитка.</param>
    public void Act(Tile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (tile.IsContainer)
            Go(tile.Node);
        else
            _model.Open(tile.Node);
    }

    /// <summary>Переводит колонку в контейнер, и дерево слева встаёт на него.</summary>
    /// <param name="container">Контейнер.</param>
    public void Go(Node container)
    {
        if (_model.Go(container))
            _located(container);
    }

    /// <summary>Поднимает колонку к родителю.</summary>
    public void Up()
    {
        if (_model.Up() && _model.Browser.Current is { } current)
            _located(current);
    }

    /// <summary>Найденное поиском — в его папке: колонка уходит туда, и плитка выделена.</summary>
    /// <param name="node">Найденный узел.</param>
    public void ShowInFolder(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Ancestors().FirstOrDefault(ancestor => ancestor.IsContainer) is not { } container)
            return;

        Go(container);
        Select(node);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TileOf(e.Source) is not { } tile)
            return;

        Act(tile);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AxListBox list)
            return;

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && list.SelectedItem is Tile tile:
                Act(tile);
                break;
            case Key.Delete when e.KeyModifiers == KeyModifiers.None && _menu.Actions.Delete is { } delete:
                delete(Selection(), EditOrigin.Pane);
                break;
            case Key.F2 when e.KeyModifiers == KeyModifiers.None && _menu.Actions.Rename is { } rename:
                rename(Selection(), EditOrigin.Pane);
                break;
            case Key.X when e.KeyModifiers == KeyModifiers.Control && _menu.Actions.CutFiles is { } cut:
                cut(Selection(), EditOrigin.Pane);
                break;
            case Key.C when e.KeyModifiers == KeyModifiers.Control && _menu.Actions.CopyFiles is { } copy:
                copy(Selection(), EditOrigin.Pane);
                break;

            // Вставка с клавиатуры идёт в папку, которую колонка показывает, — как в проводнике и в
            // Unity; в найденном поиском папки нет.
            case Key.V when e.KeyModifiers == KeyModifiers.Control && _menu.Actions.Paste is { } paste
                            && !_model.IsSearching && _model.Browser.Current is { } here && Pasting.Folder(here) is { } folder:
                paste(folder, EditOrigin.Pane);
                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None && _menu.Actions.Uncut?.Invoke() == true:
                break;
            case Key.Back when e.KeyModifiers == KeyModifiers.None:
                Up();
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
                Up();
                break;
            case Key.C when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
                            && list.SelectedItem is Tile { Node.Path.IsEmpty: false } picked:
                _copy(picked.Node.Path.Value);
                break;

            // Плюс и минус — с Shift и без: «+» на основной клавиатуре — это Shift и «=».
            case Key.OemPlus or Key.Add when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.Control:
                Step(1);
                break;
            case Key.OemMinus or Key.Subtract when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.Control:
                Step(-1);
                break;
            case Key.D0 or Key.NumPad0 when e.KeyModifiers == KeyModifiers.Control:
                Stand(TileLadder.Of(_view).Position(null), null, NavigationMethod.Directional);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Колесо с Ctrl меняет ступень, как в проводнике и в Unity: от себя — крупнее, на себя — мельче.
    /// </summary>
    /// <remarks>
    /// Колесо ловится на спуске, раньше списка: иначе список листал бы, пока человек меняет размер. У
    /// тачпада щелчок приходит долями, и они копятся до целого — ступень за щелчок, сколько бы событий
    /// его ни несло; смена направления копилку сбрасывает.
    /// </remarks>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || e.Delta.Y == 0)
            return;

        e.Handled = true;
        _wheel = Math.Sign(_wheel) == Math.Sign(e.Delta.Y) ? _wheel + e.Delta.Y : e.Delta.Y;

        // Доли складываются с погрешностью: десять десятых дают 0,999…, и щелчок не засчитывался бы.
        var steps = (int)Math.Truncate(Math.Round(_wheel, 6));

        if (steps == 0)
            return;

        _wheel -= steps;
        Step(steps, TileOf(e.Source), NavigationMethod.Pointer);
    }

    /// <summary>Ставит ползунок в положение — дальше ступень идёт обычной дорогой ползунка.</summary>
    private void Stand(int position, Tile? under, NavigationMethod method)
    {
        var from = (int)Math.Round(_view.Size.Value);
        var to = Math.Clamp(position, 0, (int)_view.Size.Maximum);

        if (to == from)
            return;

        _anchor = Selected ?? under;
        _refocus = Shown.IsKeyboardFocusWithin ? method : null;
        _view.Size.Value = to;
    }

    /// <summary>
    /// Держит в виду плитку, которую человек видел до смены ступени, и возвращает колонке клавиатуру.
    /// </summary>
    /// <remarks>
    /// Список перекладывает плитки по новым размерам в следующем проходе раскладки, и прокрутка сразу
    /// попала бы в прежние места — поэтому она ждёт этого прохода. Клавиатуру теряет смена списка:
    /// ступень «список» прячет плитки, а с ними и плитку в фокусе, и Ctrl+минус оставлял бы каретку
    /// нигде. Она возвращается на ту же плитку — строкой или плиткой, чем та стала.
    /// </remarks>
    private void Keep()
    {
        if (_anchor is null && _refocus is null)
            return;

        var (anchor, refocus) = (_anchor, _refocus);

        _anchor = null;
        _refocus = null;
        Dispatcher.UIThread.Post(
            () =>
            {
                var list = Shown;
                var kept = anchor is not null && _model.Browser.Items.Contains(anchor) ? anchor : list.SelectedItem;

                if (kept is not null)
                    list.ScrollIntoView(kept);

                if (refocus is { } method && !list.IsKeyboardFocusWithin
                    && (kept is null ? list.ContainerFromIndex(0) : list.ContainerFromItem(kept)) is { } container)
                {
                    container.Focus(method);
                }
            },
            DispatcherPriority.Background);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Shown))
            _model.Pick(Shown.SelectedItem as Tile);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not AxListBox list)
            return;

        var atPointer = e.TryGetPosition(list, out _);

        if ((atPointer ? TileOf(e.Source) : list.SelectedItem as Tile) is not { } tile)
            return;

        // Как в дереве: по выбранной плитке меню о всём выборе, по невыбранной — выбирает её одну.
        if (list.SelectedItems?.Contains(tile) != true)
            list.SelectedItem = tile;

        var anchor = atPointer ? list : list.ContainerFromItem(tile) ?? list;

        ProjectMenu.ShowAt(anchor, Items(tile), atPointer);
        e.Handled = true;
    }

    private void OnNavigated(object? sender, AxBreadcrumbNavigatedEventArgs e)
    {
        if (e.Item is Segment segment)
            Go(segment.Node);
    }

    private void OnSizeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RangeBase.ValueProperty && !_sizing)
            _resized(TileLadder.Of(_view).Size((int)Math.Round(_view.Size.Value)));
    }

    /// <summary>Выделяет плитку в показанном списке и, если просят, отдаёт ей клавиатуру.</summary>
    private void Stand(Tile tile, bool focus)
    {
        var list = Shown;

        list.SelectedItem = tile;
        list.ScrollIntoView(tile);

        if (focus)
            (list.ContainerFromItem(tile) as Control)?.Focus(NavigationMethod.Directional);
    }

    /// <summary>Плитка, в которой пришлось событие.</summary>
    private static Tile? TileOf(object? source) =>
        (source as Visual)?.FindAncestorOfType<AxListBoxItem>(includeSelf: true)?.DataContext as Tile;
}
