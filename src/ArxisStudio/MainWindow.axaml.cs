using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ArxisStudio;

/// <summary>
/// Главное окно студии — оболочка: зоны, вкладки документов, заголовок и запуск.
/// </summary>
/// <remarks>
/// Панелей у оболочки нет ни одной: и дерево проекта, и консоль, и дизайнер форм
/// приходят встроенными модулями через тот же SDK-контракт, что и внешние
/// плагины. Оболочка знает только, что бывают зоны, вкладки и редакторы
/// документов, — а какие именно, ей сообщают манифесты.
/// <para>
/// Работы у окна с тех пор осталось три вида: собрать службы, показать своё
/// меню и отдать платформе то, что она спрашивает, — исключение мимо шва и
/// закрытие. Правила расширений, документов и полосы задач живут в службах и
/// моделях рядом: там их видно и там их можно проверить без окна.
/// </para>
/// </remarks>
public partial class MainWindow : AxWindow
{
    // Журнал отражается в стандартный вывод: панели, которая показывала бы его,
    // в студии нет, и без этого о сбое плагина не узнает никто. Запущенной без
    // терминала студии писать некуда — и это ровно то, что нужно.
    private readonly StudioLog _log = new(Console.Out);
    private readonly StudioProblems _problems = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioTaskRegistry _tasks = new();
    private readonly PluginContributionRegistry _contributions = new();

    // Что окно рассказывает о себе — строка состояния и полоса задачи.
    private readonly MainWindowViewModel _model;
    private readonly StudioCommands _commands;

    // Раскладка: дерево доков, живые панели в нём и уборка по хозяину.
    private readonly StudioDock _dock;

    // Открытые файлы: кто открыт, кто показан и кого закрыть.
    private readonly StudioDocuments _documents;

    // Строка состояния одна на студию: и служба документов, и расширения
    // говорят в неё же.
    private readonly IStudioStatus _status;

    // Полоса: элементы модулей и плагинов по манифестам и по хозяину. Кнопки и
    // меню спящих плагинов стоят с самого старта — сборку для них не загружают.
    private readonly StudioToolBar _toolbar;

    // Жизнь расширений: подъём, пробуждение, перезагрузка, отключение за сбои.
    private readonly StudioPlugins _plugins;

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _model = new MainWindowViewModel(_tasks);
        DataContext = _model;

        _dock = new StudioDock(Dock, new DockLayoutStore());
        _dock.Complained += (_, message) => _log.Write(StudioLogLevel.Warning, "Layout", message);

        _status = new StatusSink(_model);
        _documents = new StudioDocuments(_dock, _contributions.EditorFor, _status);

        // Раскладка поднимается до панелей: иначе они успели бы разойтись по
        // стандартным местам, а прочитанное дерево тут же смело бы их оттуда.
        _dock.Restore();

        _commands = new StudioCommands(_guard);

        // Щелчок по кнопке идёт через реестр команд, а не напрямую: только он
        // умеет разбудить спящего хозяина и приписать падение виновнику.
        _toolbar = new StudioToolBar(LeftStrip, CenterStrip, RightStrip)
        {
            Invoke = _commands.Invoke,
            Extra = StudioBranches,
        };

        _toolbar.Complained += (_, message) => _log.Write(StudioLogLevel.Warning, "ToolBar", message);

        _plugins = new StudioPlugins(_log, _guard, _tasks, _contributions)
        {
            Commands = _commands,
            Dock = _dock,
            ToolBar = _toolbar,
            Documents = _documents,

            // Чем студия делится с расширениями — решение оболочки, а не
            // порядка подъёма: список стоит здесь и виден целиком.
            Services = new Dictionary<Type, object>
            {
                [typeof(IStudioLogFeed)] = _log,
                [typeof(IStudioProblems)] = _problems,
                [typeof(IStudioDocuments)] = new DocumentSink(_documents),
                [typeof(IStudioStatus)] = _status,
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },
        };

        // Дерево меню собирается по манифестам, а спрашивают его при каждом
        // открытии: список вкладывающихся меняется от подъёма к подъёму. Ставится
        // после службы — до неё спрашивать было бы не у кого.
        _toolbar.Menu = () => StudioMenu.Build(_plugins.Contributing);

        // Меню — элемент самой студии, а не каждого плагина: дерево команд
        // общее на всех, ветки в нём сходятся по названию, и плагин, объявивший
        // только menus, должен быть доступен, не зная о полосе. В полосе оно
        // первое: своё выше принесённого — этим и заведён нулевой ранг у
        // элемента без хозяина.
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

        // Расширения поднимаются при открытии окна, а не при открытии проекта:
        // проекта у окна может не быть вовсе, а панели плагинов ему нужны
        // в любом случае.
        Opened += (_, _) => _plugins.Start();

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

            _plugins.Stop();
            await _documents.CloseAllAsync();
        };
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

        if (_plugins.Reloadable is { Count: > 0 } plugins)
        {
            var branch = new AxMenuItem { Header = Localizer.Instance["menu.plugins"] };

            foreach (var plugin in plugins)
            {
                var item = new AxMenuItem
                {
                    Header = $"{Localizer.Instance["menu.reload"]} · {plugin.DisplayName}",
                };

                var id = plugin.Id;

                // Жалоба на невыгрузившуюся копию приходит ответом и попадает в
                // строку состояния: журнал о ней уже написал, но человек, нажавший
                // «перезагрузить», ждёт ответа там, где нажимал.
                item.Click += async (_, _) =>
                {
                    if (await _plugins.ReloadAsync(id) is { } complaint)
                        _model.Say(complaint);
                };

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
        if (_plugins.Blame(e.Exception, "необработанное исключение"))
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
        if (_plugins.Blame(e.Exception, "исключение забытой задачи"))
            e.SetObserved();
    }

    /// <summary>Отменяет задачу, которую человек видит.</summary>
    private void OnCancelTaskClick(object? sender, RoutedEventArgs e) => _model.CancelTask();

    /// <summary>Строка состояния как служба для модулей и плагинов.</summary>
    /// <param name="model">Модель окна, которая её показывает.</param>
    private sealed class StatusSink(MainWindowViewModel model) : IStudioStatus
    {
        /// <inheritdoc/>
        public void Show(string message) => model.Say(message);
    }

    /// <summary>Открытие документов как служба для модулей и плагинов.</summary>
    /// <param name="documents">Служба, которая ставит вкладки.</param>
    private sealed class DocumentSink(StudioDocuments documents) : IStudioDocuments
    {
        /// <inheritdoc/>
        public Task OpenAsync(string filePath) => documents.OpenAsync(filePath);
    }
}
