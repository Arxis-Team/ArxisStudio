using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Palette;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.SettingsHarness;

namespace ArxisStudio.Tests;

/// <summary>
/// Страница «Клавиши»: все сочетания студии, чьи они и кому не досталось.
/// </summary>
/// <remarks>
/// Запись 153 отложила страницу: она перечисляла бы то, что уже видно в палитре. С <c>keymap.json</c>
/// ей есть что сказать сверх палитры — откуда сочетание и кто остался без своего, — и есть куда
/// вести: к файлу, где сочетания меняют.
/// <para>
/// Очередь общая: подписи страницы берутся из словарей, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class KeysPageTests : IDisposable
{
    private readonly string _root = TempFolder.Create("keys");
    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();

        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Строки идут в порядке раздачи и говорят, откуда сочетание, а отказ называет победителя.
    /// </summary>
    /// <remarks>
    /// Порядок раздачи и решает, кому достаётся занятое: сперва человек, потом студия, потом плагины.
    /// Команда без названия показывается своим именем — пустая строка не сказала бы ничего.
    /// </remarks>
    [AvaloniaFact]
    public void Rows_come_in_the_order_given_and_tell_where_they_came_from()
    {
        var page = Page(Path.Combine(_root, "keymap.json"), _ => { });

        Assert.Equal(
            [
                new KeyRow("Ctrl+Alt+H", "Поздороваться", "keymap.json"),
                new KeyRow("Ctrl+W", "Закрыть вкладку", Localizer.Instance["keys.studio"]),
                new KeyRow("Ctrl+Alt+G", "hello.wave", "Пример"),
            ],
            page.Rows);

        var refusal = Assert.Single(page.Refusals);

        Assert.Equal("Ctrl+W", refusal.Gesture);
        Assert.Equal("hello.close", refusal.Command);
        Assert.Equal("Пример", refusal.Source);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, Localizer.Instance["keys.refused.winner"], "Закрыть вкладку"),
            refusal.Winner);
        Assert.False(page.HasChanges, "у страницы, которая ничего не правит, есть несохранённое");
    }

    /// <summary>Страницу находит поиск настроек — по сочетанию и по команде, и у строк, и у отказов.</summary>
    /// <remarks>
    /// Запросы выбраны так, что каждый есть ровно в одном месте: «Ctrl+W» стоит и в строке, и в
    /// отказе, и поиск по нему нашёл бы страницу, даже потеряв одно из двух.
    /// </remarks>
    [AvaloniaFact]
    public void The_page_is_found_by_a_gesture_and_by_a_command()
    {
        var page = Page(Path.Combine(_root, "keymap.json"), _ => { });

        foreach (var query in new[] { "Ctrl+Alt+G", "поздоров", "hello.close", "keymap" })
        {
            var found = SettingsViewModel.Filter([page], query);

            Assert.True(found.Count == 1, $"поиск «{query}» страницу клавиш не нашёл");
        }
    }

    /// <summary>
    /// Поиск сужает страницу до найденного — и среди строк, и среди отказов, — не меняя порядка раздачи.
    /// </summary>
    /// <remarks>
    /// «Куда делось моё Ctrl+W» — вопрос, с которым сюда приходят, и ответ на него — две строки: кому
    /// сочетание досталось и кому нет, — а не весь список. Раздел отказов, в котором поиск ничего не
    /// оставил, не показывается.
    /// </remarks>
    [AvaloniaFact]
    public void A_search_by_a_shortcut_narrows_the_keys_page()
    {
        var page = Page(Path.Combine(_root, "keymap.json"), _ => { });

        page.Narrow("Ctrl+Alt");

        Assert.Equal(["Ctrl+Alt+H", "Ctrl+Alt+G"], page.ShownRows.Select(row => row.Gesture));
        Assert.False(page.ShowsRefusals, "раздел отказов остался, хотя поиск ничего в нём не нашёл");

        page.Narrow("Ctrl+W");

        Assert.Equal(["Закрыть вкладку"], page.ShownRows.Select(row => row.Command));
        Assert.Equal(["hello.close"], page.ShownRefusals.Select(refusal => refusal.Command));

        page.Narrow(null);

        Assert.Equal(page.Rows, page.ShownRows);
        Assert.True(page.ShowsRefusals, "стёртый поиск не вернул отказы");
    }

    /// <summary>
    /// Нет файла — кнопка заводит его с подсказкой и открывает; подсказка файл не ломает.
    /// </summary>
    /// <remarks>
    /// Заведённый файл обязан читаться без единой жалобы и не назначать ничего: подсказка стоит
    /// комментарием, а не чужими сочетаниями, которых человек не выбирал.
    /// </remarks>
    [AvaloniaFact]
    public void Opening_the_file_makes_it_with_a_hint_when_there_is_none()
    {
        var file = Path.Combine(_root, "data", "keymap.json");
        var opened = new List<string>();
        var page = Page(file, opened.Add);

        page.OpenFile();

        Assert.Equal([file], opened);
        Assert.True(File.Exists(file), "файла сочетаний так и нет");

        var keymap = StudioKeymap.Load(file);

        Assert.Empty(keymap.Complaints);
        Assert.Empty(keymap.Entries);
        Assert.Contains("//", File.ReadAllText(file));
    }

    /// <summary>Существующий файл кнопка не трогает — только открывает.</summary>
    [AvaloniaFact]
    public void Opening_the_file_leaves_an_existing_one_alone()
    {
        const string written = """{ "studio.palette": "Ctrl+Alt+P" }""";

        var file = Path.Combine(_root, "keymap.json");
        var opened = new List<string>();

        File.WriteAllText(file, written);

        Page(file, opened.Add).OpenFile();

        Assert.Equal([file], opened);
        Assert.Equal(written, File.ReadAllText(file));
    }

    /// <summary>
    /// Окно настроек, открытое из студии, показывает страницу: сочетания плашками, отказы над списком,
    /// а кнопка ведёт к файлу.
    /// </summary>
    [AvaloniaFact]
    public async Task The_settings_show_the_keys_and_lead_to_the_file()
    {
        var opened = new List<string>();
        var file = Path.Combine(_root, "keymap.json");
        var (owner, settings, shown) = _harness.Open(Page(file, opened.Add), "studio.keys");

        Dispatcher.UIThread.RunJobs();

        var chips = settings.GetVisualDescendants().OfType<AxChip>().Select(chip => chip.Content as string).ToList();
        var texts = Texts(settings);

        Assert.Equal(["Ctrl+W", "Ctrl+Alt+H", "Ctrl+W", "Ctrl+Alt+G"], chips);
        Assert.Contains(Localizer.Instance["keys.refused"], texts);
        Assert.Contains("keymap.json", texts);
        Assert.Contains("Пример", texts);

        var button = settings.GetVisualDescendants().OfType<AxButton>().Single(candidate => Equals(candidate.Content, Localizer.Instance["keys.open"]));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([file], opened);

        await CloseAsync(settings, shown);

        owner.Close();
    }

    /// <summary>
    /// Открытая страница идёт за реестром сама: человек сохранил файл, расширение ушло.
    /// </summary>
    /// <remarks>
    /// Кнопка страницы ведёт к правке файла, и вернувшийся из редактора должен увидеть, что стало: новое
    /// сочетание плашкой и пропавший отказ — без раздела «Не досталось». Так же и с расширением, которое
    /// выключили на соседней странице того же окна: его сочетаний больше нет.
    /// </remarks>
    [AvaloniaFact]
    public async Task An_open_page_follows_the_registry_by_itself()
    {
        var (page, keys) = Keyed(Path.Combine(_root, "keymap.json"), _ => { });
        var (owner, settings, shown) = _harness.Open(page, "studio.keys");

        Dispatcher.UIThread.RunJobs();

        keys.Personalize(new StudioKeymap([new KeymapEntry("hello.greet", ["Ctrl+Alt+J"]), new KeymapEntry("hello.close", [])], []));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Ctrl+Alt+J", "Ctrl+W", "Ctrl+Alt+G"], Chips(settings));
        Assert.DoesNotContain(Localizer.Instance["keys.refused"], Texts(settings));

        keys.RemoveOwnedBy("arxis.hello");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Ctrl+Alt+J", "Ctrl+W"], Chips(settings));

        await CloseAsync(settings, shown);

        owner.Close();
    }

    /// <summary>Закрытое окно настроек отпускает реестр: страница больше не слушает его.</summary>
    /// <remarks>
    /// Реестр живёт весь сеанс, а страница собирается на каждое открытие окна. Не отпусти окно страницу,
    /// каждое открытие настроек оставляло бы в памяти ещё одну, перечитывающую реестр на каждой перемене.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_closed_settings_window_lets_go_of_the_registry()
    {
        var (page, keys) = Keyed(Path.Combine(_root, "keymap.json"), _ => { });
        var (owner, settings, shown) = _harness.Open(page, "studio.keys");
        var heard = 0;

        page.PropertyChanged += (_, _) => heard++;

        await CloseAsync(settings, shown);

        keys.Bind("Ctrl+Alt+K", "studio.panel.next");

        Assert.Equal(0, heard);

        owner.Close();
    }

    /// <summary>Настройки, открытые из Welcome, показывают «Клавиши» — через обе двери его полосы.</summary>
    /// <remarks>
    /// Страница стояла только в окне, открытом из студии, с доводом, что из Welcome показать нечего. Довод
    /// был неверен — окно студии собирается раньше Welcome, и сочетания к нему уже розданы, — а
    /// настройки открывают как раз отсюда: человек, запустивший студию, страницы не нашёл.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("welcome.nav.settings")]
    [InlineData("welcome.nav.plugins")]
    public async Task The_settings_opened_from_welcome_show_the_keys(string door)
    {
        var page = Page(Path.Combine(_root, "keymap.json"), _ => { });
        var (welcome, settings) = _harness.OpenFromWelcome(door, () => page);

        Assert.Contains(Localizer.Instance["keys.title"], Texts(settings));

        var closed = new TaskCompletionSource();

        settings.Closed += (_, _) => closed.TrySetResult();
        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(closed.Task, await Task.WhenAny(closed.Task, Task.Delay(Patience)));

        welcome.Close();
    }

    /// <summary>Плашки сочетаний, которые окно показывает сейчас.</summary>
    private static List<string?> Chips(Window window) =>
        [.. window.GetVisualDescendants().OfType<AxChip>().Where(chip => chip.IsEffectivelyVisible).Select(chip => chip.Content as string)];

    /// <summary>
    /// Страница по реестру, где есть всё сразу: сочетание человека, студии и плагина и один отказ.
    /// </summary>
    private static KeysPage Page(string file, Action<string> open) => Keyed(file, open).Page;

    /// <summary>Та же страница — вместе с реестром, по которому она собрана.</summary>
    private static (KeysPage Page, StudioShortcuts Keys) Keyed(string file, Action<string> open)
    {
        var keys = new StudioShortcuts(_ => true);

        keys.Personalize(new StudioKeymap([new KeymapEntry("hello.greet", ["Ctrl+Alt+H"])], []));
        keys.Bind("Ctrl+W", "studio.close");
        keys.Bind("Ctrl+Alt+G", "hello.wave", "arxis.hello");
        keys.Bind("Ctrl+W", "hello.close", "arxis.hello");

        PaletteEntry[] commands =
        [
            new("Закрыть вкладку", "studio.close"),
            new("Поздороваться", "hello.greet"),
        ];

        return (KeysPage.From(keys, () => commands, id => id == "arxis.hello" ? "Пример" : null, file, open), keys);
    }
}
