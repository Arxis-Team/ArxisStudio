using ArxisStudio.Controls;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.UiDesignerStudio;

namespace ArxisStudio.Tests;

/// <summary>
/// Уборка карточек с доски: карточка уходит из коллекции холста, файл формы остаётся, а уборка
/// отменяется и помнится.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class UiDesignerRemovalTests
{
    /// <summary>
    /// Delete убирает выбранные карточки с доски, файлы форм остаются на месте, а строка состояния
    /// говорит, что сделано и как вернуть.
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_removes_the_selected_cards_from_the_board_but_not_their_files()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")),
            ("Views/C.axaml", WindowXaml("C"))));

        var a = studio.Card("A.axaml");
        var b = studio.Card("B.axaml");

        Select(studio, a, b);
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        Assert.Equal(["C.axaml"], studio.Model.Cards.Select(card => card.Name));
        Assert.True(File.Exists(a.Path.Value) && File.Exists(b.Path.Value), "уборка с доски удалила файл формы");
        Assert.Equal(new[] { a.Path, b.Path }.Order(), Board(studio).Removed.Order());
        Assert.Contains("2", studio.Status.Last);
        Assert.True(studio.View.Sheet.IsFocused, "после уборки клавиатура ушла с холста");
    }

    /// <summary>
    /// Ctrl+Z ставит убранную карточку туда, где она стояла, — тем же объектом, — а Ctrl+Y убирает снова;
    /// файл доски идёт следом.
    /// </summary>
    [AvaloniaFact]
    public async Task Undo_puts_a_removed_card_back_where_it_stood_and_redo_removes_it_again()
    {
        using var studio = new UiDesignerStudio();
        var snapshot = studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B")));

        Directory.CreateDirectory(Path.GetDirectoryName(studio.BoardFile)!);
        File.WriteAllText(studio.BoardFile, """
            { "version": 3, "forms": { "src/App/Views/A.axaml": { "x": 560, "y": -320 }, "src/App/Views/B.axaml": { "x": 0, "y": 0 } } }
            """);

        await studio.Open(snapshot);

        var a = studio.Card("A.axaml");

        Click(studio.Panel.Menu!.Items([a]).OfType<AxMenuItem>().Last());
        await studio.Built();

        Assert.DoesNotContain(a, studio.Model.Cards);
        Assert.Equal(studio.Strings["board.removed.one"].Replace("{0}", "A.axaml"), studio.Status.Last);

        Assert.True(studio.Panel.History!.Undo());
        await studio.Built();

        Assert.Same(a, studio.Card("A.axaml"));
        Assert.Equal(new Point(560, -320), a.Location);
        Assert.Empty(Board(studio).Removed);
        Assert.Equal(studio.Strings["board.returned.one"].Replace("{0}", "A.axaml"), studio.Status.Last);

        Assert.True(studio.Panel.History.Redo());
        await studio.Built();

        Assert.DoesNotContain(a, studio.Model.Cards);
        Assert.Equal([a.Path], Board(studio).Removed);
    }

    /// <summary>
    /// Убранная форма не возвращается сама: ни перечитанным решением, ни открытым заново — уборка
    /// помнится в файле доски.
    /// </summary>
    [AvaloniaFact]
    public async Task A_removed_form_stays_removed_when_the_solution_is_read_again()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))));

        Select(studio, studio.Card("A.axaml"));
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))), sequence: 2);

        Assert.Equal(["B.axaml"], studio.Model.Cards.Select(card => card.Name));

        var other = new ProjectWindowSolution("Other", root: studio.Root);

        other.File(other.Project("Lib"), "Views/Panel.axaml", "AvaloniaXaml");
        other.OnDisk();
        File.WriteAllText(other.ToSnapshot().Projects[0].Items[0].FullPath.Value, ControlXaml("Panel"));

        await studio.Open(other.ToSnapshot(), sequence: 3);
        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))), sequence: 4);

        Assert.Equal(["B.axaml"], studio.Model.Cards.Select(card => card.Name));
        Assert.Equal(["A.axaml"], studio.Model.Removed.Select(form => form.File.Path.FileName));
    }

    /// <summary>
    /// Меню пустого холста возвращает убранные формы по одной — названными файлом и проектом — и все
    /// разом; пока убранных нет, пункта возврата нет.
    /// </summary>
    [AvaloniaFact]
    public async Task The_canvas_menu_returns_removed_forms_one_at_a_time_or_all_at_once()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")),
            ("Views/C.axaml", WindowXaml("C"))));

        Assert.DoesNotContain(studio.Strings["board.return"], Headers(studio.Panel.Menu!.Items([]).OfType<AxMenuItem>()));

        Select(studio, studio.Card("A.axaml"), studio.Card("B.axaml"));
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        var rows = Returning(studio);

        Assert.Equal(["A.axaml · src/App/Views", "B.axaml · src/App/Views", studio.Strings["board.return.all"]], Headers(rows));

        Click(rows[0]);
        await studio.Built();

        Assert.Equal(["A.axaml", "C.axaml"], studio.Model.Cards.Select(card => card.Name).Order());

        rows = Returning(studio);
        Assert.Equal(["B.axaml · src/App/Views", studio.Strings["board.return.all"]], Headers(rows));

        Click(rows[^1]);
        await studio.Built();

        Assert.Equal(["A.axaml", "B.axaml", "C.axaml"], studio.Model.Cards.Select(card => card.Name).Order());
        Assert.Empty(Board(studio).Removed);
    }

    /// <summary>
    /// Одноимённые формы из двух папок в списке возврата читаются врозь: подписаны папкой от решения.
    /// </summary>
    [AvaloniaFact]
    public async Task Removed_forms_with_one_name_read_apart_in_the_menu()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/Main.axaml", WindowXaml("Main")),
            ("Views/Admin/Main.axaml", WindowXaml("AdminMain")),
            ("Views/Other.axaml", WindowXaml("Other"))));

        Select(studio, [.. studio.Model.Cards.Where(card => card.Name == "Main.axaml")]);
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        var rows = Returning(studio);

        // «Вернуть все» сверяется на своём месте, последним, а не в общей сортировке: слово у него —
        // языка студии, и место в сортировке решали бы язык и культура машины.
        Assert.Equal(
            ["Main.axaml · src/App/Views", "Main.axaml · src/App/Views/Admin"],
            Headers(rows[..^1]).Cast<string>().Order(StringComparer.Ordinal));
        Assert.Equal(studio.Strings["board.return.all"], rows[^1].Header);
    }

    /// <summary>
    /// Убраны все формы — доска так и говорит, а не «форм нет», и возвращает их одной кнопкой.
    /// </summary>
    [AvaloniaFact]
    public async Task With_every_form_removed_the_board_says_so_and_returns_them_with_one_button()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))));

        studio.View.Sheet.Selection.SelectAll();
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        Assert.True(studio.Model.IsAllRemoved, "доска не сказала, что все формы убраны");
        Assert.False(studio.Model.IsEmpty, "убранные формы выданы за решение без форм");
        Assert.True(studio.View.ReturnAll.IsEffectivelyVisible, "кнопки «Вернуть все» не видно");
        Assert.False(studio.View.Fit.IsEffectivelyEnabled, "«Вписать всё» включена на пустой доске");
        Assert.False(studio.View.ArrangeAll.IsEffectivelyEnabled, "«Упорядочить» включена на пустой доске");

        studio.Click(studio.View.ReturnAll);
        await studio.Built();

        Assert.Equal(2, studio.Model.Cards.Count);
        Assert.False(studio.Model.IsAllRemoved);
        Assert.True(studio.View.Fit.IsEffectivelyEnabled);
        Assert.Equal(studio.Strings["board.returned.many"].Replace("{0}", "2"), studio.Status.Last);
    }

    /// <summary>
    /// Убранная карточка уходит из протяжённости холста: «Вписать всё» больше не тянется к месту, где
    /// она стояла, — это и отличает уборку из коллекции от скрытия.
    /// </summary>
    [AvaloniaFact]
    public async Task A_removed_card_leaves_what_fit_all_frames()
    {
        using var studio = new UiDesignerStudio();
        var snapshot = studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/Far.axaml", WindowXaml("Far")));

        Directory.CreateDirectory(Path.GetDirectoryName(studio.BoardFile)!);
        File.WriteAllText(studio.BoardFile, """
            { "version": 3, "forms": { "src/App/Views/A.axaml": { "x": 0, "y": 0 }, "src/App/Views/Far.axaml": { "x": 6000, "y": 4000 } } }
            """);

        await studio.Open(snapshot);
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.View.Sheet.ItemsExtent.Right > 6000, $"дальняя карточка не в протяжённости: {studio.View.Sheet.ItemsExtent}");

        Select(studio, studio.Card("Far.axaml"));
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();
        Dispatcher.UIThread.RunJobs();

        Assert.True(studio.View.Sheet.ItemsExtent.Right < 6000, $"убранная карточка осталась в протяжённости: {studio.View.Sheet.ItemsExtent}");
    }

    /// <summary>Убранная форма, чей файл удалили с диска, уходит и из списка убранных в файле доски.</summary>
    [AvaloniaFact]
    public async Task A_removed_form_deleted_from_disk_leaves_the_board_file()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")),
            ("Views/C.axaml", WindowXaml("C"))));

        var a = studio.Card("A.axaml");

        Select(studio, a);
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        File.Delete(a.Path.Value);
        await studio.Open(studio.Solution(("Views/B.axaml", WindowXaml("B")), ("Views/C.axaml", WindowXaml("C"))), sequence: 2);

        Select(studio, studio.Card("B.axaml"));
        studio.Press(studio.View.Sheet, Key.Delete);
        await studio.Built();

        Assert.Equal([studio.PathOf("Views/B.axaml")], Board(studio).Removed);
    }

    private static void Select(UiDesignerStudio studio, params FormCard[] cards)
    {
        var sheet = studio.View.Sheet;

        sheet.Selection.Clear();

        foreach (var card in cards)
            sheet.Selection.Select(studio.Model.Cards.IndexOf(card));

        sheet.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static List<AxMenuItem> Returning(UiDesignerStudio studio) =>
        studio.Panel.Menu!.Items([]).OfType<AxMenuItem>()
            .Single(item => Equals(item.Header, studio.Strings["board.return"]))
            .Items.OfType<AxMenuItem>()
            .ToList();

    private static IEnumerable<object?> Headers(IEnumerable<AxMenuItem> items) => items.Select(item => item.Header);

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static BoardData Board(UiDesignerStudio studio) =>
        BoardFile.Read(studio.BoardFile, CanonicalPath.Create(Path.Combine(studio.Root, "Forms")));
}
