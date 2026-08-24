using System.Collections.ObjectModel;
using ArxisStudio.Controls;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Highlighting;
using IOPath = System.IO.Path;

namespace ArxisStudio;

/// <summary>
/// Главное окно студии: панели, канва дизайнера и открытые документы.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsStore? _settings;
    private readonly StudioWorkspace _workspace = new();
    private readonly List<DesignDocument> _documents = [];
    private readonly ObservableCollection<InspectorSection> _inspector = [];

    /// <summary>
    /// Под каким именем контрол палитры едет в перетаскивании.
    /// </summary>
    /// <remarks>
    /// Формат внутрипроцессный: палитра и канва — одно окно, и превращать
    /// контрол в текст ради дороги длиной в сантиметр незачем.
    /// </remarks>
    private static readonly DataFormat<ToolboxItem> ToolboxFormat =
        DataFormat.CreateInProcessFormat<ToolboxItem>("arxis.toolbox.item");

    // Выбор в дереве и на канве синхронизируются в обе стороны, и каждый из них
    // поднимает событие другого. Флаг гасит это эхо.
    private bool _syncingSelection;

    // Заполнение полей инспектора поднимает те же события, что и правка
    // человеком. Флаг отличает одно от другого.
    private bool _fillingInspector;

    private HierarchyNode? _selected;

    // Правка текста доходит до документа не с каждым нажатием клавиши: пока
    // человек набирает, разметка почти всегда сломана.
    private readonly DispatcherTimer _xamlIdle = new() { Interval = TimeSpan.FromMilliseconds(700) };

    // Текст в редакторе и текст документа обновляют друг друга, и каждое
    // обновление поднимает событие другого. Флаг гасит это эхо.
    private bool _fillingEditor;

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ThemeSwitch.SelectedIndex = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? 1 : 0;
        ViewSwitch.SelectedIndex = 0;
        InspectorSections.ItemsSource = _inspector;

        // Приём перетаскивания — присоединённые события, и в разметке их
        // атрибутом не назначить.
        DragDrop.AddDragOverHandler(Designer, OnDesignerDragOver);
        DragDrop.AddDropHandler(Designer, OnDesignerDrop);

        XamlEditor.TextChanged += OnXamlTextChanged;
        XamlEditor.LostFocus += OnXamlLostFocus;
        _xamlIdle.Tick += async (_, _) => await ApplyXamlAsync();

        ApplyXamlColors();
        ActualThemeVariantChanged += (_, _) => ApplyXamlColors();

        CanvasDots.Loaded += (_, _) => ApplyDotGrid();
        ActualThemeVariantChanged += (_, _) => ApplyDotGrid();

        // Системная рамка окна красится отдельно от содержимого: сама она
        // цвета темы не знает.
        Opened += (_, _) => StudioWindowChrome.Apply(
            this, _settings?.Current.Theme ?? StudioTheme.Dark);

        Closed += async (_, _) => await CloseDocumentsAsync();
    }

    /// <summary>Создаёт окно для открытого проекта.</summary>
    /// <param name="settings">Настройки студии.</param>
    /// <param name="projectPath">Путь к решению или проекту.</param>
    public MainWindow(ISettingsStore settings, string projectPath) : this()
    {
        _settings = settings;
        ProjectPath = projectPath;

        ProjectName.Text = IOPath.GetFileNameWithoutExtension(projectPath);
        Title = $"{IOPath.GetFileNameWithoutExtension(projectPath)} — ArxisStudio";

        Opened += async (_, _) => await OpenProjectAsync(projectPath);
    }

    /// <summary>Путь к открытому решению или проекту; null, если проект не открыт.</summary>
    public string? ProjectPath { get; }

    private DesignDocument? ActiveDocument =>
        DocumentTabs.SelectedIndex >= 0 && DocumentTabs.SelectedIndex < _documents.Count
            ? _documents[DocumentTabs.SelectedIndex]
            : null;

    private async Task OpenProjectAsync(string path)
    {
        StatusText.Text = Localizer.Instance["editor.opening"];

        var error = await _workspace.OpenAsync(path);

        if (error is not null || _workspace.Snapshot is not { } snapshot)
        {
            StatusText.Text = $"{Localizer.Instance["editor.openfailed"]}: {error}";
            return;
        }

        // Дерево спрашивает у диска, какие из объявленных файлов существуют,
        // поэтому строится в фоне.
        var tree = await Task.Run(() => ProjectTree.Build(snapshot));
        ProjectTreeView.ItemsSource = tree.Children;
        ProjectEmpty.IsVisible = false;

        // Раскрывать узлы можно только после того, как дерево создало контейнеры.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var project in tree.Children)
            {
                if (ProjectTreeView.TreeContainerFromItem(project) is TreeViewItem container)
                    container.IsExpanded = true;
            }
        }, DispatcherPriority.Background);

        StatusText.Text = path;
    }

    private async void OnProjectTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectTreeView.SelectedItem is not ProjectNode { IsFile: true } node)
            return;

        if (!node.IsDesignable)
        {
            StatusText.Text = Localizer.Instance["editor.nodesigner"];
            return;
        }

        await OpenDocumentAsync(node.FullPath);
    }

    private async Task OpenDocumentAsync(string filePath)
    {
        var existing = _documents.FindIndex(d =>
            string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            DocumentTabs.SelectedIndex = existing;
            return;
        }

        if (_workspace.Snapshot is not { } snapshot || _workspace.FindProjectForFile(filePath) is not { } project)
        {
            StatusText.Text = Localizer.Instance["editor.loadfailed"];
            return;
        }

        StatusText.Text = Localizer.Instance["editor.loading"];

        // Живые объекты создаются на потоке интерфейса — иначе загрузчик
        // откажется их отдавать.
        var (document, error) = await DesignDocument.OpenAsync(filePath, snapshot, project);

        if (document is null)
        {
            StatusText.Text = $"{Localizer.Instance["editor.loadfailed"]}: {error}";
            return;
        }

        _documents.Add(document);
        document.Reloaded += OnDocumentReloaded;
        document.Changed += OnDocumentChanged;

        DocumentTabs.Items.Add(new AxTabItem
        {
            Content = document.FileName,
            Icon = AxIcons.Window,
            IconBrush = this.FindResource("AxAccBrush") as IBrush,
        });

        DocumentTabs.IsVisible = true;
        DocumentTabs.SelectedIndex = _documents.Count - 1;
    }

    private void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs e) => ShowActiveDocument();

    private void ShowActiveDocument()
    {
        ShowInspector(null);
        ShowToolbox();

        if (ActiveDocument is not { } document)
        {
            DesignerForm.Content = null;
            Designer.IsVisible = false;
            ViewBar.IsVisible = false;
            CanvasHint.IsVisible = true;
            HierarchyTree.ItemsSource = null;
            HierarchyEmpty.IsVisible = true;
            return;
        }

        DesignerForm.Content = document.Surface;
        DesignerForm.Width = double.IsNaN(document.Surface.Width) ? 1280 : document.Surface.Width;
        DesignerForm.Height = double.IsNaN(document.Surface.Height) ? 800 : document.Surface.Height;

        Designer.IsVisible = true;
        CanvasHint.IsVisible = document.IsEmpty;

        HierarchyTree.ItemsSource = document.Nodes;
        HierarchyEmpty.IsVisible = document.Nodes.Count == 0;

        ExpandHierarchyRoot(document);

        ViewBar.IsVisible = true;
        FormSize.Text = $"{DesignerForm.Width:0} × {DesignerForm.Height:0}";
        ApplyView();

        StatusText.Text = document.FilePath;
    }

    private void OnDesignerSelectionChanged(object? sender, DesignSelectionChangedEventArgs e)
    {
        if (_syncingSelection || ActiveDocument is not { } document)
            return;

        _syncingSelection = true;
        try
        {
            if (e.NewPrimary is { } target)
            {
                var path = document.FindPath(target.Target);

                RevealInHierarchy(path);
                ShowInspector(path.Count > 0 ? path[^1] : null);
                StatusText.Text = $"{Localizer.Instance["status.selected"]}: {target.DisplayName}";
            }
            else
            {
                HierarchyTree.SelectedItem = null;
                ShowInspector(null);
                StatusText.Text = document.FilePath;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Показывает узел в дереве: раскрывает предков и выделяет сам узел.
    /// Контейнеры строк создаются по мере раскрытия, поэтому спускаться
    /// приходится шаг за шагом.
    /// </summary>
    private void RevealInHierarchy(IReadOnlyList<HierarchyNode> path)
    {
        if (path.Count == 0)
        {
            HierarchyTree.SelectedItem = null;
            return;
        }

        ItemsControl parent = HierarchyTree;

        for (var i = 0; i < path.Count - 1; i++)
        {
            if (parent.ContainerFromItem(path[i]) is not TreeViewItem container)
                break;

            container.IsExpanded = true;
            container.UpdateLayout();
            parent = container;
        }

        HierarchyTree.SelectedItem = path[^1];
    }

    private void OnHierarchySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || HierarchyTree.SelectedItem is not HierarchyNode node)
            return;

        _syncingSelection = true;
        try
        {
            ShowInspector(node);

            if (node.Control is { } control)
                Designer.SelectDesignTarget(control);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Жест на канве закончился: переносим новую геометрию в разметку.
    /// </summary>
    /// <remarks>
    /// Канва уже подвинула объекты — событие приходит после, а не вместо. Наше
    /// дело записать то же самое в документ, иначе перетаскивание жило бы до
    /// первой пересборки.
    /// </remarks>
    private async void OnDesignerEditCompleted(object? sender, DesignEditCompletedEventArgs e)
    {
        if (ActiveDocument is not { } document)
            return;

        foreach (var change in e.Changes.OfType<DesignGeometryChange>())
        {
            if (document.FindNode(change.Target) is not { } node)
                continue;

            var values = GeometryWriter.Describe(node, change.OldBounds, change.NewBounds);

            if (values.Count == 0)
                continue;

            var error = await document.SetAttributesAsync(node, values, $"{node.DisplayName}: {e.Kind}");

            StatusText.Text = error ?? $"{node.DisplayName}: {string.Join(", ", values.Select(value => $"{value.Name}={value.Text}"))}";
        }

        if (_selected is { } selected)
        {
            _fillingInspector = true;
            try
            {
                InspectorModel.Refresh(_inspector, selected, document.Session);
            }
            finally
            {
                _fillingInspector = false;
            }
        }

        UpdateHistoryButtons();
    }

    /// <summary>Показывает канву, текст разметки или то и другое.</summary>
    private void OnViewChanged(object? sender, SelectionChangedEventArgs e) => ApplyView();

    private void ApplyView()
    {
        var mode = ViewSwitch.SelectedIndex;
        var design = mode != 1;
        var xaml = mode != 0;

        EditorSplit.ColumnDefinitions[0].Width = new GridLength(design ? 1 : 0, design ? GridUnitType.Star : GridUnitType.Pixel);
        EditorSplit.ColumnDefinitions[1].Width = new GridLength(design && xaml ? 4 : 0, GridUnitType.Pixel);
        EditorSplit.ColumnDefinitions[2].Width = new GridLength(xaml ? 1 : 0, xaml ? GridUnitType.Star : GridUnitType.Pixel);

        XamlSplitter.IsVisible = design && xaml;
        XamlHost.IsVisible = xaml;

        if (xaml)
            ShowXaml();
    }

    /// <summary>Перечитывает текст редактора из документа.</summary>
    /// <remarks>
    /// Позиция каретки восстанавливается: правка из инспектора не должна
    /// уводить взгляд с того места, где человек читал разметку.
    /// </remarks>
    private void ShowXaml()
    {
        if (ActiveDocument is not { } document)
            return;

        var text = document.Text;

        if (string.Equals(XamlEditor.Text, text, StringComparison.Ordinal))
            return;

        var caret = XamlEditor.CaretOffset;

        _fillingEditor = true;
        try
        {
            XamlEditor.Text = text;
            XamlEditor.CaretOffset = Math.Min(caret, text.Length);
        }
        finally
        {
            _fillingEditor = false;
        }
    }

    private void OnXamlTextChanged(object? sender, EventArgs e)
    {
        if (_fillingEditor)
            return;

        _xamlIdle.Stop();
        _xamlIdle.Start();
    }

    private async void OnXamlLostFocus(object? sender, RoutedEventArgs e) => await ApplyXamlAsync();

    private async Task ApplyXamlAsync()
    {
        _xamlIdle.Stop();

        if (_fillingEditor || ActiveDocument is not { } document)
            return;

        var error = await document.SetTextAsync(XamlEditor.Text, "Правка разметки");

        if (error is not null)
        {
            StatusText.Text = error;
            return;
        }

        StatusText.Text = Localizer.Instance["xaml.applied"];
        UpdateHistoryButtons();
    }

    /// <summary>
    /// Красит разметку в цвета темы.
    /// </summary>
    /// <remarks>
    /// Готовая подсветка XML приходит со своими цветами, рассчитанными на белый
    /// фон, и рядом с тёмной темой студии выглядит чужой. Сами правила разбора
    /// при этом верные, поэтому меняются только цвета — и меняются заново при
    /// каждой смене темы.
    /// </remarks>
    private void ApplyXamlColors()
    {
        if (HighlightingManager.Instance.GetDefinition("XML") is not { } definition)
            return;

        Paint(definition, "Comment", "AxFg3Brush");
        Paint(definition, "XmlTag", "AxAccBrush");
        Paint(definition, "AttributeName", "AxPurBrush");
        Paint(definition, "AttributeValue", "AxGrnBrush");
        Paint(definition, "XmlDeclaration", "AxFg3Brush");
        Paint(definition, "DocType", "AxFg3Brush");
        Paint(definition, "CData", "AxYelBrush");
        Paint(definition, "Entity", "AxYelBrush");

        XamlEditor.SyntaxHighlighting = definition;

        void Paint(IHighlightingDefinition target, string name, string resource)
        {
            if (target.GetNamedColor(name) is { } color &&
                this.TryFindResource(resource, ActualThemeVariant, out var value) &&
                value is ISolidColorBrush brush)
            {
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
            }
        }
    }

    /// <summary>Перечитывает палитру под открытый документ.</summary>
    private void ShowToolbox()
    {
        var root = ActiveDocument?.Document?.Root;
        var groups = ToolboxCatalog.For(root, ToolboxSearch.Text);

        ToolboxGroups.ItemsSource = groups;
        ToolboxBody.IsVisible = root is not null;
        ToolboxEmpty.IsVisible = root is null;
    }

    private void OnToolboxSearchChanged(object? sender, TextChangedEventArgs e) => ShowToolbox();

    /// <summary>
    /// Пускает контрол палитры в дело: двойным щелчком — в выделенный элемент,
    /// перетаскиванием — туда, куда его отпустят.
    /// </summary>
    /// <remarks>
    /// Оба способа начинаются здесь, потому что перетаскивание можно начать
    /// только с события нажатия, а начатое — оно забирает указатель себе, и
    /// второй щелчок до палитры уже не доходит. Поэтому нажатия и различаются
    /// по счётчику: двойное — вставка, одиночное — начало перетаскивания.
    /// </remarks>
    private async void OnToolboxItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ToolboxItem item })
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount >= 2)
        {
            await InsertAsync(item, _selected ?? ActiveDocument?.Nodes.FirstOrDefault(), placement: "");
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ToolboxFormat, item));

        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
    }

    private void OnDesignerDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(ToolboxFormat) && ActiveDocument is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDesignerDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ToolboxFormat) is not { } item || ActiveDocument is not { } document)
            return;

        var point = e.GetPosition(DesignerForm);
        var target = FindDropTarget(document, point);

        if (target is null)
        {
            StatusText.Text = Localizer.Instance["toolbox.nowhere"];
            return;
        }

        // На Canvas контрол ложится туда, где его отпустили; в остальных
        // раскладках место ему отводит родитель, и координаты только мешали бы.
        var placement = "";

        if (target.Control is Canvas canvas && PointIn(document, canvas, point) is { } local)
            placement = $"Canvas.Left=\"{Math.Round(local.X)}\" Canvas.Top=\"{Math.Round(local.Y)}\"";

        await InsertAsync(item, target, placement);
    }

    private async Task InsertAsync(ToolboxItem item, HierarchyNode? parent, string placement)
    {
        if (ActiveDocument is not { } document || parent is null)
            return;

        if (!CanHold(parent))
        {
            StatusText.Text = Localizer.Instance["toolbox.nowhere"];
            return;
        }

        if (ToolboxCatalog.Markup(item, parent.Element, placement) is not { } markup)
        {
            StatusText.Text = $"{Localizer.Instance["toolbox.nonamespace"]}: {item.TypeName}";
            return;
        }

        var path = parent.Path;
        var error = await document.InsertAsync(parent, -1, markup, $"Вставить {item.TypeName}");

        StatusText.Text = error
            ?? $"{Localizer.Instance["toolbox.inserted"]}: {item.TypeName} → {parent.DisplayName}";

        // Вставка перестраивает дерево, поэтому прежние узлы больше не те:
        // родителя приходится искать заново, зато вставленный контрол сразу
        // виден и выделен — иначе после вставки не за что взяться.
        if (error is null && document.FindByPath(path)?.Children.LastOrDefault() is { } inserted)
            Select(inserted);

        UpdateHistoryButtons();
    }

    /// <summary>Выделяет узел и на канве, и в дереве.</summary>
    private void Select(HierarchyNode node)
    {
        _syncingSelection = true;
        try
        {
            RevealInHierarchy(ActiveDocument?.FindPath(node.Control) ?? []);
            ShowInspector(node);

            if (node.Control is { } control)
                Designer.SelectDesignTarget(control);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Находит элемент документа под точкой канвы.
    /// </summary>
    /// <remarks>
    /// Обычная проверка попадания тут не работает: в режиме показа готовой формы
    /// её содержимое не участвует в проверке попадания — иначе документ ловил бы
    /// щелчки вместо редактора. Поэтому попадание считается по прямоугольникам,
    /// как это делает и сама канва, и побеждает самый глубокий элемент, который
    /// вообще может кого-то в себе держать.
    /// </remarks>
    private HierarchyNode? FindDropTarget(DesignDocument document, Point point)
    {
        HierarchyNode? found = null;

        Walk(document.Nodes);
        return found;

        void Walk(IEnumerable<HierarchyNode> nodes)
        {
            foreach (var node in nodes)
            {
                // Корень документа — окно, а окно частью чужого дерева не
                // бывает: на канве лежит его содержимое. Точку к нему не
                // пересчитать, но идти вглубь это не мешает.
                if (node.Control is { IsVisible: true } control &&
                    PointIn(document, control, point) is { } local)
                {
                    if (!new Rect(control.Bounds.Size).Contains(local))
                        continue;

                    if (CanHold(node))
                        found = node;
                }

                Walk(node.Children);
            }
        }
    }

    /// <summary>
    /// Пересчитывает точку формы в координаты контрола документа.
    /// </summary>
    /// <param name="document">Открытый документ — он владеет поверхностью показа.</param>
    /// <param name="control">Контрол внутри показанной формы.</param>
    /// <param name="point">Точка в координатах <c>DesignerForm</c>.</param>
    /// <returns>Точка в координатах контрола или null, если он не в этой форме.</returns>
    /// <remarks>
    /// Готовый пересчёт координат тут неприменим: содержимое документа
    /// принадлежит своему окну, а не нашему, и <c>TranslatePoint</c> через эту
    /// границу возвращает пустоту. Зато внутри самого документа прямоугольник
    /// каждого контрола задан относительно его визуального родителя, и цепочку
    /// до поверхности можно сложить самим. Дальше поверхности идти не нужно и
    /// нельзя: она занимает форму целиком, а выше начинается чужая система
    /// координат, к которой эти прямоугольники отношения не имеют.
    /// </remarks>
    private static Point? PointIn(DesignDocument document, Control control, Point point)
    {
        var offset = new Point();

        for (Visual? visual = control; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, document.Surface))
                return point - offset;

            offset += visual.Bounds.Position;
        }

        return null;
    }

    /// <summary>
    /// Может ли элемент принять в себя ещё один контрол.
    /// </summary>
    /// <remarks>
    /// Панель держит сколько угодно детей, а рамка и контрол с содержимым —
    /// ровно одного, и то если он ещё не занят: ни вложенным элементом, ни
    /// значением, записанным атрибутом.
    /// </remarks>
    private static bool CanHold(HierarchyNode node) => node.Control switch
    {
        Panel => true,
        Decorator => IsFree(node, "Child"),
        ContentControl => IsFree(node, "Content"),
        _ => false,
    };

    private static bool IsFree(HierarchyNode node, string member) =>
        !node.Element.ContentElements.Any() &&
        node.Element.GetAttribute(member) is null;

    private async void OnDesignerDeleteRequested(object? sender, DesignEditorDeleteRequestedEventArgs e)
    {
        if (ActiveDocument is not { } document)
            return;

        // Флаг читают сразу после возврата из обработчика, то есть до первого
        // ожидания: поставить его позже — значит не поставить вовсе.
        e.Handled = true;

        foreach (var target in e.Targets)
        {
            if (document.FindNode(target.Target) is not { } node)
                continue;

            var error = await document.RemoveAsync(node);

            StatusText.Text = error ?? $"{Localizer.Instance["structure.deleted"]}: {node.DisplayName}";
        }

        UpdateHistoryButtons();
    }

    private async void OnDesignerReorderRequested(object? sender, DesignEditorReorderRequestedEventArgs e)
    {
        if (ActiveDocument is not { } document || document.FindNode(e.Target) is not { } node)
            return;

        e.Handled = true;

        var error = await document.MoveAsync(node, e.NewIndex);

        StatusText.Text = error ?? $"{Localizer.Instance["structure.moved"]}: {node.DisplayName}";
        UpdateHistoryButtons();
    }

    /// <summary>Показывает в инспекторе свойства узла; null очищает панель.</summary>
    private void ShowInspector(HierarchyNode? node)
    {
        _selected = node;
        _inspector.Clear();

        if (node is null || ActiveDocument is not { } document)
        {
            InspectorBody.IsVisible = false;
            InspectorEmpty.IsVisible = true;
            return;
        }

        _fillingInspector = true;
        try
        {
            foreach (var section in InspectorModel.Build(node, document.Session))
                _inspector.Add(section);
        }
        finally
        {
            _fillingInspector = false;
        }

        InspectorTitle.Text = node.DisplayName;
        InspectorType.Text = node.Control?.GetType().FullName ?? node.TypeName;

        InspectorBody.IsVisible = true;
        InspectorEmpty.IsVisible = false;

        UpdateHistoryButtons();
    }

    private async void OnInspectorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter)
            await CommitAsync(sender);
    }

    private async void OnInspectorLostFocus(object? sender, RoutedEventArgs e) => await CommitAsync(sender);

    private async void OnInspectorToggled(object? sender, RoutedEventArgs e) => await CommitAsync(sender);

    private async void OnInspectorChoice(object? sender, SelectionChangedEventArgs e) => await CommitAsync(sender);

    private async void OnInspectorReset(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: InspectorRow row })
            await CommitAsync(row, null);
    }

    private async Task CommitAsync(object? sender)
    {
        if (_fillingInspector || sender is not Control { DataContext: InspectorRow row })
            return;

        await CommitAsync(row, row.Value);
    }

    /// <summary>
    /// Доводит правку строки до документа и показывает, что из этого вышло.
    /// </summary>
    /// <remarks>
    /// Строки перечитываются в любом случае: и когда правка прошла — значение
    /// могло нормализоваться, — и когда нет, чтобы поле не осталось показывать
    /// то, чего в документе нет.
    /// </remarks>
    private async Task CommitAsync(InspectorRow row, string? text)
    {
        if (_selected is not { } node || ActiveDocument is not { } document)
            return;

        var error = await document.SetAttributeAsync(node, row.Name, text);

        StatusText.Text = error is null
            ? $"{node.DisplayName}.{row.Name} = {text ?? "—"}"
            : error;

        _fillingInspector = true;
        try
        {
            InspectorModel.Refresh(_inspector, node, document.Session);
        }
        finally
        {
            _fillingInspector = false;
        }

        UpdateHistoryButtons();
    }

    private async void OnUndoClick(object? sender, RoutedEventArgs e) => await StepHistoryAsync(undo: true);

    private async void OnRedoClick(object? sender, RoutedEventArgs e) => await StepHistoryAsync(undo: false);

    private async Task StepHistoryAsync(bool undo)
    {
        if (ActiveDocument is not { } document)
            return;

        var error = undo ? await document.UndoAsync() : await document.RedoAsync();

        if (error is not null)
            StatusText.Text = error;

        if (_selected is { } node)
        {
            _fillingInspector = true;
            try
            {
                InspectorModel.Refresh(_inspector, node, document.Session);
            }
            finally
            {
                _fillingInspector = false;
            }
        }

        UpdateHistoryButtons();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
            return;

        await document.SaveAsync();
        StatusText.Text = $"{Localizer.Instance["inspector.saved"]}: {document.FilePath}";
        UpdateHistoryButtons();
    }

    /// <summary>Текст документа изменился — показываем его в редакторе.</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActiveDocument) && XamlHost.IsVisible)
            ShowXaml();
    }

    private void OnDocumentReloaded(object? sender, EventArgs e)
    {
        if (sender is not DesignDocument document || !ReferenceEquals(document, ActiveDocument))
            return;

        // Дерево пересобрано: прежний узел больше не тот объект, что лежит в
        // дереве, и держаться за него нельзя.
        HierarchyTree.ItemsSource = document.Nodes;
        HierarchyEmpty.IsVisible = document.Nodes.Count == 0;
        ShowInspector(null);
        ExpandHierarchyRoot(document);
    }

    /// <summary>Раскрывает корень дерева документа.</summary>
    /// <remarks>
    /// Раскрывать можно только после того, как дерево создало контейнеры строк,
    /// поэтому не сразу.
    /// </remarks>
    private void ExpandHierarchyRoot(DesignDocument document) => Dispatcher.UIThread.Post(
        () =>
        {
            if (document.Nodes.FirstOrDefault() is { } root &&
                HierarchyTree.TreeContainerFromItem(root) is TreeViewItem container)
            {
                container.IsExpanded = true;
            }
        },
        DispatcherPriority.Background);

    private void UpdateHistoryButtons()
    {
        var document = ActiveDocument;

        UndoButton.IsEnabled = document?.CanUndo ?? false;
        RedoButton.IsEnabled = document?.CanRedo ?? false;
        SaveButton.IsEnabled = document?.IsModified ?? false;
    }

    private async Task CloseDocumentsAsync()
    {
        foreach (var document in _documents)
        {
            document.Reloaded -= OnDocumentReloaded;
            document.Changed -= OnDocumentChanged;
            await document.DisposeAsync();
        }

        _documents.Clear();
        await _workspace.DisposeAsync();
    }

    private void ApplyDotGrid()
    {
        var showGrid = _settings?.Current.ShowCanvasGrid ?? true;
        if (!showGrid)
        {
            CanvasDots.Background = null;
            return;
        }

        if (this.TryFindResource("AxDotColor", ActualThemeVariant, out var value) && value is Color color)
        {
            CanvasDots.Background = new VisualBrush
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                DestinationRect = new RelativeRect(0, 0, 20, 20, RelativeUnit.Absolute),
                Visual = new Border
                {
                    Width = 20,
                    Height = 20,
                    Child = new Ellipse
                    {
                        Width = 2,
                        Height = 2,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Fill = new SolidColorBrush(color),
                    },
                },
            };
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var theme = ThemeSwitch.SelectedIndex == 1 ? StudioTheme.Light : StudioTheme.Dark;
        StudioTheming.Apply(theme);

        if (_settings is not null)
        {
            _settings.Current.Theme = theme;
            _settings.Save();
        }
    }
}
