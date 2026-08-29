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
    // Журнал отражается в стандартный вывод: панели, которая показывала бы его,
    // в студии нет, и без этого о сбое плагина не узнает никто. Запущенной без
    // терминала студии писать некуда — и это ровно то, что нужно.
    private readonly StudioLog _log = new(Console.Out);
    private readonly StudioProblems _problems = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioTaskRegistry _tasks = new();
    private readonly StudioCommands _commands;
    private readonly PluginContributionRegistry _contributions = new();
    private PluginHost? _plugins;
    private IReadOnlyList<InstalledPlugin> _installed = [];

    // Модули приезжают со студией, и в каталоге плагинов их нет. Список
    // держится отдельно: меню и сообщения знают о них ровно то же, что о
    // плагинах, — модуль отличается способом доставки, а не правами.
    private IReadOnlyList<InstalledPlugin> _modules = [];
    private IReadOnlyList<StudioMenuItem> _menu = [];
    private DocumentView? _active;

    // Панели модулей и плагинов: без этого списка снять их со стен нечем, а
    // перезагрузка плагина только и делает, что снимает старые и ставит новые.
    private readonly List<MountedPanel> _panels = [];

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

        // Задачи идут не в потоке интерфейса, а показывать их надо в нём.
        _tasks.Changed += (_, _) => Dispatcher.UIThread.Post(ShowTasks);

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
        var host = new PluginHost(new StudioContextFactory(
            _log,
            _commands,
            ProjectPath,
            services,
            settings: null,
            tasks: _tasks,
            guard: _guard));

        _plugins = host;
        _installed = catalog.Scan();
        _contributions.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);

        var modules = BuiltInModules.Select(host.LoadBuiltIn).ToList();

        _modules = modules.Select(loaded => loaded.Installed).ToList();

        foreach (var loaded in modules.Concat(host.LoadStartup(_installed)))
            Accept(loaded);

        // Заметки графа — не отказы, но молчать о них нельзя: устаревший
        // необязательный сосед считается отсутствующим, и человек должен
        // узнать об этом отсюда, а не гадать, почему связка не работает.
        foreach (var note in host.Resolution?.Notes ?? [])
            _log.Write(StudioLogLevel.Warning, "Plugins", note);

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

    /// <summary>
    /// Показывает в строке состояния, что делается в фоне.
    /// </summary>
    /// <remarks>
    /// Показывается свежая задача: она та, ради которой человек только что
    /// что-то нажал. Об остальных говорит счётчик — строка состояния узкая, а
    /// список задач студии пока не нужен: заводить его стоит, когда задач
    /// станет столько, что счётчик перестанет отвечать на вопрос.
    /// </remarks>
    private void ShowTasks()
    {
        var running = _tasks.Running;

        TaskStrip.IsVisible = running.Count > 0;

        if (running.Count == 0)
            return;

        var task = running[^1];

        TaskTitle.Text = task.Title;
        TaskMessage.Text = task.Message;
        TaskProgress.IsIndeterminate = task.Fraction is null;
        TaskProgress.Value = (task.Fraction ?? 0) * 100;
        TaskCancel.IsEnabled = !task.IsCancelling;

        TaskRest.IsVisible = running.Count > 1;
        TaskRest.Text = $"+{running.Count - 1}";
    }

    /// <summary>Отменяет задачу, которую человек видит.</summary>
    private void OnCancelTaskClick(object? sender, RoutedEventArgs e)
    {
        if (_tasks.Running is { Count: > 0 } running)
            running[^1].Cancel();
    }

    /// <summary>Как плагин называется в сообщениях.</summary>
    private string Named(string pluginId) =>
        _modules.Concat(_installed).FirstOrDefault(plugin => plugin.Id == pluginId)?.DisplayName ?? pluginId;

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

        if (_contributions.EditorFor(filePath) is not { } match)
        {
            StatusText.Text = Localizer.Instance["editor.noeditor"];
            return;
        }

        StatusText.Text = Localizer.Instance["editor.loading"];

        var (view, error) = await match.Editor.OpenAsync(filePath);

        if (view is null)
        {
            StatusText.Text = $"{Localizer.Instance["editor.loadfailed"]}: {error}";
            return;
        }

        _documents.Add(new OpenDocument(filePath, view, match.PluginId));

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
        _menu = StudioMenu.Build([.. _modules, .. _installed]);

        // Кнопка нужна и без единой команды: перезагрузить плагин — тоже
        // действие, и другого места для него в окне нет.
        MenuButton.IsVisible = _menu.Count > 0 || Reloadable().Count > 0;
    }

    /// <summary>
    /// Плагины, которые можно поднять заново.
    /// </summary>
    /// <remarks>
    /// Только внешние: у встроенного модуля нет своего контекста загрузки, и
    /// предлагать перезагрузить то, что перезагрузить нельзя, — обещание,
    /// которое студия не сдержит.
    /// </remarks>
    private IReadOnlyList<InstalledPlugin> Reloadable() =>
        _plugins?.Loaded
            .Where(plugin => plugin is { IsLoaded: true, Context: not null })
            .Select(plugin => plugin.Installed)
            .ToList() ?? [];

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach (var item in _menu)
            flyout.Items.Add(Build(item));

        if (Reloadable() is { Count: > 0 } plugins)
        {
            if (_menu.Count > 0)
                flyout.Items.Add(new Separator());

            var branch = new MenuItem { Header = Localizer.Instance["menu.plugins"] };

            foreach (var plugin in plugins)
            {
                var item = new MenuItem
                {
                    Header = $"{Localizer.Instance["menu.reload"]} · {plugin.DisplayName}",
                };

                item.Click += async (_, _) => await ReloadPluginAsync(plugin.Id);
                branch.Items.Add(item);
            }

            flyout.Items.Add(branch);
        }

        if (flyout.Items.Count == 0)
            return;

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

            Mount(loaded.Installed, declared, surface);
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
    /// <param name="plugin">Чья это панель — по нему её потом и снимут.</param>
    /// <param name="declared">Объявление панели из манифеста.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    private void Mount(InstalledPlugin plugin, Sdk.Plugins.PluginToolWindow declared, Control content)
    {
        var (zone, title, strings) = (declared.Zone, declared.Title, plugin.Strings);
        var pluginId = plugin.Id;

        switch (zone.ToLowerInvariant())
        {
            // Внизу заголовок панели несёт вкладка, и своего окна панели не
            // нужно. Заворачивать её в него «на всякий случай» нельзя: окно
            // забрало бы себе логического родителя, а оно само никуда не
            // встало бы — и стили до панели просто не дошли бы.
            case "bottom":
                var tab = new AxTabItem { Classes = { "compact" }, IsClosable = false };

                SetTabTitle(tab, title, strings);
                BottomTabs.Items.Add(tab);
                BottomPluginHost.Children.Add(content);

                content.IsVisible = false;
                _panels.Add(new MountedPanel(pluginId, "bottom", content, tab));

                break;

            case "left":
                Add(pluginId, "left", LeftZone, title, content, strings);
                break;

            default:
                Add(pluginId, "right", RightZone, title, content, strings);
                break;
        }

        _log.Write(StudioLogLevel.Debug, "Plugins", $"Панель «{strings.Resolve(title)}» встала в зону {zone}");
    }

    /// <summary>Ставит панель в боковую зону и запоминает, чья она.</summary>
    private void Add(string pluginId, string zone, Grid grid, string title, Control content, PluginStrings strings)
    {
        var window = Window(title, content, strings);

        _panels.Add(new MountedPanel(pluginId, zone, window, null));
        Append(grid, window);
    }

    /// <summary>
    /// Снимает со стен всё, что поставил плагин.
    /// </summary>
    /// <remarks>
    /// Боковые зоны перекладываются заново, а не правятся по месту: между
    /// панелями стоят разделители, и вынуть одну панель, не тронув соседний
    /// разделитель, значит оставить черту, которая ничего не делит.
    /// </remarks>
    private void Unmount(string pluginId)
    {
        foreach (var panel in _panels.Where(panel => panel.PluginId == pluginId).ToList())
        {
            if (panel.Tab is { } tab)
            {
                BottomTabs.Items.Remove(tab);
                BottomPluginHost.Children.Remove(panel.Content);
            }

            _panels.Remove(panel);
        }

        Relayout("left", LeftZone);
        Relayout("right", RightZone);

        if (BottomTabs.Items.Count > 0 && BottomTabs.SelectedIndex < 0)
            BottomTabs.SelectedIndex = 0;

        ShowBottomPanel();
    }

    /// <summary>Перекладывает зону из тех панелей, что в ней остались.</summary>
    private void Relayout(string zone, Grid grid)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();

        foreach (var panel in _panels.Where(panel => panel.Zone == zone))
            Append(grid, panel.Content);
    }

    /// <summary>
    /// Поднимает плагин заново, не перезапуская студию.
    /// </summary>
    /// <remarks>
    /// Внутренний цикл автора плагина: собрал, выложил в папку плагинов,
    /// перезагрузил. Порядок здесь важнее самих действий — сперва студия
    /// отпускает всё, что держит: панели со стен, вклады из реестра, команды из
    /// меню. Обработчик команды, оставленный в реестре, ссылается на типы
    /// плагина, а через них — на его контекст загрузки, и тот не выгрузится:
    /// перезагрузка копила бы в памяти по контексту за раз.
    /// </remarks>
    private async Task ReloadPluginAsync(string pluginId)
    {
        if (_plugins is not { } host)
            return;

        // Манифест мог измениться вместе со сборкой — берём запись с диска.
        _installed = new PluginCatalog().Scan();

        if (_installed.FirstOrDefault(plugin => plugin.Id == pluginId) is not { } installed)
        {
            _log.Write(StudioLogLevel.Warning, "Plugins", $"Плагина {pluginId} больше нет в папке плагинов");
            return;
        }

        // Задачи плагина держат его типы: не остановив их, мы выгрузим плагин
        // только на словах — и сами же скажем человеку, что копия осталась.
        if (!await _tasks.StopAsync(pluginId, TimeSpan.FromSeconds(5)))
            _log.Write(StudioLogLevel.Warning, "Plugins", $"{Named(pluginId)}: фоновая задача не остановилась за пять секунд");

        await CloseDocumentsOfAsync(pluginId);

        Unmount(pluginId);
        _contributions.Remove(pluginId);
        _commands.Remove(installed.Manifest?.Contributions.Commands.Select(command => command.Id) ?? []);
        _guard.Forget(pluginId);

        // Снятые контролы отпускает не список, а дерево: пока проход раскладки
        // и отрисовки не прошёл, они ещё чьи-то. Ждём его — иначе проверка
        // выгрузки увидит помеху, которой через миг не будет.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var reload = host.Reload(installed);

        if (reload.Plugin is not { } loaded)
        {
            _log.Write(StudioLogLevel.Warning, "Plugins", reload.Error!);
            StatusText.Text = reload.Error!;
            return;
        }

        Accept(loaded);
        ShowMenu();

        // Выгрузка кооперативная, и не удаться она может по вине плагина:
        // подписка на событие студии, оставленный таймер, работающий поток.
        // Промолчать об этом нельзя — прежняя копия осталась в памяти и
        // продолжает получать то, на что подписалась.
        if (reload.Released)
            return;

        var warning = $"{installed.DisplayName}: прежняя копия осталась в памяти — надёжнее перезапустить студию";

        _log.Write(StudioLogLevel.Warning, "Plugins", warning);
        StatusText.Text = warning;
    }

    /// <summary>
    /// Закрывает документы, открытые редактором этого плагина.
    /// </summary>
    /// <remarks>
    /// Представление документа построил плагин, и живёт оно в его контексте
    /// загрузки. Оставить вкладку открытой значит и держать контекст, и
    /// показывать человеку окно, за которым уже ничего нет.
    /// </remarks>
    private async Task CloseDocumentsOfAsync(string pluginId)
    {
        foreach (var document in _documents.Where(document => document.PluginId == pluginId).ToList())
        {
            var index = _documents.IndexOf(document);

            if (ReferenceEquals(_active, document.View))
            {
                _active.OnDeactivated();
                _active = null;
                DocumentHost.Content = null;
            }

            _documents.RemoveAt(index);
            DocumentTabs.Items.RemoveAt(index);

            await document.View.DisposeAsync();
        }

        DocumentTabs.IsVisible = _documents.Count > 0;
        ShowActiveDocument();
    }

    /// <summary>Заворачивает панель в окно инструментов с заголовком.</summary>
    /// <param name="title">Заголовок из манифеста.</param>
    /// <param name="content">Содержимое панели.</param>
    /// <param name="strings">Словари плагина, которому принадлежит панель.</param>
    private static AxToolWindow Window(string title, Control content, PluginStrings strings)
    {
        var window = new AxToolWindow { Content = content };

        SetTitle(window, title, strings);
        return window;
    }

    /// <summary>
    /// Ставит заголовок панели, переводя ключ вида <c>%panel.hierarchy%</c>.
    /// </summary>
    /// <remarks>
    /// Заголовок из манифеста — единственный текст панели, который показывает не
    /// её автор, а студия, поэтому и переводить его при смене языка — забота
    /// студии. Текст берётся из словарей самого плагина: ключ вроде
    /// <c>%panel.main%</c> у каждого свой.
    /// </remarks>
    private static void SetTitle(AxToolWindow window, string title, PluginStrings strings)
    {
        if (PluginStrings.IsKey(title, out var key))
            window.Bind(AxToolWindow.TitleProperty, strings.Text(key));
        else
            window.Title = title;
    }

    private static void SetTabTitle(AxTabItem tab, string title, PluginStrings strings)
    {
        if (PluginStrings.IsKey(title, out var key))
            tab.Bind(ContentControl.ContentProperty, strings.Text(key));
        else
            tab.Content = title;
    }

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
        var selected = BottomTabs.SelectedIndex >= 0 && BottomTabs.SelectedIndex < BottomTabs.Items.Count
            ? BottomTabs.Items[BottomTabs.SelectedIndex] as AxTabItem
            : null;

        foreach (var panel in _panels.Where(panel => panel.Tab is not null))
            panel.Content.IsVisible = ReferenceEquals(panel.Tab, selected);
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
    /// <summary>Панель, стоящая в студии.</summary>
    /// <param name="PluginId">Чья она.</param>
    /// <param name="Zone">В какой зоне стоит.</param>
    /// <param name="Content">Что стоит: окно инструментов или само содержимое внизу.</param>
    /// <param name="Tab">Вкладка нижней зоны; null у боковых панелей.</param>
    private sealed record MountedPanel(string PluginId, string Zone, Control Content, AxTabItem? Tab);

    /// <summary>Открытый документ.</summary>
    /// <param name="Path">Путь к файлу.</param>
    /// <param name="View">Представление, построенное редактором.</param>
    /// <param name="PluginId">
    /// Чей редактор его открыл: при перезагрузке плагина документ придётся
    /// закрыть — иначе останется вкладка, за которой стоит объект из
    /// выгруженного контекста.
    /// </param>
    private sealed record OpenDocument(string Path, DocumentView View, string PluginId);

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
