using ArxisStudio.Controls;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.UiDesignerStudio;

namespace ArxisStudio.Tests;

/// <summary>
/// Доска форм в окне: карточки решения, их места на холсте и в файле, отмена и открытие.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class UiDesignerBoardTests
{
    /// <summary>
    /// Окно и пользовательский элемент встают карточками, приложение — нет; строки карточки — класс и
    /// объявленный размер.
    /// </summary>
    [AvaloniaFact]
    public async Task Cards_stand_for_the_forms_of_the_solution()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("App.axaml", ApplicationXaml),
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card"))));

        Assert.True(studio.Model.IsReady);
        Assert.Equal(["Card.axaml", "MainWindow.axaml"], studio.Model.Cards.Select(card => card.Name).Order());

        var window = studio.Card("MainWindow.axaml");

        Assert.Equal(FormKind.Window, window.Kind);
        Assert.Equal("App.Views.MainWindow", window.Detail);
        Assert.Contains("800", window.Footer);
        Assert.Equal(studio.Strings["board.kind.userControl"], studio.Card("Card.axaml").KindText);
        Assert.Equal(string.Format(studio.Strings["board.count"], 2), studio.Model.Summary);
    }

    /// <summary>
    /// Нерасставленные карточки встают сеткой и не налезают друг на друга, а места сразу уходят в файл.
    /// </summary>
    [AvaloniaFact]
    public async Task New_cards_get_their_own_places_and_the_file_keeps_them()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card")),
            ("Views/Other.axaml", ControlXaml("Other"))));

        var places = studio.Model.Cards
            .Select(card => new Rect(card.Location, studio.Container(card).Bounds.Size))
            .ToList();

        for (var left = 0; left < places.Count; left++)
        {
            for (var right = left + 1; right < places.Count; right++)
                Assert.False(places[left].Intersects(places[right]), $"карточки налезают: {places[left]} и {places[right]}");
        }

        var written = Read(studio);

        Assert.Equal(3, written.Count);
        Assert.Equal(new Spot(studio.Card("Other.axaml").Location.X, studio.Card("Other.axaml").Location.Y),
            written[studio.PathOf("Views/Other.axaml")]);
    }

    /// <summary>Карточка встаёт туда, где её оставили: место берётся из файла доски решения.</summary>
    [AvaloniaFact]
    public async Task A_card_comes_back_to_the_place_from_the_file()
    {
        using var studio = new UiDesignerStudio();
        var snapshot = studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow")));

        Directory.CreateDirectory(Path.GetDirectoryName(studio.BoardFile)!);
        File.WriteAllText(studio.BoardFile, """{ "version": 1, "forms": { "src/App/Views/MainWindow.axaml": { "x": 560, "y": -320 } } }""");

        await studio.Open(snapshot);

        Assert.Equal(new Point(560, -320), studio.Card("MainWindow.axaml").Location);
    }

    /// <summary>
    /// «Упорядочить» ложится в историю холста: отмена возвращает карточки, а файл идёт следом за экраном.
    /// </summary>
    [AvaloniaFact]
    public async Task Arranging_is_undone_and_the_file_follows_the_screen()
    {
        using var studio = new UiDesignerStudio();
        var snapshot = studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")));

        Directory.CreateDirectory(Path.GetDirectoryName(studio.BoardFile)!);
        File.WriteAllText(studio.BoardFile, """
            { "forms": { "src/App/Views/A.axaml": { "x": 900, "y": 900 }, "src/App/Views/B.axaml": { "x": -40, "y": 20 } } }
            """);

        await studio.Open(snapshot);
        studio.View.ArrangeAll.Focus();
        studio.Click(studio.View.ArrangeAll);
        await studio.Built();

        Assert.True(studio.View.Sheet.IsFocused, "кнопка полосы не вернула клавиатуру холсту — Ctrl+Z до истории не дойдёт");

        var a = studio.Card("A.axaml");
        var b = studio.Card("B.axaml");

        Assert.Equal(new Point(-40, 20), a.Location);
        Assert.Equal(a.Location.Y, b.Location.Y);
        Assert.True(b.Location.X > a.Location.X, "B встал не справа от A: порядок решения потерян");
        Assert.Equal(new Spot(b.Location.X, b.Location.Y), Read(studio)[b.Path]);

        Assert.True(studio.Panel.History!.Undo());
        await studio.Built();

        Assert.Equal(new Point(900, 900), a.Location);
        Assert.Equal(new Spot(900, 900), Read(studio)[a.Path]);
    }

    /// <summary>
    /// Стрелка двигает выбранную карточку правкой холста, а правка доходит до файла.
    /// </summary>
    [AvaloniaFact]
    public async Task A_nudged_card_is_written()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));

        var card = studio.Card("MainWindow.axaml");
        var was = card.Location;

        studio.View.Sheet.SelectedItem = card;
        studio.View.Sheet.Focus();
        studio.Press(studio.View.Sheet, Key.Right);
        await studio.Built();

        Assert.True(card.Location.X > was.X, "стрелка не сдвинула выбранную карточку");
        Assert.Equal(new Spot(card.Location.X, card.Location.Y), Read(studio)[card.Path]);
        Assert.True(studio.Panel.History!.CanUndo, "сдвиг не попал в историю");
    }

    /// <summary>Enter открывает выбранные формы, двойной щелчок — форму под указателем.</summary>
    [AvaloniaFact]
    public async Task Enter_and_a_double_click_open_forms_in_the_editor()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/MainWindow.axaml", WindowXaml("MainWindow")),
            ("Views/Card.axaml", ControlXaml("Card"))));

        var window = studio.Card("MainWindow.axaml");
        var card = studio.Card("Card.axaml");

        studio.View.Sheet.SelectedItem = window;
        studio.Press(studio.View.Sheet, Key.Enter);

        Assert.Equal([window.Path.Value], studio.Documents.Opened);

        studio.Panel.Frame([]);
        Dispatcher.UIThread.RunJobs();
        studio.DoubleClick(studio.Container(card));

        Assert.Equal([window.Path.Value, card.Path.Value], studio.Documents.Opened);
    }

    /// <summary>
    /// Проект, не загрузившийся на этот раз, уносит карточки, но не места: вернувшись, они встают туда же.
    /// </summary>
    [AvaloniaFact]
    public async Task Forms_that_leave_for_a_while_keep_their_places()
    {
        using var studio = new UiDesignerStudio();
        var full = studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")));

        await studio.Open(full);

        var place = studio.Card("B.axaml").Location;
        var without = studio.Solution(("Views/A.axaml", WindowXaml("A")));

        await studio.Open(without, sequence: 2);
        studio.Model.Moved();
        await studio.Built();

        Assert.Equal(["A.axaml"], studio.Model.Cards.Select(card => card.Name));
        Assert.True(Read(studio).ContainsKey(studio.PathOf("Views/B.axaml")), "место ушедшей на время формы стёрто");

        await studio.Open(full, sequence: 3);

        Assert.Equal(place, studio.Card("B.axaml").Location);
    }

    /// <summary>Место формы, которой нет и на диске, из файла уходит.</summary>
    [AvaloniaFact]
    public async Task A_deleted_form_leaves_the_file()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B"))));

        var without = studio.Solution(("Views/A.axaml", WindowXaml("A")));

        File.Delete(studio.PathOf("Views/B.axaml").Value);
        await studio.Open(without, sequence: 2);
        studio.Model.Moved();
        await studio.Built();

        Assert.Equal([studio.PathOf("Views/A.axaml")], Read(studio).Keys);
    }

    /// <summary>Другое решение — другая доска: отмена прежней ничего не значит и забывается.</summary>
    [AvaloniaFact]
    public async Task Another_solution_forgets_the_undo_of_the_old_board()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B"))));

        studio.Card("A.axaml").Location = new Point(1000, 1000);
        studio.Click(studio.View.ArrangeAll);
        Assert.True(studio.Panel.History!.CanUndo);

        var other = new ProjectWindowSolution("Other", root: studio.Root);

        other.File(other.Project("Lib"), "Views/Panel.axaml", "AvaloniaXaml");
        other.OnDisk();
        File.WriteAllText(other.ToSnapshot().Projects[0].Items[0].FullPath.Value, ControlXaml("Panel"));

        await studio.Open(other.ToSnapshot(), sequence: 2);

        Assert.Equal(["Panel.axaml"], studio.Model.Cards.Select(card => card.Name));
        Assert.False(studio.Panel.History.CanUndo, "отмена прежней доски пережила смену решения");
    }

    /// <summary>
    /// Холст одет палитрой студии в обоих вариантах: фон и рамка выбора — токены темы, а не числа ядра.
    /// </summary>
    [AvaloniaFact]
    public async Task The_canvas_wears_the_studio_palette_in_both_variants()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            studio.Window.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();

            var sheet = studio.View.Sheet;

            Assert.True(studio.Window.TryFindResource("AxSurfaceSunkenBrush", variant, out var sunken));
            Assert.True(studio.Window.TryFindResource("AxAccentBrush", variant, out var accent));
            Assert.Same(sunken, sheet.Background);
            Assert.True(sheet.TryFindResource("SurfaceItem.SelectionBrush", variant, out var selection));
            Assert.Same(accent, selection);
        }
    }

    /// <summary>Сетку прячет настройка — и кнопка полосы, которая её же и пишет.</summary>
    [AvaloniaFact]
    public async Task The_grid_follows_its_setting()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));
        Assert.True(studio.View.Sheet.ShowGrid);

        studio.Settings.Set(UiDesignerModule.GridKey, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(studio.View.Sheet.ShowGrid);
        Assert.False(studio.View.GridToggle.IsChecked);

        studio.View.GridToggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.Settings.Get<bool?>(UiDesignerModule.GridKey));
        Assert.True(studio.View.Sheet.ShowGrid);
    }

    /// <summary>Меню карточки открывает её, меню пустого холста вписывает и раскладывает доску.</summary>
    [AvaloniaFact]
    public async Task The_menu_speaks_of_cards_or_of_the_board()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/MainWindow.axaml", WindowXaml("MainWindow"))));

        var card = studio.Card("MainWindow.axaml");
        var cardMenu = studio.Panel.Menu!.Items([card]).OfType<AxMenuItem>().ToList();
        var boardMenu = studio.Panel.Menu.Items([]).OfType<AxMenuItem>().ToList();

        Assert.Equal(
            [studio.Strings["board.open"], studio.Strings["board.frame"]],
            cardMenu.Select(item => item.Header));
        Assert.Equal(
            [studio.Strings["board.fit"], studio.Strings["board.arrange"]],
            boardMenu.Select(item => item.Header));

        cardMenu[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal([card.Path.Value], studio.Documents.Opened);
    }

    /// <summary>Без службы проектов доска говорит, что показывать нечего, а не стоит пустой.</summary>
    [AvaloniaFact]
    public void Without_the_projects_service_the_board_says_so()
    {
        using var studio = new UiDesignerStudio(service: false);

        Assert.True(studio.Model.IsNoService);
        Assert.False(studio.View.Sheet.IsVisible);
    }

    /// <summary>Решение без форм — пустая доска с объяснением, а не холст без слов.</summary>
    [AvaloniaFact]
    public async Task A_solution_without_forms_says_so()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("App.axaml", ApplicationXaml)));

        Assert.True(studio.Model.IsEmpty);
        Assert.Empty(studio.Model.Cards);
        Assert.False(File.Exists(studio.BoardFile), "пустая доска записала файл");
    }

    private static Dictionary<CanonicalPath, Spot> Read(UiDesignerStudio studio) =>
        BoardFile.Read(studio.BoardFile, CanonicalPath.Create(Path.Combine(studio.Root, "Forms")));
}
