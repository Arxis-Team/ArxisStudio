using ArxisStudio.Controls;
using ArxisStudio.Palette;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Палитра на экране.
/// </summary>
/// <remarks>
/// Живёт в слое оверлеев окна, а не отдельным окном: второе окно студии на
/// секунду пришлось бы позиционировать, поднимать, отбирать у него фокус и
/// следить, чтобы оно не осталось висеть, когда главное свернули.
/// <para>
/// Очередь общая: подписи палитры берутся из словарей, а <c>Localizer</c> один
/// на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PaletteOverlayTests
{
    /// <summary>Палитра открывается поверх окна и берёт каретку в поле ввода.</summary>
    /// <remarks>Её открывают, чтобы набирать: каретка в другом месте — потерянное нажатие.</remarks>
    [AvaloniaFact]
    public void The_palette_opens_over_the_window_and_takes_the_caret()
    {
        var (palette, window, _) = Shown();

        Assert.True(palette.IsOpen);

        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());
        var box = Assert.Single(card.GetVisualDescendants().OfType<AxTextBox>());

        Assert.True(box.IsFocused, "палитру открыли, а набирать негде");

        palette.Close();
    }

    /// <summary>
    /// Второе нажатие того же сочетания палитру закрывает.
    /// </summary>
    /// <remarks>
    /// Открывшее обязано и убрать: человек, нажавший дважды, иначе остаётся с
    /// открытым окном и вопросом, что он сделал не так.
    /// </remarks>
    [AvaloniaFact]
    public void Asking_twice_closes_the_palette()
    {
        var (palette, window, entries) = Shown();

        palette.Show(window, entries);
        Dispatcher.UIThread.RunJobs();

        Assert.False(palette.IsOpen);
        Assert.Empty(window.GetVisualDescendants().OfType<AxQuickSearch>());
    }

    /// <summary>Набранное отбирает список, и выбор встаёт на первую строку.</summary>
    [AvaloniaFact]
    public void Typing_narrows_the_list_and_the_choice_goes_to_the_first_row()
    {
        var (palette, window, _) = Shown();
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());

        card.Text = "пан";
        Dispatcher.UIThread.RunJobs();

        var shown = Assert.IsAssignableFrom<IReadOnlyList<PaletteEntry>>(card.ItemsSource);

        Assert.Equal(["Следующая панель"], shown.Select(entry => entry.Title));
        Assert.Same(shown[0], card.SelectedItem);

        palette.Close();
    }

    /// <summary>
    /// Enter выполняет выбранное и закрывает палитру.
    /// </summary>
    /// <remarks>
    /// Закрывается до вызова, а не после: команда может открыть своё окно, и
    /// карточка, оставшаяся поверх, оказалась бы поверх её же результата.
    /// </remarks>
    [AvaloniaFact]
    public void Enter_runs_the_chosen_one_and_closes_the_palette()
    {
        var called = new List<string>();
        var (palette, window, _) = Shown(called);
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());

        card.Text = "пан";
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["studio.panel.next"], called);
        Assert.False(palette.IsOpen);
    }

    /// <summary>Стрелка вниз двигает выбор.</summary>
    [AvaloniaFact]
    public void The_down_arrow_moves_the_choice()
    {
        var called = new List<string>();
        var (palette, window, _) = Shown(called);
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Следующая панель", Assert.IsType<PaletteEntry>(card.SelectedItem).Title);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["studio.panel.next"], called);
        Assert.False(palette.IsOpen);
    }

    /// <summary>
    /// Подложка палитры занимает всё окно, а карточка стоит по центру.
    /// </summary>
    /// <remarks>
    /// Слой оверлеев — канва, и детей она не растягивает. Подложка без размера
    /// выходила ровно с карточку: карточка прижималась к левому краю, а мимо
    /// неё щёлкнуть было некуда — значит и закрыть щелчком нельзя. Нашлось это
    /// снимком живой студии, а не тестом; тест поставлен следом.
    /// </remarks>
    [AvaloniaFact]
    public void The_backdrop_covers_the_window_and_the_card_stands_in_the_middle()
    {
        var (palette, window, _) = Shown();
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());
        var scrim = Assert.IsType<Panel>(card.GetVisualParent());

        window.UpdateLayout();

        Assert.Equal(window.ClientSize.Width, scrim.Bounds.Width);
        Assert.Equal(window.ClientSize.Height, scrim.Bounds.Height);

        // По центру: слева и справа остаётся поровну, с точностью до пикселя.
        var left = card.Bounds.X;
        var right = scrim.Bounds.Width - card.Bounds.Right;

        Assert.True(Math.Abs(left - right) <= 1, $"карточка не по центру: слева {left}, справа {right}");

        palette.Close();
    }

    /// <summary>Esc закрывает палитру и ничего не выполняет.</summary>
    [AvaloniaFact]
    public void Escape_closes_the_palette_and_runs_nothing()
    {
        var called = new List<string>();
        var (palette, window, _) = Shown(called);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(palette.IsOpen);
        Assert.Empty(called);
        Assert.Empty(window.GetVisualDescendants().OfType<AxQuickSearch>());
    }

    /// <summary>
    /// Значок команды стоит в строке слева, а место под него держит каждая строка.
    /// </summary>
    /// <remarks>
    /// Колонка значков — как в меню: пункт без значка получает пустое место той же ширины. Иначе
    /// названия стояли бы лесенкой, и глаз, идущий по списку сверху вниз, спотыкался бы на каждой
    /// строке без значка.
    /// </remarks>
    [AvaloniaFact]
    public void The_icon_of_a_command_stands_on_the_left_and_every_name_stands_in_line()
    {
        IReadOnlyList<PaletteEntry> entries =
        [
            new("Обновить проект", "projects.reload") { Icon = ArxisStudio.Icons.AxIcons.Refresh },
            new("Закрыть вкладку", "studio.close", "Ctrl+W"),
        ];

        var (palette, window, _) = Shown(entries: entries);
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());
        var list = Assert.Single(card.GetVisualDescendants().OfType<AxListBox>());
        var icons = list.GetVisualDescendants().OfType<ArxisStudio.Icons.AxIcon>().ToList();
        var names = entries
            .Select(entry => Assert.Single(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == entry.Title))
            .ToList();

        Assert.Equal(2, icons.Count);
        Assert.Same(ArxisStudio.Icons.AxIcons.Refresh, icons[0].Data);
        Assert.Null(icons[1].Data);

        var first = names[0].TranslatePoint(default, list)!.Value.X;
        var second = names[1].TranslatePoint(default, list)!.Value.X;
        var iconEnd = icons[0].TranslatePoint(new Avalonia.Point(icons[0].Bounds.Width, 0), list)!.Value.X;

        Assert.Equal(first, second, 3);
        Assert.True(first > iconEnd, $"название начинается на {first:0.##}, а значок кончается на {iconEnd:0.##}");

        palette.Close();
    }

    /// <summary>Сочетания у правого края строк не заходят под ползунок прокрутки.</summary>
    /// <remarks>
    /// Полоса прокрутки лежит поверх строк, и живая палитра показала сочетания, чей последний знак
    /// стоял под ползунком: строка отступала от края на 8, а полоса шириной 12. Команд здесь столько,
    /// что список прокручивается, как у студии с модулями и плагинами.
    /// </remarks>
    [AvaloniaFact]
    public void The_gestures_stay_out_of_the_scroll_lane()
    {
        IReadOnlyList<PaletteEntry> entries = [.. Enumerable.Range(1, 40).Select(index => new PaletteEntry($"Команда {index}", $"test.{index}", "Ctrl+Alt+W"))];

        var (palette, window, _) = Shown(entries: entries);
        var card = Assert.Single(window.GetVisualDescendants().OfType<AxQuickSearch>());
        var list = Assert.Single(card.GetVisualDescendants().OfType<AxListBox>());
        var bar = list.GetVisualDescendants().OfType<ScrollBar>().Single(candidate => candidate.Orientation == Orientation.Vertical);
        var gestures = list.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Text == "Ctrl+Alt+W").ToList();

        Assert.True(bar.IsVisible, "список палитры не прокручивается, и полосы, в которую заходить, нет");
        Assert.NotEmpty(gestures);

        var lane = bar.TranslatePoint(default, card)!.Value.X;

        foreach (var gesture in gestures)
        {
            var right = gesture.TranslatePoint(new Point(gesture.Bounds.Width, 0), card)!.Value.X;

            Assert.True(right <= lane + 0.01, $"сочетание кончается на {right:0.##}, а полоса прокрутки начинается на {lane:0.##}");
        }

        palette.Close();
    }

    /// <summary>
    /// Закрытая палитра возвращает каретку туда, где она стояла, — и команда из палитры застаёт её
    /// уже там.
    /// </summary>
    /// <remarks>
    /// Карточка уходила из дерева вместе с кареткой: после Esc печатать было некуда, а команда не
    /// знала, в какой панели стоял человек, — «закрыть» закрывало показанный документ, а не его
    /// панель.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_the_palette_gives_the_caret_back()
    {
        var field = new AxTextBox();
        var window = new Window { Width = 900, Height = 600, Content = field };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        field.Focus();

        object? focusedWhenRun = null;
        var palette = new PaletteOverlay(_ =>
        {
            focusedWhenRun = window.FocusManager?.GetFocusedElement();

            return true;
        });
        IReadOnlyList<PaletteEntry> entries = [new("Закрыть вкладку", "studio.close", "Ctrl+W")];

        palette.Show(window, entries);
        Dispatcher.UIThread.RunJobs();

        Assert.False(field.IsFocused, "палитра не взяла каретку — проверять нечего");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(field.IsFocused, "Esc закрыл палитру, а каретка осталась нигде");

        palette.Show(window, entries);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(field, focusedWhenRun);
        Assert.True(field.IsFocused, "после команды из палитры каретка осталась нигде");

        window.Close();
    }

    /// <summary>Открытая палитра над показанным окном.</summary>
    private static (PaletteOverlay Palette, Window Window, IReadOnlyList<PaletteEntry> Entries) Shown(
        List<string>? called = null,
        IReadOnlyList<PaletteEntry>? entries = null)
    {
        var window = new Window { Width = 900, Height = 600, Content = new Border() };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var palette = new PaletteOverlay(id =>
        {
            called?.Add(id);

            return true;
        });

        entries ??=
        [
            new("Закрыть вкладку", "studio.close", "Ctrl+W"),
            new("Следующая панель", "studio.panel.next", "F6"),
        ];

        palette.Show(window, entries);
        Dispatcher.UIThread.RunJobs();

        return (palette, window, entries);
    }
}
