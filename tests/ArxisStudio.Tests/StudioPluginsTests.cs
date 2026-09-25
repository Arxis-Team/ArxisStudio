using System.Reflection;
using System.Text;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Жизнь расширений студии: подъём, пробуждение, перезагрузка, закрытие.
/// </summary>
/// <remarks>
/// Дорог здесь шесть, и все они трогают одни и те же реестры — команды,
/// экспорты, вклады, полосу, раскладку. Пока они лежали в главном окне,
/// проверить их было нечем: чтобы дойти до кода, надо было поднять окно,
/// прочитать настоящую папку плагинов и зацепиться за обработчики платформы.
/// Списки на этих дорогах уже разъезжались однажды.
/// <para>
/// Плагин здесь настоящий: пример студии, поставленный из своего архива во
/// временную папку. Поддельный манифест доказывал бы согласие службы с
/// выдумкой теста, а не с тем, что студия делает с плагином на диске.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioPluginsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-plugins-{Guid.NewGuid():N}");
    private readonly ToolBarStrip _left = new();
    private readonly ToolBarStrip _center = new();
    private readonly ToolBarStrip _right = new();
    private readonly DockView _view = new();
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioTaskRegistry _tasks = new();
    private readonly PluginContributionRegistry _contributions = new();
    private readonly StudioCommands _commands;
    private readonly StudioDock _dock;
    private readonly StudioToolBar _toolbar;
    private readonly StudioDocuments _documents;
    private readonly StudioShortcuts _keys;

    private StudioPlugins? _plugins;

    // Каталог — единственное, что тест подменяет: плагин на диске настоящий, а
    // меняются обстоятельства, в которых студия его застаёт.
    private bool _vanished;
    private bool _broken;

    public StudioPluginsTests()
    {
        _commands = new StudioCommands(_guard);
        _keys = new StudioShortcuts(_commands.Invoke);
        _dock = new StudioDock(_view);
        _toolbar = new StudioToolBar(_left, _center, _right) { Invoke = _commands.Invoke };
        _documents = new StudioDocuments(_dock, _contributions.EditorFor, new Silence());

        new Window
        {
            Width = 1200,
            Height = 800,
            Content = new DockPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Avalonia.Controls.Dock.Top,
                        Orientation = Orientation.Horizontal,
                        Children = { _left, _center, _right },
                    },
                    _view,
                },
            },
        }.Show();

        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        // Хост отпускается первым: пока жив контекст загрузки плагина, его
        // файлы держит процесс, и папку не убрать.
        _plugins?.Stop();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Студия без единого расширения поднимается молча.</summary>
    /// <remarks>
    /// Не мелочь: свежая установка выглядит именно так, и падение здесь было бы
    /// падением при первом запуске.
    /// </remarks>
    [AvaloniaFact]
    public void A_studio_without_extensions_starts_quietly()
    {
        var plugins = Start();

        Assert.Empty(plugins.Installed);
        Assert.Empty(plugins.Modules);
        Assert.Empty(plugins.Reloadable);
        Assert.Empty(_toolbar.Shown("left"));
        Assert.Empty(_toolbar.Shown("right"));
        Assert.DoesNotContain(_log.Records, record => record.Level == StudioLogLevel.Error);
    }

    /// <summary>Встроенный модуль поднимается и ставит свою панель.</summary>
    [AvaloniaFact]
    public void A_built_in_module_is_raised_and_puts_its_panel_up()
    {
        var plugins = Start(modules: typeof(SampleModule).Assembly);

        var module = Assert.Single(plugins.Modules);

        Assert.Equal("arxis.sample", module.Id);
        Assert.True(module.IsBuiltIn);
        Assert.Contains(_dock.Items.Known(), id => id.StartsWith("arxis.sample:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Выключая расширение, студия прощается с его панелями.
    /// </summary>
    /// <remarks>
    /// Панель держит то, до чего точке входа не дотянуться: процессы, потоки,
    /// подписки. Создаёт её студия, и своих экземпляров расширение не видит —
    /// значит и звать <see cref="ToolWindow.Release"/> может только студия.
    /// Прежде такой точки в контракте не было вовсе: студия брала у панели
    /// содержимое, а саму панель отпускала вместе со всем, что та держала.
    /// <para>
    /// Проверяется на терминале: его панель — единственная, которой есть что
    /// отпускать, и она отпускает хаб. Оболочек в этом прогоне нет — панель на
    /// экран не ставили, — но прощание идёт по той же дороге.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Switching_an_extension_off_says_goodbye_to_its_panels()
    {
        TerminalHub.Reset();

        try
        {
            var plugins = Start(modules: typeof(TerminalModule).Assembly);

            Assert.Contains(_dock.Items.Known(), id => id.StartsWith("arxis.terminal:", StringComparison.Ordinal));

            // Панель встала и заняла хаб: до прощания просьба идёт прямо ей.
            var before = new List<TerminalRequest>();

            TerminalHub.Attach(before.Add);
            TerminalHub.Open(new TerminalRequest(TerminalRequestKind.Open));

            Assert.Single(before);

            plugins.Stop();
            Dispatcher.UIThread.RunJobs();

            // Панель попрощалась и отпустила хаб: просьба больше никому не
            // идёт, а ложится в очередь.
            var after = new List<TerminalRequest>();

            TerminalHub.Open(new TerminalRequest(TerminalRequestKind.Open));
            TerminalHub.Attach(after.Add);

            Assert.Single(after);
            Assert.Single(before);
        }
        finally
        {
            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Установленный плагин встаёт целиком: панель, кнопки, команда.
    /// </summary>
    /// <remarks>
    /// Пример студии объявляет свою панель событием активации и свой контрол в
    /// полосе, а и то и другое просит поднять сразу: нарисовать чужой контрол,
    /// не загрузив сборку, нечем.
    /// </remarks>
    [AvaloniaFact]
    public void An_installed_plugin_stands_up_whole()
    {
        Install();

        var plugins = Start();

        Assert.Equal("arxis.hello", Assert.Single(plugins.Installed).Id);
        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);

        Assert.Contains("arxis.hello:hello.panel", _dock.Items.Known());
        Assert.Contains("hello.greet", _commands.Registered);

        Assert.Contains(StudioToolBar.Key("arxis.hello", "hello.menu"), _toolbar.Shown("right"));
        Assert.Contains(StudioToolBar.Key("arxis.hello", "hello.strip"), _toolbar.Shown("right"));
    }

    /// <summary>
    /// Спящий плагин ставит объявленное, не поднимаясь.
    /// </summary>
    /// <remarks>
    /// В этом весь смысл событий активации: студия обязана показать, что плагин
    /// установлен, не загрузив ни одной его сборки. Кнопку и меню она рисует
    /// сама — по манифесту.
    /// </remarks>
    [AvaloniaFact]
    public void A_sleeping_plugin_puts_its_menu_up_without_being_raised()
    {
        Install();

        var plugins = Start(sleeping: true);

        Assert.Empty(plugins.Reloadable);
        Assert.Empty(_dock.Items.Known());
        Assert.DoesNotContain("hello.greet", _commands.Registered);

        Assert.Contains(StudioToolBar.Key("arxis.hello", "hello.menu"), _toolbar.Shown("right"));
    }

    /// <summary>
    /// Команда будит хозяина, и его панель встаёт на стену.
    /// </summary>
    /// <remarks>
    /// Дорога одна на всех: и щелчок по кнопке в полосе, и вызов из кода соседа
    /// идут через реестр команд, а он и будит спящего. Проверяется именно она —
    /// не прямой вызов пробуждения.
    /// </remarks>
    [AvaloniaFact]
    public void A_command_wakes_its_owner_and_the_panel_goes_up()
    {
        Install();

        var plugins = Start(sleeping: true);

        Assert.Empty(plugins.Reloadable);

        // Один вызов делает всё: будит хозяина и зовёт зарегистрированную им
        // команду. Ответить «не нашлось», разбудив, значило бы потерять то самое
        // нажатие, ради которого будили.
        Assert.True(_commands.Invoke("hello.greet"), "команда не нашла хозяина даже после подъёма");

        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);
        Assert.Contains("arxis.hello:hello.panel", _dock.Items.Known());
        Assert.Contains("hello.greet", _commands.Registered);
    }

    /// <summary>
    /// Пункт «Добавить ▸» спящего плагина стоит в меню, а его выбор будит плагин, и код пункта
    /// собирает файл.
    /// </summary>
    /// <remarks>
    /// Дорога та же, что у команды: пункт объявлен манифестом и виден без сборки, а событие
    /// <c>onNewItem:</c> поднимает хозяина ровно тогда, когда его код понадобился. Приветствие — из
    /// настройки плагина: ради неё пункт и объявлен кодом, а не шаблоном.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_code_item_wakes_its_sleeping_owner_and_makes_the_file()
    {
        Install();

        var plugins = Start(sleeping: true);
        var items = NewItems(plugins);

        Assert.Empty(plugins.Reloadable);

        var greeting = Assert.Single(items.Items, item => item.Owner == "arxis.hello" && item.Id == "hello.greeting");

        Assert.Equal(NewItemKind.Code, greeting.Kind);

        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;

        Assert.Equal("Greeting1.txt", items.Suggest(greeting, target));

        var made = await items.MakeAsync(
            greeting,
            new NewItemRequest("Hi.txt", target) { Project = "Проба" },
            TestContext.Current.CancellationToken);

        Assert.Null(made.Error);
        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);

        var file = Assert.Single(made.Files);
        var year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("Hi.txt", file.Path);
        Assert.True(file.Open);
        Assert.Equal($"Здравствуйте!\n\nПроба · {year}\n", Encoding.UTF8.GetString(file.Content.Span));
    }

    /// <summary>
    /// Выключенный спящий плагин уносит свои пункты «Добавить ▸», и прежний пункт его не будит.
    /// </summary>
    /// <remarks>
    /// Меню, открытое до выключения, держит пункт на руках; выбор его после — не повод поднимать
    /// плагин, который человек только что выключил.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_sleeping_plugin_switched_off_takes_its_items_away()
    {
        Install();

        var plugins = Start(sleeping: true);
        var items = NewItems(plugins);
        var greeting = Assert.Single(items.Items, item => item.Id == "hello.greeting");

        Assert.Null(new PluginCatalog(_root).SetEnabled("arxis.hello", false));
        Assert.Null(await plugins.ApplyAsync(["arxis.hello"], []));

        Assert.DoesNotContain(items.Items, item => item.Owner == "arxis.hello");

        var made = await items.MakeAsync(
            greeting,
            new NewItemRequest("Hi.txt", Path.Combine(_root, "target")),
            TestContext.Current.CancellationToken);

        Assert.NotNull(made.Error);
        Assert.Empty(plugins.Reloadable);
    }

    /// <summary>Служба создания над службой расширений — так её связывает окно.</summary>
    private StudioNewItems NewItems(StudioPlugins plugins) =>
        new(_log, _guard, _contributions)
        {
            Contributing = () => plugins.Contributing,
            Activate = plugins.Activate,
        };

    /// <summary>
    /// Перезагрузка отдаёт реестры свежей копии, а прежнюю снимает.
    /// </summary>
    /// <remarks>
    /// Ровно здесь списки и разъезжались: уборку реестров переписывал каждый,
    /// кто выгружает, и снятие упавшего забывало команды. Теперь она одна — по
    /// сигналу хоста, — и проверяется тем, что после перезагрузки команда есть
    /// и она одна.
    /// </remarks>
    [AvaloniaFact]
    public async Task Reloading_hands_the_registries_to_the_fresh_copy()
    {
        Install();

        var plugins = Start();

        _commands.Invoke("hello.greet");

        // Умерла ли прежняя копия — вопрос отдельный, и у него свой набор:
        // ответ зависит от сборщика мусора, а в живом окне панель прежней копии
        // держит ещё и дерево. Здесь проверяется другое — кому достались реестры.
        await plugins.ReloadAsync("arxis.hello");

        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);
        Assert.Equal(1, _commands.Registered.Count(id => id == "hello.greet"));
        Assert.Contains("arxis.hello:hello.panel", _dock.Items.Known());
    }

    /// <summary>Плагина, которого нет на диске, перезагружать нечего.</summary>
    [AvaloniaFact]
    public async Task Reloading_a_plugin_that_is_gone_says_so()
    {
        var plugins = Start();

        await plugins.ReloadAsync("arxis.nobody");

        Assert.Empty(plugins.AwaitingRestart);
        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Warning && record.Message.Contains("arxis.nobody"));
    }

    /// <summary>
    /// Перезагружать предлагают только внешние плагины.
    /// </summary>
    /// <remarks>
    /// У встроенного модуля нет своего контекста загрузки, и предлагать
    /// перезагрузить то, что перезагрузить нельзя, — обещание, которое студия не
    /// сдержит.
    /// </remarks>
    [AvaloniaFact]
    public void Only_external_plugins_are_offered_a_reload()
    {
        Install();

        var plugins = Start(modules: typeof(SampleModule).Assembly);

        _commands.Invoke("hello.greet");

        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);
        Assert.Contains(plugins.Modules, module => module.Id == "arxis.sample");
    }

    /// <summary>
    /// Отключённому за сбои в меню больше не вкладываются.
    /// </summary>
    /// <remarks>
    /// Иначе человек видел бы пункт, за которым стоит плагин, которого студия
    /// уже отказывается звать.
    /// </remarks>
    [AvaloniaFact]
    public void A_plugin_disabled_for_failures_is_not_offered_the_menu()
    {
        Install();

        var plugins = Start();

        Assert.Contains(plugins.Contributing, plugin => plugin.Id == "arxis.hello");

        for (var failure = 0; failure < PluginGuard.FailureLimit; failure++)
            _guard.Report("arxis.hello", "проба", new InvalidOperationException("сломалось"));

        Assert.DoesNotContain(plugins.Contributing, plugin => plugin.Id == "arxis.hello");
    }

    /// <summary>
    /// Панель плагина, отключённого за сбои, всё равно получает прощание.
    /// </summary>
    /// <remarks>
    /// Гвард помечает плагин сбойным раньше, чем сообщает об этом, а прощание шло рабочей дорогой
    /// шва — той, что отключённому отказывает. <c>Release</c> не звался ровно на той дороге, где он
    /// нужнее всего: упавший терминал оставлял свои оболочки работать без окна.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_of_an_extension_disabled_for_failures_still_gets_its_farewell()
    {
        var module = Farewell("Probe.FarewellDisabled", "arxis.farewell-disabled", broken: false);
        var panel = module.GetType("Probe.FarewellPanel")!;

        Start(modules: module);

        Assert.Equal(1, Counter(panel, "Built"));

        for (var failure = 0; failure < PluginGuard.FailureLimit; failure++)
            _guard.Report("arxis.farewell-disabled", "проба", new InvalidOperationException("сломалось"));

        Pump();

        Assert.Equal(1, Counter(panel, "Released"));
        Assert.DoesNotContain(_dock.Items.Known(), id => id.StartsWith("arxis.farewell-disabled:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Перезапуск панели прощается с упавшей прежде, чем строить новую.
    /// </summary>
    /// <remarks>
    /// Без прощания упавший экземпляр жил до выгрузки расширения, а у встроенного модуля это
    /// закрытие студии: каждый перезапуск добавлял ещё одну панель со своими процессами и
    /// подписками. Закрытие студии прощается уже только с той, что стоит.
    /// </remarks>
    [AvaloniaFact]
    public void Restarting_a_panel_says_goodbye_to_the_one_that_fell()
    {
        var module = Farewell("Probe.FarewellRestart", "arxis.farewell-restart", broken: true);
        var panel = module.GetType("Probe.FarewellPanel")!;
        var touchy = module.GetType("Probe.Touchy")!;

        var plugins = Start(modules: module);

        (TopLevel.GetTopLevel(_view) as Window)!.UpdateLayout();
        Pump();

        var surface = Assert.IsType<PluginSurface>(_dock.Items.Find("arxis.farewell-restart:farewell.panel")?.Content);

        Assert.True(surface.IsBroken, "панель обязана упасть на замере — иначе проверять нечего");
        Assert.Equal(0, Counter(panel, "Released"));

        touchy.GetField("Broken")!.SetValue(null, false);

        var restart = surface.GetVisualDescendants().OfType<Button>().Single();

        restart.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.Equal(2, Counter(panel, "Built"));
        Assert.Equal(1, Counter(panel, "Released"));

        plugins.Stop();
        Pump();

        Assert.Equal(2, Counter(panel, "Released"));
    }

    /// <summary>
    /// Перезапуск оставляет каретку в панели, хотя кнопка, державшая её, уходит вместе с заглушкой.
    /// </summary>
    /// <remarks>
    /// Новая панель вставала на место заглушки, и каретка уходила вместе с нажатой кнопкой: человек,
    /// перезапустивший панель с клавиатуры, оставался нигде.
    /// </remarks>
    [AvaloniaFact]
    public void Restarting_a_panel_keeps_the_caret_in_it()
    {
        var module = Farewell("Probe.CaretRestart", "arxis.caret-restart", broken: true, focusable: true);
        var touchy = module.GetType("Probe.Touchy")!;

        Start(modules: module);

        (TopLevel.GetTopLevel(_view) as Window)!.UpdateLayout();
        Pump();

        var surface = Assert.IsType<PluginSurface>(_dock.Items.Find("arxis.caret-restart:farewell.panel")?.Content);

        Assert.True(surface.IsBroken, "панель обязана упасть на замере — иначе проверять нечего");

        var restart = surface.GetVisualDescendants().OfType<Button>().Single();

        Assert.True(restart.Focus(), "кнопка перезапуска обязана брать каретку");

        touchy.GetField("Broken")!.SetValue(null, false);
        restart.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.False(surface.IsBroken);
        Assert.True(DockFocus.Holds(surface), "перезапуск унёс каретку вместе с кнопкой");
    }

    private static int Counter(Type type, string field) => (int)type.GetField(field)!.GetValue(null)!;

    /// <summary>Даёт отложенной работе студии дойти до конца: отключение идёт через очередь дважды.</summary>
    private static void Pump()
    {
        for (var turn = 0; turn < 4; turn++)
            Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Модуль с панелью, которая считает свои постройки и прощания.
    /// </summary>
    /// <param name="name">Имя сборки — своё на тест: тип со статическим счётом один на процесс.</param>
    /// <param name="id">Идентификатор модуля.</param>
    /// <param name="broken">Падает ли содержимое панели на замере.</param>
    /// <param name="focusable">Берёт ли панель каретку: содержимое тогда в рамке, которая её берёт.</param>
    private static Assembly Farewell(string name, string id, bool broken, bool focusable = false) => TestAssembly.EmitModule(
        name,
        $$"""
            using ArxisStudio.Sdk;
            using Avalonia;
            using Avalonia.Controls;

            namespace Probe;

            public sealed class FarewellModule : StudioPlugin
            {
                public override void Activate(IStudioContext context)
                {
                }
            }

            public sealed class Touchy : Control
            {
                public static bool Broken = {{(broken ? "true" : "false")}};

                protected override Size MeasureOverride(Size availableSize) =>
                    Broken ? throw new System.InvalidOperationException("панель сломана") : new Size(10, 10);
            }

            [ToolWindow("farewell.panel")]
            public sealed class FarewellPanel : ToolWindow
            {
                public static int Built;
                public static int Released;

                protected override Control Build()
                {
                    Built++;

                    return {{(focusable ? "new Border { Focusable = true, Child = new Touchy() }" : "new Touchy()")}};
                }

                public override void Release() => Released++;
            }
            """,
        $$"""
            {
              "id": "{{id}}",
              "name": "Прощание",
              "version": "1.0.0",
              "contributions": {
                "toolWindows": [ { "id": "farewell.panel", "title": "Прощание", "placement": { "side": "bottom" } } ]
              },
              "activation": [ "onStartup" ]
            }
            """);

    /// <summary>
    /// Плагин, забывший отписаться от своих настроек, всё равно выгружается.
    /// </summary>
    /// <remarks>
    /// Объект настроек студия выдаёт плагину и помнит сама — чтобы сказать ему о правке из окна
    /// настроек. Подписчики его <c>Changed</c> — методы плагина, и пока запись жила в словаре
    /// фабрики после ухода плагина, контекст загрузки держала сама студия: перезагрузка честно
    /// сообщала «прежняя копия осталась в памяти» о ссылке из собственного словаря.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_plugin_that_forgot_its_settings_subscription_still_unloads()
    {
        var folder = Path.Combine(_root, "arxis.forgetful");

        Directory.CreateDirectory(Path.Combine(folder, "bin"));

        File.WriteAllText(Path.Combine(folder, "plugin.json"), """
            {
              "id": "arxis.forgetful",
              "name": "Забывчивый",
              "version": "1.0.0",
              "entry": "bin/Probe.Forgetful.dll",
              "activation": [ "onStartup" ]
            }
            """);

        TestAssembly.EmitFile(Path.Combine(folder, "bin", "Probe.Forgetful.dll"), "Probe.Forgetful", """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class ForgetfulPlugin : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    context.Settings.Changed += (_, _) => { };
            }
            """);

        var plugins = Start();

        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == "arxis.forgetful");

        await plugins.ReloadAsync("arxis.forgetful");

        Assert.Empty(plugins.AwaitingRestart);
    }

    /// <summary>
    /// Сохранение перед перезапуском пишет галочку на диск, а плагин не трогает — пока перезапуск
    /// не сорвался.
    /// </summary>
    /// <remarks>
    /// Перезапуск из окна настроек сперва дописывает несохранённое. Опускать выключенного в
    /// процессе, который через миг закроется, — ожидание выгрузки и десяток проходов сборщика
    /// мусора ради состояния, которое новая копия и так прочтёт с диска. Не поднялась новая копия
    /// — записанное применяется вживую: иначе выключенный работал бы, а его строка стояла бы
    /// выключенной.
    /// </remarks>
    [AvaloniaFact]
    public async Task Saving_before_a_restart_writes_the_switch_and_catches_up_if_it_fails()
    {
        Install();

        var plugins = Start();
        var page = new ArxisStudio.Settings.PluginsPage(new PluginCatalog(_root), plugins, new Dialogs());
        var problems = new List<string>();

        await page.ToggleAsync(Assert.Single(page.Cards));
        await page.CommitAsync(problems, live: false);

        Assert.Empty(problems);
        Assert.False(Assert.Single(new PluginCatalog(_root).Scan()).IsEnabled, "выключение не записалось");
        Assert.Contains(plugins.Reloadable, plugin => plugin.Id == "arxis.hello");

        // Перезапуск не состоялся — записанное применяется вживую, и студия не расходится с диском.
        await page.CatchUpAsync();

        Assert.DoesNotContain(plugins.Reloadable, plugin => plugin.Id == "arxis.hello");
        Assert.False(Assert.Single(page.Cards).IsOn);
    }

    /// <summary>
    /// Контракт, пересобранный на ходу, ждёт перезапуска: общий контекст его не обновит.
    /// </summary>
    /// <remarks>
    /// Каскад говорил об этом заметкой в журнале, и только: человек, перезагрузивший плагин, видел
    /// новую сборку и прежние типы контракта, а что поправит только перезапуск, узнавал, найдя эту
    /// заметку. Теперь плагин встаёт в ждущие, и студия предлагает перезапуск сама.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_contract_changed_on_disk_waits_for_a_restart()
    {
        Install();

        var plugins = Start();

        File.SetLastWriteTimeUtc(
            Path.Combine(_root, "arxis.hello", "bin", "Arxis.Hello.Contracts.dll"),
            DateTime.UtcNow.AddMinutes(1));

        await plugins.ReloadAsync("arxis.hello");

        Assert.True(
            plugins.AwaitingRestart.TryGetValue("arxis.hello", out var reason),
            "плагин с пересобранным контрактом не ждёт перезапуска");
        Assert.Contains("Arxis.Hello.Contracts", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Плагин, которого держат реестры Avalonia, ждёт перезапуска: человеку — что он нужен, журналу —
    /// почему.
    /// </summary>
    /// <remarks>
    /// Свой <c>StyledProperty</c> у контрола — обычное дело, а снять его Avalonia не умеет: прежняя
    /// копия остаётся в памяти до перезапуска. Запись 284 называла человеку владельцев свойств — в
    /// менеджере и в строке состояния; владелец студии счёл это лишним. Работающий плагин теперь
    /// заранее не говорит ничего — применять пока нечего; спущенный встаёт в ждущие перезапуска, и
    /// менеджер говорит только это. Причина — в журнале, и повтор той же причины нового вопроса не
    /// заводит.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_plugin_held_by_avalonia_waits_for_a_restart_and_only_the_log_says_why()
    {
        const string id = "probe.held";

        var folder = Path.Combine(_root, id);

        Directory.CreateDirectory(Path.Combine(folder, "bin"));

        File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "Держится",
              "version": "1.0.0",
              "entry": "bin/Probe.Held.dll",
              "activation": [ "onStartup" ]
            }
            """);

        TestAssembly.EmitFile(Path.Combine(folder, "bin", "Probe.Held.dll"), "Probe.Held", """
            using ArxisStudio.Controls;
            using ArxisStudio.Sdk;
            using Avalonia;

            namespace Probe;

            public sealed class HeldPlugin : StudioPlugin
            {
                public override void Activate(IStudioContext context) => _ = Badge.CountProperty;
            }

            public sealed class Badge : AxUserControl
            {
                public static readonly StyledProperty<int> CountProperty =
                    AvaloniaProperty.Register<Badge, int>(nameof(Count));

                public int Count
                {
                    get => GetValue(CountProperty);
                    set => SetValue(CountProperty, value);
                }
            }
            """);

        // Обновление ставится из копии папки: установка поверх заменяет каталог плагина целиком.
        var incoming = Path.Combine(_root, "..", $"arxis-held-{Guid.NewGuid():N}");

        Copy(folder, incoming);

        var plugins = Start();
        var dialogs = new Dialogs { Folder = incoming };
        var page = new ArxisStudio.Settings.PluginsPage(new PluginCatalog(_root), plugins, dialogs);
        var asked = new List<string>();
        var required = Localizer.Instance["restart.required"];

        plugins.RestartRequired += (_, plugin) => asked.Add(plugin);

        try
        {
            // Работающему применять нечего, и заранее он молчит.
            Assert.False(Card().NeedsRestart, "работающий плагин уже требует перезапуска — применять пока нечего");

            await plugins.ReloadAsync(id);

            Assert.Equal([id], asked);
            Assert.Contains("Badge", plugins.AwaitingRestart[id], StringComparison.Ordinal);
            Assert.Contains(_log.Records, record =>
                record.Level == StudioLogLevel.Warning && record.Message.Contains("Badge", StringComparison.Ordinal));

            page.Refresh();

            Assert.True(Card().NeedsRestart, "менеджер не сказал, что плагин ждёт перезапуска");
            Assert.Equal(required, Card().Status);
            Assert.Null(Card().Problem);

            await page.InstallFromFolderAsync();

            Assert.EndsWith(required, page.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("Badge", page.Status, StringComparison.Ordinal);

            // Та же причина — не новый повод: вопрос о перезапуске не должен звучать на каждое действие.
            Assert.Equal([id], asked);

            Assert.Null(new PluginCatalog(_root).SetEnabled(id, false));
            Assert.Null(await plugins.ApplyAsync([id], []));

            await page.RemoveAsync(Card());

            Assert.DoesNotContain(page.Cards, card => card.Plugin.Id == id);
            Assert.EndsWith(required, page.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("Badge", page.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(incoming, recursive: true);
        }

        ViewModels.PluginCard Card() => Assert.Single(page.Cards, card => card.Plugin.Id == id);

        static void Copy(string from, string to)
        {
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, file));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    /// <summary>
    /// Выключенный спящий плагин уходит целиком и больше не будится.
    /// </summary>
    /// <remarks>
    /// Спящего опускать нечем — его сборка не загружена, — и выключение выходило ранним возвратом,
    /// не тронув ничего: кнопки, сочетания и запись среди ждущих оставались. Первое же его событие
    /// поднимало плагин, который человек только что выключил.
    /// </remarks>
    [AvaloniaFact]
    public async Task Switching_a_sleeping_plugin_off_takes_it_out_for_good()
    {
        Install();

        var plugins = Start(sleeping: true);

        Assert.Contains(StudioToolBar.Key("arxis.hello", "hello.menu"), _toolbar.Shown("right"));
        Assert.NotNull(_keys.Gesture("hello.greet"));

        Assert.Null(new PluginCatalog(_root).SetEnabled("arxis.hello", false));
        Assert.Null(await plugins.ApplyAsync(["arxis.hello"], []));

        Assert.DoesNotContain(StudioToolBar.Key("arxis.hello", "hello.menu"), _toolbar.Shown("right"));
        Assert.Null(_keys.Gesture("hello.greet"));

        Assert.False(_commands.Invoke("hello.greet"), "выключенный плагин разбужен своей командой");
        Assert.Empty(plugins.Reloadable);
        Assert.DoesNotContain("arxis.hello:hello.panel", _dock.Items.Known());
    }

    /// <summary>
    /// Перезагруженный плагин остаётся при сочетаниях своего манифеста.
    /// </summary>
    /// <remarks>
    /// Уход прежней копии снимает всё, что записано на хозяина, — и сочетания тоже. Заявлялись они
    /// только на старте, и после «Перезагрузить» команда оставалась без клавиши до перезапуска
    /// студии. Заявка при этом обязана быть одна: повторную реестр считает спором за занятое.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_reloaded_plugin_keeps_the_gestures_of_its_manifest()
    {
        Install();

        var plugins = Start();
        var before = _keys.Gesture("hello.greet");

        Assert.NotNull(before);

        // Уже старт заявляет ровно один раз: вторая заявка — отказ хозяину в его же сочетании,
        // и страница «Клавиши» показывала бы плагин проигравшим самому себе.
        Assert.DoesNotContain(_keys.Refused, refusal => refusal.CommandId == "hello.greet");

        await plugins.ReloadAsync("arxis.hello");

        Assert.Equal(before, _keys.Gesture("hello.greet"));
        Assert.Single(_keys.All, bound => bound.CommandId == "hello.greet");
        Assert.DoesNotContain(_keys.Refused, refusal => refusal.CommandId == "hello.greet");
    }

    /// <summary>
    /// Включённый плагин, чьей зависимости нет, говорит почему не поднялся.
    /// </summary>
    /// <remarks>
    /// Граф ему отказывал, а отказ терялся: в журнал шли только заметки, карточка причины не
    /// получала, окно настроек считало сохранение удачным. Причина появлялась после перезапуска —
    /// записью старта.
    /// </remarks>
    [AvaloniaFact]
    public async Task Enabling_a_plugin_whose_neighbour_is_missing_says_why()
    {
        var folder = Path.Combine(_root, "arxis.needy");

        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "plugin.json"), """
            {
              "id": "arxis.needy",
              "name": "Нуждающийся",
              "version": "1.0.0",
              "entry": "bin/Probe.Needy.dll",
              "dependencies": [ { "id": "arxis.nowhere" } ],
              "activation": [ "onStartup" ]
            }
            """);

        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.SetEnabled("arxis.needy", false));

        var plugins = Start();

        Assert.Empty(plugins.Unrisen);

        Assert.Null(catalog.SetEnabled("arxis.needy", true));

        var complaint = await plugins.ApplyAsync([], ["arxis.needy"]);

        Assert.NotNull(complaint);
        Assert.Contains("arxis.nowhere", complaint, StringComparison.Ordinal);
        Assert.Contains("arxis.nowhere", plugins.Unrisen["arxis.needy"], StringComparison.Ordinal);

        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Error && record.Message.Contains("Нуждающийся", StringComparison.Ordinal));
    }

    /// <summary>
    /// Чужое исключение расширению не приписывают.
    /// </summary>
    /// <remarks>
    /// Приписать студийный дефект плагину значит отключить невиновного и
    /// спрятать свою же ошибку: после третьего раза плагин перестают звать.
    /// </remarks>
    [AvaloniaFact]
    public void An_exception_from_nobody_is_not_charged_to_a_plugin()
    {
        var plugins = Start();

        Assert.False(plugins.Blame(new InvalidOperationException("своё"), "проба"));
        Assert.Empty(_guard.Faulty);
    }

    /// <summary>
    /// Студию закрывают — реестры отпускают записи расширений.
    /// </summary>
    /// <remarks>
    /// Запись реестра держит объект из контекста загрузки плагина, а через него
    /// и сам контекст: не убрав её, студия «выгружает» плагин только на словах.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_the_studio_lets_the_registries_go()
    {
        Install();

        var plugins = Start();

        _commands.Invoke("hello.greet");
        Assert.Contains("hello.greet", _commands.Registered);

        plugins.Stop();

        Assert.DoesNotContain("hello.greet", _commands.Registered);
    }

    /// <summary>
    /// Реестр вкладов отпускает выгруженного вместе с остальными.
    /// </summary>
    /// <remarks>
    /// Редактор документов — объект из контекста загрузки плагина: оставленная
    /// запись держит и его, и весь контекст. Уборка одна на все дороги выгрузки
    /// именно поэтому — разнеси её, и один из путей о ней забудет.
    /// <para>
    /// Вклад подкладывается тестом от имени плагина: своих у примера не
    /// осталось — рисовальщик, которым эта проверка держалась прежде, снят
    /// вместе со своим контрактом. Реестру всё равно, откуда пришла сборка;
    /// проверяется здесь не он, а то, что дорога выгрузки его зовёт.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_contributions_of_an_unloaded_plugin_go_too()
    {
        Install();

        var plugins = Start();
        var note = Path.Combine(Path.GetTempPath(), "Список.note");

        _contributions.Add("arxis.hello", "Hello", [typeof(NoteEditor).Assembly]);

        Assert.NotNull(_contributions.EditorFor(note));

        plugins.Stop();

        Assert.Null(_contributions.EditorFor(note));
    }

    /// <summary>
    /// Плагин, пропавший с диска между запуском и перезагрузкой, не поднимают.
    /// </summary>
    /// <remarks>
    /// Список берётся с диска заново, а не из памяти: перезагружают потому, что
    /// на диске что-то изменилось, и прежний список — рассказ о том, чего там
    /// уже нет. Поднять по нему значило бы поднять то, что человек только что
    /// удалил.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_plugin_that_vanished_from_disk_is_not_raised_again()
    {
        Install();

        var plugins = Start();

        Assert.Single(plugins.Installed);

        _vanished = true;

        await plugins.ReloadAsync("arxis.hello");

        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Warning && record.Message.Contains("каталоге плагинов"));
    }

    /// <summary>
    /// Кнопки несостоявшегося плагина уходят с полосы.
    /// </summary>
    /// <remarks>
    /// Стоят они с объявления — студия рисует их по манифесту, не загружая
    /// сборку. Плагин не поднялся, значит команда за кнопкой не найдётся
    /// никогда: нажатие ушло бы в никуда, и человек решил бы, что сломана
    /// студия.
    /// </remarks>
    [AvaloniaFact]
    public void The_buttons_of_a_plugin_that_failed_to_rise_go_away()
    {
        Install();

        _broken = true;

        var plugins = Start();

        Assert.Empty(plugins.Reloadable);
        Assert.Empty(_toolbar.Shown("right"));

        Assert.Contains(_log.Records, record => record.Level == StudioLogLevel.Error);
    }

    /// <summary>
    /// Модули и плагины поднимаются двумя шагами, а не одним.
    /// </summary>
    /// <remarks>
    /// Шаги порознь нужны заставке: она называет человеку, что грузится сейчас,
    /// и «загрузка модулей» с «загрузкой плагинов» — это две строки, а не одна.
    /// Проверяется, что шаг делает ровно свою часть: после первого плагинов ещё
    /// нет.
    /// </remarks>
    [AvaloniaFact]
    public void Modules_and_plugins_rise_in_two_steps()
    {
        Install();

        var plugins = Build(modules: typeof(SampleModule).Assembly);

        plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("arxis.sample", Assert.Single(plugins.Modules).Id);
        Assert.Empty(plugins.Reloadable);

        plugins.LoadPlugins();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);
    }

    /// <summary>
    /// Упавший модуль не стоит соседям ничего.
    /// </summary>
    /// <remarks>
    /// <c>Activate</c> — чужой код, и зовётся он на загрузке напрямую, мимо
    /// <c>PluginGuard</c>: считать падения ещё не поднятого плагина некому.
    /// Швом работает фильтр <c>catch</c> в хосте, и пока он перечислял беды по
    /// именам, неназванная уносила всё: <c>NullReferenceException</c> из
    /// первого же модуля роняла <c>LoadModules</c> целиком, и остальные модули
    /// не поднимались вовсе — а те, что успели, не получали <c>Accept</c> и
    /// оставались в памяти, не зарегистрированные нигде.
    /// </remarks>
    [AvaloniaFact]
    public void A_module_that_falls_costs_the_others_nothing()
    {
        var plugins = Build(modules: [Falling(), typeof(SampleModule).Assembly]);

        plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        // Оба в списке: упавший записью, целый — работой. Пустой список здесь
        // означал бы, что LoadModules бросил и не дошёл до конца.
        Assert.Equal(
            ["arxis.falling", "arxis.sample"],
            plugins.Modules.Select(module => module.Id).Order());

        Assert.Contains(_log.Records, record =>
            record.Level == StudioLogLevel.Error && record.Message.Contains("модуль уронил студию"));

        Assert.DoesNotContain(_log.Records, record =>
            record.Level == StudioLogLevel.Error && record.Message.Contains("arxis.sample"));
    }

    /// <summary>
    /// Загрузка называет тех, кто не поднялся, — отчёту запуска.
    /// </summary>
    /// <remarks>
    /// Отказ каждого расширения ловится порознь и соседям ничего не стоит, и этап запуска сам не
    /// падал никогда: модуль или плагин, падающий на каждом запуске, падал молча, а человек видел
    /// только, что его кнопки нет. Имена несостоявшихся уходят в отчёт, и студия говорит, что
    /// запуск прошёл не полностью.
    /// </remarks>
    [AvaloniaFact]
    public void Loading_names_the_extensions_that_did_not_rise()
    {
        Install();

        var plugins = Build(modules: [Falling(), typeof(SampleModule).Assembly]);

        Assert.Equal(["Падающий"], plugins.LoadModules());
        Assert.Empty(plugins.LoadPlugins());

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Модуль, который падает ровно там, где студия зовёт чужой код.</summary>
    private static Assembly Falling() => TestAssembly.EmitModule(
        "Probe.Falling",
        """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class FallingModule : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    throw new System.NullReferenceException("модуль уронил студию");
            }
            """,
        """
            {
              "id": "arxis.falling",
              "name": "Падающий",
              "version": "1.0.0",
              "activation": [ "onStartup" ]
            }
            """);

    /// <summary>
    /// Подготовка зовётся сама и повторов не боится.
    /// </summary>
    /// <remarks>
    /// Порядок трёх шагов иначе стал бы ловушкой: забывший подготовку получил
    /// бы пустую студию без единого слова о том, почему. Здесь шаг зовут вторым
    /// и лишний раз — студия обязана подняться целиком и один раз.
    /// </remarks>
    [AvaloniaFact]
    public void Preparing_calls_itself_and_survives_a_repeat()
    {
        Install();

        var plugins = Build();

        plugins.Prepare();
        plugins.Prepare();
        plugins.LoadPlugins();

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("arxis.hello", Assert.Single(plugins.Installed).Id);
        Assert.Equal("arxis.hello", Assert.Single(plugins.Reloadable).Id);
        Assert.Single(_toolbar.Shown("right"), StudioToolBar.Key("arxis.hello", "hello.menu"));
    }

    /// <summary>Ставит пример плагина во временную папку студии.</summary>
    private void Install()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);
    }

    /// <summary>
    /// Записи каталога о поставленном.
    /// </summary>
    /// <param name="sleeping">
    /// Оставить плагину события, которых он ждёт, — вызов команды и выбор его
    /// пункта создания, — сняв то, из-за чего он поднимается сразу.
    /// </param>
    /// <remarks>
    /// Плагин на диске один и настоящий; меняется только объявленное им условие
    /// подъёма — ровно то, что здесь и проверяется. Пример студии просит поднять
    /// себя сразу: он объявляет свою панель и рисует свой контрол в полосе, а
    /// нарисовать чужой контрол, не загрузив сборку, нечем.
    /// </remarks>
    private IReadOnlyList<InstalledPlugin> Scan(bool sleeping)
    {
        if (_vanished)
            return [];

        var installed = new PluginCatalog(_root).Scan();

        foreach (var manifest in installed.Select(plugin => plugin.Manifest).OfType<Sdk.Plugins.PluginManifest>())
        {
            if (_broken)
                manifest.Entry = "bin/Пропавшая.dll";

            if (!sleeping)
                continue;

            manifest.Activation = ["onCommand:hello.greet", "onNewItem:hello.greeting"];
            manifest.Contributions.ToolBar = [.. manifest.Contributions.ToolBar.Where(item => !item.IsCustom)];
        }

        return installed;
    }

    /// <summary>
    /// Поднимает службу так же, как её поднимает окно.
    /// </summary>
    /// <param name="sleeping">Отложить подъём плагинов до вызова их команды.</param>
    /// <param name="modules">Встроенные модули; без них студия — пустой каркас.</param>
    /// <summary>
    /// Студия берёт службу проектов из экспортов — той же дорогой, что и плагин.
    /// </summary>
    /// <remarks>
    /// Модуль публикует её при подъёме, и другого пути к ней у студии нет: путь проекта в контексте
    /// плагина, проектные настройки и недавние висят на этой подписке.
    /// </remarks>
    [AvaloniaFact]
    public void The_studio_takes_the_projects_service_from_the_exports()
    {
        var plugins = Start(modules: TestAssembly.EmitModule("Probe.Projects", ProjectsSource, ProjectsManifest));

        Assert.Equal(ProbeSolution.Value, plugins.Project.Path);
        Assert.NotNull(plugins.Projects);
    }

    /// <summary>
    /// Без модуля открывать нечем, и студия это знает.
    /// </summary>
    /// <remarks>
    /// Проект, названный в командной строке, открывать некому: студия скажет об этом в журнал и
    /// покажет Welcome, а не сделает вид, что открыла.
    /// </remarks>
    [AvaloniaFact]
    public void Without_the_module_there_is_nothing_to_open_with()
    {
        var plugins = Start();

        Assert.Null(plugins.Projects);
        Assert.Null(plugins.Project.Path);
    }

    /// <summary>
    /// Проектные настройки идут за открытым проектом.
    /// </summary>
    /// <remarks>
    /// Область настроек лежит в самом проекте, и открытие другого меняет значения, которых плагин
    /// не трогал: хранилище обязано переехать вместе с проектом, а не остаться у прежнего.
    /// </remarks>
    [AvaloniaFact]
    public void Project_settings_follow_the_open_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-follow-{Guid.NewGuid():N}");
        var plugins = Start();

        Assert.Null(plugins.Settings.ProjectFile);

        plugins.Project.Apply(new ProjectsStatus
        {
            Sequence = 1,
            Session = 1,
            State = ProjectsState.Ready,
            EntryPoint = CanonicalPath.Create(Path.Combine(folder, "Волна.slnx")),
        });

        Assert.Equal(Path.Combine(folder, ".arxis", "settings.json"), plugins.Settings.ProjectFile);

        plugins.Project.Apply(ProjectsStatus.Closed);

        Assert.Null(plugins.Settings.ProjectFile);
    }

    private StudioPlugins Start(bool sleeping = false, params Assembly[] modules)
    {
        var plugins = Build(sleeping, modules);

        plugins.Start();
        Dispatcher.UIThread.RunJobs();

        return plugins;
    }

    /// <summary>Собирает службу, никого не поднимая.</summary>
    private StudioPlugins Build(bool sleeping = false, params Assembly[] modules)
    {
        var plugins = new StudioPlugins(_log, _guard, _tasks, _contributions)
        {
            Commands = _commands,
            Dock = _dock,
            ToolBar = _toolbar,
            Documents = _documents,
            Shortcuts = _keys,
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },

            // Папка плагинов — своя на тест: настоящая принадлежит человеку, и
            // прогон, читающий её, отвечал бы по-разному на разных машинах. По той
            // же причине своё и хранилище настроек: приветствие примера человек
            // вправе поменять у себя.
            Catalog = () => Scan(sleeping),
            Settings = new PluginSettingsStore(userFile: Path.Combine(_root, "plugin-settings.json")),
            Assemblies = modules,
        };

        _plugins = plugins;

        return plugins;
    }

    /// <summary>Модуль, публикующий подставную службу проектов с уже открытым решением.</summary>
    private const string ProjectsSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ArxisStudio.Projects;
        using ArxisStudio.ProjectSystem;
        using ArxisStudio.Sdk;

        namespace Probe;

        public sealed class ProbeProjects : IStudioProjects
        {
            public ProjectsStatus Status { get; } = new()
            {
                Sequence = 1,
                Session = 1,
                State = ProjectsState.Ready,
                EntryPoint = CanonicalPath.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Проба", "Проба.slnx")),
            };

            public SolutionSnapshot? Current => Status.Snapshot;

            public event EventHandler<ProjectsChangedEventArgs>? Changed;

            public Task<WorkspaceLoadResult> OpenAsync(CanonicalPath entryPoint, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<WorkspaceLoadResult> ReloadAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<WorkspaceLoadResult> SetConfigurationAsync(string? configuration, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task CloseAsync() => throw new NotSupportedException();
        }

        public sealed class ProbeModule : StudioPlugin
        {
            public override void Activate(IStudioContext context) =>
                context.GetService<IStudioExports>()?.Publish<IStudioProjects>(new ProbeProjects());
        }
        """;

    /// <summary>Манифест подставного модуля.</summary>
    private const string ProjectsManifest = """
        {
          "id": "probe.projects",
          "name": "Проба проектов",
          "version": "1.0.0"
        }
        """;

    /// <summary>Проект, который отдаёт подставная служба.</summary>
    private static readonly CanonicalPath ProbeSolution =
        CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Проба", "Проба.slnx"));

    /// <summary>Диалоги менеджера: папку отдают названную, на вопросы соглашаются.</summary>
    private sealed class Dialogs : ArxisStudio.Settings.IPluginDialogs
    {
        public string? Folder { get; init; }

        public Task<string?> AskFolderAsync(string title) => Task.FromResult(Folder);

        public Task<string?> AskArchiveAsync(string title) => Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger) => Task.FromResult(true);

        public void Reveal(string path)
        {
        }
    }

    /// <summary>Строка состояния, которой здесь никто не смотрит.</summary>
    private sealed class Silence : IStudioStatus
    {
        /// <inheritdoc/>
        public void Show(string message)
        {
        }
    }
}
