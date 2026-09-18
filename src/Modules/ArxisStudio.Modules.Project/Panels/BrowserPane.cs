using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using ArxisStudio.Modules.Project.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// </para>
/// </remarks>
internal sealed class BrowserPane : IDisposable
{
    private readonly ProjectPanelView _view;
    private readonly ProjectModel _model;
    private readonly ProjectMenu _menu;
    private readonly Action<Node> _located;
    private readonly Action<int> _resized;
    private readonly Action<string> _copy;
    private bool _sizing;

    /// <summary>Связывает колонку с разметкой.</summary>
    /// <param name="view">Разметка окна.</param>
    /// <param name="model">Модель окна.</param>
    /// <param name="menu">Меню узла — то же, что у дерева.</param>
    /// <param name="located">Колонка ушла в контейнер — дерево слева встаёт на него.</param>
    /// <param name="resized">Человек сдвинул ползунок — ступень уходит в настройки.</param>
    /// <param name="copy">Положить текст в буфер обмена.</param>
    public BrowserPane(
        ProjectPanelView view, ProjectModel model, ProjectMenu menu, Action<Node> located, Action<int> resized, Action<string> copy)
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
    }

    /// <summary>Ставит ползунок на ступень, не записывая её обратно в настройки.</summary>
    /// <param name="size">Ступень.</param>
    public void Show(int size)
    {
        _sizing = true;
        _view.Size.Value = size;
        _sizing = false;
    }

    /// <summary>Выделяет плитку узла в показанном списке.</summary>
    /// <param name="node">Узел.</param>
    /// <returns>Была ли плитка в колонке.</returns>
    public bool Select(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var tile = _model.Browser.Items.FirstOrDefault(item => string.Equals(item.Key, node.Key, StringComparison.OrdinalIgnoreCase));

        if (tile is null)
            return false;

        var list = Shown;

        list.SelectedItem = tile;
        list.ScrollIntoView(tile);

        return true;
    }

    /// <summary>Пункты меню плитки — тестам: попап — отдельное окно, которого у безголового прогона нет.</summary>
    /// <param name="tile">Плитка.</param>
    internal IReadOnlyList<AxMenuItem> Items(Tile tile) =>
        _menu.Items(tile, _model.IsSearching ? () => ShowInFolder(tile.Node) : null);

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
            default:
                return;
        }

        e.Handled = true;
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
            _resized((int)Math.Round(_view.Size.Value));
    }

    /// <summary>Плитка, в которой пришлось событие.</summary>
    private static Tile? TileOf(object? source) =>
        (source as Visual)?.FindAncestorOfType<AxListBoxItem>(includeSelf: true)?.DataContext as Tile;
}
