using System.Collections.Specialized;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.History;
using ArxisStudio.Modules.Project.Model;
using ArxisStudio.Modules.Project.Tree;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
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
/// <para>
/// В две колонки, как в Unity, дерево слева — карта контейнеров, а их содержимое показывает правая
/// колонка (<see cref="BrowserPane"/>): выбор в дереве ведёт колонку, переход в колонке выделяет
/// контейнер в дереве. Раскладку выбирают в ⋮ полосы или в настройках студии, и применяется она
/// из настройки — одной дорогой, откуда бы ни пришла. Смена раскладки оставляет человека там, где
/// он стоял: файл, выделенный в дереве, в две колонки выделен плиткой в своей папке, и наоборот.
/// </para>
/// <para>
/// Выбор — множественный, как в Rider и Unity: Ctrl и Shift со щелчком и Shift со стрелками. Правка
/// берёт весь выбор (<see cref="Editing"/>): Delete удаляет, F2 переименовывает, как в проводнике
/// и VS Code, — Shift+F6 Rider у студии занят обходом панелей. После правки выделение встаёт на то,
/// что получилось: переименованное — на новое имя, удалённое — на соседа, занявшего его место.
/// </para>
/// <para>
/// Файлы из проводника приносят перетаскиванием (<see cref="FileDrop"/>), как в Rider и Unity: на
/// каталог — в него, на файл — в его каталог, копией и той же правкой, что Ctrl+V. Внутри окна файлы
/// и каталоги носят мышью (<see cref="FileDrag"/>): отпущенное переносится, с Ctrl — копируется.
/// </para>
/// </remarks>
[ToolWindow(ProjectModule.PanelId)]
public sealed partial class ProjectPanel : ToolWindow
{
    private readonly List<Action> _release = [];
    private readonly HistoryWindows _histories = new();
    private ProjectPanelView? _view;
    private ProjectModel? _model;
    private ProjectMenu? _menu;
    private BrowserPane? _pane;
    private Editing? _editing;
    private FileDrop? _fileDrop;
    private FileDrag? _fileDrag;
    private CanonicalPath? _dropFolder;
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

    /// <summary>Правая колонка — тестам.</summary>
    internal BrowserPane? Pane => _pane;

    /// <summary>Перетаскивание из проводника — тестам: его таймеры они зовут, а не ждут.</summary>
    internal FileDrop? Drop => _fileDrop;

    /// <summary>Тяга файлов внутри окна — тестам: что сделает отпускание и что написано у курсора.</summary>
    internal FileDrag? Drag => _fileDrag;

    /// <inheritdoc/>
    /// <remarks>Клавиатура окна — у дерева: с него начинают, и в него возвращаются из поиска.</remarks>
    public override Control? FocusTarget => _view?.Tree;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var settings = ProjectSettings.Read(Context.Settings);

        _model = new ProjectModel(Context, WordsOf(Context.Strings), settings);
        _view = new ProjectPanelView { DataContext = _model };

        // Создавать окно может, когда у студии есть обе службы: собирает пункт служба создания, а
        // кладёт на диск служба файлов — мимо неё окно диска не трогает.
        var newItems = Context.GetService<IStudioNewItems>();

        _editing = Context.Files() is { } files
            ? new Editing(
                Context,
                files,
                Context.History(),
                Owner,
                SystemFiles.For(() => _view is { } view ? TopLevel.GetTopLevel(view) : null),
                newItems)
            : null;

        var creating = _editing is not null && newItems is not null;

        _menu = new ProjectMenu(Context.Strings, new MenuActions(
            _model.Open,
            Reveal.Show,
            Copy,
            row => Keep(() => _model.Tree.ExpandBranch(row)),
            row => Keep(() => _model.Tree.CollapseBranch(row)),
            _editing is null ? null : Delete,
            _editing is null ? null : Rename,
            _editing is null ? null : Cut,
            _editing is null ? null : CopyFiles,
            _editing is null ? null : Paste,
            _editing is null ? null : _editing.Uncut,
            _editing is null || Context.History() is null ? null : Undo,
            Context.History() is null ? null : ShowHistory,
            _editing is null || Context.History() is null ? null : PutLabel,
            creating ? node => Creatable(newItems!, node) : null,
            creating ? Create : null));
        _pane = new BrowserPane(_view, _model, _menu, Located, Resize, Copy);
        _pane.Show(settings.IconSize, settings.Previews);

        Wire(_view, _model);

        return _view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        foreach (var release in _release)
            release();

        // Окна истории держат службу и словари модуля: прощаясь, панель закрывает их сама.
        _histories.CloseAll();

        _release.Clear();
        _pane?.Dispose();
        _pane = null;
        _language?.Dispose();
        _language = null;
        _model?.Dispose();
        _model = null;
        _menu = null;
        _editing = null;
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
        tree.Expanding = OnExpanding;
        view.KeyDown += OnViewKeyDown;
        view.Query.PropertyChanged += OnQueryChanged;
        view.Columns.PropertyChanged += OnColumnsChanged;
        view.CollapseAll.Click += OnCollapseAll;
        view.Options.Click += OnOptions;
        view.OpenSolution.Click += OnOpenSolution;
        view.Retry.Click += OnRetry;
        view.Stale.Closed += OnStaleClosed;
        model.Tree.Rows.CollectionChanged += OnRowsChanged;
        model.Tree.Rows.CollectionChanged += OnMarksChanged;
        model.Browser.Items.CollectionChanged += OnMarksChanged;
        model.Shown += Mark;
        Context.Settings.Changed += OnSettingsChanged;

        if (_editing is not null)
            _editing.ClipChanged += Mark;

        _release.Add(() =>
        {
            tree.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            tree.RemoveHandler(InputElement.KeyDownEvent, OnTreeKeyDown);
            tree.DoubleTapped -= OnDoubleTapped;
            tree.ContextRequested -= OnContextRequested;
            tree.SelectionChanged -= OnSelectionChanged;
            tree.Expanding = null;
            view.KeyDown -= OnViewKeyDown;
            view.Query.PropertyChanged -= OnQueryChanged;
            view.Columns.PropertyChanged -= OnColumnsChanged;
            view.CollapseAll.Click -= OnCollapseAll;
            view.Options.Click -= OnOptions;
            view.OpenSolution.Click -= OnOpenSolution;
            view.Retry.Click -= OnRetry;
            view.Stale.Closed -= OnStaleClosed;
            model.Tree.Rows.CollectionChanged -= OnRowsChanged;
            model.Tree.Rows.CollectionChanged -= OnMarksChanged;
            model.Browser.Items.CollectionChanged -= OnMarksChanged;
            model.Shown -= Mark;
            Context.Settings.Changed -= OnSettingsChanged;

            if (_editing is not null)
                _editing.ClipChanged -= Mark;
        });

        // Принесённое из проводника и перенесённое внутри окна кладёт служба файлов, как вставку: без
        // неё окно файлов не берёт и не носит.
        if (_editing is not null)
        {
            bool Ready() => _editing is { IsBusy: false } && _model?.IsReady == true;

            _fileDrop = new FileDrop(view, model, Ready, MarkDrop, DropFiles, row => Keep(() => model.Tree.Expand(row)));
            _fileDrag = new FileDrag(view, _fileDrop, Context.Strings, CarryFiles);

            _release.Add(() =>
            {
                _fileDrag?.Dispose();
                _fileDrag = null;
                _fileDrop?.Dispose();
                _fileDrop = null;
            });
        }

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
        if (_model is null || Chevron(e.Source) is not null || ListItems.At<Row>(e.Source) is not { } row)
            return;

        Act(row);
        e.Handled = true;
    }

    /// <summary>Диктор просит раскрыть или свернуть узел — так же, как стрелкой.</summary>
    private void OnExpanding(Row row, bool expand)
    {
        if (_model?.Tree is not { } tree)
            return;

        Keep(() =>
        {
            if (expand)
                tree.Expand(row);
            else
                tree.Collapse(row);
        });
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || Current() is not { } row)
            return;

        if (_menu is { } menu && EditKeys.Press(e, menu.Actions, TreeSelection, () => Pasting.Folder(row.Node), EditOrigin.Tree))
        {
            e.Handled = true;
            return;
        }

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
            case Key.Add when plain:
                Keep(() => tree.Expand(row));
                break;
            case Key.Subtract when plain:
                Keep(() => tree.Collapse(row));
                break;
            case Key.Multiply when plain:
                Keep(() => tree.ExpandBranch(row));
                break;
            case var _ when e.Is(EditKeys.Open):
                Act(row);
                break;
            case var _ when e.Is(EditKeys.Create) && _view is { } view
                            && _menu?.ShowAdd(row.Node, view.Tree.ContainerFromItem(row) ?? view.Tree, EditOrigin.Tree) == true:
                break;
            case var _ when e.Is(EditKeys.CopyPath) && !row.Node.Path.IsEmpty:
                Copy(row.Node.Path.Value);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <remarks>
    /// Правый щелчок по выбранной строке оставляет выбор как есть — меню о нём, как в Rider и в
    /// проводнике; по невыбранной — выбирает её одну.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_menu is null || _view is null)
            return;

        var atPointer = e.TryGetPosition(_view.Tree, out _);
        var row = atPointer ? ListItems.At<Row>(e.Source) : Current();

        if (row is null)
            return;

        if (_view.Tree.SelectedItems?.Contains(row) != true)
            _view.Tree.SelectedItem = row;

        var anchor = atPointer ? (Control)_view.Tree : _view.Tree.ContainerFromItem(row) ?? _view.Tree;

        ProjectMenu.ShowAt(anchor, Items(row), atPointer);
        e.Handled = true;
    }

    /// <summary>Пункты меню строки — с правкой того, что выбрано в дереве. Тестам — тем же путём, что меню.</summary>
    /// <param name="row">Строка, по которой щёлкнули.</param>
    internal IReadOnlyList<AxMenuItem> Items(Row row) => _menu?.Items(row, TreeSelection()) ?? [];

    /// <summary>Что выбрано в дереве — так, как его возьмёт правка.</summary>
    internal EditSelection TreeSelection() =>
        EditSelection.Of(_view is { } view ? ListItems.Of<Row>(view.Tree).Select(row => row.Node) : []);

    /// <summary>
    /// Строка, на которой стоит человек: та, где клавиатура, а без неё — выделенная.
    /// </summary>
    /// <remarks>
    /// При множественном выборе выделенных строк много, а клавиши говорят об одной — о той, где
    /// кольцо фокуса: Ctrl со стрелкой ведёт его, не трогая выбора.
    /// </remarks>
    private Row? Current()
    {
        if (_view?.Tree is not { } tree)
            return null;

        if (tree.IsKeyboardFocusWithin
            && (Caret() as Visual)?.FindAncestorOfType<TreeRow>(includeSelf: true)?.DataContext is Row focused)
        {
            return focused;
        }

        return tree.SelectedItem as Row;
    }

    /// <summary>Окно, которому принадлежат вопросы правки.</summary>
    private Window? Owner() => _view is { } view ? TopLevel.GetTopLevel(view) as Window : null;

    /// <summary>
    /// Показывает локальную историю узла отдельным окном; окно этого пути уже открыто — выводит его.
    /// </summary>
    private void ShowHistory(Node node)
    {
        if (Context.History() is not { } history || Owner() is not { } owner || ProjectMenu.HistoryTarget(node) is not var (path, folder, name))
            return;

        _histories.Show(path, () => HistoryWindow.Open(owner, new HistoryModel(history, Context.Strings, path, folder, name), Context.Strings));
    }

    /// <summary>Открытые окна истории — тестам.</summary>
    internal IReadOnlyCollection<HistoryWindow> Histories => _histories.All;

    /// <summary>
    /// Выделение в дереве сменилось; в две колонки выбранный контейнер открывается справа.
    /// </summary>
    /// <remarks>
    /// Переход снимает поиск: колонка показывает место, а не найденное, и строка поиска, оставшаяся
    /// с запросом, говорила бы неправду о том, что видно.
    /// </remarks>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_view?.Tree.SelectedItem is not Row row || _model is null)
            return;

        _selected = row.Node;

        if (!_model.IsTwoColumns || !row.Node.IsContainer
            || _model.Browser.Current?.Is(row.Node) == true)
        {
            return;
        }

        _model.Go(row.Node);
        Unsearch();
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
        else if (e.Key == Key.Down && query.IsKeyboardFocusWithin && _model is { IsTwoColumns: true, IsSearching: true } && _pane is not null)
        {
            // В две колонки найденное — справа, и стрелка ведёт туда, а не в дерево.
            var list = _pane.Shown;

            if (list.ItemCount > 0)
            {
                list.SelectedIndex = Math.Max(list.SelectedIndex, 0);
                (list.ContainerFromIndex(list.SelectedIndex) as Control)?.Focus(NavigationMethod.Directional);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down && query.IsKeyboardFocusWithin && _model?.Tree.Rows.Count > 0)
        {
            Select(_view.Tree.SelectedItem as Row ?? _model.Tree.Rows[0]);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Дерево встало на экран: каретка, ждавшая его на кнопке окна, переходит в дерево.
    /// </summary>
    /// <remarks>
    /// Студия отдаёт каретку окну при открытии, когда решение ещё открывается и дерева нет, и та
    /// встаёт на первую кнопку полосы — «Свернуть всё». Решение открывают и кнопкой «Открыть
    /// решение…», которая уходит с экрана вместе с кареткой. Работают же в дереве, и каретка,
    /// дождавшись его, идёт туда. Каретку в поиске дерево не трогает: там человек печатает.
    /// </remarks>
    private void OnColumnsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || _view is not { Columns.IsVisible: true } || Waiting() is not { } waiting)
            return;

        // Дерево строится вне потока интерфейса и встаёт на экран раньше своих строк: каретка ждёт
        // постройки. Кнопка «Открыть решение…» за это время уходит с экрана и роняет каретку — её
        // ведут в дерево; каретку, которую человек увёл сам, не трогают. Без кольца: клавишу никто
        // не нажимал, каретку переносит окно.
        Dispatcher.UIThread.Post(async () =>
        {
            if (_model is not { } model)
                return;

            await model.Settled.ConfigureAwait(true);

            if (Caret() is { } now && !ReferenceEquals(now, waiting) && Waiting() is null)
                return;

            if (_view?.Tree is not { } tree || model.Tree.Rows is not { Count: > 0 } rows)
                return;

            var row = tree.SelectedItem as Row ?? rows[0];

            tree.SelectedItem = row;
            tree.ScrollIntoView(row);
            (tree.ContainerFromItem(row) as Control)?.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Кнопка этого окна, на которой стоит каретка; <c>null</c> — каретка не на кнопке окна.</summary>
    private Button? Waiting() =>
        _view is { } view && Caret() is Button button && button.GetVisualAncestors().Contains(view) ? button : null;

    /// <summary>Где сейчас каретка в окне студии; <c>null</c> — нигде.</summary>
    private IInputElement? Caret() =>
        _view is { } view ? TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement() : null;

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

    /// <summary>⋮ полосы: одна колонка или две.</summary>
    private void OnOptions(object? sender, RoutedEventArgs e)
    {
        if (_view is null)
            return;

        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        foreach (var item in LayoutItems())
            flyout.Items.Add(item);

        flyout.ShowAt(_view.Options);
    }

    /// <summary>
    /// Пункты ⋮ — раскладка окна переключателями.
    /// </summary>
    /// <remarks>
    /// Пункт пишет настройку, а не раскладку: применит её обработчик настройки, тот же, что
    /// ловит правку в окне настроек студии. Отдельно от показа — тестам: попап — отдельное окно.
    /// </remarks>
    internal IReadOnlyList<AxMenuItem> LayoutItems()
    {
        var two = _model?.IsTwoColumns == true;

        return [Choice("project.layout.one", !two, false), Choice("project.layout.two", two, true)];

        AxMenuItem Choice(string key, bool chosen, bool columns)
        {
            var item = new AxMenuItem
            {
                Header = Context.Strings[key],
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = chosen,
            };

            item.Click += (_, _) => Context.Settings.Set(ProjectSettings.TwoColumnsKey, columns);

            return item;
        }
    }

    /// <summary>
    /// Настройку окна изменили — в ⋮, ползунком или в окне настроек студии.
    /// </summary>
    private void OnSettingsChanged(object? sender, string key)
    {
        if (key is not (ProjectSettings.TwoColumnsKey or ProjectSettings.IconSizeKey or ProjectSettings.PreviewsKey))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            Arrange();
        else
            Dispatcher.UIThread.Post(Arrange);
    }

    /// <summary>
    /// Применяет раскладку из настроек, оставляя человека на том же узле.
    /// </summary>
    private void Arrange()
    {
        if (_model is null || _view is null || _pane is null)
            return;

        var settings = ProjectSettings.Read(Context.Settings);
        var picked = _model.IsTwoColumns ? _pane.Selected?.Node : null;
        var stood = picked ?? (_view.Tree.SelectedItem as Row)?.Node;
        var columns = settings.TwoColumns != _model.IsTwoColumns;

        _model.Arrange(settings);
        _pane.Show(settings.IconSize, settings.Previews);

        // Плитки и строки — два списка, и выделение у каждого своё: сменив ступень, человек видел
        // бы невыделенный список, а строка под ним говорила бы о выбранном.
        if (columns && stood is not null)
            Stand(stood);
        else if (!columns && picked is not null)
            _pane.Select(picked);
    }

    /// <summary>Ставит выделение на узел в текущей раскладке.</summary>
    /// <remarks>
    /// В две колонки контейнер открывается справа, а файл выделяется плиткой в своей папке; в одну —
    /// дерево раскрывает дорогу до узла и выделяет его строку.
    /// </remarks>
    private void Stand(Node node)
    {
        if (_model is null || _pane is null)
            return;

        if (!_model.IsTwoColumns)
        {
            _model.Tree.Reveal(node);

            if (_model.Tree.Find(node.Key) is { } row)
                Select(row);

            return;
        }

        var container = node.IsContainer ? node : node.Ancestors().FirstOrDefault(ancestor => ancestor.IsContainer);

        if (container is null)
            return;

        _pane.Go(container);

        if (!node.IsContainer)
            _pane.Select(node);
    }

    /// <summary>
    /// Правая колонка ушла в контейнер — дерево слева раскрывает дорогу и выделяет его.
    /// </summary>
    /// <remarks>
    /// Выделяет, но клавиатуры не забирает: человек работает в колонке, и дерево только показывает,
    /// где он.
    /// </remarks>
    private void Located(Node container)
    {
        if (_view is null || _model is null)
            return;

        Unsearch();
        _model.Tree.Reveal(container);

        if (_model.Tree.Find(container.Key) is { } row)
        {
            _view.Tree.SelectedItem = row;
            _view.Tree.ScrollIntoView(row);
        }
    }

    /// <summary>Снимает поиск со строки поиска, если он там есть.</summary>
    private void Unsearch()
    {
        if (_view?.Query.Text is { Length: > 0 })
            _view.Query.Text = string.Empty;
    }

    /// <summary>Ступень сменили — ползунком, колесом или с клавиатуры, — и её размер уходит в настройки.</summary>
    private void Resize(double size)
    {
        if (ProjectSettings.Read(Context.Settings).IconSize != size)
            Context.Settings.Set(ProjectSettings.IconSizeKey, size);
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
}
