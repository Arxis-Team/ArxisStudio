using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перезапуск глазами окон: менеджер плагинов, окно настроек и Welcome.
/// </summary>
/// <remarks>
/// Вопрос и сам перезапуск подменены — настоящий закрыл бы процесс тестов, — а окна настоящие:
/// проверяется то, что человек видит и нажимает, и то, что окно отдаёт перезапуску и получает от
/// него обратно.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsRestartTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly SettingsHarness _harness = new();
    private readonly List<Window> _asked = [];
    private readonly List<string> _performed = [];

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Плагин, ждущий перезапуска, говорит только это — в подробностях, значком в списке и диктору.
    /// </summary>
    /// <remarks>
    /// Запись 284 называла здесь владельцев свойств Avalonia. Человеку это ни к чему: ему нужно
    /// знать, что изменения применит перезапуск, и где его нажать. Причина — в журнале.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_plugin_awaiting_a_restart_says_so_without_technical_words()
    {
        _harness.Install("arxis.one", "Первый");
        _harness.Plugins.Await("arxis.one", "Probe.Held заводит свои свойства и события Avalonia — Badge");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var card = Page(settings).Cards.Single();
        var row = Row(settings, card);
        var required = Localizer.Instance["restart.required"];
        var texts = Texts(settings);

        Assert.True(card.NeedsRestart);
        Assert.Contains(required, texts);
        Assert.DoesNotContain(texts, text => text?.Contains("Badge", StringComparison.Ordinal) == true);
        Assert.Contains(
            settings.GetVisualDescendants().OfType<AxLink>(),
            link => link.IsEffectivelyVisible && Equals(link.Content, Localizer.Instance["restart.now"]));

        var marker = row.GetVisualDescendants().OfType<AxIcon>().Single(icon => ReferenceEquals(icon.Data, AxIcons.Refresh));

        Assert.True(marker.IsEffectivelyVisible, "в строке списка нет знака перезапуска");
        Assert.Equal(required, ToolTip.GetTip(marker));
        Assert.Equal(required, AutomationProperties.GetItemStatus(row));

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// «Перезапустить» у плагина сперва записывает несохранённое — не применяя, — потом перезапускает.
    /// </summary>
    [AvaloniaFact]
    public async Task The_restart_link_saves_what_is_unsaved_and_then_restarts()
    {
        _harness.Install("arxis.one", "Первый");
        _harness.Plugins.Await("arxis.one", "прежняя копия осталась в памяти");

        var restart = Restart(agree: true);
        var (owner, settings, shown) = _harness.Open(page: "studio.plugins", restart: restart);
        var page = Page(settings);

        await page.ToggleAsync(page.Cards.Single());

        Assert.True(Model(settings).HasChanges);

        restart.Perform = () =>
        {
            _performed.Add(Page(settings).Cards.Single().Plugin.IsEnabled ? "включён" : "выключен");
            return Task.FromResult(true);
        };

        Link(settings, Localizer.Instance["restart.now"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => _performed.Count > 0);

        Assert.Equal(["выключен"], _performed);
        Assert.False(Model(settings).HasChanges, "перезапуск начался, а несохранённое так и не записалось");

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Удаление плагина спрашивает о перезапуске сразу и поверх настроек; «Не сейчас» ничего не делает.
    /// </summary>
    [AvaloniaFact]
    public async Task Removing_a_plugin_asks_about_the_restart_over_the_settings()
    {
        _harness.Install("arxis.one", "Первый");

        var restart = Restart(agree: false);
        var (owner, settings, shown) = _harness.Open(page: "studio.plugins", restart: restart);
        var card = Page(settings).Cards.Single();

        _harness.Plugins.Await("arxis.one", "прежняя копия осталась в памяти");

        Row(settings, card).Focus(NavigationMethod.Tab);
        settings.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        Dispatcher.UIThread.RunJobs();

        var question = Assert.Single(settings.OwnedWindows.OfType<AxDialog>());

        question.GetVisualDescendants()
            .OfType<AxButton>()
            .Single(button => Equals(button.Content, Localizer.Instance["plugins.remove"]))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => _asked.Count > 0);

        Assert.Equal([settings], _asked);
        Assert.Empty(_performed);
        Assert.Empty(Page(settings).Cards);
        Assert.EndsWith(Localizer.Instance["restart.required"], Page(settings).Status, StringComparison.Ordinal);

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Окно, закрываемое ради перезапуска, не спрашивает о несохранённом и не отменяет закрытие.
    /// </summary>
    /// <remarks>
    /// Крестик отменяет первое закрытие всегда — чтобы спросить; закрытие студии, наткнувшись на
    /// это окно, отменилось бы вместе с ним.
    /// </remarks>
    [AvaloniaFact]
    public async Task Closing_for_a_restart_asks_nothing()
    {
        _harness.Install("arxis.one", "Первый");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = Page(settings);

        await page.ToggleAsync(page.Cards.Single());

        settings.CloseForRestart();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));
        Assert.Empty(owner.OwnedWindows);

        owner.Close();
    }

    /// <summary>
    /// Окно настроек возвращается тем, каким его застал перезапуск: раздел, поиск, плагин, группы, место.
    /// </summary>
    [AvaloniaFact]
    public async Task The_settings_come_back_as_the_restart_found_them()
    {
        _harness.Install("arxis.one", "Первый");
        _harness.Install("arxis.two", "Второй");

        var restore = new SettingsSession
        {
            Page = "studio.plugins",
            Search = "arxis",
            Plugin = "arxis.two",
            Folded = new Dictionary<string, bool> { ["external"] = false },
            Window = new StudioPlacement(123, 77, 900, 600, 1, Maximized: false),
        };

        var (owner, settings, shown) = _harness.Open(restore: restore);
        var page = Page(settings);

        Assert.Equal("studio.plugins", Model(settings).Page?.Id);
        Assert.Equal("arxis", Model(settings).Search);
        Assert.Equal("arxis.two", page.Card?.Plugin.Id);
        Assert.False(page.Groups.Single(group => group.Key == "external").IsExpanded, "свёрнутая группа вернулась раскрытой");
        Assert.Equal(new PixelPoint(123, 77), settings.Position);
        Assert.Equal(900, settings.Width);

        var snapshot = settings.Snapshot();

        Assert.Equal("studio.plugins", snapshot.Page);
        Assert.Equal("arxis", snapshot.Search);
        Assert.Equal("arxis.two", snapshot.Plugin);
        Assert.False(snapshot.Folded["external"]);
        Assert.Equal(new PixelPoint(123, 77), new PixelPoint(snapshot.Window!.X, snapshot.Window.Y));

        await Close(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Welcome возвращается с разделом, поиском недавних и окном настроек, открытым той же дверью.
    /// </summary>
    [AvaloniaFact]
    public async Task Welcome_comes_back_with_its_section_filter_and_door()
    {
        var welcome = _harness.Welcome();

        welcome.Show();
        Dispatcher.UIThread.RunJobs();

        welcome.Resume(
            new WelcomeSession(WelcomeSection.Learn, "hello", Plugins: true),
            new SettingsSession { Page = "studio.plugins" });
        Dispatcher.UIThread.RunJobs();

        var model = (WelcomeViewModel)welcome.DataContext!;
        var settings = Assert.Single(welcome.OwnedWindows.OfType<SettingsWindow>());

        Assert.Equal(WelcomeSection.Learn, model.Section);
        Assert.Equal("hello", model.ProjectFilter);
        Assert.True(model.IsPluginsOpen, "отмечена не та дверь, которой открыли настройки");
        Assert.Equal("studio.plugins", Model(settings).Page?.Id);
        Assert.Equal(new WelcomeSession(WelcomeSection.Learn, "hello", true), welcome.Snapshot());

        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !model.IsPluginsOpen);

        welcome.Close();
    }

    /// <summary>
    /// О перезапуске спрашивают, когда окно настроек закрылось, — и хозяин вопроса жив.
    /// </summary>
    /// <remarks>
    /// Закрытое окно обнуляет своего хозяина, и вопрос, заданный от его имени, остался бы без
    /// владельца: модальный диалог с пустым хозяином роняет студию из <c>async void</c>.
    /// Спрашивает тот, кто окно открывал.
    /// </remarks>
    [AvaloniaFact]
    public async Task After_the_settings_close_the_question_comes_from_a_live_owner()
    {
        var restart = Restart(agree: false);
        var (welcome, settings) = _harness.OpenFromWelcome("welcome.nav.plugins", keys: null, restart);

        _harness.Plugins.Await("arxis.one", "прежняя копия осталась в памяти");

        settings.Save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => _asked.Count > 0);

        Assert.Equal([welcome], _asked);
        Assert.Empty(welcome.OwnedWindows);

        welcome.Close();
    }

    private StudioRestart Restart(bool agree) => new(_harness.Plugins)
    {
        Ask = owner =>
        {
            _asked.Add(owner);
            return Task.FromResult(agree);
        },
        Perform = () =>
        {
            _performed.Add("перезапуск");
            return Task.FromResult(true);
        },
    };

    private static SettingsViewModel Model(SettingsWindow settings) => (SettingsViewModel)settings.DataContext!;

    private static PluginsPage Page(SettingsWindow settings) =>
        Assert.IsType<PluginsPage>(Model(settings).Page);

    private static AxListBoxItem Row(SettingsWindow settings, PluginCard card) =>
        settings.GetVisualDescendants().OfType<AxListBoxItem>().Single(row => ReferenceEquals(row.DataContext, card));

    private static AxLink Link(SettingsWindow settings, string content) =>
        settings.GetVisualDescendants().OfType<AxLink>().Single(link => link.IsEffectivelyVisible && Equals(link.Content, content));

    private static List<string?> Texts(SettingsWindow settings) =>
        [.. settings.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)];

    /// <summary>Прогоняет диспетчер, пока условие не станет истинным, — но не дольше терпения.</summary>
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Assert.True(condition(), "не дождались");
    }

    private static async Task Close(SettingsWindow settings, Task shown)
    {
        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));
    }
}
