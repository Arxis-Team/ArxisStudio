using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Полоса студии: порядок хозяев, кнопки и меню по манифесту, состояние по
/// сообщению плагина.
/// </summary>
/// <remarks>
/// Проверяется без окна студии, как раскладка: реестр склеивает манифесты с
/// лентой, и всё, что он обещает, видно на ленте. Сборки плагинов здесь нет ни
/// одной — кнопки и меню обязаны строиться без них.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioToolBarTests : IDisposable
{
    private readonly ToolBarStrip _left = new();
    private readonly ToolBarStrip _center = new();
    private readonly ToolBarStrip _right = new();
    private readonly StudioToolBar _bar;
    private readonly List<string> _complaints = [];
    private readonly List<string> _invoked = [];
    private readonly Window _window;

    public StudioToolBarTests()
    {
        _bar = new StudioToolBar(_left, _center, _right)
        {
            Invoke = command =>
            {
                _invoked.Add(command);
                return true;
            },
        };

        _bar.Complained += (_, message) => _complaints.Add(message);

        _window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _left, _center, _right },
            },
        };

        _window.Show();
        _window.UpdateLayout();
    }

    public void Dispose()
    {
        _window.Close();
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Порядок: студия, модули, плагины по идентификатору, внутри — манифест.
    /// </summary>
    /// <remarks>
    /// Подаются в обратном порядке нарочно: очередь подачи — это порядок
    /// подъёма, а он зависит от того, что человек успел установить. Знакомая
    /// полоса не должна перестраиваться после каждой установки.
    /// </remarks>
    [AvaloniaFact]
    public void Owners_stand_host_first_then_modules_then_plugins_by_id()
    {
        var zeta = Plugin("zeta", ButtonOf("z.two", "z.2"), ButtonOf("z.one", "z.1"));
        var alpha = Plugin("alpha", ButtonOf("a", "a.1"));
        var sample = Plugin("sample", builtIn: true, ButtonOf("s", "s.1"));

        _bar.Add(zeta, zeta.Manifest!.Contributions.ToolBar[1]);
        _bar.Add(zeta, zeta.Manifest.Contributions.ToolBar[0]);
        _bar.Add(alpha, alpha.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(sample, sample.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(null, ButtonOf("menu", "studio.menu"));

        Assert.Equal(
            ["studio:menu", "sample:s", "alpha:a", "zeta:z.two", "zeta:z.one"],
            _bar.Shown("right"));

        // Четыре хозяина — три разделителя, и ни одного между кнопками zeta.
        Assert.Equal(3, _right.Children.OfType<AxDivider>().Count());
    }

    /// <summary>Место — по объявлению; незнакомое слово читается как «справа» и отмечается.</summary>
    [AvaloniaFact]
    public void Items_take_the_slot_they_asked_for()
    {
        var plugin = Plugin("hello",
            ButtonOf("l", "c.l", slot: "left"),
            ButtonOf("c", "c.c", slot: "Center"),
            ButtonOf("r", "c.r", slot: "right"),
            ButtonOf("odd", "c.o", slot: "top"));

        foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
            _bar.Add(plugin, declared);

        Assert.Equal(["hello:l"], _bar.Shown("left"));
        Assert.Equal(["hello:c"], _bar.Shown("center"));
        Assert.Equal(["hello:r", "hello:odd"], _bar.Shown("right"));
        Assert.Contains(_complaints, message => message.Contains("hello:odd", StringComparison.Ordinal) && message.Contains("top", StringComparison.Ordinal));
    }

    /// <summary>
    /// Уход хозяина уносит его элементы и их состояние; соседей не трогает.
    /// </summary>
    /// <remarks>
    /// Состояние тоже уходит: вернувшийся плагин — новая копия, и включённый
    /// инструмент прежней копии ему ничего не обещал.
    /// </remarks>
    [AvaloniaFact]
    public void The_owner_leaving_takes_its_items_and_their_state()
    {
        var hello = Plugin("hello", ButtonOf("run", "hello.run"));
        var friend = Plugin("friend", ButtonOf("cheer", "friend.cheer"));

        _bar.Add(hello, hello.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(friend, friend.Manifest!.Contributions.ToolBar[0]);
        _bar.Update("hello", "run", isChecked: true);

        _bar.RemoveOwnedBy("hello");

        Assert.Equal(["friend:cheer"], _bar.Shown("right"));

        _bar.Add(hello, hello.Manifest.Contributions.ToolBar[0]);

        Assert.Equal(["friend:cheer", "hello:run"], _bar.Shown("right"));
        Assert.False(View<ToolBarButton>("hello:run").IsChecked);
    }

    /// <summary>
    /// Повторное объявление ничего не пересобирает.
    /// </summary>
    /// <remarks>
    /// Спящий плагин получает кнопки при старте, а поднявшись, объявляет их снова
    /// — и должен увидеть их стоящими, а не заменёнными: у стоящей кнопки уже
    /// есть состояние.
    /// </remarks>
    [AvaloniaFact]
    public void Declaring_the_same_item_again_keeps_the_one_that_stands()
    {
        var plugin = Plugin("hello", ButtonOf("run", "hello.run"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        var standing = View<ToolBarButton>("hello:run");

        _bar.Update("hello", "run", isChecked: true);
        _bar.Add(plugin, plugin.Manifest.Contributions.ToolBar[0]);

        Assert.Same(standing, View<ToolBarButton>("hello:run"));
        Assert.True(standing.IsChecked);
        Assert.Equal(["hello:run"], _bar.Shown("right"));
    }

    /// <summary>
    /// Щелчок зовёт команду через студию; команда без хозяина — замечание.
    /// </summary>
    /// <remarks>
    /// Через студию, а не напрямую: только реестр команд умеет разбудить
    /// спящего хозяина и приписать падение виновнику. Кнопка сама ничего не
    /// знает о плагине — и не держит его.
    /// </remarks>
    [AvaloniaFact]
    public void A_button_invokes_its_command_through_the_studio()
    {
        var plugin = Plugin("hello", ButtonOf("run", "hello.run"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        Click(View<ToolBarButton>("hello:run"));

        Assert.Equal(["hello.run"], _invoked);
        Assert.Empty(_complaints);

        _bar.Invoke = _ => false;
        Click(View<ToolBarButton>("hello:run"));

        Assert.Contains(_complaints, message => message.Contains("hello.run", StringComparison.Ordinal));
    }

    /// <summary>Кнопка без команды не ставится — и говорит почему.</summary>
    [AvaloniaFact]
    public void A_button_without_a_command_is_refused_with_a_word()
    {
        var plugin = Plugin("hello", new PluginToolBarItem { Id = "mute", Icon = "arxis:Play", Title = "Тишина" });

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        Assert.Empty(_bar.Shown("right"));
        Assert.Contains(_complaints, message => message.Contains("hello:mute", StringComparison.Ordinal));
    }

    /// <summary>
    /// Меню показывает всё дерево или названную ветку — на каждом открытии заново.
    /// </summary>
    [AvaloniaFact]
    public void A_menu_shows_the_whole_tree_or_the_branch_it_named()
    {
        var tools = new StudioMenuItem("Инструменты");

        tools.Children.Add(new StudioMenuItem("Импорт…", "figma", "figma.import"));
        tools.Children.Add(new StudioMenuItem("Экспорт…", "figma", "figma.export"));

        var help = new StudioMenuItem("Справка");

        help.Children.Add(new StudioMenuItem("О студии", null, "studio.about"));

        _bar.Menu = () => [tools, help];

        var plugin = Plugin("figma",
            MenuOf("all", path: null),
            MenuOf("tools", path: "Инструменты"),
            MenuOf("lost", path: "Нет такой"));

        foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
            _bar.Add(plugin, declared);

        Assert.Equal(["Инструменты", "Справка"], Opened("figma:all").Select(item => item.Header));
        Assert.Equal(["Импорт…", "Экспорт…"], Opened("figma:tools").Select(item => item.Header));
        Assert.Empty(Opened("figma:lost"));
        Assert.Contains(_complaints, message => message.Contains("Нет такой", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пункты собранного меню доезжают до экрана.
    /// </summary>
    /// <remarks>
    /// Презентер Avalonia снимает пункты в момент своего создания, и меню,
    /// наполненное при открытии, показывалось пустым — при полном Items. Поэтому
    /// меню собирается целиком до показа, а проверяется не список, а презентер.
    /// </remarks>
    [AvaloniaFact]
    public void The_items_of_a_built_menu_reach_the_presenter()
    {
        var tools = new StudioMenuItem("Инструменты");

        tools.Children.Add(new StudioMenuItem("Импорт…", "figma", "figma.import"));
        tools.Children.Add(new StudioMenuItem("Экспорт…", "figma", "figma.export"));
        _bar.Menu = () => [tools];

        var plugin = Plugin("figma", MenuOf("tools", path: "Инструменты"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        var button = View<ToolBarButton>("figma:tools");
        var flyout = _bar.BuildMenu("figma", "tools");

        Assert.NotNull(flyout);

        flyout!.ShowAt(button);
        _window.UpdateLayout();

        Assert.Equal(2, Presented(flyout).ItemCount);

        flyout.Hide();
    }

    /// <summary>
    /// Собственные ветки студии стоят в её меню — и только в нём.
    /// </summary>
    /// <remarks>
    /// Перезагрузка плагина и раскладка манифестами не объявлены и объявлены
    /// быть не могут: их пункты зависят от того, что сейчас поднято. Строит их
    /// студия и подаёт готовыми, а в чужую ветку они не попадают — плагину там
    /// делать нечего.
    /// </remarks>
    [AvaloniaFact]
    public void The_studio_adds_its_own_branches_to_its_own_menu_only()
    {
        var tools = new StudioMenuItem("Инструменты");

        tools.Children.Add(new StudioMenuItem("Импорт…", "figma", "figma.import"));
        _bar.Menu = () => [tools];
        _bar.Extra = () => [new AxMenuItem { Header = "Раскладка" }];

        var plugin = Plugin("figma", MenuOf("tools", path: null));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(null, MenuOf("menu", path: null));

        var own = _bar.BuildMenu(null, "menu");

        Assert.NotNull(own);
        Assert.Equal(["Инструменты", null, "Раскладка"], own!.Items.Select(item => (item as MenuItem)?.Header));

        // У плагина — только его дерево: ни черты, ни чужих веток.
        Assert.Equal(["Инструменты"], _bar.BuildMenu("figma", "tools")!.Items.Select(item => ((MenuItem)item!).Header));
    }

    /// <summary>
    /// Нечего показать сверху — и черты нет.
    /// </summary>
    /// <remarks>
    /// Студия без единого плагина показывает одну раскладку, и висящая над ней
    /// черта отделяла бы её от пустоты.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_tree_leaves_the_studio_branches_without_a_separator()
    {
        _bar.Menu = () => [];
        _bar.Extra = () => [new AxMenuItem { Header = "Раскладка" }];

        _bar.Add(null, MenuOf("menu", path: null));

        var own = _bar.BuildMenu(null, "menu");

        Assert.NotNull(own);
        Assert.Equal(["Раскладка"], own!.Items.Select(item => ((MenuItem)item!).Header));
    }

    /// <summary>Лист меню зовёт свою команду той же дорогой, что кнопка.</summary>
    [AvaloniaFact]
    public void A_menu_leaf_invokes_its_command()
    {
        var tools = new StudioMenuItem("Инструменты");

        tools.Children.Add(new StudioMenuItem("Импорт…", "figma", "figma.import"));
        _bar.Menu = () => [tools];

        var plugin = Plugin("figma", MenuOf("tools", path: "Инструменты"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        var leaf = Assert.Single(Opened("figma:tools"));

        leaf.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(["figma.import"], _invoked);
    }

    /// <summary>
    /// Плагин меняет только свои элементы; чужой ключ — замечание, а не правка.
    /// </summary>
    [AvaloniaFact]
    public void A_plugin_updates_its_own_items_and_nothing_else()
    {
        var hello = Plugin("hello", ButtonOf("run", "hello.run"));
        var friend = Plugin("friend", ButtonOf("run", "friend.run"));

        _bar.Add(hello, hello.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(friend, friend.Manifest!.Contributions.ToolBar[0]);

        IStudioToolBar own = new PluginToolBar(_bar, "hello");

        own.Update("run", isEnabled: false, isChecked: true);

        Assert.False(View<ToolBarButton>("hello:run").IsEnabled);
        Assert.True(View<ToolBarButton>("hello:run").IsChecked);
        Assert.True(View<ToolBarButton>("friend:run").IsEnabled);
        Assert.False(View<ToolBarButton>("friend:run").IsChecked);

        own.Update("cheer", isEnabled: false);

        Assert.Contains(_complaints, message => message.Contains("hello:cheer", StringComparison.Ordinal));
    }

    /// <summary>Включённым бывает только кнопка: меню и свой контрол отвечают замечанием.</summary>
    [AvaloniaFact]
    public void Only_a_button_can_be_checked()
    {
        var plugin = Plugin("hello", MenuOf("menu", path: null), CustomOf("strip"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(plugin, plugin.Manifest.Contributions.ToolBar[1], new Border());

        _bar.Update("hello", "menu", isChecked: true);
        _bar.Update("hello", "strip", isChecked: true);

        Assert.False(View<ToolBarButton>("hello:menu").IsChecked);
        Assert.Equal(2, _complaints.Count(message => message.Contains("не кнопка", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Состояние, заданное до постройки своего контрола, применяется, когда он приходит.
    /// </summary>
    /// <remarks>
    /// Модуль объявлен раньше, чем поднят, и может выключить свой элемент из
    /// <c>Activate</c> — контрола в этот момент ещё нет, а слово уже сказано.
    /// </remarks>
    [AvaloniaFact]
    public void State_said_before_a_custom_control_is_built_applies_when_it_arrives()
    {
        var plugin = Plugin("hello", CustomOf("strip"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        Assert.Empty(_bar.Shown("right"));

        _bar.Update("hello", "strip", isEnabled: false);

        Assert.Empty(_complaints);

        var content = new Border();

        _bar.Add(plugin, plugin.Manifest.Contributions.ToolBar[0], content);

        Assert.Equal(["hello:strip"], _bar.Shown("right"));
        Assert.False(content.IsEnabled);
    }

    /// <summary>Скрытый элемент места не занимает — и разделителя за собой не оставляет.</summary>
    [AvaloniaFact]
    public void A_hidden_item_leaves_the_strip_without_a_trace()
    {
        var hello = Plugin("hello", ButtonOf("run", "hello.run"));
        var friend = Plugin("friend", ButtonOf("cheer", "friend.cheer"));

        _bar.Add(hello, hello.Manifest!.Contributions.ToolBar[0]);
        _bar.Add(friend, friend.Manifest!.Contributions.ToolBar[0]);

        _bar.Update("hello", "run", isVisible: false);

        Assert.Equal(["friend:cheer"], _bar.Shown("right"));
        Assert.Empty(_right.Children.OfType<AxDivider>());

        _bar.Update("hello", "run", isVisible: true);

        Assert.Equal(["friend:cheer", "hello:run"], _bar.Shown("right"));
    }

    /// <summary>
    /// Слово из фоновой задачи доходит до полосы в потоке интерфейса.
    /// </summary>
    /// <remarks>
    /// Плагин говорит «готово» из своей задачи — из потока пула, где контролы
    /// трогать нельзя. Полоса откладывает вызов сама, автору помнить об этом
    /// не нужно.
    /// </remarks>
    [AvaloniaFact]
    public void An_update_from_a_background_thread_lands_on_the_ui_thread()
    {
        var plugin = Plugin("hello", ButtonOf("run", "hello.run"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        // Сам вызов — из потока пула. Тронь он контрол там же, Avalonia отказала
        // бы ему исключением, и ожидание принесло бы его сюда. Когда именно
        // отложенное слово дойдёт до полосы, не проверяется: это дело
        // диспетчера, и другие тесты уже успевают его расшевелить.
        Task.Run(() => _bar.Update("hello", "run", isChecked: true)).Wait();

        Dispatcher.UIThread.RunJobs();

        Assert.True(View<ToolBarButton>("hello:run").IsChecked);
    }

    /// <summary>
    /// Подпись-ключ переводится при смене языка — студией, не автором.
    /// </summary>
    [AvaloniaFact]
    public void A_title_key_follows_the_language()
    {
        var module = Plugin("sample", builtIn: true, ButtonOf("about", "sample.about", title: "%menu.tools%"));

        _bar.Add(module, module.Manifest!.Contributions.ToolBar[0]);

        var button = View<ToolBarButton>("sample:about");

        Localizer.Instance.SetLanguage("ru");
        var russian = Localizer.Instance["menu.tools"];

        Assert.Equal(russian, ToolTip.GetTip(button));

        // Имя для средств доступности идёт той же дорогой: человек, который
        // кнопку не видит, читает её тем же словом и на том же языке.
        Assert.Equal(russian, AutomationProperties.GetName(button));

        Localizer.Instance.SetLanguage("en");

        Assert.Equal(Localizer.Instance["menu.tools"], ToolTip.GetTip(button));
        Assert.Equal(Localizer.Instance["menu.tools"], AutomationProperties.GetName(button));
        Assert.NotEqual(russian, ToolTip.GetTip(button));
    }

    /// <summary>Фабрика контекста выдаёт полосу именным фасадом.</summary>
    [AvaloniaFact]
    public void The_facade_from_the_factory_is_stamped_with_its_owner()
    {
        var plugin = Plugin("hello", ButtonOf("run", "hello.run"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        var factory = new StudioContextFactory(new StudioLog(), new StudioCommands(), null, toolbar: _bar);
        var toolbar = factory.Create(plugin).GetService<IStudioToolBar>();

        Assert.NotNull(toolbar);

        toolbar!.Update("run", isChecked: true);

        Assert.True(View<ToolBarButton>("hello:run").IsChecked);
    }

    /// <summary>
    /// Значок, который не разобрался, оставляет текстовую кнопку и замечание.
    /// </summary>
    /// <remarks>
    /// Кнопку это не отменяет: подпись у неё есть, и без значка она читается —
    /// а вот молча подменять значок чужим было бы хуже пропажи.
    /// </remarks>
    [AvaloniaFact]
    public void An_icon_that_does_not_resolve_leaves_a_text_button_and_a_complaint()
    {
        var plugin = Plugin("hello", ButtonOf("named", "hello.run", icon: "arxis:Nope", title: "Run"));

        _bar.Add(plugin, plugin.Manifest!.Contributions.ToolBar[0]);

        var named = View<ToolBarButton>("hello:named");

        Assert.Contains("ghost", named.Classes);
        Assert.Equal("Run", named.Content);

        Assert.Single(_complaints, message => message.Contains("arxis:Nope", StringComparison.Ordinal));
    }

    /// <summary>Элемент без подписи не ставится — и говорит почему.</summary>
    /// <remarks>
    /// Подпись — это и подсказка, и имя для средств доступности. Без неё в
    /// полосе остаётся значок 24×24, про который нельзя узнать ничего: ни
    /// наведя курсор, ни программой чтения с экрана. Раньше такой элемент
    /// вставал молча — со значком вопроса, если не было и значка.
    /// </remarks>
    [AvaloniaFact]
    public void An_item_without_a_title_is_refused_with_a_word()
    {
        var plugin = Plugin("hello",
            ButtonOf("run", "hello.run", title: null),
            MenuOf("more", null, title: null));

        foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
            _bar.Add(plugin, declared);

        Assert.Empty(_bar.Shown("right"));
        Assert.Equal(2, _complaints.Count(message => message.Contains("подписи", StringComparison.Ordinal)));
    }

    /// <summary>Вид элемента по ключу — с той ленты, где он стоит.</summary>
    private T View<T>(string key) where T : Control
    {
        foreach (var strip in new[] { _left, _center, _right })
        {
            if (Find(strip, key) is T typed)
                return typed;
        }

        throw new Xunit.Sdk.XunitException($"в полосе нет элемента {key}");
    }

    private IReadOnlyList<string> Keys(ToolBarStrip strip) =>
        strip == _left ? _bar.Shown("left") : strip == _center ? _bar.Shown("center") : _bar.Shown("right");

    private Control? Find(ToolBarStrip strip, string key)
    {
        var views = strip.Children.OfType<Control>().Where(child => child is not AxDivider).ToList();
        var index = Keys(strip).ToList().IndexOf(key);

        return index >= 0 ? views[index] : null;
    }

    /// <summary>Пункты меню элемента — то, что соберёт щелчок.</summary>
    private IReadOnlyList<MenuItem> Opened(string key)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        var owner = key[..colon];
        var flyout = _bar.BuildMenu(owner == StudioToolBar.Studio ? null : owner, key[(colon + 1)..]);

        Assert.NotNull(flyout);

        return flyout!.Items.OfType<MenuItem>().ToList();
    }

    /// <summary>
    /// Презентер показанного меню: попап в headless-окне из дерева не виден.
    /// </summary>
    /// <remarks>
    /// Достаётся отражением по членам самого меню — единственная дорога к тому,
    /// что человек увидит на экране. Именно этого ради и проверка: пункты,
    /// добавленные в меню после создания презентера, до экрана не доезжают, и
    /// тест на одни лишь Items этого не заметил бы.
    /// </remarks>
    private static MenuFlyoutPresenter Presented(FlyoutBase flyout)
    {
        for (var type = flyout.GetType(); type is not null; type = type.BaseType)
        {
            var found = type
                .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Where(field => typeof(Popup).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(flyout))
                .Concat(type
                    .GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(property => typeof(Popup).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0)
                    .Select(property => property.GetValue(flyout)))
                .OfType<Popup>()
                .FirstOrDefault();

            if (found?.Child is MenuFlyoutPresenter presenter)
                return presenter;
        }

        throw new Xunit.Sdk.XunitException("у показанного меню нет презентера");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static InstalledPlugin Plugin(string id, params PluginToolBarItem[] items) => Plugin(id, false, items);

    private static InstalledPlugin Plugin(string id, bool builtIn, params PluginToolBarItem[] items)
    {
        var manifest = new PluginManifest { Id = id, Name = id };

        foreach (var item in items)
            manifest.Contributions.ToolBar.Add(item);

        return new InstalledPlugin(Path.Combine(Path.GetTempPath(), $"arxis-bar-{id}"), manifest, null, IsEnabled: true, IsBuiltIn: builtIn);
    }

    private static PluginToolBarItem ButtonOf(string id, string command, string? icon = "arxis:Play", string? title = "Кнопка", string slot = "right") =>
        new() { Id = id, Command = command, Icon = icon, Title = title, Slot = slot };

    private static PluginToolBarItem MenuOf(string id, string? path, string slot = "right", string? title = "Меню") =>
        new() { Id = id, Kind = "menu", Menu = path, Icon = "arxis:MoreHorizontal", Title = title, Slot = slot };

    private static PluginToolBarItem CustomOf(string id, string slot = "right") =>
        new() { Id = id, Kind = "custom", Slot = slot };
}
