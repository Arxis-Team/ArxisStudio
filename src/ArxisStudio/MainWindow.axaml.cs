using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
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
/// Главное окно студии — оболочка: зоны, вкладки документов, заголовок и запуск.
/// </summary>
/// <remarks>
/// Панелей у оболочки нет ни одной: и дерево проекта, и консоль, и дизайнер форм
/// приходят встроенными модулями через тот же SDK-контракт, что и внешние
/// плагины. Оболочка знает только, что бывают зоны, вкладки и редакторы
/// документов, — а какие именно, ей сообщают манифесты.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Сборки встроенных модулей — состав студии.
    /// </summary>
    /// <remarks>
    /// Порядок здесь виден человеку: модули встают в зоны по очереди, и вкладки
    /// внизу идут в том же порядке, что строки этого списка.
    /// </remarks>
    /// <summary>
    /// Модули, приезжающие вместе со студией.
    /// </summary>
    /// <remarks>
    /// Список здесь, а не в настройках: встроенный модуль — часть поставки, и
    /// выключать его отдельно нечем. Поднимаются они первыми, до внешних
    /// плагинов: панели студии должны стоять на своих местах раньше, чем к ним
    /// встанут чужие.
    /// </remarks>
    private static readonly Assembly[] BuiltInModules =
    [
        typeof(Modules.Sample.SampleModule).Assembly,
    ];

    private readonly ISettingsStore? _settings;
    private readonly List<OpenDocument> _documents = [];
    private readonly StudioLog _log = new();
    private readonly StudioProblems _problems = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioCommands _commands;
    private readonly PluginContributionRegistry _contributions = new();
    private PluginHost? _plugins;
    private IReadOnlyList<InstalledPlugin> _installed = [];
    private IReadOnlyList<StudioMenuItem> _menu = [];
    private DocumentView? _active;

    // Панели модулей и плагинов, разложенные по нижним вкладкам: вкладка знает
    // свой номер, а содержимое лежит в общем месте и показывается по очереди.
    private readonly Dictionary<int, Control> _bottomPanels = [];

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();
        // Выбранная вкладка ставится здесь, а не в разметке: заданная там, она
        // поднимает событие ещё во время разбора, когда полей окна нет и в
        // помине.
        ThemeSwitch.SelectedIndex = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? 1 : 0;

        _commands = new StudioCommands(_guard);

        // Плагины поднимаются при открытии окна, а не при открытии проекта:
        // проекта у окна может не быть вовсе, а панели плагинов ему нужны
        // в любом случае.
        Opened += (_, _) => LoadModulesAndPlugins();

        _guard.Failed += (_, failure) => _log.Write(
            StudioLogLevel.Error, "Plugins",
            $"{Named(failure.PluginId)}: {failure.What} — {failure.Message}");

        _guard.Disabled += (_, failure) => Disable(failure);

        // Системная рамка окна красится отдельно от содержимого: сама она
        // цвета темы не знает.
        Opened += (_, _) => StudioWindowChrome.Apply(
            this, _settings?.Current.Theme ?? StudioTheme.Dark);

        // Исключение, пришедшее мимо шва, — из обработчика события плагина, из
        // его же задачи, — иначе доходит до платформы и роняет студию. Виновник
        // узнаётся по стеку: назвать себя тут некому.
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;

        Closed += async (_, _) =>
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;

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

        Opened += (_, _) => StatusText.Text = projectPath;
    }

    /// <summary>Путь к открытому решению или проекту; null, если проект не открыт.</summary>
    public string? ProjectPath { get; }

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
            [typeof(IStudioLogFeed)] = _log,
            [typeof(IStudioProblems)] = _problems,
            [typeof(IStudioDocuments)] = new DocumentSink(this),
            [typeof(IStudioStatus)] = new StatusSink(StatusText),
            [typeof(PluginContributionRegistry)] = _contributions,
            [typeof(PluginGuard)] = _guard,
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

        // Вкладок до этого момента не было ни одной. Первая выбирается сама,
        // едва появившись, — то есть раньше, чем к ней приложили содержимое, и
        // события о выборе уже не будет: показывать панель приходится здесь.
        if (BottomTabs.Items.Count > 0)
            BottomTabs.SelectedIndex = 0;

        ShowBottomPanel();
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

    /// <summary>
    /// Ловит исключение потока интерфейса.
    /// </summary>
    /// <remarks>
    /// Продолжать работу студия соглашается только за плагин: его сбой посчитан
    /// швом, и после третьего плагин отключат. Свой же дефект не глушится —
    /// исключение идёт дальше, как шло: спрятанный, он остался бы навсегда, а
    /// студия продолжила бы работать в состоянии, о котором ничего не знает.
    /// </remarks>
    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Blame(e.Exception, "необработанное исключение"))
            e.Handled = true;
    }

    /// <summary>
    /// Ловит исключение задачи, которое никто не забрал.
    /// </summary>
    /// <remarks>
    /// Забытая задача плагина — обычное дело: он запустил её и не стал ждать.
    /// Считается это так же, как остальное, и отмечается прочитанным: своё
    /// исключение студия оставляет платформе.
    /// </remarks>
    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (Blame(e.Exception, "исключение забытой задачи"))
            e.SetObserved();
    }

    /// <summary>
    /// Приписывает исключение плагину, если его код есть в стеке.
    /// </summary>
    /// <returns><c>true</c>, если виновник найден и записан.</returns>
    private bool Blame(Exception? error, string what)
    {
        if (_plugins?.Blame(error) is not { } plugin || error is null)
            return false;

        _guard.Report(plugin.Installed.Id, what, error);

        return true;
    }

    /// <summary>Как плагин называется в сообщениях.</summary>
    private string Named(string pluginId) =>
        _installed.FirstOrDefault(plugin => plugin.Id == pluginId)?.DisplayName ?? pluginId;

    /// <summary>
    /// Отключает плагин, падающий раз за разом.
    /// </summary>
    /// <remarks>
    /// Три падения подряд — это не случайность, а сломанный плагин, и звать его
    /// дальше значит показывать человеку одну и ту же ошибку до конца сеанса.
    /// Вклады снимаются, сборки выгружаются, панели остаются на экране
    /// заглушками: убирать зону, в которую человек уже привык смотреть, хуже,
    /// чем сказать в ней, что случилось.
    /// </remarks>
    private void Disable(PluginFailure failure)
    {
        _log.Write(StudioLogLevel.Error, "Plugins",
            $"{Named(failure.PluginId)}: отключён после {failure.Count} сбоев подряд");

        if (_plugins?.Loaded.FirstOrDefault(plugin => plugin.Installed.Id == failure.PluginId) is not { } loaded)
            return;

        _contributions.Remove(failure.PluginId);
        loaded.Unload();
    }

    /// <summary>
    /// Открывает файл во вкладке, спросив редактор у реестра вкладов.
    /// </summary>
    /// <remarks>
    /// Оболочка не знает ни одного расширения: какой модуль возьмётся за файл,
    /// решает объявленный им тип файла. Панель проекта просит «открой этот
    /// путь» — и на этом её знание о содержимом кончается.
    /// </remarks>
    /// <param name="filePath">Путь к файлу.</param>
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
            StatusText.Text = Localizer.Instance["editor.noeditor"];
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

            if (Build(loaded, declared, type, studio) is not { } content)
                continue;

            // Панель живёт не прямо в дереве окна, а в своей поверхности: сбой
            // на замере или раскладке иначе унёс бы весь проход, а с ним и окно
            // студии со всеми открытыми документами.
            PluginSurface? surface = null;

            surface = new PluginSurface(
                content,
                error => _guard.Report(loaded.Installed.Id, $"раскладка панели {declared.Id}", error),
                () => Reload(loaded, declared, type, studio, surface!));

            Mount(declared, surface);
        }
    }

    /// <summary>
    /// Строит панель плагина: создать, подключить, спросить содержимое.
    /// </summary>
    /// <remarks>
    /// Три чужих вызова подряд, и упасть плагин может на любом. Идут они одним
    /// куском: панель, построенная наполовину, студии не нужна.
    /// </remarks>
    private Control? Build(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio) =>
        _guard.Get(loaded.Installed.Id, $"панель {declared.Id}", () =>
        {
            if (Activator.CreateInstance(type) is not Sdk.ToolWindow panel)
                return null;

            panel.Attach(studio);

            return panel.Content;
        });

    /// <summary>
    /// Строит упавшую панель заново по кнопке в заглушке.
    /// </summary>
    /// <remarks>
    /// Счёт падений при этом обнуляется: человек попросил новую попытку, и
    /// отказать ему на том основании, что прежняя копия падала, значит сделать
    /// кнопку бессмысленной.
    /// </remarks>
    private void Reload(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio,
        PluginSurface surface)
    {
        _guard.Forget(loaded.Installed.Id);

        if (Build(loaded, declared, type, studio) is { } content)
            surface.Reset(content);
    }

    /// <summary>Ставит содержимое панели в зону студии.</summary>
    /// <param name="declared">Объявление панели из манифеста.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    private void Mount(Sdk.Plugins.PluginToolWindow declared, Control content)
    {
        var (zone, title) = (declared.Zone, declared.Title);

        switch (zone.ToLowerInvariant())
        {
            // Внизу заголовок панели несёт вкладка, и своего окна панели не
            // нужно. Заворачивать её в него «на всякий случай» нельзя: окно
            // забрало бы себе логического родителя, а оно само никуда не
            // встало бы — и стили до панели просто не дошли бы.
            case "bottom":
                var tab = new AxTabItem { Classes = { "compact" }, IsClosable = false };

                SetTabTitle(tab, title);
                BottomTabs.Items.Add(tab);
                BottomPluginHost.Children.Add(content);

                content.IsVisible = false;
                _bottomPanels[BottomTabs.Items.Count - 1] = content;

                break;

            case "left":
                Append(LeftZone, Window(title, content));
                break;

            default:
                Append(RightZone, Window(title, content));
                break;
        }

        _log.Write(StudioLogLevel.Debug, "Plugins", $"Панель «{Resolve(title)}» встала в зону {zone}");
    }

    /// <summary>Заворачивает панель в окно инструментов с заголовком.</summary>
    /// <param name="title">Заголовок из манифеста.</param>
    /// <param name="content">Содержимое панели.</param>
    private static AxToolWindow Window(string title, Control content)
    {
        var window = new AxToolWindow { Content = content };

        SetTitle(window, title);
        return window;
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

    private void OnBottomTabChanged(object? sender, SelectionChangedEventArgs e) => ShowBottomPanel();

    /// <summary>Показывает панель выбранной нижней вкладки, пряча остальные.</summary>
    private void ShowBottomPanel()
    {
        var tab = BottomTabs.SelectedIndex;

        foreach (var (index, panel) in _bottomPanels)
            panel.IsVisible = index == tab;
    }

    private async Task CloseDocumentsAsync()
    {
        _active?.OnDeactivated();
        _active = null;

        foreach (var document in _documents)
            await document.View.DisposeAsync();

        _documents.Clear();
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

    /// <summary>Открытие документов как служба для модулей и плагинов.</summary>
    /// <param name="owner">Окно, которое ставит вкладки.</param>
    private sealed class DocumentSink(MainWindow owner) : IStudioDocuments
    {
        /// <inheritdoc/>
        public Task OpenAsync(string filePath) => owner.OpenDocumentAsync(filePath);
    }
}
