using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Иерархия формы, с которой работают: элементы её документа деревом, выбор — общий с холстом и XAML.
/// </summary>
/// <remarks>
/// <para>
/// <b>Форма — того холста, что впереди</b> (<see cref="FormFront"/>): вкладки формы или доски, с которой работали
/// последней, пока её видно. На доске это форма главного выбранного — та же, чей XAML стоит под доской; без неё
/// дерево пусто и говорит, как её выбрать.
/// </para>
/// <para>
/// <b>Дерево строится из текста формы, а не из живого</b>: строка — путь, тип и имя. Правка, отмена и текст с
/// диска перестраивают его заново, а свёрнутое человеком остаётся свёрнутым — по путям, отдельно у каждой формы.
/// </para>
/// <para>
/// <b>Клавиатура — дерева</b>: стрелки ходят и раскрывают, набор ищет по раскрытому. Своё у иерархии — Delete,
/// убирающий выбранное из документа, Ctrl со стрелкой вверх или вниз, переставляющий элемент среди соседей, —
/// клавиатурная дорога туда, куда на холсте ведёт тяга (WCAG 2.5.7), — и Enter, показывающий выбранное на холсте.
/// Буфер обмена, дубликат, Esc к родителю и меню — те же, что у холста (<see cref="FormCommands"/>,
/// <see cref="FormMenu"/>), а отмена и возврат — история того, кто держит холст: у доски её общая, у вкладки —
/// история документа.
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.HierarchyId)]
public sealed class HierarchyPanel : ToolWindow
{
    private readonly Dictionary<CanonicalPath, HashSet<XamlElementPath>> _collapsed = [];

    private FormFront? _front;
    private FormCanvas? _canvas;
    private FormSlot? _slot;
    private HierarchyView? _view;
    private IReadOnlyList<HierarchyNode> _nodes = [];
    private Dictionary<XamlElementPath, HierarchyNode> _byPath = [];
    private bool _keepAfterMenu;
    private int _syncing;

    /// <summary>Разметка — тестам.</summary>
    internal HierarchyView? View => _view;

    /// <summary>Строки дерева сейчас — тестам.</summary>
    internal IReadOnlyList<HierarchyNode> Nodes => _nodes;

    /// <summary>Холст, за которым идёт иерархия, — тестам.</summary>
    internal FormCanvas? Canvas => _canvas;

    /// <inheritdoc/>
    /// <remarks>Каретка — у дерева: им работают, и выбранная строка получает её сама.</remarks>
    public override Control? FocusTarget => _view?.Tree;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var view = new HierarchyView();

        _view = view;
        _front = FormFront.Of(Context);
        _front.CanvasChanged += OnCanvasChanged;

        view.Tree.SelectionChanged += OnTreeSelectionChanged;
        view.Tree.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.Tree.ContextRequested += OnContextRequested;

        Follow(_front.Canvas);

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_front is { } front)
            front.CanvasChanged -= OnCanvasChanged;

        Unfollow();

        if (_view is { } view)
        {
            view.Tree.SelectionChanged -= OnTreeSelectionChanged;
            view.Tree.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            view.Tree.ContextRequested -= OnContextRequested;
        }

        _front = null;
        _view = null;
        _slot = null;
    }

    /// <summary>Пункты меню иерархии — тем же путём, каким их собирает меню; тестам.</summary>
    internal IReadOnlyList<Control> MenuItems() =>
        _canvas is { } canvas && _view is { } view ? canvas.FormMenuItems(view.Tree) : [];

    private void OnCanvasChanged(object? sender, EventArgs e) => Follow(_front?.Canvas);

    /// <summary>Иерархия идёт за холстом впереди — или ни за каким — и строит его дерево.</summary>
    private void Follow(FormCanvas? canvas)
    {
        if (!ReferenceEquals(canvas, _canvas))
        {
            Unfollow();

            _canvas = canvas;

            if (canvas is not null)
            {
                canvas.ActiveChanged += OnActiveChanged;
                canvas.SelectionChanged += OnCanvasSelectionChanged;
                canvas.ContentChanged += OnContentChanged;
            }
        }

        Rebuild();
    }

    private void Unfollow()
    {
        if (_canvas is not { } canvas)
            return;

        canvas.ActiveChanged -= OnActiveChanged;
        canvas.SelectionChanged -= OnCanvasSelectionChanged;
        canvas.ContentChanged -= OnContentChanged;
        _canvas = null;
    }

    private void OnActiveChanged(object? sender, EventArgs e) => Rebuild();

    private void OnContentChanged(object? sender, EventArgs e) => Rebuild();

    private void OnCanvasSelectionChanged(object? sender, EventArgs e) => ShowSelection();

    /// <summary>Строит дерево заново из текста формы, с которой работают.</summary>
    /// <remarks>
    /// Строки — новые, и строка, державшая клавиатуру, уходит из дерева вместе с ней. Правка, сделанная из
    /// дерева, — Delete, Ctrl со стрелкой, вставка, пункт меню — оставляла бы клавиатуру нигде, и вторая такая же
    /// клавиша не делала бы ничего; поэтому клавиатура возвращается выбранной строке.
    /// </remarks>
    private void Rebuild()
    {
        if (_view is not { } view)
            return;

        var keyboard = view.Tree.IsKeyboardFocusWithin || _keepAfterMenu;

        _keepAfterMenu = false;
        Remember();

        _slot = _canvas?.Active;
        _nodes = _slot is { Document.Syntax: { } syntax } slot ? HierarchyNode.Of(syntax, CollapsedOf(slot)) : [];
        _byPath = _nodes.SelectMany(static node => node.SelfAndDescendants()).ToDictionary(static node => node.Path);

        using (Syncing())
            view.Tree.ItemsSource = _nodes;

        view.FormOf.Text = _slot?.Session.Path.FileName ?? string.Empty;
        view.Header.IsVisible = _slot is not null;
        view.Empty.Text = Context.Strings[_canvas is null ? "hierarchy.empty" : "hierarchy.pick"];
        view.Empty.IsVisible = _slot is null;

        ShowSelection();

        if (keyboard)
            KeepKeyboard();
    }

    /// <summary>Запоминает, что человек свернул и раскрыл в дереве прежней формы.</summary>
    private void Remember()
    {
        if (_slot is null || _byPath.Count == 0)
            return;

        var collapsed = CollapsedOf(_slot);

        foreach (var node in _byPath.Values)
        {
            if (node.IsExpanded)
                collapsed.Remove(node.Path);
            else
                collapsed.Add(node.Path);
        }
    }

    /// <summary>Свёрнутое в форме: своё у каждого файла — у другой формы те же пути значат другое.</summary>
    private HashSet<XamlElementPath> CollapsedOf(FormSlot slot)
    {
        if (!_collapsed.TryGetValue(slot.Session.Path, out var collapsed))
            _collapsed[slot.Session.Path] = collapsed = [];

        return collapsed;
    }

    /// <summary>Выбор формы на холсте — пути её элементов, первый главный.</summary>
    private IReadOnlyList<XamlElementPath> Selection() =>
        _canvas is { } canvas && _slot is { } slot ? canvas.SelectionOf(slot) : [];

    /// <summary>
    /// Отдаёт клавиатуру выбранной строке, если её не держит никто или держит само дерево: строку, державшую её,
    /// унесла перестройка, или меню, закрывшись, вернуло её строке, которой больше нет.
    /// </summary>
    /// <remarks>Чужую клавиатуру не берёт: ушедшая на холст или в другую панель там и остаётся.</remarks>
    private void KeepKeyboard() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_view is not { } view
                || TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement() is { } focused && !ReferenceEquals(focused, view.Tree))
            {
                return;
            }

            var row = Selection().FirstOrDefault() is { } primary ? Container(primary) : null;

            (row ?? view.Tree.ContainerFromIndex(0))?.Focus();
        }, DispatcherPriority.Loaded);

    /// <summary>Ставит выбор формы на строки: раскрывает дорогу к ним и показывает главную.</summary>
    private void ShowSelection()
    {
        if (_view is not { } view)
            return;

        var selected = Selection()
            .Select(path => _byPath.GetValueOrDefault(path))
            .OfType<HierarchyNode>()
            .ToList();

        foreach (var node in selected)
            ExpandTo(node.Path);

        using (Syncing())
        {
            if (view.Tree.SelectedItems is { } items && !(items.Count == selected.Count && selected.All(items.Contains)))
            {
                items.Clear();

                foreach (var node in selected)
                    items.Add(node);
            }
        }

        if (selected.FirstOrDefault() is { } primary)
            Dispatcher.UIThread.Post(() => Container(primary.Path)?.BringIntoView(), DispatcherPriority.Loaded);
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

    /// <summary>Человек выбрал строки: выбор формы — их пути, главный — первый выбранный.</summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing > 0 || _view is not { } view || _canvas is not { } canvas || _slot is not { } slot)
            return;

        canvas.Select(slot, [.. (view.Tree.SelectedItems ?? Array.Empty<object>()).OfType<HierarchyNode>().Select(static node => node.Path)]);
    }

    /// <summary>
    /// Отмена и возврат — история хозяина холста; Esc, Ctrl+X, Ctrl+C, Ctrl+V и Ctrl+D — как на холсте; Delete,
    /// Ctrl со стрелкой и Enter — до дерева: его стрелки ходят по строкам.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is not { } view || _canvas is not { } canvas)
            return;

        if (view.GetPlatformSettings()?.HotkeyConfiguration is { } keys)
        {
            var back = keys.Undo.Any(gesture => gesture.Matches(e));

            if (back || keys.Redo.Any(gesture => gesture.Matches(e)))
            {
                e.Handled = true;
                canvas.Host.Step(back);
                return;
            }
        }

        if (e.Is(FormKeys.Parent))
        {
            // Esc — к тому, в чём стоит выбранное, как на холсте, и меню подписывает его так же; на корне выбор
            // снимается. Клавиатура идёт за выбором: строка родителя уже стоит — она над прежней.
            if (canvas.SelectParent())
            {
                e.Handled = true;

                if (Selection().FirstOrDefault() is { } parent)
                    Container(parent)?.Focus();
            }

            return;
        }

        if (canvas.Commands is not { } commands)
            return;

        foreach (var gesture in (KeyGesture[])[FormKeys.Cut, FormKeys.Copy, FormKeys.Paste, FormKeys.Duplicate])
        {
            if (e.Is(gesture))
            {
                e.Handled = commands.Run(gesture, view.Tree);
                return;
            }
        }

        if (e.Is(FormKeys.Delete))
        {
            if (commands.CanTake)
            {
                e.Handled = true;
                _ = commands.DeleteAsync();
            }

            return;
        }

        if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Control)
        {
            // Переставлять некуда — край среди соседей: клавиша всё равно своя, а не ход по строкам.
            e.Handled = true;
            Move(later: e.Key == Key.Down);
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && Selection().Count > 0)
        {
            e.Handled = true;
            ShowOnCanvas();
        }
    }

    /// <summary>Переставляет выбранный элемент на одного соседа раньше или позже — одной правкой.</summary>
    /// <param name="later">Позже, а не раньше.</param>
    private void Move(bool later)
    {
        if (_slot is not { Session.Edits: { } edits, Document.Syntax: { } syntax }
            || Selection() is not [var path]
            || path.Resolve(syntax) is not { Parent: XamlElement parent } element)
        {
            return;
        }

        var siblings = parent.ContentElements.ToList();
        var at = siblings.IndexOf(element);

        if (at < 0 || (later ? at >= siblings.Count - 1 : at == 0))
            return;

        // Якорь — сосед, перед которым элемент встанет: раньше — прежний сосед, позже — сосед через одного.
        XamlElementPath? anchor = later
            ? at + 2 < siblings.Count ? XamlElementPath.Of(siblings[at + 2]) : null
            : XamlElementPath.Of(siblings[at - 1]);

        _ = edits.MoveAsync(path, anchor);
    }

    /// <summary>
    /// Ставит выбранное в середину холста, не меняя масштаба: выбор холст ставит после раскладки, поэтому — после
    /// прохода.
    /// </summary>
    /// <remarks>
    /// Двойной щелчок этого не делает: у строк дерева студии он раскрывает и сворачивает узел
    /// (<see cref="AxTreeViewItem"/>), и второе значение того же жеста у листьев было бы своим только здесь.
    /// </remarks>
    private void ShowOnCanvas()
    {
        if (_canvas is not { } canvas)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!canvas.IsDisposed)
                canvas.Sheet.CenterOnSelection();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Меню строки: мышью — под указателем, и щелчок мимо выбранного выбирает строку под ним; клавишей — у
    /// главной выбранной строки.
    /// </summary>
    /// <remarks>
    /// Правый щелчок по выбранной строке оставляет выбор как есть — меню о нём, как в Rider и в проводнике. Пункты
    /// — пункты холста для формы, без пунктов доски для карточек.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_view is not { } view || _canvas is not { } canvas)
            return;

        var tree = view.Tree;
        var atPointer = e.TryGetPosition(tree, out _);

        if (atPointer && NodeOf(e.Source) is { } node && tree.SelectedItems?.Contains(node) != true)
            tree.SelectedItem = node;

        Control anchor = !atPointer && Selection().FirstOrDefault() is { } primary && Container(primary) is { } row
            ? row
            : tree;

        // Пункт меню правит форму, и дерево перестраивается: строка, которой меню вернёт клавиатуру, может уже
        // уйти — клавиатуру тогда вернёт перестройка.
        _keepAfterMenu = true;
        FormMenu.Show(anchor, canvas.FormMenuItems(tree), atPointer);
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

    /// <summary>Строки от корня до этой.</summary>
    private Stack<HierarchyNode> Chain(XamlElementPath path)
    {
        var chain = new Stack<HierarchyNode>();

        for (XamlElementPath? current = path; current is not null; current = current.Parent)
        {
            if (_byPath.TryGetValue(current, out var node))
                chain.Push(node);
        }

        return chain;
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
