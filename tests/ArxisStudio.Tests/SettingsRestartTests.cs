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
using static ArxisStudio.Tests.SettingsHarness;

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
        var card = PluginsOf(settings).Cards.Single();
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

        await CloseAsync(settings, shown);
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
        var page = PluginsOf(settings);

        await page.ToggleAsync(page.Cards.Single());

        Assert.True(Model(settings).HasChanges);

        restart.Perform = () =>
        {
            _performed.Add(PluginsOf(settings).Cards.Single().Plugin.IsEnabled ? "включён" : "выключен");
            return Task.FromResult(true);
        };

        Link(settings, Localizer.Instance["restart.now"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => _performed.Count > 0);

        Assert.Equal(["выключен"], _performed);
        Assert.False(Model(settings).HasChanges, "перезапуск начался, а несохранённое так и не записалось");

        await CloseAsync(settings, shown);
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
        var card = PluginsOf(settings).Cards.Single();

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
        Assert.Empty(PluginsOf(settings).Cards);
        Assert.EndsWith(Localizer.Instance["restart.required"], PluginsOf(settings).Status, StringComparison.Ordinal);

        await CloseAsync(settings, shown);
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
        var page = PluginsOf(settings);

        await page.ToggleAsync(page.Cards.Single());

        settings.CloseForRestart();
        Dispatcher.UIThread.RunJobs();

        await ClosedAsync(shown);
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
        var page = PluginsOf(settings);

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

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Развёрнутое окно настроек отдаёт перезапуску обычные границы и признак разворота, а не экран.
    /// </summary>
    /// <remarks>
    /// Окно снимало место тем, что видно сейчас: развёрнутое — размером экрана. Вернувшись
    /// развёрнутым, оно и после снятого разворота оставалось во весь экран. Главное окно помнило
    /// обычные границы и прежде; правило теперь одно у обоих. Система сообщает новые место и размер
    /// раньше нового вида окна — тест делает так же.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_maximized_settings_window_hands_the_restart_its_normal_bounds()
    {
        var (owner, settings, shown) = _harness.Open();
        var size = settings.ClientSize;
        var position = settings.Position;

        settings.Position = new PixelPoint(0, 0);
        settings.Width = 1900;
        settings.Height = 1000;
        settings.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();

        var placement = settings.Snapshot().Window!;

        Assert.True(placement.Maximized, "разворот не записан");
        Assert.Equal((size.Width, size.Height), (placement.Width, placement.Height));
        Assert.Equal(position, new PixelPoint(placement.X, placement.Y));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Сдвинутое, а потом растянутое окно отдаёт перезапуску то место и тот размер, где стоит.
    /// </summary>
    /// <remarks>
    /// Хранитель слушает место и размер порознь, и проверяются они порознь: сдвиг без растяжки и
    /// растяжка без сдвига.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_moved_and_resized_settings_window_hands_the_restart_where_it_stands()
    {
        var (owner, settings, shown) = _harness.Open();
        var before = settings.ClientSize;

        settings.Position = new PixelPoint(40, 30);
        Dispatcher.UIThread.RunJobs();

        var moved = settings.Snapshot().Window!;

        Assert.Equal(new PixelPoint(40, 30), new PixelPoint(moved.X, moved.Y));

        settings.Width = before.Width + 100;
        settings.Height = before.Height + 50;
        Dispatcher.UIThread.RunJobs();

        var resized = settings.Snapshot().Window!;

        Assert.NotEqual(before, settings.ClientSize);
        Assert.Equal((settings.ClientSize.Width, settings.ClientSize.Height), (resized.Width, resized.Height));
        Assert.False(resized.Maximized);

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Окно, вернувшееся развёрнутым, отдаёт следующему перезапуску те же обычные границы.
    /// </summary>
    /// <remarks>
    /// Развёрнутым окно обычным ещё не бывало, и узнать, куда ему вернуться, кроме записи прежней
    /// копии, неоткуда. Хранитель берёт границы из неё сразу, до показа, — иначе следующий
    /// перезапуск вернул бы окно без места вовсе.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_settings_window_that_came_back_maximized_keeps_its_normal_bounds()
    {
        var restore = new SettingsSession { Window = new StudioPlacement(123, 77, 900, 600, 1, Maximized: true) };
        var (owner, settings, shown) = _harness.Open(restore: restore);

        var placement = settings.Snapshot().Window;

        Assert.NotNull(placement);
        Assert.True(placement.Maximized, "разворот не записан");
        Assert.Equal((123, 77, 900d, 600d), (placement.X, placement.Y, placement.Width, placement.Height));

        await CloseAsync(settings, shown);
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

    private static AxLink Link(SettingsWindow settings, string content) =>
        settings.GetVisualDescendants().OfType<AxLink>().Single(link => link.IsEffectivelyVisible && Equals(link.Content, content));

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
}
