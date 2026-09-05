using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
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

    private StudioPlugins? _plugins;

    // Каталог — единственное, что тест подменяет: плагин на диске настоящий, а
    // меняются обстоятельства, в которых студия его застаёт.
    private bool _vanished;
    private bool _broken;

    public StudioPluginsTests()
    {
        _commands = new StudioCommands(_guard);
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

        Assert.Null(await plugins.ReloadAsync("arxis.nobody"));

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
    /// Рисовальщик свойства — объект из контекста загрузки плагина: оставленная
    /// запись держит и его, и весь контекст. Уборка одна на все дороги выгрузки
    /// именно поэтому — разнеси её, и один из путей о ней забудет.
    /// </remarks>
    [AvaloniaFact]
    public void The_contributions_of_an_unloaded_plugin_go_too()
    {
        Install();

        var plugins = Start();

        Assert.NotEmpty(_contributions.DrawnTypes);

        plugins.Stop();

        Assert.Empty(_contributions.DrawnTypes);
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
            record.Level == StudioLogLevel.Warning && record.Message.Contains("папке плагинов"));
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
    /// Оставить плагину одно событие активации — вызов команды, — сняв то, из-за
    /// чего он поднимается сразу.
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

            manifest.Activation = ["onCommand:hello.greet"];
            manifest.Contributions.ToolBar = [.. manifest.Contributions.ToolBar.Where(item => !item.IsCustom)];
        }

        return installed;
    }

    /// <summary>
    /// Поднимает службу так же, как её поднимает окно.
    /// </summary>
    /// <param name="sleeping">Отложить подъём плагинов до вызова их команды.</param>
    /// <param name="modules">Встроенные модули; без них студия — пустой каркас.</param>
    private StudioPlugins Start(bool sleeping = false, params Assembly[] modules)
    {
        var plugins = new StudioPlugins(_log, _guard, _tasks, _contributions)
        {
            Commands = _commands,
            Dock = _dock,
            ToolBar = _toolbar,
            Documents = _documents,
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },

            // Папка плагинов — своя на тест: настоящая принадлежит человеку, и
            // прогон, читающий её, отвечал бы по-разному на разных машинах.
            Catalog = () => Scan(sleeping),
            Assemblies = modules,
        };

        _plugins = plugins;
        plugins.Start();

        Dispatcher.UIThread.RunJobs();

        return plugins;
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
