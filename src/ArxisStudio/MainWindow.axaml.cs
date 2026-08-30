using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
public partial class MainWindow : AxWindow
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
    private readonly StudioExportRegistry _exports = new();
    private readonly PluginContributionRegistry _contributions = new();
    private PluginHost? _plugins;
    private IReadOnlyList<InstalledPlugin> _installed = [];

    // Модули приезжают со студией, и в каталоге плагинов их нет. Список
    // держится отдельно: меню и сообщения знают о них ровно то же, что о
    // плагинах, — модуль отличается способом доставки, а не правами.
    private IReadOnlyList<InstalledPlugin> _modules = [];
    private IReadOnlyList<StudioMenuItem> _menu = [];
    private DocumentView? _active;

    // Раскладка: дерево доков, живые панели в нём и уборка по хозяину.
    // Перезагрузка плагина только и делает, что снимает старые панели и ставит
    // новые, — этим списком она и живёт.
    private readonly StudioDock _dock;

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _dock = new StudioDock(Dock, new DockLayoutStore());
        _dock.Chosen += (_, id) => ShowDocument(id);
        _dock.Complained += (_, message) => _log.Write(StudioLogLevel.Warning, "Layout", message);
        _dock.Closing += async (_, id) => await CloseDocumentAsync(id);

        // Раскладка поднимается до панелей: иначе они успели бы разойтись по
        // стандартным местам, а прочитанное дерево тут же смело бы их оттуда.
        _dock.Restore();

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

        // Исключение, пришедшее мимо шва, — из обработчика события плагина, из
        // его же задачи, — иначе доходит до платформы и роняет студию. Виновник
        // узнаётся по стеку: назвать себя тут некому.
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;

        Closed += async (_, _) =>
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;

            // Раскладка пишется с задержкой, и до закрытия та просто не доживёт.
            _dock.Flush();

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
        var roster = new StudioPluginRoster();

        _exports.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);
        var host = new PluginHost(new StudioContextFactory(
            _log,
            _commands,
            ProjectPath,
            services,
            settings: null,
            tasks: _tasks,
            guard: _guard,
            plugins: roster,
            exports: _exports));

        // Уборка реестров, заведённых на владельца, — по одному сигналу от
        // хоста: он один знает про все дороги выгрузки. Раньше её переписывал
        // каждый, кто выгружает, и списки успели разъехаться — снятие
        // упавшего забывало команды, а закрытие студии не убирало ничего.
        host.Unloading += (_, id) =>
        {
            _commands.RemoveOwnedBy(id);
            _exports.RemoveOwnedBy(id);
            _contributions.Remove(id);
        };

        _plugins = host;
        _installed = catalog.Scan();

        // Ядро подключается до первого подъёма: контексты раздаются при
        // загрузке, и служба соседей обязана отвечать правду с первого.
        roster.Attach(host, () => _installed);

        // Пробуждение по команде живёт в реестре, а не только в меню: команду
        // соседа зовут и из кода плагина, и дорога обязана быть одна. Подъём
        // ставит панели, поэтому вне потока интерфейса он откладывается — тот
        // Invoke честно вернёт false, а хозяин поднимется следом.
        _commands.Awaken = command =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Activate(waiting => PluginActivation.WaitsForCommand(waiting.Manifest, command));
                return;
            }

            _log.Write(StudioLogLevel.Warning, "Plugins",
                $"Команда {command} позвана вне потока интерфейса — хозяин поднимется следом");
            Dispatcher.UIThread.Post(() =>
                Activate(waiting => PluginActivation.WaitsForCommand(waiting.Manifest, command)));
        };
        _contributions.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);

        var modules = StudioModules.Assemblies.Select(host.LoadBuiltIn).ToList();

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
    /// <para>
    /// Панели при этом уходят со стен, хотя раньше обещалось оставить вместо них
    /// заглушки. Обещание было невыполнимым: заглушка панели держит замыкание
    /// перезапуска, а оно — типы плагина и через них его контекст загрузки.
    /// Студия выгружала бы плагин только на словах и сама же потом жаловалась,
    /// что прежняя копия осталась в памяти. О случившемся говорит журнал и
    /// менеджер плагинов, а не пустая рамка на экране.
    /// </para>
    /// </remarks>
    private void Disable(PluginFailure failure)
    {
        _log.Write(StudioLogLevel.Error, "Plugins",
            $"{Named(failure.PluginId)}: отключён после {failure.Count} сбоев подряд");

        // Через хост, а не Unload напрямую: только он снимет запись со счёта
        // и разошлёт уборку реестрам.
        _plugins?.Drop(failure.PluginId);

        // Снятие панелей откладываем. Сюда попадают в том числе из прохода
        // раскладки — барьер панели сообщает о сбое прямо на замере, — а
        // вынимать контролы из дерева окна во время его же прохода нельзя.
        Dispatcher.UIThread.Post(() => Unmount(failure.PluginId));
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
        var id = Document(filePath);

        if (_documents.Any(document => string.Equals(document.Id, id, StringComparison.Ordinal)))
        {
            _dock.Show(id);
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

        _documents.Add(new OpenDocument(id, filePath, view, match.PluginId));
        _dock.Open(match.PluginId, id, view.Title, view.Content);
        ShowDocument(id);
    }

    /// <summary>Имя документа в раскладке.</summary>
    /// <remarks>
    /// Путь и есть имя: два документа одного файла студии не нужны, а сравнение
    /// путей — единственное, чем «этот файл уже открыт» и проверяется.
    /// </remarks>
    private static string Document(string filePath) => $"doc:{filePath}";

    /// <summary>
    /// Показывает выбранный документ, если выбран документ.
    /// </summary>
    /// <remarks>
    /// Выбор приходит на любую вкладку, а не только на документную: щелчок по
    /// панели внизу документ не меняет и не обязан менять. Поэтому чужое имя
    /// здесь просто ни к чему не приводит.
    /// </remarks>
    private void ShowDocument(string? id)
    {
        var document = id is null
            ? null
            : _documents.FirstOrDefault(open => string.Equals(open.Id, id, StringComparison.Ordinal));

        if (document is null || ReferenceEquals(_active, document.View))
            return;

        _active?.OnDeactivated();
        _active = document.View;
        _active.OnActivated();

        StatusText.Text = document.Path;
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

        // Раскладка есть в меню всегда: перетаскивание иначе было бы дверью в
        // одну сторону, и первый же промах мышью человек разбирал бы вручную.
        if (flyout.Items.Count > 0)
            flyout.Items.Add(new Separator());

        flyout.Items.Add(Layouts());

        if (flyout.Items.Count == 0)
            return;

        flyout.ShowAt(MenuButton);

        MenuItem Layouts()
        {
            var branch = new MenuItem { Header = Localizer.Instance["menu.layout"] };

            foreach (var name in _dock.Layouts)
            {
                var set = new MenuItem { Header = name };

                // Показанный набор помечен галочкой в колонке значков, которую
                // тема держит у каждого пункта: переключаться на самого себя
                // человеку незачем, поэтому щелчка у него и нет.
                if (string.Equals(name, _dock.Layout, StringComparison.Ordinal))
                {
                    set.Icon = new AxIcon { Classes = { "small" }, Data = AxIcons.Check };
                }
                else
                {
                    var chosen = name;

                    set.Click += (_, _) => _dock.Switch(chosen);
                }

                branch.Items.Add(set);
            }

            branch.Items.Add(new Separator());

            var save = new MenuItem { Header = Localizer.Instance["menu.layout.save"] };
            var reset = new MenuItem { Header = Localizer.Instance["menu.layout.reset"] };

            save.Click += async (_, _) => await SaveLayoutAsync();
            reset.Click += (_, _) => _dock.Reset();

            branch.Items.Add(save);
            branch.Items.Add(reset);

            // Стандартный набор не удаляется: он — то, куда возвращаются.
            if (!string.Equals(_dock.Layout, DockLayout.DefaultName, StringComparison.Ordinal))
            {
                var forget = new MenuItem
                {
                    Header = Localizer.Instance["menu.layout.delete"],
                    Classes = { "danger" },
                };

                forget.Click += (_, _) => _dock.Forget();
                branch.Items.Add(forget);
            }

            return branch;
        }

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

        // Спящего хозяина разбудит сам реестр: дорога у меню и у чужого кода
        // одна, и второй здесь не нужно.
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

            foreach (var loaded in host.Activate(waiting.Id))
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

    /// <summary>Ставит содержимое панели в раскладку студии.</summary>
    /// <param name="plugin">Чья это панель — по нему её потом и снимут.</param>
    /// <param name="declared">Объявление панели из манифеста.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    /// <remarks>
    /// Имя панели в раскладке — с именем плагина впереди: манифест обещает
    /// уникальность только внутри своего плагина, а дерево доков одно на всю
    /// студию и переживает перезапуск.
    /// </remarks>
    private void Mount(InstalledPlugin plugin, Sdk.Plugins.PluginToolWindow declared, Control content)
    {
        var id = Panel(plugin.Id, declared.Id);

        _dock.Add(plugin.Id, id, declared.Wanted, declared.Title, plugin.Strings, content);

        _log.Write(StudioLogLevel.Debug, "Plugins",
            $"Панель «{plugin.Strings.Resolve(declared.Title)}» встала в раскладку");
    }

    /// <summary>Имя панели в раскладке.</summary>
    private static string Panel(string pluginId, string toolWindowId) => $"{pluginId}:{toolWindowId}";

    /// <summary>Снимает со стен всё, что поставил плагин.</summary>
    private void Unmount(string pluginId) => _dock.RemoveOwnedBy(pluginId);

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

        // Зависимые считаются по манифестам прежних копий: перезагружают
        // потому, что плагин изменился, и свежий манифест мог зависимость
        // убрать — а прежний зависимый всё ещё держит прежние типы. Вместе с
        // необязательными: их гарантия «сосед стоит подо мной» не делится.
        var dependents = PluginGraph.Dependents(
                pluginId,
                host.Loaded
                    .Where(loaded => loaded is { IsLoaded: true, Context: not null })
                    .Select(loaded => loaded.Installed)
                    .ToList(),
                includeOptional: true)
            .Select(dependent => dependent.Id)
            .ToList();

        // Манифесты могли измениться вместе со сборками — записи берутся с
        // диска. Опускаются зависимые первыми, зависимость последней;
        // поднимается всё в обратном порядке.
        _installed = new PluginCatalog().Scan();

        if (_installed.FirstOrDefault(plugin => plugin.Id == pluginId) is not { } installed)
        {
            _log.Write(StudioLogLevel.Warning, "Plugins", $"Плагина {pluginId} больше нет в папке плагинов");
            return;
        }

        var lower = dependents.Append(pluginId).ToList();
        var raise = new List<InstalledPlugin> { installed };

        foreach (var dependentId in dependents)
        {
            if (_installed.FirstOrDefault(plugin => plugin.Id == dependentId) is { } dependent)
                raise.Add(dependent);
            else
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"{Named(dependentId)} зависел от {installed.DisplayName}, но пропал с диска — опущен и не поднят");
        }

        foreach (var id in lower)
        {
            // Задачи плагина держат его типы: не остановив их, мы выгрузим
            // плагин только на словах — и сами же скажем, что копия осталась.
            if (!await _tasks.StopAsync(id, TimeSpan.FromSeconds(5)))
                _log.Write(StudioLogLevel.Warning, "Plugins", $"{Named(id)}: фоновая задача не остановилась за пять секунд");

            await CloseDocumentsOfAsync(id);

            // Реестры владельца уберёт подписка на Unloading; здесь —
            // только то, чего хост знать не может: панели на экране и счёт
            // сбоев.
            Unmount(id);
            _guard.Forget(id);
        }

        // Снятые контролы отпускает не список, а дерево: пока проход раскладки
        // и отрисовки не прошёл, они ещё чьи-то. Ждём его — иначе проверка
        // выгрузки увидит помеху, которой через миг не будет. Проход один на
        // всех: дерево тоже одно.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var cascade = host.Reload(lower, raise);

        foreach (var skipped in cascade.Skipped)
            _log.Write(StudioLogLevel.Warning, "Plugins", skipped.Value);

        foreach (var note in cascade.Notes)
            _log.Write(StudioLogLevel.Warning, "Plugins", note);

        foreach (var loaded in cascade.Raised)
            Accept(loaded);

        ShowMenu();

        // Выгрузка кооперативная, и не удаться она может по вине любого из
        // опущенных: подписка на событие студии, оставленный таймер,
        // работающий поток. Каждый невыгрузившийся называется своим именем —
        // безымянное предупреждение не говорит, кого чинить.
        var stuck = cascade.Released
            .Where(pair => !pair.Value)
            .Select(pair => Named(pair.Key))
            .ToList();

        if (stuck.Count == 0)
            return;

        var warning = $"{string.Join(", ", stuck)}: прежняя копия осталась в памяти — надёжнее перезапустить студию";

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
    /// <summary>
    /// Спрашивает имя и сохраняет под ним нынешнюю раскладку.
    /// </summary>
    /// <remarks>
    /// Имя спрашивают модальным окном, а не полем в меню: меню закрывается от
    /// первого же щелчка мимо, и набор пропал бы вместе с недопечатанным именем.
    /// </remarks>
    private async Task SaveLayoutAsync()
    {
        var box = new AxTextBox { PlaceholderText = Localizer.Instance["layout.name.hint"], Width = 260 };
        var cancel = new AxButton { Content = Localizer.Instance["common.cancel"], MinWidth = 96 };
        var save = new AxButton
        {
            Content = Localizer.Instance["common.save"],
            MinWidth = 96,
            Classes = { "accent" },
        };

        var dialog = new AxDialog
        {
            Title = Localizer.Instance["layout.name.title"],
            Content = box,
            Buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, save },
            },
        };

        // Курсор сразу в поле: другого дела у этого окна нет.
        dialog.Opened += (_, _) => box.Focus();
        cancel.Click += (_, _) => dialog.Close(null);
        save.Click += (_, _) => dialog.Close(box.Text);

        box.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                dialog.Close(box.Text);
        };

        if (await dialog.ShowDialog<string?>(this) is { } name)
            _dock.SaveAs(name);
    }

    private async Task CloseDocumentsOfAsync(string pluginId)
    {
        foreach (var document in _documents.Where(document => document.PluginId == pluginId).ToList())
            await CloseAsync(document);

        // Место закрытого документа занял сосед — его и показываем.
        ShowDocument(_dock.Showing);
    }

    /// <summary>Закрывает документ по просьбе человека — крестиком на вкладке.</summary>
    /// <param name="id">Имя документа в раскладке.</param>
    private async Task CloseDocumentAsync(string id)
    {
        if (_documents.FirstOrDefault(open => string.Equals(open.Id, id, StringComparison.Ordinal))
            is not { } document)
        {
            return;
        }

        await CloseAsync(document);

        ShowDocument(_dock.Showing);
    }

    /// <summary>Убирает документ отовсюду и отпускает его представление.</summary>
    private async Task CloseAsync(OpenDocument document)
    {
        if (ReferenceEquals(_active, document.View))
        {
            _active.OnDeactivated();
            _active = null;
        }

        _documents.Remove(document);
        _dock.Remove(document.Id);

        await document.View.DisposeAsync();
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

    /// <summary>Открытый документ.</summary>
    /// <param name="Id">
    /// Имя документа в раскладке. По имени, а не по номеру: номер разъезжается,
    /// стоит отсеять хоть одну вкладку, — от этого и умирала прежняя связь
    /// списка документов с полосой вкладок.
    /// </param>
    /// <param name="Path">Путь к файлу.</param>
    /// <param name="View">Представление, построенное редактором.</param>
    /// <param name="PluginId">
    /// Чей редактор его открыл: при перезагрузке плагина документ придётся
    /// закрыть — иначе останется вкладка, за которой стоит объект из
    /// выгруженного контекста.
    /// </param>
    private sealed record OpenDocument(string Id, string Path, DocumentView View, string PluginId);

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
