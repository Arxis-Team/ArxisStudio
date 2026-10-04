using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Иерархия формы впереди: элементы документа деревом, выбор — общий с холстом и XAML.
/// </summary>
/// <remarks>
/// <para>
/// Дерево строится из текста формы, а не из живого: строка — путь, тип и имя. Правка, отмена и замена
/// поколения перестраивают его заново, а свёрнутое человеком остаётся свёрнутым — по путям.
/// </para>
/// <para>
/// Клавиатура — дерева: стрелки ходят и раскрывают, набор ищет по раскрытому. Своё у иерархии —
/// Delete, убирающий выбранное из документа, и Ctrl со стрелкой вверх или вниз, переставляющий элемент
/// среди соседей, — клавиатурная дорога туда, куда на холсте ведёт тяга (WCAG 2.5.7). Буфер обмена,
/// дубликат и меню — те же, что у холста (<see cref="FormCommands"/>, <see cref="FormMenu"/>).
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.HierarchyId)]
public sealed class HierarchyPanel : ToolWindow
{
    private readonly HashSet<XamlElementPath> _collapsed = [];

    private DesignerWorkbench? _bench;
    private HierarchyView? _view;
    private FormMenu? _menu;
    private LiveFormDocument? _form;
    private IReadOnlyList<HierarchyNode> _nodes = [];
    private Dictionary<XamlElementPath, HierarchyNode> _byPath = [];
    private int _syncing;

    /// <summary>Разметка — тестам.</summary>
    internal HierarchyView? View => _view;

    /// <summary>Строки дерева сейчас — тестам.</summary>
    internal IReadOnlyList<HierarchyNode> Nodes => _nodes;

    /// <inheritdoc/>
    /// <remarks>Каретка — у дерева: им работают, и выбранная строка получает её сама.</remarks>
    public override Control? FocusTarget => _view?.Tree;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var view = new HierarchyView();
        var bench = DesignerWorkbench.Of(Context);

        _view = view;
        _bench = bench;
        _menu = new FormMenu(Context.Strings);

        bench.FormChanged += OnFormChanged;
        bench.SelectionChanged += OnSelectionChanged;
        bench.ContentChanged += OnContentChanged;

        view.Tree.SelectionChanged += OnTreeSelectionChanged;
        view.Tree.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.Tree.ContextRequested += OnContextRequested;

        Rebuild();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_bench is { } bench)
        {
            bench.FormChanged -= OnFormChanged;
            bench.SelectionChanged -= OnSelectionChanged;
            bench.ContentChanged -= OnContentChanged;
        }

        if (_view is { } view)
        {
            view.Tree.SelectionChanged -= OnTreeSelectionChanged;
            view.Tree.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            view.Tree.ContextRequested -= OnContextRequested;
        }

        _bench = null;
        _view = null;
        _menu = null;
        _form = null;
    }

    /// <summary>Пункты меню иерархии — тем же путём, каким их собирает меню; тестам.</summary>
    internal IReadOnlyList<Control> MenuItems() =>
        _form?.Commands is { } commands && _menu is { } menu && _view is { } view ? menu.Items(commands, view.Tree, canvas: null) : [];

    private void OnFormChanged(object? sender, EventArgs e)
    {
        // Свёрнутое — свойство формы, а не панели: у другой формы те же пути значат другое.
        _collapsed.Clear();
        _byPath = [];
        Rebuild();
    }

    private void OnContentChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSelectionChanged(object? sender, EventArgs e) => ShowSelection();

    /// <summary>Строит дерево заново из текста формы впереди.</summary>
    /// <remarks>
    /// Строки — новые, и строка, державшая клавиатуру, уходит из дерева вместе с ней. Правка, сделанная из
    /// дерева, — Delete, Ctrl со стрелкой, вставка — оставляла бы клавиатуру нигде, и вторая такая же клавиша
    /// не делала бы ничего; поэтому клавиатура возвращается выбранной строке.
    /// </remarks>
    private void Rebuild()
    {
        if (_view is not { } view)
            return;

        var keyboard = view.Tree.IsKeyboardFocusWithin;

        foreach (var node in _byPath.Values)
        {
            if (node.IsExpanded)
                _collapsed.Remove(node.Path);
            else
                _collapsed.Add(node.Path);
        }

        _form = _bench?.Form;
        _nodes = _form?.Document?.Syntax is { } syntax ? HierarchyNode.Of(syntax, _collapsed) : [];
        _byPath = _nodes.SelectMany(node => node.SelfAndDescendants()).ToDictionary(node => node.Path);

        using (Syncing())
            view.Tree.ItemsSource = _nodes;

        view.Empty.IsVisible = _nodes.Count == 0;

        ShowSelection();

        if (keyboard)
            KeepKeyboard();
    }

    /// <summary>
    /// Отдаёт клавиатуру выбранной строке, если она не досталась никому: строку, державшую её, унесла
    /// перестройка дерева, или меню, закрывшись, вернуло её строке, которой больше нет.
    /// </summary>
    /// <remarks>Чужую клавиатуру не берёт: ушедшая в другую панель там и остаётся.</remarks>
    private void KeepKeyboard() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_view is not { } view || TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement() is not null)
                return;

            var row = _form?.Selection.FirstOrDefault() is { } primary ? Container(primary) : null;

            (row ?? view.Tree.ContainerFromIndex(0))?.Focus();
        }, DispatcherPriority.Loaded);

    /// <summary>Ставит выбор формы на строки: раскрывает дорогу к ним и показывает главную.</summary>
    private void ShowSelection()
    {
        if (_view is not { } view)
            return;

        var selected = (_form?.Selection ?? [])
            .Select(path => _byPath.GetValueOrDefault(path))
            .OfType<HierarchyNode>()
            .ToList();

        foreach (var node in selected)
            ExpandTo(node.Path);

        using (Syncing())
        {
            var items = view.Tree.SelectedItems;

            if (items.Count == selected.Count && selected.All(items.Contains))
                return;

            items.Clear();

            foreach (var node in selected)
                items.Add(node);
        }

        if (selected.FirstOrDefault() is { } primary)
            Dispatcher.UIThread.Post(() => Reveal(primary), DispatcherPriority.Loaded);
    }

    /// <summary>Раскрывает предков строки.</summary>
    private void ExpandTo(XamlElementPath path)
    {
        for (var parent = path.Parent; parent is not null; parent = parent.Parent)
        {
            if (_byPath.TryGetValue(parent, out var node))
                node.IsExpanded = true;
        }
    }

    /// <summary>Прокручивает к строке, если её не видно.</summary>
    private void Reveal(HierarchyNode node) => Container(node.Path)?.BringIntoView();

    /// <summary>Строки от корня до этой.</summary>
    private IEnumerable<HierarchyNode> Chain(XamlElementPath path)
    {
        var chain = new Stack<HierarchyNode>();

        for (XamlElementPath? current = path; current is not null; current = current.Parent)
        {
            if (_byPath.TryGetValue(current, out var node))
                chain.Push(node);
        }

        return chain;
    }

    /// <summary>Человек выбрал строки: выбор формы — их пути, главный — первый выбранный.</summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing > 0 || _view is not { } view || _form is not { } form)
            return;

        form.Select([.. view.Tree.SelectedItems.OfType<HierarchyNode>().Select(node => node.Path)]);
    }

    /// <summary>
    /// Delete и Ctrl со стрелкой — до дерева: его стрелки ходят по строкам; Ctrl+Z и Ctrl+Y — история формы;
    /// Ctrl+X, Ctrl+C, Ctrl+V и Ctrl+D — правки строения, как на холсте.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is not { } view || HistoryKeys.Step(e, view.Tree, _bench))
            return;

        if (_form?.Commands is { } commands && commands.Press(e, view.Tree))
        {
            e.Handled = true;
            return;
        }

        if (_form is not { Edits: { } edits } form || form.Selection is not { Count: > 0 } selection)
            return;

        switch (e.Key)
        {
            case Key.Delete when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                _ = edits.DeleteAsync(selection);
                break;

            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.Control && selection.Count == 1:
                // Переставлять некуда — край среди соседей: клавиша всё равно своя, а не ход по строкам.
                e.Handled = true;
                edits.MoveBy(selection[0], later: e.Key == Key.Down);
                break;
        }
    }

    /// <summary>
    /// Меню строки: мышью — под указателем, и щелчок мимо выбранного выбирает строку под ним; клавишей — у
    /// главной выбранной строки.
    /// </summary>
    /// <remarks>Правый щелчок по выбранной строке оставляет выбор как есть — меню о нём, как в Rider и в проводнике.</remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_view is not { } view || _menu is not { } menu || _form?.Commands is not { } commands)
            return;

        var tree = view.Tree;
        var atPointer = e.TryGetPosition(tree, out _);

        if (atPointer && NodeOf(e.Source) is { } node && tree.SelectedItems?.Contains(node) != true)
            tree.SelectedItem = node;

        Control anchor = !atPointer && _form.Selection.FirstOrDefault() is { } primary && Container(primary) is { } row
            ? row
            : tree;

        // Пункт меню правит форму, и дерево перестраивается: строка, которой меню вернёт клавиатуру, может
        // уже уйти.
        FormMenu.Show(anchor, menu.Items(commands, tree, canvas: null), atPointer, closed: KeepKeyboard);
        e.Handled = true;
    }

    /// <summary>Строка дерева, которой принадлежит элемент разметки.</summary>
    private static HierarchyNode? NodeOf(object? source) =>
        source is StyledElement element
            ? element.DataContext as HierarchyNode ?? (element as Visual)?.FindAncestorOfType<TreeViewItem>()?.DataContext as HierarchyNode
            : null;

    /// <summary>Контейнер строки, если он развёрнут.</summary>
    private TreeViewItem? Container(XamlElementPath path)
    {
        ItemsControl? owner = _view?.Tree;

        foreach (var step in Chain(path))
        {
            if (owner?.ContainerFromItem(step) is not TreeViewItem container)
                return null;

            if (step.Path.Equals(path))
                return container;

            owner = container;
        }

        return null;
    }

    /// <summary>Выбор, который ставит панель: его события — не выбор человека.</summary>
    private SyncScope Syncing()
    {
        _syncing++;

        return new SyncScope(this);
    }

    private readonly struct SyncScope(HierarchyPanel owner) : IDisposable
    {
        public void Dispose() => owner._syncing--;
    }
}
