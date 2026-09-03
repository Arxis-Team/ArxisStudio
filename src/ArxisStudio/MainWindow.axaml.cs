using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using ArxisStudio.Docking;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
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
    private DocumentView? _active;

    // Раскладка: дерево доков, живые панели в нём и уборка по хозяину.
    // Перезагрузка плагина только и делает, что снимает старые панели и ставит
    // новые, — этим списком она и живёт.
    private readonly StudioDock _dock;

    // Полоса: элементы модулей и плагинов по манифестам и по хозяину. Кнопки и
    // меню спящих плагинов стоят с самого старта — сборку для них не загружают.
    private readonly StudioToolBar _toolbar;

    // Уборка перед выгрузкой: задачи, документы, экран — в одном порядке на все
    // дороги. Реестры владельца хост убирает сам, по своему Unloading.
    private readonly PluginRelease _release;

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

        _commands = new StudioCommands(_guard);

        // Щелчок по кнопке идёт через реестр команд, а не напрямую: только он
        // умеет разбудить спящего хозяина и приписать падение виновнику.
        _toolbar = new StudioToolBar(LeftStrip, CenterStrip, RightStrip)
        {
            Invoke = _commands.Invoke,
            Menu = () => StudioMenu.Build(Contributing()),
            Extra = StudioBranches,
        };

        _toolbar.Complained += (_, message) => _log.Write(StudioLogLevel.Warning, "ToolBar", message);

        _release = new PluginRelease(_tasks)
        {
            Documents = CloseDocumentsOfAsync,
            Views = Unmount,
        };

        _release.Lingered += (_, id) => _log.Write(StudioLogLevel.Warning, "Plugins",
            $"{Named(id)}: фоновая задача не остановилась за пять секунд");

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

        // Раскладка пишется с задержкой, и до закрытия та просто не доживёт.
        // Прощаемся до закрытия, а не после: оторванные окна закрываются вместе
        // с главным и возвращают панели домой — правка, которая, дойдя до
        // файла, стёрла бы из него сами окна.
        Closing += (_, _) => _dock.Farewell();

        Closed += async (_, _) =>
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;

            _plugins?.Dispose();
            await CloseDocumentsAsync();
        };
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
            // Проекта у студии пока нет: работа с ними приедет модулем.
            // Место в контракте плагинов остаётся — сам контракт не менялся.
            projectPath: null,
            services,
            settings: null,
            tasks: _tasks,
            guard: _guard,
            plugins: roster,
            exports: _exports,
            toolbar: _toolbar,
            dock: _dock));

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

        // Полоса собирается по манифестам до подъёма кого бы то ни было: кнопки
        // и меню сборки не требуют, и спящий плагин получает их здесь и только
        // здесь. Модуль, поднятый следом, может тут же выключить свой элемент
        // из Activate — слово должно найти запись.
        MountDeclared(StudioModules.Describe().Concat(_installed));

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

        // Меню — элемент самой студии, а не каждого плагина: дерево команд
        // общее на всех, ветки в нём сходятся по названию, и плагин, объявивший
        // только menus, должен быть доступен, не зная о полосе. Стоит первым:
        // своё выше принесённого.
        //
        // Стоит оно всегда, даже когда вкладываться некому: раскладка живёт
        // здесь же, и без меню перетаскивание было бы дверью в одну сторону —
        // первый же промах мышью человек разбирал бы вручную.
        _toolbar.Add(null, new Sdk.Plugins.PluginToolBarItem
        {
            Id = "menu",
            Kind = "menu",
            Slot = "left",
            Icon = "arxis:MoreHorizontal",
            Title = "%toolbar.menu%",
        });
    }

    /// <summary>
    /// Собственные ветки студии в её меню: перезагрузка плагина и раскладка.
    /// </summary>
    /// <remarks>
    /// Манифестами они не объявлены и объявлены быть не могут: пункты зависят
    /// от того, что сейчас поднято и какая раскладка показана, — список
    /// собирается на каждом открытии заново.
    /// </remarks>
    private IReadOnlyList<MenuItem> StudioBranches()
    {
        var branches = new List<MenuItem>();

        if (Reloadable() is { Count: > 0 } plugins)
        {
            var branch = new AxMenuItem { Header = Localizer.Instance["menu.plugins"] };

            foreach (var plugin in plugins)
            {
                var item = new AxMenuItem
                {
                    Header = $"{Localizer.Instance["menu.reload"]} · {plugin.DisplayName}",
                };

                var id = plugin.Id;

                item.Click += async (_, _) => await ReloadPluginAsync(id);
                branch.Items.Add(item);
            }

            branches.Add(branch);
        }

        branches.Add(Layouts());

        return branches;

        MenuItem Layouts()
        {
            var branch = new AxMenuItem { Header = Localizer.Instance["menu.layout"] };

            foreach (var name in _dock.Layouts)
            {
                var set = new AxMenuItem { Header = name };

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

            var save = new AxMenuItem { Header = Localizer.Instance["menu.layout.save"] };
            var reset = new AxMenuItem { Header = Localizer.Instance["menu.layout.reset"] };

            save.Click += async (_, _) => await SaveLayoutAsync();
            reset.Click += (_, _) => _dock.Reset();

            branch.Items.Add(save);
            branch.Items.Add(reset);

            // Стандартный набор не удаляется: он — то, куда возвращаются.
            if (!string.Equals(_dock.Layout, DockLayout.DefaultName, StringComparison.Ordinal))
            {
                var forget = new AxMenuItem
                {
                    Header = Localizer.Instance["menu.layout.delete"],
                    Classes = { "danger" },
                };

                forget.Click += (_, _) => _dock.Forget();
                branch.Items.Add(forget);
            }

            return branch;
        }
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

    /// <summary>
    /// Кто сейчас вправе вкладываться в меню: модули и установленные, кроме
    /// отключённых за сбои.
    /// </summary>
    private IEnumerable<InstalledPlugin> Contributing() =>
        _modules.Concat(_installed).Where(plugin => !_guard.IsFaulty(plugin.Id));

    /// <summary>
    /// Ставит в полосу всё, что объявлено манифестами, — не поднимая никого.
    /// </summary>
    /// <remarks>
    /// Кнопка и меню сборки не требуют: студия рисует их сама, а щелчок будит
    /// хозяина через реестр команд. Свой контрол здесь только занимает место —
    /// придёт он, когда плагин поднимут.
    /// </remarks>
    private void MountDeclared(IEnumerable<InstalledPlugin> plugins)
    {
        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
        {
            foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
                _toolbar.Add(plugin, declared);
        }
    }

    /// <summary>Принимает поднятый модуль или плагин: вклады и панели.</summary>
    private void Accept(LoadedPlugin loaded)
    {
        if (loaded.Error is { } error)
        {
            _log.Write(StudioLogLevel.Error, "Plugins", $"{loaded.Installed.DisplayName}: {error}");

            // Кнопки несостоявшегося плагина стоять не должны: команда за ними
            // не найдётся никогда.
            _toolbar.RemoveOwnedBy(loaded.Installed.Id);
            return;
        }

        _log.Write(StudioLogLevel.Info, "Plugins", $"{loaded.Installed.DisplayName} поднят");

        _contributions.Add(loaded);
        MountPanels(loaded);
        MountToolBar(loaded);
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

        // Всё откладывается. Сюда попадают в том числе из прохода раскладки —
        // барьер панели сообщает о сбое прямо на замере, — а ни ждать задачи, ни
        // вынимать контролы из дерева окна во время его же прохода нельзя.
        //
        // Ждать не страшно: гвард пометил плагина сбойным раньше, чем позвал
        // сюда, и звать его код он уже отказывается.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await _release.LetGoAsync(failure.PluginId);

                // Снятые контролы отпускает дерево, а не список: ждём его
                // проход, иначе выгрузка упрётся в помеху, которой через миг
                // не будет.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            finally
            {
                // Через хост, а не Unload напрямую: только он снимет запись со
                // счёта и разошлёт уборку реестрам. В finally, потому что выше
                // закрываются документы плагина — его же кодом, и упасть он
                // волен и здесь; бросить плагина неснятым нельзя.
                _plugins?.Drop(failure.PluginId);
            }
        });
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

    /// <summary>Снимает со стен и с полосы всё, что поставил плагин.</summary>
    private void Unmount(string pluginId)
    {
        _dock.RemoveOwnedBy(pluginId);
        _toolbar.RemoveOwnedBy(pluginId);
    }

    /// <summary>
    /// Ставит в полосу свои контролы модуля или плагина.
    /// </summary>
    /// <remarks>
    /// Кнопки и меню стоят с объявления; здесь достраивается то, чего без
    /// сборки не нарисовать. Класс — по атрибуту, как у панели. Объявленное
    /// объявляется заново: реестр ничего не пересобирает, а на дороге
    /// перезагрузки возвращает снятое.
    /// </remarks>
    private void MountToolBar(LoadedPlugin loaded)
    {
        if (loaded.Installed.Manifest is not { } manifest || loaded.Studio is not { } studio)
            return;

        var items = loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(Sdk.ToolBarItem).IsAssignableFrom(type))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ToolBarItemAttribute>()))
            .Where(found => found.Attribute is not null)
            .ToDictionary(found => found.Attribute!.Id, found => found.Type, StringComparer.Ordinal);

        foreach (var declared in manifest.Contributions.ToolBar)
        {
            if (!declared.IsCustom)
            {
                _toolbar.Add(loaded.Installed, declared);
                continue;
            }

            if (!items.TryGetValue(declared.Id, out var type))
            {
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"Элемент полосы {declared.Id} объявлен в манифесте, но в сборке его нет");
                continue;
            }

            if (BuildItem(loaded, declared, type, studio) is not { } content)
                continue;

            var id = loaded.Installed.Id;

            // Заглушки в полосе нет: в сорок пикселей она не поместится, а
            // держала бы замыкание с типами плагина. Упавший элемент снимается
            // — следующим проходом, потому что сюда приходят из прохода
            // раскладки, и вынимать контрол посреди него нельзя.
            var surface = new PluginSurface(
                content,
                error =>
                {
                    _guard.Report(id, $"раскладка элемента полосы {declared.Id}", error);
                    Dispatcher.UIThread.Post(() => _toolbar.Remove(id, declared.Id));
                });

            _toolbar.Add(loaded.Installed, declared, surface);
        }
    }

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
            await _release.LetGoAsync(id);

            // Счёт сбоев обнуляется только здесь: обновлённый плагин отвечает за
            // себя, а не за грехи прежней копии. Отключённому упавшему такого
            // прощения не полагается — потому это и не в общей уборке.
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

    /// <summary>Строит свой контрол плагина: создать, подключить, спросить содержимое — одним куском.</summary>
    private Control? BuildItem(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolBarItem declared,
        Type type,
        IStudioContext studio) =>
        _guard.Get(loaded.Installed.Id, $"элемент полосы {declared.Id}", () =>
        {
            if (Activator.CreateInstance(type) is not Sdk.ToolBarItem item)
                return null;

            item.Attach(studio);

            return item.Content;
        });

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
