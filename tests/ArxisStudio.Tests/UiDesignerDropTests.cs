using ArxisStudio.Dragging;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
using static ArxisStudio.Tests.UiDesignerStudio;

namespace ArxisStudio.Tests;

/// <summary>
/// Доска — цель перетаскивания студии: на неё несут файлы форм, и в точке курсора стоит заготовка той
/// карточки, которая встанет.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class UiDesignerDropTests
{
    /// <summary>
    /// Убранная форма, принесённая на доску, встаёт серединой под курсор — тем же объектом, выбранной,
    /// с клавиатурой у холста; пока несут, на её месте заготовка, а у курсора — «вернуть». Ctrl+Z
    /// убирает её снова, Ctrl+Y возвращает в точку броска; файл доски идёт следом.
    /// </summary>
    [AvaloniaFact]
    public async Task A_removed_form_carried_onto_the_board_lands_under_the_cursor()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))));

        var a = studio.Card("A.axaml");
        var stood = a.Spot;

        Remove(studio, a);

        var sheet = studio.View.Sheet;
        var cursor = new Point(300, 200);
        var spot = SpotUnder(studio, cursor);

        // Несут из чужой панели — клавиатура не у холста, и перейти к нему должна броском.
        studio.View.GridToggle.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.False(sheet.IsFocused);

        var session = studio.Carry(a.Path.Value);

        Assert.Equal(DragDropEffects.Link, session.Over(studio.OnSheet(cursor), KeyModifiers.None));
        Assert.Equal(Say(studio, "board.drop.return", "A.axaml"), session.Hint);

        var host = Assert.IsType<Panel>(Assert.Single(studio.View.Preview.Children));

        Assert.Equal((spot.X - sheet.ViewportLocation.X) * sheet.ViewportZoom, Canvas.GetLeft(host), 3);
        Assert.Equal((spot.Y - sheet.ViewportLocation.Y) * sheet.ViewportZoom, Canvas.GetTop(host), 3);
        Assert.DoesNotContain(a, studio.Model.Cards);

        Assert.Equal(DragDropEffects.Link, session.Drop());
        await studio.Built();

        Assert.Same(a, studio.Card("A.axaml"));
        Assert.Equal(new Point(spot.X, spot.Y), a.Location);
        Assert.Equal([a], sheet.Selection.SelectedItems.OfType<FormCard>());
        Assert.True(sheet.IsFocused, "после броска клавиатура не у холста: Ctrl+Z не отменит его");
        Assert.Empty(studio.View.Preview.Children);
        Assert.Empty(Board(studio).Removed);
        Assert.Equal(spot, Board(studio).Spots[a.Path]);
        Assert.Equal(Say(studio, "board.returned.one", "A.axaml"), studio.Status.Last);

        Assert.True(studio.Panel.History!.Undo());
        await studio.Built();

        Assert.DoesNotContain(a, studio.Model.Cards);
        Assert.Equal([a.Path], Board(studio).Removed);
        Assert.Equal(stood, Board(studio).Spots[a.Path]);

        Assert.True(studio.Panel.History.Redo());
        await studio.Built();

        Assert.Same(a, studio.Card("A.axaml"));
        Assert.Equal(new Point(spot.X, spot.Y), a.Location);
    }

    /// <summary>
    /// Форма, которая уже на доске, переезжает в точку броска, а у курсора — «передвинуть»; Ctrl+Z
    /// возвращает её на прежнее место.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_already_on_the_board_moves_to_the_drop_and_undo_brings_it_back()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("Views/B.axaml", WindowXaml("B"))));

        var a = studio.Card("A.axaml");
        var before = a.Location;
        var cursor = new Point(420, 260);
        var spot = SpotUnder(studio, cursor);
        var session = studio.Carry(a.Path.Value);

        session.Over(studio.OnSheet(cursor), KeyModifiers.None);

        Assert.Equal(Say(studio, "board.drop.move", "A.axaml"), session.Hint);

        session.Drop();
        await studio.Built();

        Assert.Equal(new Point(spot.X, spot.Y), a.Location);
        Assert.Equal(spot, Board(studio).Spots[a.Path]);

        Assert.True(studio.Panel.History!.Undo());
        await studio.Built();

        Assert.Equal(before, a.Location);
    }

    /// <summary>
    /// Несколько форм встают сеткой от курсора — первая под ним, следующие рядами, — выбранными все;
    /// заготовок столько же, сколько форм.
    /// </summary>
    [AvaloniaFact]
    public async Task Several_forms_land_in_a_grid_from_the_cursor()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(
            ("Views/A.axaml", WindowXaml("A")),
            ("Views/B.axaml", WindowXaml("B")),
            ("Views/C.axaml", ControlXaml("C"))));

        var (a, b, c) = (studio.Card("A.axaml"), studio.Card("B.axaml"), studio.Card("C.axaml"));
        var cursor = new Point(200, 150);
        var spot = SpotUnder(studio, cursor);
        var session = studio.Carry(a.Path.Value, b.Path.Value, c.Path.Value);

        session.Over(studio.OnSheet(cursor), KeyModifiers.None);

        Assert.Equal(Say(studio, "board.drop.many", 3), session.Hint);
        Assert.Equal(3, studio.View.Preview.Children.Count);

        session.Drop();
        await studio.Built();

        var step = Length(studio, "AxFormCardWidth") + Length(studio, "AxFormCardGap");

        Assert.Equal(new Point(spot.X, spot.Y), a.Location);
        Assert.Equal(new Point(spot.X + step, spot.Y), b.Location);
        Assert.Equal(spot.X, c.Location.X);
        Assert.True(c.Location.Y > a.Location.Y, "третья форма не встала вторым рядом");
        Assert.Equal([a, b, c], studio.View.Sheet.Selection.SelectedItems.OfType<FormCard>());
    }

    /// <summary>
    /// Не формы доска не берёт и говорит почему: среди несомого нет разметки — или есть, но это не форма
    /// решения, как разметка приложения. Заготовок при этом нет.
    /// </summary>
    [AvaloniaFact]
    public async Task What_is_not_a_form_of_the_solution_is_refused_and_said_why()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A")), ("App.axaml", ApplicationXaml)));

        var at = studio.OnSheet(new Point(300, 200));
        var code = Path.Combine(studio.Root, "Forms", "src", "App", "Program.cs");
        var outside = Path.Combine(studio.Root, "Elsewhere.axaml");
        var app = studio.PathOf("App.axaml").Value;

        Assert.Equal((DragDropEffects.None, studio.Strings["board.drop.none"]), Ask(studio.Carry(code), at));
        Assert.Equal((DragDropEffects.None, Say(studio, "board.drop.notForm.one", "App.axaml")), Ask(studio.Carry(app), at));
        Assert.Equal((DragDropEffects.None, studio.Strings["board.drop.notForm.many"]), Ask(studio.Carry(app, outside), at));
        Assert.Empty(studio.View.Preview.Children);
        Assert.True(studio.View.States.IsVisible);
    }

    /// <summary>
    /// Доска просит ссылку, а нет её — копию: карточка показывает файл, а не забирает его. Перенос она
    /// не берёт никогда — источник счёл бы файл забранным.
    /// </summary>
    [AvaloniaFact]
    public async Task The_board_asks_for_a_link_and_never_takes_a_move()
    {
        using var studio = new UiDesignerStudio();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A"))));

        var path = studio.Card("A.axaml").Path.Value;
        var at = studio.OnSheet(new Point(300, 200));

        Assert.Equal(DragDropEffects.Link, Ask(studio.Carry(DragDropEffects.Copy | DragDropEffects.Link, path), at).Effect);
        Assert.Equal(DragDropEffects.Copy, Ask(studio.Carry(DragDropEffects.Copy, path), at).Effect);
        Assert.Equal(DragDropEffects.None, Ask(studio.Carry(DragDropEffects.Move, path), at).Effect);
        Assert.Empty(studio.View.Preview.Children);
    }

    /// <summary>
    /// Над доской, где убрано всё, надпись на время заготовки прячется, а брошенная тяга возвращает её и
    /// уносит заготовку; поставить на закрытую доску нечего.
    /// </summary>
    [AvaloniaFact]
    public async Task A_dropped_carry_takes_its_preview_away_and_a_closed_board_takes_nothing()
    {
        using var studio = new UiDesignerStudio();

        var closed = studio.Carry(Path.Combine(studio.Root, "A.axaml"));

        Assert.Equal(DragDropEffects.None, closed.Over(studio.OnSheet(new Point(10, 10)), KeyModifiers.None));
        Assert.Null(closed.Hint);
        closed.Dispose();

        await studio.Open(studio.Solution(("Views/A.axaml", WindowXaml("A"))));

        var a = studio.Card("A.axaml");

        Remove(studio, a);

        Assert.True(studio.Model.IsAllRemoved);

        var session = studio.Carry(a.Path.Value);

        session.Over(studio.OnSheet(new Point(300, 200)), KeyModifiers.None);

        Assert.False(studio.View.States.IsVisible);
        Assert.Single(studio.View.Preview.Children);

        session.Dispose();

        Assert.True(studio.View.States.IsVisible);
        Assert.Empty(studio.View.Preview.Children);
        Assert.DoesNotContain(a, studio.Model.Cards);
    }

    /// <summary>
    /// Сквозная дорога: форму берут мышью в дереве окна проекта и несут в окно дизайнера поверх него —
    /// курсор говорит «ссылка», и карточка встаёт под ним, а файлы решения окно проекта не трогает.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_carried_from_the_project_window_lands_on_the_board()
    {
        var windows = new List<TopLevel>();
        var drags = new StudioDrags(() => windows, new StudioLog());
        var files = new FilesProbe();

        using var project = new ProjectWindowStudio(files: files, drags: drags);
        using var board = new UiDesignerStudio(drags: drags);

        var snapshot = project.Solution();
        var app = snapshot.Projects.Single(each => each.Name == "App");
        var form = app.Items.Single(item => item.FullPath.FileName == "MainWindow.axaml").FullPath;

        File.WriteAllText(form.Value, WindowXaml("MainWindow"));
        File.WriteAllText(app.Items.Single(item => item.FullPath.FileName == "App.axaml").FullPath.Value, ApplicationXaml);

        await project.Open(snapshot);
        await board.Open(snapshot);

        // Дизайнер — окно поверх окна проекта: оторванное, как его отрывают от главного.
        board.Window.Position = project.Window.Position;
        windows.Add(board.Window);
        windows.Add(project.Window);

        var card = board.Card("MainWindow.axaml");

        Remove(board, card);
        project.Model.Tree.Expand(project.Row("Views"));
        Frame();

        project.Grab(project.Item(project.Row("MainWindow.axaml")));

        var cursor = new Point(260, 300);
        var spot = SpotUnder(board, board.Window.TranslatePoint(cursor, board.View.Sheet)!.Value);

        project.Carry(cursor);

        Assert.Equal(DragDropEffects.Link, project.Panel.Drag!.Effect);
        Assert.Equal(Say(board, "board.drop.return", "MainWindow.axaml"), drags.Ghost!.Hint);
        Assert.Single(board.View.Preview.Children);

        project.Window.MouseUp(cursor, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await board.Built();

        Assert.Same(card, board.Card("MainWindow.axaml"));
        Assert.Equal(new Point(spot.X, spot.Y), card.Location);
        Assert.Null(drags.Current);
        Assert.Empty(files.Moved);
        Assert.Empty(files.Copied);
    }

    /// <summary>Убирает карточку с доски, как Delete по выбранной.</summary>
    private static void Remove(UiDesignerStudio studio, FormCard card)
    {
        var sheet = studio.View.Sheet;

        sheet.Selection.Clear();
        sheet.Selection.Select(studio.Model.Cards.IndexOf(card));
        sheet.Focus();
        Dispatcher.UIThread.RunJobs();
        studio.Press(sheet, Key.Delete);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Спрашивает доску одним движением и закрывает тягу: ответ и подсказка.</summary>
    private static (DragDropEffects Effect, string? Hint) Ask(IStudioDragSession session, Point at)
    {
        var effect = session.Over(at, KeyModifiers.None);
        var hint = session.Hint;

        session.Dispose();

        return (effect, hint);
    }

    /// <summary>Место, куда встанет первая карточка под курсором: серединой под него, в целых точках.</summary>
    private static Spot SpotUnder(UiDesignerStudio studio, Point inSheet)
    {
        var world = studio.View.Sheet.GetWorldPosition(inSheet);

        return new Spot(
            Math.Round(world.X - Length(studio, "AxFormCardWidth") / 2),
            Math.Round(world.Y - Length(studio, "AxFormCardMinHeight") / 2));
    }

    private static double Length(UiDesignerStudio studio, string key) => SheetControls.LengthOf(studio.View, key);

    private static string Say(UiDesignerStudio studio, string key, object value) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, studio.Strings[key], value);

    private static BoardData Board(UiDesignerStudio studio) =>
        BoardFile.Read(studio.BoardFile, CanonicalPath.Create(Path.Combine(studio.Root, "Forms")));
}
