using ArxisStudio.Controls;
using ArxisStudio.Settings;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.SettingsHarness;

namespace ArxisStudio.Tests;

/// <summary>
/// Клавиатура в окне настроек: где она стоит, когда окно открылось, и что делает Esc.
/// </summary>
/// <remarks>
/// Запись 148 научила Esc диалог, а окно настроек построено на <c>AxWindow</c>, и клавиша до
/// него не доходила. Живая проверка нашла и причину, по которой этого не заметили: фокуса в
/// открытом окне не было ни у кого, и нажатию некуда было идти вовсе.
/// <para>
/// Окно модальное, поэтому каждое ожидание закрытия — со сторожем: не отозвавшееся на клавишу
/// повесило бы весь прогон вместо того, чтобы честно упасть.
/// </para>
/// <para>
/// Очередь общая с остальными: страница оформления читает словари, а <c>Localizer</c> один на
/// процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsKeyboardTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Настройки открываются с кареткой в поиске.
    /// </summary>
    /// <remarks>
    /// Так открываются настройки Rider: окно зовут, чтобы найти настройку. Прежде фокуса в
    /// открытом окне не было ни у кого — первое нажатие уходило в пустоту, а инструменты
    /// разработчика отказывались послать клавишу вовсе.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_settings_open_with_the_caret_in_the_search_field()
    {
        var (owner, settings, shown) = Open();

        var focused = settings.FocusManager?.GetFocusedElement() as Visual;

        Assert.True(
            focused is not null && (focused == settings.SearchBox || settings.SearchBox.IsVisualAncestorOf(focused)),
            $"фокус у {focused?.GetType().Name ?? "никого"}, а не в поиске");

        Escape(settings);

        await ClosedAsync(shown);

        owner.Close();
    }

    /// <summary>Нечего терять — Esc закрывает настройки сразу.</summary>
    [AvaloniaFact]
    public async Task Escape_closes_the_settings_when_nothing_has_changed()
    {
        var (owner, settings, shown) = Open();

        Escape(settings);

        await ClosedAsync(shown);

        owner.Close();
    }

    /// <summary>
    /// Несохранённое Esc молча не выбрасывает: спрашивает, и «нет» оставляет правку на месте.
    /// </summary>
    /// <remarks>
    /// Esc жмут и затем, чтобы закрыть подсказку или список, и второй нажатый подряд не должен
    /// стоить человеку набранного. Поэтому клавиша идёт дорогой крестика, а не «Отмены».
    /// </remarks>
    [AvaloniaFact]
    public async Task Escape_asks_before_it_drops_unsaved_changes()
    {
        var (owner, settings, shown) = Open();
        var row = Rows(settings).Single();

        row.Text = "20";
        Escape(settings);

        var question = Assert.Single(settings.OwnedWindows.OfType<AxDialog>());

        Assert.False(shown.IsCompleted, "Esc выбросил несохранённую правку, не спросив");

        // Esc на вопросе — это «нет»: окно остаётся, правка в нём.
        Escape(question);

        Assert.False(shown.IsCompleted, "ответ «нет» закрыл настройки");
        Assert.Equal("20", row.Text);

        await CloseAsync(settings, shown);

        owner.Close();
    }

    /// <summary>
    /// Esc в поиске, где что-то набрано, очищает поиск и оставляет окно; следующий закрывает его.
    /// </summary>
    /// <remarks>
    /// Так в настройках Rider: человек бросает запрос, а не окно. Каретка стоит в поиске с самого
    /// открытия, и Esc, закрывший бы окно вместе с набранным, стоил бы заново открытых настроек.
    /// </remarks>
    [AvaloniaFact]
    public async Task Escape_in_a_filled_search_clears_it_and_keeps_the_window()
    {
        var (owner, settings, shown) = Open();
        var model = (SettingsViewModel)settings.DataContext!;

        settings.SearchBox.Text = "кегль";
        Dispatcher.UIThread.RunJobs();

        Escape(settings);

        Assert.False(shown.IsCompleted, "Esc в заполненном поиске закрыл окно");
        Assert.Equal(string.Empty, model.Search);

        Escape(settings);

        await ClosedAsync(shown);

        owner.Close();
    }

    /// <summary>Esc с модификатором окно не закрывает: это уже другое сочетание.</summary>
    [AvaloniaFact]
    public async Task Escape_with_a_modifier_is_not_escape()
    {
        var (owner, settings, shown) = Open();

        settings.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(shown.IsCompleted, "Shift+Esc закрыл настройки");

        Escape(settings);

        await ClosedAsync(shown);

        owner.Close();
    }

    private (Window Owner, SettingsWindow Settings, Task Shown) Open() => _harness.Open();

    private static void Escape(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<PluginSettingRow> Rows(SettingsWindow settings) =>
        ((SettingsViewModel)settings.DataContext!).Nodes
            .SelectMany(node => node.Children)
            .Select(node => node.Page)
            .OfType<ExtensionPage>()
            .SelectMany(page => page.Rows)
            .ToList();
}
