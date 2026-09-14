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
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-keys-{Guid.NewGuid():N}");
    private readonly SettingsHarness _harness = new();

    public KeysPageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _harness.Dispose();

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
        var texts = settings.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();

        Assert.Equal(["Ctrl+W", "Ctrl+Alt+H", "Ctrl+W", "Ctrl+Alt+G"], chips);
        Assert.Contains(Localizer.Instance["keys.refused"], texts);
        Assert.Contains("keymap.json", texts);
        Assert.Contains("Пример", texts);

        var button = settings.GetVisualDescendants().OfType<AxButton>().Single(candidate => Equals(candidate.Content, Localizer.Instance["keys.open"]));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([file], opened);

        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(shown, await Task.WhenAny(shown, Task.Delay(Patience)));

        owner.Close();
    }

    /// <summary>
    /// Страница по реестру, где есть всё сразу: сочетание человека, студии и плагина и один отказ.
    /// </summary>
    private static KeysPage Page(string file, Action<string> open)
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

        return KeysPage.From(keys, commands, id => id == "arxis.hello" ? "Пример" : null, file, open);
    }
}
