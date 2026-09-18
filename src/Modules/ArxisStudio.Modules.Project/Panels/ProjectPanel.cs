using System.Collections.Specialized;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Окно проекта: открытое решение деревом, как Solution в Rider.
/// </summary>
/// <remarks>
/// Панель связывает разметку с моделью и отвечает за то, чего нет в строках дерева: клавиатуру,
/// указатель, меню и буфер обмена. Решение приходит от службы проектов; подписка на неё живёт в
/// модели, а отпустить её может только панель — прощание для этого и отведено.
/// <para>
/// Клавиатура — как в дереве Rider: Right раскрывает узел, а на раскрытом шагает к первому ребёнку;
/// Left сворачивает, а на свёрнутом шагает к родителю; Enter открывает файл или раскрывает папку;
/// цифровые «+», «−» и «*» раскрывают, сворачивают и раскрывают ветку целиком. Буквы ищут строку
/// по имени среди видимых — это умеет сам список. Ctrl+F ставит каретку в поиск, Esc его очищает,
/// а стрелка вниз из поиска возвращает в дерево.
/// </para>
/// </remarks>
[ToolWindow(ProjectModule.PanelId)]
public sealed class ProjectPanel : ToolWindow
{
    private readonly List<Action> _release = [];
    private ProjectPanelView? _view;
    private ProjectModel? _model;
    private ProjectMenu? _menu;
    private LanguageProbe? _language;
    private Node? _selected;
    private string? _query;
    private bool _searching;

    /// <summary>Модель окна — тестам, чтобы ждать постройку дерева, а не время.</summary>
    internal ProjectModel? Model => _model;

    /// <summary>Разметка окна — тестам.</summary>
    internal ProjectPanelView? View => _view;

    /// <summary>Меню строк — тестам: попап — отдельное окно, которого у безголового прогона нет.</summary>
    internal ProjectMenu? Menu => _menu;

    /// <inheritdoc/>
    /// <remarks>Клавиатура окна — у дерева: с него начинают, и в него возвращаются из поиска.</remarks>
    public override Control? FocusTarget => _view?.Tree;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _model = new ProjectModel(Context, WordsOf(Context.Strings));
        _view = new ProjectPanelView { DataContext = _model };
        _menu = new ProjectMenu(Context.Strings, new MenuActions(
            _model.Open,
            Reveal.Show,
            Copy,
            row => Keep(() => _model.Tree.ExpandBranch(row)),
            row => Keep(() => _model.Tree.CollapseBranch(row))));

        Wire(_view, _model);

        return _view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        foreach (var release in _release)
            release();

        _release.Clear();
        _language?.Dispose();
        _language = null;
        _model?.Dispose();
        _model = null;
        _menu = null;
        _view = null;
    }

    /// <summary>Подписи дерева на языке студии.</summary>
    /// <param name="strings">Словари модуля.</param>
    internal static Words WordsOf(IStudioStrings strings) => new(
        strings["project.dependencies"],
        strings["project.frameworks"],
        strings["project.packages"],
        strings["project.projects"],
        strings["project.assemblies"],
        strings["project.analyzers"],
        strings["project.notLoaded"],
        strings["project.count.one"],
        strings["project.count"]);

    private void Wire(ProjectPanelView view, ProjectModel model)
    {
        var tree = view.Tree;

        tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        tree.AddHandler(InputElement.KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        tree.DoubleTapped += OnDoubleTapped;
        tree.ContextRequested += OnContextRequested;
        tree.SelectionChanged += OnSelectionChanged;
        view.KeyDown += OnViewKeyDown;
        view.Query.PropertyChanged += OnQueryChanged;
        view.CollapseAll.Click += OnCollapseAll;
        view.OpenSolution.Click += OnOpenSolution;
        view.Retry.Click += OnRetry;
        view.Stale.Closed += OnStaleClosed;
        model.Tree.Rows.CollectionChanged += OnRowsChanged;

        _release.Add(() =>
        {
            tree.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            tree.RemoveHandler(InputElement.KeyDownEvent, OnTreeKeyDown);
            tree.DoubleTapped -= OnDoubleTapped;
            tree.ContextRequested -= OnContextRequested;
            tree.SelectionChanged -= OnSelectionChanged;
            view.KeyDown -= OnViewKeyDown;
            view.Query.PropertyChanged -= OnQueryChanged;
            view.CollapseAll.Click -= OnCollapseAll;
            view.OpenSolution.Click -= OnOpenSolution;
            view.Retry.Click -= OnRetry;
            view.Stale.Closed -= OnStaleClosed;
            model.Tree.Rows.CollectionChanged -= OnRowsChanged;
        });

        // Подписи дерева — «Зависимости», «Пакеты», счёт проектов — строятся вместе с деревом, и
        // смена языка их не трогала бы. Проба держит привязку к словарю и перестраивает дерево,
        // когда перевод сменился.
        _language = new LanguageProbe(Context.Strings.Text("project.dependencies"), () =>
            _model?.Relabel(WordsOf(Context.Strings)));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Щелчок по шеврону раскрывает узел, но выделения не двигает — как в Rider: раскрывают,
        // чтобы заглянуть, а не чтобы перейти.
        if (_model is null || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed || Chevron(e.Source) is not { } row)
            return;

        Keep(() => _model.Tree.Toggle(row));
        e.Handled = true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null || Chevron(e.Source) is not null || RowOf(e.Source) is not { } row)
            return;

        Act(row);
        e.Handled = true;
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || _view?.Tree.SelectedItem is not Row row)
            return;

        var tree = _model.Tree;
        var plain = e.KeyModifiers == KeyModifiers.None;

        switch (e.Key)
        {
            case Key.Right when plain:
                if (row.HasChildren && !row.IsExpanded)
                    Keep(() => tree.Expand(row));
                else if (row.IsExpanded && tree.Rows.IndexOf(row) is var at && at + 1 < tree.Rows.Count)
                    Select(tree.Rows[at + 1]);
                break;
            case Key.Left when plain:
                if (row.IsExpanded)
                    Keep(() => tree.Collapse(row));
                else if (tree.ParentOf(row) is { } parent)
                    Select(parent);
                break;
            case Key.Enter when plain:
                Act(row);
                break;
            case Key.Add when plain:
                Keep(() => tree.Expand(row));
                break;
            case Key.Subtract when plain:
                Keep(() => tree.Collapse(row));
                break;
            case Key.Multiply when plain:
                Keep(() => tree.ExpandBranch(row));
                break;
            case Key.C when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && !row.Node.Path.IsEmpty:
                Copy(row.Node.Path.Value);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_menu is null || _view is null)
            return;

        var atPointer = e.TryGetPosition(_view.Tree, out _);
        var row = atPointer ? RowOf(e.Source) : _view.Tree.SelectedItem as Row;

        if (row is null)
            return;

        _view.Tree.SelectedItem = row;

        var anchor = atPointer ? (Control)_view.Tree : _view.Tree.ContainerFromItem(row) ?? _view.Tree;

        _menu.ShowAt(anchor, row, atPointer);
        e.Handled = true;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_view?.Tree.SelectedItem is Row row)
            _selected = row.Node;
    }

    /// <summary>
    /// Строка выделения ушла вместе с узлом — выделение переходит к ближайшему видимому предку.
    /// </summary>
    /// <remarks>
    /// Файл удалили, ветку свернули, решение перезагрузилось без него — список снимает выделение
    /// сам, и человек терял бы место, где стоял. Предок узла — ближайшее, что от него осталось.
    /// </remarks>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add || _view is null || _model is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_view?.Tree is not { SelectedItem: null } tree || _model is null || _selected is null)
                return;

            foreach (var node in _selected.Ancestors().Prepend(_selected))
            {
                if (_model.Tree.Find(node.Key) is { } row)
                {
                    tree.SelectedItem = row;
                    return;
                }
            }
        }, DispatcherPriority.Background);
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is null)
            return;

        var query = _view.Query;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            query.Focus();
            query.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && query.IsKeyboardFocusWithin && query.Text is { Length: > 0 })
        {
            query.Text = string.Empty;
            _view.Tree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && query.IsKeyboardFocusWithin && _model?.Tree.Rows.Count > 0)
        {
            Select(_view.Tree.SelectedItem as Row ?? _model.Tree.Rows[0]);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Запрос поиска сменился — дерево сужается на следующем кадре, один раз за все буквы кадра.
    /// </summary>
    private void OnQueryChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
            return;

        _query = e.GetNewValue<string?>();

        if (_searching)
            return;

        _searching = true;

        Dispatcher.UIThread.Post(() =>
        {
            _searching = false;
            _model?.Search(_query);
        }, DispatcherPriority.Background);
    }

    private void OnCollapseAll(object? sender, RoutedEventArgs e)
    {
        if (_model is not null)
            Keep(_model.Tree.CollapseAll);
    }

    /// <summary>
    /// Спрашивает решение и отдаёт его службе проектов.
    /// </summary>
    /// <remarks>
    /// Обработчик нажатия — <c>async void</c>, и исключение из него ушло бы необработанным в поток
    /// интерфейса. Окно выбора файлов бывает недоступно платформе, открытие может прерваться —
    /// окно проекта от этого не падает, а говорит в журнал.
    /// </remarks>
    private async void OnOpenSolution(object? sender, RoutedEventArgs e)
    {
        if (_view is null || _model is null)
            return;

        try
        {
            if (await SolutionPicker.Ask(_view, Context.Strings) is { } path && _model is { } model)
                await model.OpenSolution(path);
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException
                                            or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Context.Log.Write(StudioLogLevel.Warning, ProjectModule.LogSource, $"Решение не открылось: {failure.Message}");
        }
    }

    private void OnRetry(object? sender, RoutedEventArgs e) => _ = _model?.Retry();

    private void OnStaleClosed(object? sender, RoutedEventArgs e) => _model?.Dismiss();

    /// <summary>Файл открывается, всё прочее раскрывается или сворачивается.</summary>
    private void Act(Row row)
    {
        if (_model is null)
            return;

        if (row.Node.Kind == NodeKind.File)
            _model.Open(row.Node);
        else
            Keep(() => _model.Tree.Toggle(row));
    }

    /// <summary>
    /// Меняет раскрытое так, что выделение остаётся на месте или переходит к свёрнутому узлу.
    /// </summary>
    /// <remarks>
    /// Свёрнутая ветка уносит строки, и выделенная среди них пропала бы — выделение переходит к
    /// узлу, который свернули: человек только что стоял внутри него.
    /// </remarks>
    private void Keep(Action change)
    {
        if (_view is null || _model is null)
            return;

        var before = _view.Tree.SelectedItem as Row;

        change();

        if (before is null || _model.Tree.Rows.Contains(before))
            return;

        foreach (var node in before.Node.Ancestors())
        {
            if (_model.Tree.Find(node.Key) is { } row)
            {
                Select(row);
                return;
            }
        }
    }

    private void Select(Row row)
    {
        if (_view is null)
            return;

        var tree = _view.Tree;

        tree.SelectedItem = row;
        tree.ScrollIntoView(row);
        (tree.ContainerFromItem(row) as Control)?.Focus(NavigationMethod.Directional);
    }

    private void Copy(string text)
    {
        if (_view is not null)
            _ = TopLevel.GetTopLevel(_view)?.Clipboard?.SetTextAsync(text);
    }

    /// <summary>Строка, по шеврону которой пришлось событие; иначе — пусто.</summary>
    private static Row? Chevron(object? source) =>
        source is Visual visual
        && visual.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => control.Name == "Chevron") is { DataContext: Row row }
            ? row
            : null;

    /// <summary>Строка, в которой пришлось событие.</summary>
    private static Row? RowOf(object? source) =>
        (source as Visual)?.FindAncestorOfType<AxListBoxItem>(includeSelf: true)?.DataContext as Row;

    /// <summary>
    /// Слушает перевод одного ключа — чтобы знать, что язык студии сменился.
    /// </summary>
    /// <remarks>
    /// У словарей модуля события смены языка нет, а привязка к строке есть: она обновляется вместе
    /// с языком, и проба зовёт перестройку, когда перевод сменился.
    /// </remarks>
    private sealed class LanguageProbe : AvaloniaObject, IDisposable
    {
        private static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<LanguageProbe, string?>("Text");

        private readonly IDisposable _binding;
        private readonly Action _changed;

        public LanguageProbe(Avalonia.Data.BindingBase binding, Action changed)
        {
            _changed = changed;
            _binding = Bind(TextProperty, binding);
        }

        public void Dispose() => _binding.Dispose();

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TextProperty && change.OldValue is not null)
                _changed();
        }
    }
}
