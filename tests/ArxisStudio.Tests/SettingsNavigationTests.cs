using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.SettingsHarness;

namespace ArxisStudio.Tests;

/// <summary>
/// Шапка страницы настроек: путь, стрелки по пройденным страницам, «Сбросить» и точка у раздела
/// с несохранённым.
/// </summary>
/// <remarks>
/// Всё это есть в шапке настроек Rider, и нужно оно стало с тех пор, как страницы ведут друг на
/// друга: ветка — на страницу ребёнка, плагин — на свои настройки. Уйдя по ссылке, человек должен
/// вернуться туда, откуда пришёл, а не искать это место в дереве заново.
/// <para>
/// Очередь общая с остальными: страница оформления читает словари, а <c>Localizer</c> один на
/// процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsNavigationTests : IDisposable
{
    private readonly string _home = TempFolder.Create("settings-way");
    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();

        TempFolder.Erase(_home, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>«Назад» возвращает на страницу, открытую перед этой, и дальше — по порядку.</summary>
    [Fact]
    public void Going_back_returns_to_the_page_that_was_open_before()
    {
        var model = Model();

        Choose(model, "extension:arxis.terminal");
        Choose(model, "extension:arxis.figma");

        model.Back();
        Assert.Equal("extension:arxis.terminal", model.Page?.Id);

        model.Back();
        Assert.Equal("studio.appearance", model.Page?.Id);
        Assert.False(model.CanGoBack, "с первой страницы идти назад некуда");

        model.Forward();
        Assert.Equal("extension:arxis.terminal", model.Page?.Id);
        Assert.True(model.CanGoForward, "дорога вперёд пропала на первом же шаге");
    }

    /// <summary>Новый выбор после «назад» стирает дорогу вперёд — как в браузере и в Rider.</summary>
    [Fact]
    public void Choosing_a_page_after_going_back_drops_the_way_forward()
    {
        var model = Model();

        Choose(model, "extension:arxis.terminal");
        Choose(model, "extension:arxis.figma");
        model.Back();

        Choose(model, "studio.appearance");

        Assert.False(model.CanGoForward, "после нового выбора вперёд вела старая дорога");
    }

    /// <summary>
    /// Страница, которую окно выбрало само после поиска, в историю не ложится.
    /// </summary>
    /// <remarks>
    /// Поиск прячет выбранную страницу и ставит справа первую найденную. Человек туда не ходил:
    /// «назад» после такого поиска должен вести туда, откуда он пришёл, а не на страницу, которую
    /// окно показало ему на миг.
    /// </remarks>
    [Fact]
    public void A_search_that_reselects_a_page_leaves_no_trace_in_history()
    {
        var model = Model();

        Choose(model, "extension:arxis.terminal");

        model.Search = "Figma";
        Assert.Equal("extension:arxis.figma", model.Page?.Id);

        model.Back();

        Assert.Equal("studio.appearance", model.Page?.Id);
        Assert.False(model.CanGoBack, "поиск записал в историю страницу, куда человек не ходил");
    }

    /// <summary>
    /// Ссылка на страницу, спрятанную поиском, очищает поиск и ведёт на неё, запомнив, откуда шли.
    /// </summary>
    [Fact]
    public void Opening_a_page_hidden_by_the_search_clears_the_search()
    {
        var model = Model();

        model.Search = "Figma";
        model.Open("studio.appearance");

        Assert.Equal("studio.appearance", model.Page?.Id);
        Assert.Equal(string.Empty, model.Search);
        Assert.True(model.CanGoBack, "переход по ссылке не запомнил, откуда шли");
    }

    /// <summary>Путь в шапке — от корня дерева до открытой страницы.</summary>
    [Fact]
    public void The_path_runs_from_the_root_to_the_open_page()
    {
        var model = Model();

        Choose(model, "extension:arxis.figma");

        Assert.Equal(
            [Localizer.Instance["settings.plugins"], "Figma"],
            model.Path.Select(node => node.ToString()));
    }

    /// <summary>
    /// Несохранённая правка отмечает и свою страницу, и ветку над ней — и сама говорит об этом дереву.
    /// </summary>
    /// <remarks>
    /// Узлы дерева пересобираются на каждый запрос поиска, и помнить отметку им нечем: они
    /// спрашивают страницу. Спросить заново их просит окно, узнав о правке от страницы, — иначе точка
    /// появилась бы только после следующего поиска.
    /// </remarks>
    [Fact]
    public async Task An_unsaved_edit_marks_its_page_and_its_branch()
    {
        var model = Model();
        var branch = model.Nodes.Last();
        var terminal = branch.Children.First();
        var told = new List<string?>();

        terminal.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        Row(model, "terminal.fontSize").Text = "20";

        Assert.True(terminal.IsModified, "правленая страница не отмечена");
        Assert.True(branch.IsModified, "ветка над правленой страницей не отмечена");
        Assert.False(model.Nodes[0].IsModified, "отмечена страница, которую не правили");
        Assert.Contains(nameof(SettingsNode.IsModified), told);
        Assert.Equal(Localizer.Instance["settings.modified"], terminal.ModifiedStatus);

        Assert.True(await model.SaveAsync());

        Assert.False(terminal.IsModified, "записанное осталось отмеченным");
        Assert.Equal(string.Empty, terminal.ModifiedStatus);
    }

    /// <summary>«Сбросить» забывает правки открытой страницы и не трогает остальных.</summary>
    [Fact]
    public void Resetting_a_page_forgets_only_its_own_edits()
    {
        var model = Model();
        var fontSize = Row(model, "terminal.fontSize");
        var token = Row(model, "figma.token");

        fontSize.Text = "20";
        token.Text = "секрет";

        model.Open("extension:arxis.terminal");

        Assert.True(model.CanReset, "у правленой страницы нечего сбросить");

        model.Reset();

        Assert.Equal("13", fontSize.Text);
        Assert.False(model.CanReset, "после сброса осталось что сбрасывать");
        Assert.Equal("секрет", token.Text);
        Assert.True(model.HasChanges, "сброс одной страницы забыл правку другой");
    }

    /// <summary>Шапка называет путь к странице, а сегмент пути ведёт на свою страницу.</summary>
    [AvaloniaFact]
    public async Task The_header_names_the_way_to_the_page()
    {
        var (owner, settings, shown) = _harness.Open(page: "extension:arxis.terminal");
        var model = (SettingsViewModel)settings.DataContext!;

        var segments = settings.Crumbs.GetVisualDescendants().OfType<AxBreadcrumbItem>().ToList();

        Assert.Equal(
            [Localizer.Instance["settings.plugins"], "Терминал"],
            segments.Select(segment => segment.DataContext?.ToString()));

        segments[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("studio.extensions", model.Page?.Id);
        Assert.True(settings.Back.IsEnabled, "после перехода по пути стрелка «назад» выключена");

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>Alt+← и Alt+→ ходят по истории — и из дерева разделов, где стоит каретка после выбора.</summary>
    /// <remarks>
    /// Строка дерева берёт стрелки себе при любых модификаторах: Left сворачивает её или уводит к
    /// родителю. Окно слушает сочетание на спуске, иначе из дерева «назад» не доходил бы — а каретка
    /// стоит там всякий раз, как раздел выбрали мышью.
    /// </remarks>
    [AvaloniaFact]
    public async Task Alt_arrows_walk_the_history_even_from_the_tree()
    {
        var (owner, settings, shown) = _harness.Open();
        var model = (SettingsViewModel)settings.DataContext!;

        model.Open("extension:arxis.terminal");
        Dispatcher.UIThread.RunJobs();

        settings.GetVisualDescendants()
            .OfType<AxTreeViewItem>()
            .Single(item => item.DataContext is SettingsNode { Page.Id: "extension:arxis.terminal" })
            .Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        settings.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("studio.appearance", model.Page?.Id);

        settings.KeyPress(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("extension:arxis.terminal", model.Page?.Id);

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// «Сбросить» в шапке нажимается мышью и виден, только пока на странице есть что сбросить.
    /// </summary>
    /// <remarks>
    /// Ссылка темы лежала подписью внутри кольца фокуса, которое мышь не ловит, — и щелчок уходил
    /// мимо неё. Поэтому здесь нажатие настоящее, указателем, а не событие, поднятое в обход.
    /// </remarks>
    [AvaloniaFact]
    public async Task Reset_in_the_header_takes_a_click_and_shows_only_with_something_to_reset()
    {
        var (owner, settings, shown) = _harness.Open(page: "extension:arxis.terminal");
        var model = (SettingsViewModel)settings.DataContext!;
        var row = Row(model, "terminal.fontSize");

        Assert.False(settings.Reset.IsEffectivelyVisible, "«Сбросить» виден на странице без правок");
        Assert.Empty(Dots(settings));

        row.Text = "20";
        Dispatcher.UIThread.RunJobs();
        settings.UpdateLayout();

        Assert.True(settings.Reset.IsEffectivelyVisible, "у правленой страницы нет «Сбросить»");
        Assert.Equal(["studio.extensions", "extension:arxis.terminal"], Dots(settings));

        var at = settings.Reset.TranslatePoint(
            new Point(settings.Reset.Bounds.Width / 2, settings.Reset.Bounds.Height / 2), settings)!.Value;

        settings.MouseDown(at, MouseButton.Left);
        settings.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("13", row.Text);
        Assert.False(model.HasChanges, "щелчок по «Сбросить» правку не забыл");
        Assert.Empty(Dots(settings));

        // «Сбросить» спрятался вместе с правкой — каретка, стоявшая на нём, не пропадает, а встаёт
        // на раздел, откуда работают с окном.
        Assert.Equal("extension:arxis.terminal", CaretSection(settings));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// «Сбросить» на оформлении возвращает и сами сегменты, а не только значения за ними.
    /// </summary>
    /// <remarks>
    /// Страница остаётся на экране после сброса, и её контролы обязаны показать вернувшееся. Прежде
    /// откат шёл только с закрытием окна, и тема возвращалась, а сегмент оставался на прежнем выборе.
    /// <para>
    /// Сама правка говорит окну о себе так же, как правка строки расширения: у страницы точка, в шапке
    /// «Сбросить». Тема применяется сразу, и без этого извещения окно показывало бы новую тему как
    /// сохранённую — сбрасывать её было бы нечем.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task Resetting_the_appearance_brings_its_choices_back_on_screen()
    {
        var (owner, settings, shown) = _harness.Open();
        var model = (SettingsViewModel)settings.DataContext!;
        var appearance = Assert.IsType<AppearancePage>(model.Page);
        var theme = Segments(settings, "settings.theme");
        var density = Segments(settings, "settings.density");
        var themeAtOpen = theme.SelectedIndex;
        var densityAtOpen = density.SelectedIndex;

        appearance.ThemeIndex = 1 - themeAtOpen;
        appearance.DensityIndex = (densityAtOpen + 1) % 3;
        Dispatcher.UIThread.RunJobs();
        settings.UpdateLayout();

        Assert.True(settings.Reset.IsEffectivelyVisible, "правка оформления не показала «Сбросить»");
        Assert.Equal([appearance.Id], Dots(settings));

        model.Reset();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(themeAtOpen, theme.SelectedIndex);
        Assert.Equal(densityAtOpen, density.SelectedIndex);

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Ссылка ветки ведёт на страницу ребёнка и оставляет каретку на его разделе, а не ни на чём.
    /// </summary>
    /// <remarks>
    /// Ссылка уходит вместе со страницей, на которой стояла, и каретка, взятая ею при нажатии,
    /// оставалась бы в пустоте: следующая клавиша не доставалась бы никому.
    /// </remarks>
    [AvaloniaFact]
    public async Task Following_a_link_keeps_the_caret_on_the_chosen_section()
    {
        var (owner, settings, shown) = _harness.Open(page: "studio.extensions");
        var model = (SettingsViewModel)settings.DataContext!;
        var link = settings.GetVisualDescendants().OfType<AxLink>().Single(candidate => Equals(candidate.Content, "Терминал"));

        link.Focus(NavigationMethod.Tab);
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("extension:arxis.terminal", model.Page?.Id);
        Assert.Equal("extension:arxis.terminal", CaretSection(settings));
        Assert.True(model.CanGoBack, "переход по ссылке не запомнил ветку");

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Alt+← с поля страницы: страница уходит вместе с полем, а каретка встаёт на выбранный раздел.
    /// </summary>
    /// <remarks>
    /// Найдено живой проверкой: сочетание уводило страницу, на контроле которой стояла каретка, и
    /// следующая клавиша не доставалась никому.
    /// </remarks>
    [AvaloniaFact]
    public async Task Going_back_from_a_field_of_the_page_keeps_the_caret()
    {
        var (owner, settings, shown) = _harness.Open();
        var model = (SettingsViewModel)settings.DataContext!;

        model.Open("extension:arxis.terminal");
        Dispatcher.UIThread.RunJobs();

        settings.GetVisualDescendants()
            .OfType<AxTextBox>()
            .First(box => box.IsEffectivelyVisible && box.DataContext is ViewModels.PluginSettingRow)
            .Focus();
        Dispatcher.UIThread.RunJobs();

        settings.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("studio.appearance", model.Page?.Id);
        Assert.Equal("studio.appearance", CaretSection(settings));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>Раздел дерева, на котором стоит каретка; null — она не на разделе.</summary>
    private static string? CaretSection(SettingsWindow settings) =>
        (settings.FocusManager?.GetFocusedElement() as AxTreeViewItem)?.DataContext is SettingsNode node ? node.Page.Id : null;

    /// <summary>Сегменты оформления по ключу подписи строки.</summary>
    private static AxSegmentedControl Segments(SettingsWindow settings, string label) =>
        settings.GetVisualDescendants()
            .OfType<AxSegmentedControl>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == Localizer.Instance[label]);

    /// <summary>Разделы дерева, у которых сейчас видна точка несохранённого.</summary>
    /// <remarks>Точка ребёнка лежит и внутри строки ветки — считается только своя.</remarks>
    private static List<string> Dots(SettingsWindow settings) =>
    [
        .. settings.GetVisualDescendants()
            .OfType<AxTreeViewItem>()
            .Where(item => item.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>()
                .Any(dot => dot.IsEffectivelyVisible && dot.FindAncestorOfType<AxTreeViewItem>() == item))
            .Select(item => ((SettingsNode)item.DataContext!).Page.Id),
    ];

    /// <summary>Выбирает страницу так, как выбирает её дерево, — через выбранный узел.</summary>
    private static void Choose(SettingsViewModel model, string pageId) =>
        model.Selected = Flat(model.Nodes).Single(node => node.Page.Id == pageId);

    private static IEnumerable<SettingsNode> Flat(IEnumerable<SettingsNode> nodes) =>
        nodes.SelectMany(node => Flat(node.Children).Prepend(node));

    private static ViewModels.PluginSettingRow Row(SettingsViewModel model, string key) =>
        Flat(model.Nodes)
            .Select(node => node.Page)
            .OfType<ExtensionPage>()
            .SelectMany(page => page.Rows)
            .Single(row => row.Key == key);

    /// <summary>Окно с оформлением и двумя страницами расширений: модуля и плагина.</summary>
    private SettingsViewModel Model() =>
        new(
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            new PluginSettingsStore(userFile: Path.Combine(_home, "plugin-settings.json")),
            [Module(), Plugin()]);

    private static InstalledPlugin Module() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13) },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    private InstalledPlugin Plugin() => new(
        _home,
        new PluginManifest
        {
            Id = "arxis.figma",
            Name = "Figma",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("figma.token", "string", "user", "Токен", "") },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: false);
}
