using ArxisStudio.Controls;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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

    // Выбор в дереве и на канве синхронизируются в обе стороны, и каждый из них
    // поднимает событие другого. Флаг гасит это эхо.
    private bool _syncingSelection;

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ThemeSwitch.SelectedIndex = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? 1 : 0;

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
        if (ActiveDocument is not { } document)
        {
            DesignerForm.Content = null;
            Designer.IsVisible = false;
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

        Dispatcher.UIThread.Post(() =>
        {
            if (document.Nodes.FirstOrDefault() is { } root &&
                HierarchyTree.TreeContainerFromItem(root) is TreeViewItem container)
            {
                container.IsExpanded = true;
            }
        }, DispatcherPriority.Background);

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
                RevealInHierarchy(document.FindPath(target.Target));
                StatusText.Text = $"{Localizer.Instance["status.selected"]}: {target.DisplayName}";
            }
            else
            {
                HierarchyTree.SelectedItem = null;
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
        if (_syncingSelection || HierarchyTree.SelectedItem is not HierarchyNode { Control: { } control })
            return;

        _syncingSelection = true;
        try
        {
            Designer.SelectDesignTarget(control);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async Task CloseDocumentsAsync()
    {
        foreach (var document in _documents)
            await document.DisposeAsync();

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
