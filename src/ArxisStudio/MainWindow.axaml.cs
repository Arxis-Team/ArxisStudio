using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using IOPath = System.IO.Path;

namespace ArxisStudio;

/// <summary>
/// Главное окно студии — оболочка: зоны, вкладки документов, панель проекта,
/// консоль и запуск.
/// </summary>
/// <remarks>
/// Всё, что умеет редактировать, оболочка получает от модулей и плагинов через
/// SDK: редактор документов открывает вкладки, панели встают в зоны. Дизайнер
/// форм — встроенный модуль и проходит тем же путём, что и внешний плагин, —
/// это и есть проверка контракта на себе.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>Сборки встроенных модулей — состав студии.</summary>
    private static readonly Assembly[] BuiltInModules =
    [
        typeof(Modules.Designer.DesignerModule).Assembly,
    ];

    private readonly ISettingsStore? _settings;
    private readonly StudioWorkspace _workspace = new();
    private readonly List<OpenDocument> _documents = [];
    private readonly StudioLog _log = new();
    private readonly StudioCommands _commands = new();
    private readonly StudioRunner _runner;
    private readonly PluginContributionRegistry _contributions = new();
    private PluginHost? _plugins;
    private IReadOnlyList<InstalledPlugin> _installed = [];
    private IReadOnlyList<StudioMenuItem> _menu = [];
    private DocumentView? _active;

    // Панели плагинов, разложенные по нижним вкладкам: вкладка знает свой
    // номер, а содержимое лежит в общем месте и показывается по очереди.
    private readonly Dictionary<int, Control> _bottomPanels = [];

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();
        // Выбранная вкладка ставится здесь, а не в разметке: заданная там, она
        // поднимает событие ещё во время разбора, когда полей окна нет и в
        // помине.
        ThemeSwitch.SelectedIndex = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? 1 : 0;
        BottomTabs.SelectedIndex = 0;

        _runner = new StudioRunner(_log);
        _runner.StateChanged += (_, _) => UpdateRunButtons();

        ConsoleList.ItemsSource = _log.Entries;
        _log.Entries.CollectionChanged += (_, _) => ConsoleScroll.ScrollToEnd();

        // Системная рамка окна красится отдельно от содержимого: сама она
        // цвета темы не знает.
        Opened += (_, _) => StudioWindowChrome.Apply(
            this, _settings?.Current.Theme ?? StudioTheme.Dark);

        Closed += async (_, _) =>
        {
            _runner.Dispose();
            _plugins?.Dispose();
            await CloseDocumentsAsync();
        };
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

        LoadModulesAndPlugins();
        UpdateRunButtons();
    }

    /// <summary>
    /// Поднимает встроенные модули, а за ними — включённые плагины.
    /// </summary>
    /// <remarks>
    /// Путь у них один: и модуль, и плагин активируются общим контрактом,
    /// заявляют панели и редакторы, попадают в реестр вкладов. Разница только в
    /// доставке — модуль приезжает со студией и живёт в основном контексте.
    /// </remarks>
    private void LoadModulesAndPlugins()
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(Modules.Designer.IDesignerWorkspace)] = _workspace,
            [typeof(IStudioStatus)] = new StatusSink(StatusText),
            [typeof(PluginContributionRegistry)] = _contributions,
        };

        var catalog = new PluginCatalog();
        var host = new PluginHost(new StudioContextFactory(_log, _commands, ProjectPath, services));

        _plugins = host;
        _installed = catalog.Scan();
        _contributions.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);

        var raised = BuiltInModules.Select(host.LoadBuiltIn)
            .Concat(host.LoadStartup(_installed));

        foreach (var loaded in raised)
            Accept(loaded);

        foreach (var waiting in host.Deferred)
            _log.Write(StudioLogLevel.Debug, "Plugins", $"{waiting.DisplayName} ждёт своего события");

        ShowMenu();
    }

    /// <summary>Принимает поднятый модуль или плагин: вклады и панели.</summary>
    private void Accept(LoadedPlugin loaded)
    {
        if (loaded.Error is { } error)
        {
            _log.Write(StudioLogLevel.Error, "Plugins", $"{loaded.Installed.DisplayName}: {error}");
            return;
        }

        _log.Write(StudioLogLevel.Info, "Plugins", $"{loaded.Installed.DisplayName} поднят");

        _contributions.Add(loaded);
        MountPanels(loaded);
    }

    private async void OnProjectTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectTreeView.SelectedItem is not ProjectNode { IsFile: true } node)
            return;

        await OpenDocumentAsync(node.FullPath);
    }

    private async Task OpenDocumentAsync(string filePath)
    {
        var existing = _documents.FindIndex(document =>
            string.Equals(document.Path, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            DocumentTabs.SelectedIndex = existing;
            return;
        }

        // Открытие файла — тоже событие: плагин, объявивший его тип, ждал
        // именно этого.
        Activate(waiting => PluginActivation.WaitsForFileType(waiting.Manifest, IOPath.GetExtension(filePath)));

        if (_contributions.EditorFor(filePath) is not { } editor)
        {
            StatusText.Text = Localizer.Instance["editor.nodesigner"];
            return;
        }

        StatusText.Text = Localizer.Instance["editor.loading"];

        var (view, error) = await editor.OpenAsync(filePath);

        if (view is null)
        {
            StatusText.Text = $"{Localizer.Instance["editor.loadfailed"]}: {error}";
            return;
        }

        _documents.Add(new OpenDocument(filePath, view));

        DocumentTabs.Items.Add(new AxTabItem
        {
            Content = view.Title,
            Icon = AxIcons.Window,
            IconBrush = this.FindResource("AxAccBrush") as IBrush,
        });

        DocumentTabs.IsVisible = true;
        DocumentTabs.SelectedIndex = _documents.Count - 1;
    }

    private void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs e) => ShowActiveDocument();

    private void ShowActiveDocument()
    {
        var view = DocumentTabs.SelectedIndex >= 0 && DocumentTabs.SelectedIndex < _documents.Count
            ? _documents[DocumentTabs.SelectedIndex].View
            : null;

        if (ReferenceEquals(_active, view))
            return;

        _active?.OnDeactivated();
        _active = view;

        DocumentHost.Content = view?.Content;
        CanvasHint.IsVisible = view is null;

        view?.OnActivated();

        if (view is not null)
            StatusText.Text = _documents[DocumentTabs.SelectedIndex].Path;
    }

    /// <summary>
    /// Собирает меню студии; кнопка появляется, только если есть что показать.
    /// </summary>
    private void ShowMenu()
    {
        _menu = StudioMenu.Build(_installed);
        MenuButton.IsVisible = _menu.Count > 0;
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (_menu.Count == 0)
            return;

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach (var item in _menu)
            flyout.Items.Add(Build(item));

        flyout.ShowAt(MenuButton);

        MenuItem Build(StudioMenuItem source)
        {
            var item = new MenuItem { Header = source.Title };

            if (source.IsCommand)
                item.Click += (_, _) => Run(source);

            foreach (var child in source.Children)
                item.Items.Add(Build(child));

            return item;
        }
    }

    /// <summary>
    /// Выполняет команду пункта меню, подняв плагин, если тот ещё ждал.
    /// </summary>
    /// <remarks>
    /// Плагин, объявивший <c>onCommand:</c>, до этого момента не был загружен —
    /// значит и обработчика команды пока нет, и поднять его нужно раньше вызова.
    /// </remarks>
    private void Run(StudioMenuItem item)
    {
        if (item.CommandId is not { } command)
            return;

        Activate(waiting => PluginActivation.WaitsForCommand(waiting.Manifest, command));

        if (!_commands.Invoke(command))
            _log.Write(StudioLogLevel.Warning, "Plugins", $"Команду {command} никто не обрабатывает");
    }

    /// <summary>Поднимает ждущие плагины, которым подошло событие.</summary>
    /// <param name="matches">Какое событие произошло.</param>
    private void Activate(Func<InstalledPlugin, bool> matches)
    {
        if (_plugins is not { } host)
            return;

        foreach (var waiting in host.Deferred.Where(matches).ToList())
        {
            _log.Write(StudioLogLevel.Info, "Plugins", $"{Localizer.Instance["menu.activating"]}: {waiting.DisplayName}");

            if (host.Activate(waiting.Id) is { } loaded)
                Accept(loaded);
        }
    }

    /// <summary>
    /// Ставит панели модуля или плагина в объявленные зоны.
    /// </summary>
    /// <remarks>
    /// Зону и заголовок берём из манифеста, а сам класс панели — из сборки по
    /// атрибуту: манифест студия читает, не загружая сборку, и список панелей у
    /// неё есть раньше, чем атрибут вообще становится виден.
    /// </remarks>
    private void MountPanels(LoadedPlugin loaded)
    {
        if (loaded.Installed.Manifest is not { } manifest || loaded.Studio is not { } studio)
            return;

        var panels = loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(Sdk.ToolWindow).IsAssignableFrom(type))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ToolWindowAttribute>()))
            .Where(found => found.Attribute is not null)
            .ToDictionary(found => found.Attribute!.Id, found => found.Type, StringComparer.Ordinal);

        foreach (var declared in manifest.Contributions.ToolWindows)
        {
            if (!panels.TryGetValue(declared.Id, out var type))
            {
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"Панель {declared.Id} объявлена в манифесте, но в сборке её нет");
                continue;
            }

            if (Activator.CreateInstance(type) is not Sdk.ToolWindow panel)
                continue;

            panel.Attach(studio);
            Mount(declared.Zone, declared.Title, panel.Content);
        }
    }

    /// <summary>Ставит содержимое панели в зону студии.</summary>
    private void Mount(string zone, string title, Control content)
    {
        var window = new AxToolWindow { Content = content };

        SetTitle(window, title);

        switch (zone.ToLowerInvariant())
        {
            case "bottom":
                var tab = new AxTabItem { Classes = { "compact" }, IsClosable = false };

                SetTabTitle(tab, title);
                BottomTabs.Items.Add(tab);
                BottomPluginHost.Children.Add(content);

                content.IsVisible = false;
                _bottomPanels[BottomTabs.Items.Count - 1] = content;
                break;

            case "left":
                Append(LeftZone, window);
                break;

            default:
                Append(RightZone, window);
                break;
        }

        _log.Write(StudioLogLevel.Debug, "Plugins", $"Панель «{Resolve(title)}» встала в зону {zone}");
    }

    /// <summary>
    /// Ставит заголовок панели, переводя ключ вида <c>%panel.hierarchy%</c>.
    /// </summary>
    /// <remarks>
    /// Заголовок из манифеста — единственный текст панели, который пишет не её
    /// автор, а студия, поэтому и переводить его при смене языка — забота студии.
    /// </remarks>
    private static void SetTitle(AxToolWindow window, string title)
    {
        if (Key(title) is { } key)
        {
            window.Bind(
                AxToolWindow.TitleProperty,
                new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track(key) });
        }
        else
        {
            window.Title = title;
        }
    }

    private static void SetTabTitle(AxTabItem tab, string title)
    {
        if (Key(title) is { } key)
        {
            tab.Bind(
                ContentControl.ContentProperty,
                new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track(key) });
        }
        else
        {
            tab.Content = title;
        }
    }

    private static string? Key(string title) =>
        title.Length > 2 && title[0] == '%' && title[^1] == '%' ? title[1..^1] : null;

    private static string Resolve(string title) =>
        Key(title) is { } key ? Localizer.Instance[key] : title;

    /// <summary>
    /// Добавляет панель новой строкой сетки, отделив её от соседей.
    /// </summary>
    private static void Append(Grid zone, Control panel)
    {
        if (zone.RowDefinitions.Count > 0)
        {
            zone.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var divider = new AxDivider();

            Grid.SetRow(divider, zone.RowDefinitions.Count - 1);
            zone.Children.Add(divider);
        }

        zone.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetRow(panel, zone.RowDefinitions.Count - 1);
        zone.Children.Add(panel);
    }

    private void OnBottomTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        var tab = BottomTabs.SelectedIndex;

        ProjectTreeView.IsVisible = tab == 0;
        ProjectEmpty.IsVisible = tab == 0 && ProjectTreeView.ItemsSource is null;
        ConsolePane.IsVisible = tab == 1;
        ProblemsEmpty.IsVisible = tab == 2;

        foreach (var (index, panel) in _bottomPanels)
            panel.IsVisible = index == tab;
    }

    private void OnConsoleClear(object? sender, RoutedEventArgs e) => _log.Clear();

    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectPath is not { } path || _workspace.Snapshot is not { } snapshot)
            return;

        // Вывод сборки и запуска идёт в журнал, и смотреть на него человек
        // должен без лишнего щелчка.
        BottomTabs.SelectedIndex = 1;
        RunButton.IsEnabled = false;

        try
        {
            await _runner.RunAsync(snapshot, path);
        }
        finally
        {
            UpdateRunButtons();
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e) => _runner.Stop();

    private void UpdateRunButtons()
    {
        RunButton.IsEnabled = ProjectPath is not null && !_runner.IsRunning;
        StopButton.IsEnabled = _runner.IsRunning;
    }

    private async Task CloseDocumentsAsync()
    {
        _active?.OnDeactivated();
        _active = null;

        foreach (var document in _documents)
            await document.View.DisposeAsync();

        _documents.Clear();
        await _workspace.DisposeAsync();
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

    /// <summary>Открытая вкладка: путь файла и представление от редактора.</summary>
    /// <param name="Path">Путь к файлу.</param>
    /// <param name="View">Представление документа.</param>
    private sealed record OpenDocument(string Path, DocumentView View);

    /// <summary>Строка состояния как служба для модулей и плагинов.</summary>
    /// <param name="target">Куда писать.</param>
    private sealed class StatusSink(TextBlock target) : IStudioStatus
    {
        /// <inheritdoc/>
        public void Show(string message) => target.Text = message;
    }
}
