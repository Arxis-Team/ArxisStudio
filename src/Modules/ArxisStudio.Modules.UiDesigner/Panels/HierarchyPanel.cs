using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
/// среди соседей, — клавиатурная дорога туда, куда на холсте ведёт тяга (WCAG 2.5.7).
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.HierarchyId)]
public sealed class HierarchyPanel : ToolWindow
{
    private readonly HashSet<XamlElementPath> _collapsed = [];

    private DesignerWorkbench? _bench;
    private HierarchyView? _view;
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

        bench.FormChanged += OnFormChanged;
        bench.SelectionChanged += OnSelectionChanged;
        bench.ContentChanged += OnContentChanged;

        view.Tree.SelectionChanged += OnTreeSelectionChanged;
        view.Tree.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

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
        }

        _bench = null;
        _view = null;
        _form = null;
    }

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
    private void Rebuild()
    {
        if (_view is not { } view)
            return;

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
    }

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
    private void Reveal(HierarchyNode node)
    {
        if (_view is not { } view)
            return;

        ItemsControl? owner = view.Tree;

        foreach (var step in Chain(node.Path))
        {
            if (owner?.ContainerFromItem(step) is not TreeViewItem container)
                return;

            if (ReferenceEquals(step, node))
            {
                container.BringIntoView();
                return;
            }

            owner = container;
        }
    }

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
    /// Delete и Ctrl со стрелкой — до дерева: его стрелки ходят по строкам; Ctrl+Z и Ctrl+Y — история формы.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is { } view && HistoryKeys.Step(e, view.Tree, _bench))
            return;

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
