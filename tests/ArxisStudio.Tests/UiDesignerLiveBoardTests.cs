using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Живая доска: все формы решения на одном холсте, и те, что на виду, правят так же, как во вкладке, — с
/// именем над каждой, за которое форму берут целиком.
/// </summary>
/// <remarks>
/// Службы проектов и XAML настоящие (<see cref="LiveFormStudio"/>), формы стоят на встроенных контролах:
/// поколению хватает Avalonia самой студии. Окно 1200 × 800 вмещает обе формы стенда целиком, и
/// «Показать всё» ставит их на холст в натуральную величину.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class UiDesignerLiveBoardTests
{
    private const string Main = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" MinHeight="30" />
            <TextBox x:Name="Input" Text="поле" Width="200" Height="24" />
          </StackPanel>
        </Window>
        """;

    private const string Badge = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Width="200" Height="120">
          <Border x:Name="Frame" Width="160" Height="80" Background="Gray" />
        </UserControl>
        """;

    /// <summary>
    /// Формы на виду встают живыми — корень показа на своей карточке, объявленное предложено к выбору, — и
    /// над каждой стоит её имя.
    /// </summary>
    [AvaloniaFact]
    public async Task Forms_in_view_stand_live_with_their_names_above()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var badge = Card(board, "Badge.axaml");

        Assert.True(board.Forms!.IsLive(main), "окно не встало живым");
        Assert.True(board.Forms.IsLive(badge), "элемент не встал живым");
        Assert.IsType<Window>(Item(board, main).Root);
        Assert.IsType<UserControl>(Item(board, badge).Root);
        Assert.Equal("MainWindow.axaml", Item(board, main).Caption);
        Assert.Equal("Badge.axaml", Item(board, badge).Caption);
        Assert.True(SurfaceLayout.GetIsTracked(Go(board)), "кнопка формы не предложена к выбору");
        Assert.Equal(SurfaceContentMode.Annotated, Item(board, main).ContentMode);

        // Имя — текст студии: второстепенный, у выбранной формы — цветом ссылки.
        var sheet = board.View!.Sheet;
        var name = Item(board, main).GetVisualDescendants().OfType<TextBlock>().Single(part => part.Name == "PART_CaptionText");

        Assert.True(sheet.TryFindResource("AxTextSecondaryBrush", sheet.ActualThemeVariant, out var secondary));
        Assert.True(sheet.TryFindResource("AxLinkBrush", sheet.ActualThemeVariant, out var link));
        Assert.Same(secondary, name.Foreground);

        Press(studio, Caption(Item(board, main)));

        Assert.Same(link, name.Foreground);
    }

    /// <summary>
    /// Форма, ушедшая с виду, отдаёт показ — на её карточке снова нет корня, — а вернувшаяся встаёт живой
    /// снова.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_out_of_view_lets_its_show_go_and_stands_again_when_back()
    {
        await using var studio = new LiveFormStudio(hideDelay: TimeSpan.Zero);
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var main = Card(board, "MainWindow.axaml");
        var shown = sheet.ViewportLocation;

        sheet.ViewportLocation = new Point(100_000, 100_000);
        LiveFormStudio.Frame();
        board.Sight!.Update();

        Assert.Empty(board.Sight.Seen);

        await XamlStudio.UntilAsync(() => Item(board, main).Root is null, "ушедшая с виду форма держит корень");
        await XamlStudio.UntilAsync(() => board.Forms!.Sessions.Count == 0, "нечего беречь — а документы не отпущены");

        Assert.False(board.Forms!.IsLive(main));

        sheet.ViewportLocation = shown;
        await LiveFormStudio.UntilLiveAsync(board);

        Assert.True(board.Forms.IsLive(main), "вернувшаяся форма не встала живой");
        Assert.IsType<Window>(Item(board, main).Root);
    }

    /// <summary>
    /// Форма, в которой выбрано, показ не отдаёт и вне вида — выбранное можно двигать стрелками, — а
    /// выбор, ушедший из неё, отпускает и её.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_holding_the_selection_keeps_its_show_until_the_selection_leaves()
    {
        await using var studio = new LiveFormStudio(hideDelay: TimeSpan.Zero);
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var main = Card(board, "MainWindow.axaml");
        var badge = Card(board, "Badge.axaml");

        Assert.True(sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");

        sheet.ViewportLocation = new Point(100_000, 100_000);
        LiveFormStudio.Frame();
        board.Sight!.Update();

        await XamlStudio.UntilAsync(() => Item(board, badge).Root is null, "соседняя форма вне вида держит корень");

        Assert.True(board.Forms!.IsLive(main), "форма с выбранным отдала показ");

        sheet.SelectedItems!.Clear();
        LiveFormStudio.Frame();

        await XamlStudio.UntilAsync(() => Item(board, main).Root is null, "выбор ушёл, а форма вне вида держит корень");
    }

    /// <summary>
    /// Правка на доске ложится в текст формы, а Ctrl+Z на доске её отменяет: история у доски одна — места
    /// форм и правки их текста.
    /// </summary>
    [AvaloniaFact]
    public async Task An_edit_on_the_board_writes_the_form_and_ctrl_z_takes_it_back()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var session = board.Forms!.SessionOf(Card(board, "MainWindow.axaml"))!;

        Assert.True(sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Right, KeyModifiers.Alt);

        await XamlStudio.UntilAsync(() => Text(session).Contains("Width=\"121\"", StringComparison.Ordinal), "ширина не записана");

        Assert.True(board.History!.CanUndo, "правка формы не легла в историю доски");

        LiveFormStudio.Press(sheet, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(session) == Main, "Ctrl+Z на доске не вернул текст формы");
    }

    /// <summary>
    /// Delete внутри формы убирает элемент из её текста и оставляет форму на доске; форма, взятая целиком за
    /// имя, уходит с доски, а файл её остаётся.
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_inside_removes_the_element_and_on_the_name_removes_the_form_from_the_board()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var main = Card(board, "MainWindow.axaml");
        var session = board.Forms!.SessionOf(main)!;

        Assert.True(sheet.SelectTarget(Go(board)));

        sheet.Focus();
        LiveFormStudio.Press(sheet, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(session).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка осталась в тексте");

        Assert.Contains(main, board.Model!.Cards);

        Press(studio, Caption(Item(board, main)));

        Assert.Same(Item(board, main), Assert.Single(sheet.SelectedTargets).Target);

        LiveFormStudio.Press(sheet, Key.Delete);

        Assert.DoesNotContain(main, board.Model.Cards);
        Assert.True(File.Exists(main.Path.Value), "убранная с доски форма потеряла файл");
    }

    /// <summary>
    /// Форму тянут за имя: она едет по холсту, её место — запись истории доски, и Ctrl+Z ставит её назад.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_dragged_by_its_name_moves_and_ctrl_z_puts_it_back()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var badge = Card(board, "Badge.axaml");
        var before = badge.Location;
        var from = Middle(studio, Caption(Item(board, badge)));

        studio.Window.MouseDown(from, MouseButton.Left);
        studio.Window.MouseMove(from + new Vector(10, 10));
        studio.Window.MouseMove(from + new Vector(80, 60));
        studio.Window.MouseUp(from + new Vector(80, 60), MouseButton.Left);
        LiveFormStudio.Frame();

        Assert.NotEqual(before, badge.Location);
        Assert.True(badge.Location.X > before.X && badge.Location.Y > before.Y, $"форма уехала не туда: {before} → {badge.Location}");

        LiveFormStudio.Press(sheet, Key.Z, KeyModifiers.Control);

        Assert.Equal(before, badge.Location);
    }

    /// <summary>
    /// XAML под доской — формы, с которой работают: пока её нет, вместо текста подсказка; выбрали на
    /// форме — её текст с отметкой выбранного и её имя над ним; выбрали на другой — текст другой.
    /// </summary>
    [AvaloniaFact]
    public async Task The_xaml_under_the_board_is_the_xaml_of_the_form_being_worked_on()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var main = board.Forms!.SessionOf(Card(board, "MainWindow.axaml"))!;
        var badge = Card(board, "Badge.axaml");

        Assert.True(view.CodeHint.IsVisible, "без формы, с которой работают, подсказки нет");
        Assert.False(view.Code.IsVisible, "под подсказкой стоит пустой просмотр");
        Assert.Equal(string.Empty, view.Code.Text ?? string.Empty);

        Assert.True(view.Sheet.SelectTarget(Go(board)));

        await XamlStudio.UntilAsync(() => view.Code.Text == Main, "XAML формы с выбранным не показан");

        Assert.Equal("MainWindow.axaml", view.CodeOf.Text);
        Assert.False(view.CodeHint.IsVisible, "подсказка осталась над текстом");
        Assert.True(view.Code.IsVisible, "текст формы не показан");
        Assert.Equal(Range(main, "Go"), view.Code.Highlight);

        var frame = Item(board, badge).GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Frame");

        Assert.True(view.Sheet.SelectTarget(frame));

        await XamlStudio.UntilAsync(() => view.Code.Text == Badge, "XAML не перешёл к другой форме");

        Assert.Equal("Badge.axaml", view.CodeOf.Text);
    }

    /// <summary>
    /// Форма, убранная с доски, уносит с собой и свой XAML: работать с ней больше нельзя, хотя её сессия
    /// ещё ждёт, не вернут ли форму. Под доской — текст той, с которой работают теперь: здесь оставшейся
    /// единственной.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_taken_off_the_board_takes_its_xaml_with_it()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var main = Card(board, "MainWindow.axaml");

        Press(studio, Caption(Item(board, main)));

        await XamlStudio.UntilAsync(() => view.Code.Text == Main, "XAML взятой формы не показан");

        LiveFormStudio.Press(view.Sheet, Key.Delete);

        Assert.DoesNotContain(main, board.Model!.Cards);

        await XamlStudio.UntilAsync(() => view.Code.Text == Badge, "XAML убранной формы остался под доской");

        Assert.Equal("Badge.axaml", view.CodeOf.Text);
    }

    /// <summary>
    /// Форма, выбранная раньше, чем встала живой, — открытая на доске из окна проекта, — становится той, с
    /// которой работают, когда встанет: выбор её карточки ждёт её, а XAML прежней формы под доской не
    /// остаётся.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_selected_before_it_stands_live_is_worked_on_once_it_does()
    {
        await using var studio = new LiveFormStudio(hideDelay: TimeSpan.Zero);
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var badge = Card(board, "Badge.axaml");

        studio.Context.Settings.Set(UiDesignerModule.TabsKey, false);

        // Работают с окном, а значок уходит с доски — и его сессия вместе с ним.
        Assert.True(view.Sheet.SelectTarget(Go(board)));

        await XamlStudio.UntilAsync(() => view.Code.Text == Main, "XAML окна не показан");

        board.Model!.Remove([badge]);

        await XamlStudio.UntilAsync(() => board.Forms!.SessionOf(badge) is null, "сессия убранного значка осталась");

        Assert.True(await studio.Editor().RevealAsync(badge.Path.Value), "значок не показан на доске");

        await XamlStudio.UntilAsync(() => view.Code.Text == Badge, "XAML вставшего значка не показан");

        Assert.Equal("Badge.axaml", view.CodeOf.Text);
        Assert.Same(Item(board, Card(board, "Badge.axaml")), Assert.Single(view.Sheet.SelectedTargets).Target);
    }

    /// <summary>
    /// Карточка формы, которая ещё не встала живой, остаётся выбранной, пока холст переносит на доску выбор
    /// других форм: правка соседней формы её выбор не снимает.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_selected_while_it_waits_stays_selected_when_its_neighbour_changes()
    {
        await using var studio = new LiveFormStudio(hideDelay: TimeSpan.Zero);
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var badge = Card(board, "Badge.axaml");
        var main = board.Forms!.SessionOf(Card(board, "MainWindow.axaml"))!;

        // Значок уходит с доски вместе с сессией и возвращается снимком: его держит вкладка на экране рядом.
        board.Model!.Remove([badge]);

        await XamlStudio.UntilAsync(() => board.Forms.SessionOf(badge) is null, "сессия убранного значка осталась");

        var (tab, place) = await BesideAsync(studio, board, badge);

        try
        {
            board.Model.Return([badge.Path]);
            LiveFormStudio.Frame();
            board.Sight!.Update();
            LiveFormStudio.Frame();

            var item = Item(board, Card(board, "Badge.axaml"));

            Assert.True(view.Sheet.SelectTarget(item), "карточку значка не выбрать");
            Assert.False(board.Forms.IsLive(Card(board, "Badge.axaml")), "значок встал живым, хотя его держит вкладка на экране");

            await main.Document!.EditAsync(
                "Ширина", editor => editor.SetAttribute(editor.Document.Root!, XamlQualifiedName.Unprefixed("Width"), "420"),
                TestContext.Current.CancellationToken);

            await XamlStudio.UntilAsync(() => Text(main).Contains("Width=\"420\"", StringComparison.Ordinal), "правка окна не легла");

            LiveFormStudio.Frame();

            Assert.Same(item, Assert.Single(view.Sheet.SelectedTargets).Target);
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>Каретка в XAML под доской выбирает на доске элемент под собой — в форме этого текста.</summary>
    [AvaloniaFact]
    public async Task A_caret_in_the_xaml_under_the_board_selects_on_the_board()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var main = board.Forms!.SessionOf(Card(board, "MainWindow.axaml"))!;

        Assert.True(view.Sheet.SelectTarget(Go(board)));

        await XamlStudio.UntilAsync(() => view.Code.Text == Main, "XAML формы с выбранным не показан");

        var inside = Main.IndexOf("Text=\"поле\"", StringComparison.Ordinal);

        view.Code.Focus();
        view.Code.CaretOffset = inside;
        LiveFormStudio.Press(view.Code, Key.Right);

        var input = Item(board, Card(board, "MainWindow.axaml")).GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Input");

        Assert.Same(input, view.Sheet.SelectedTargets.Single().Target);
        Assert.Equal(Range(main, "Input"), view.Code.Highlight);
        Assert.True(view.Code.IsKeyboardFocusWithin, "каретка в XAML увела клавиатуру на доску");
    }

    /// <summary>
    /// Вид доски — тот же, что у вкладок: один XAML прячет холст и отдаёт клавиатуру тексту, а выбор
    /// записывается настройкой для следующих.
    /// </summary>
    [AvaloniaFact]
    public async Task The_board_switches_its_view_like_a_tab()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;

        Assert.Equal(FormViewMode.Split, board.Modes!.Mode);
        Assert.True(view.Stage.IsVisible && view.CodePane.IsVisible, "разделение показывает не холст и XAML");

        view.Mode.SelectedIndex = (int)FormViewMode.Xaml;
        LiveFormStudio.Frame();

        Assert.False(view.Stage.IsVisible, "в виде XAML холст остался");
        Assert.True(view.CodePane.IsVisible);
        Assert.Same(view.Code, board.FocusTarget);
        Assert.Equal("xaml", studio.Context.Settings.Get<string>(UiDesignerModule.ViewKey));

        view.Mode.SelectedIndex = (int)FormViewMode.Design;
        LiveFormStudio.Frame();

        Assert.True(view.Stage.IsVisible);
        Assert.False(view.CodePane.IsVisible, "в виде дизайна XAML остался");
        Assert.Same(view.Sheet, board.FocusTarget);
    }

    /// <summary>
    /// Вкладка забирает форму у доски — показ у документа один, — и на доске она стоит снимком; закрытая
    /// вкладка отдаёт её назад, и форма на виду встаёт живой снова.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_takes_the_form_from_the_board_and_gives_it_back_when_closed()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var (view, error) = await studio.Editor().OpenAsync(main.Path.Value);

        Assert.Null(error);

        var tab = Assert.IsType<LiveFormDocument>(view);
        var beside = new Window { Width = 800, Height = 600, Content = tab.Content };

        beside.Show();

        try
        {
            await tab.Opening;
            await XamlStudio.UntilAsync(() => tab.Form.Root is not null, "вкладка не взяла форму");
            await XamlStudio.UntilAsync(() => Item(board, main).Root is null, "доска не отдала форму вкладке");

            Assert.False(board.Forms!.IsLive(main));
            Assert.True(board.Forms.IsLive(Card(board, "Badge.axaml")), "соседняя форма доски перестала быть живой");
        }
        finally
        {
            beside.Content = null;
            beside.Close();
            await tab.DisposeAsync();
        }

        await XamlStudio.UntilAsync(() => board.Forms!.IsLive(main), "закрытая вкладка не вернула форму доске");
    }

    /// <summary>
    /// Вкладка, ушедшая с экрана, — в группе выбрали доску, — отдаёт форму доске, у которой та на виду: форма на
    /// доске живая, и файл, переписанный другим редактором, виден на ней сразу, без переключений.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_off_screen_gives_its_form_to_the_board_in_view()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var (tab, place) = await BesideAsync(studio, board, main);

        try
        {
            await XamlStudio.UntilAsync(() => Item(board, main).Root is null, "доска не отдала форму вкладке на экране");

            Away(place);

            await XamlStudio.UntilAsync(() => board.Forms!.IsLive(main), "доска не взяла форму у вкладки, ушедшей с экрана");

            Assert.Null(tab.Shown);
            Assert.Null(tab.Form.Root);

            // Так пишет файл Rider: мимо студии и целиком.
            File.WriteAllText(main.Path.Value, Main.Replace("Width=\"400\"", "Width=\"420\"", StringComparison.Ordinal));

            await XamlStudio.UntilAsync(() => Item(board, main).Root is Window { Width: 420d }, "правка снаружи не видна на доске");
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>
    /// Вкладка, вернувшаяся на экран, забирает форму у доски, и выбранное в ней до ухода выбрано снова: выбор
    /// вкладка помнит путями.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_back_on_screen_takes_its_form_back_with_its_selection()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var (tab, place) = await BesideAsync(studio, board, main);

        try
        {
            var go = PathTo(tab, "Go");

            tab.Select([go]);
            LiveFormStudio.Frame();

            Away(place);

            await XamlStudio.UntilAsync(() => board.Forms!.IsLive(main), "доска не взяла форму у вкладки, ушедшей с экрана");

            Back(place, tab);

            await XamlStudio.UntilAsync(() => tab.Form.Root is not null && !board.Forms!.IsLive(main), "вкладка не забрала форму назад");
            LiveFormStudio.Frame();

            Assert.Null(board.Forms!.SessionOf(main)?.Problem);
            Assert.Equal(go, Assert.Single(tab.Selection));
            Assert.Equal("Go", Assert.IsType<Button>(Assert.Single(tab.View.Sheet.SelectedTargets).Target).Name);
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>
    /// Док переставляет вкладку — снимает и ставит за один проход, как на щелчке по соседней вкладке, — и форма
    /// остаётся у неё: тот же показ, а доска его не получала.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_the_dock_replants_in_one_pass_keeps_its_show()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var (tab, place) = await BesideAsync(studio, board, main);

        try
        {
            var shown = Assert.IsAssignableFrom<IXamlDesignView>(tab.Shown);

            place.Content = null;
            place.Content = tab.Content;
            LiveFormStudio.Frame();
            LiveFormStudio.Frame();

            Assert.Same(shown, tab.Shown);
            Assert.Null(board.Forms!.SessionOf(main)?.Shown);
            Assert.False(board.Forms.IsLive(main), "форма ушла на доску, пока док переставлял вкладку");
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>
    /// Вкладка, ушедшая с экрана, держит форму, пока доска её не видит: вернулась — форма та же, а не построена
    /// заново.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_off_screen_keeps_its_show_while_no_board_shows_it()
    {
        await using var studio = new LiveFormStudio();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var sheet = board.View!.Sheet;
        var (tab, place) = await BesideAsync(studio, board, main);

        try
        {
            var shown = Assert.IsAssignableFrom<IXamlDesignView>(tab.Shown);

            sheet.ViewportLocation = new Point(100_000, 100_000);
            LiveFormStudio.Frame();
            board.Sight!.Update();

            Assert.Empty(board.Sight.Seen);

            Away(place);

            Assert.Same(shown, tab.Shown);

            Back(place, tab);

            Assert.Same(shown, tab.Shown);
            Assert.Same(shown.Root, tab.Form.Root);
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>
    /// Форма, переходящая со вкладки на доску и назад, встаёт на новом месте одетой: корень ложится на карточку,
    /// когда приложение формы уже стоит, а не на миг без его стилей.
    /// </summary>
    [AvaloniaFact]
    public async Task A_form_moving_between_tab_and_board_comes_with_its_application()
    {
        await using var studio = new LiveFormStudio();

        studio.Xaml.Write("App.axaml", """<Application xmlns="https://github.com/avaloniaui" />""");

        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var item = Item(board, main);

        await XamlStudio.UntilAsync(() => item.ApplicationRoot is not null, "приложение формы не встало на доске");

        var (tab, place) = await BesideAsync(studio, board, main);
        var bare = new List<string>();

        void Watch(UiDesignerFormItem card, string where) =>
            card.PropertyChanged += (_, e) =>
            {
                if (e.Property == UiDesignerFormItem.RootProperty && card.Root is not null && card.ApplicationRoot is null)
                    bare.Add(where);
            };

        try
        {
            Assert.NotNull(tab.Form.ApplicationRoot);

            Watch(item, "доска");
            Watch(tab.Form, "вкладка");

            Away(place);

            await XamlStudio.UntilAsync(() => board.Forms!.IsLive(main), "доска не взяла форму у вкладки, ушедшей с экрана");

            Back(place, tab);

            await XamlStudio.UntilAsync(() => tab.Form.Root is not null && !board.Forms!.IsLive(main), "вкладка не забрала форму назад");

            Assert.Empty(bare);
            Assert.NotNull(tab.Form.ApplicationRoot);
        }
        finally
        {
            await CloseAsync(tab, place);
        }
    }

    /// <summary>
    /// Вкладка ушла с экрана и вернулась, пока её показ ещё шёл, — доска успела попросить форму: показы
    /// не сталкиваются, форма встаёт во вкладке, а ни вкладка, ни доска не говорят, что она не открылась.
    /// </summary>
    [AvaloniaFact]
    public async Task A_tab_that_leaves_while_its_show_comes_still_stands_when_back()
    {
        var parking = false;
        var parked = new TaskCompletionSource();
        var park = new TaskCompletionSource();

        await using var studio = new LiveFormStudio(formShown: async (rank, token) =>
        {
            if (rank != FormShowRank.Tab || !parking)
                return;

            parking = false;
            parked.TrySetResult();
            await park.Task.WaitAsync(token);
        });
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var main = Card(board, "MainWindow.axaml");
        var (view, error) = await studio.Editor().OpenAsync(main.Path.Value);

        Assert.Null(error);

        var tab = Assert.IsType<LiveFormDocument>(view);

        await tab.Opening;

        parking = true;

        var place = Place(studio, board);

        place.Content = tab.Content;
        LiveFormStudio.Frame();

        try
        {
            await XamlStudio.UntilAsync(() => parked.Task.IsCompleted, "показ вкладки не встал на стоянку");

            // Показ вкладки уже занял документ: вкладка уходит, доска просит форму, вкладка возвращается.
            Away(place);
            Back(place, tab);

            park.TrySetResult();

            await XamlStudio.UntilAsync(() => tab.Form.Root is not null, "вкладка не встала, вернувшись");
            LiveFormStudio.Frame();

            Assert.False(board.Forms!.IsLive(main), "форма живая и на доске");
            Assert.Null(board.Forms.SessionOf(main)?.Problem);
            Assert.False(tab.View.Notice.IsVisible, $"вкладка говорит о сбое: {tab.View.NoticeText.Text}");
        }
        finally
        {
            park.TrySetResult();
            await CloseAsync(tab, place);
        }
    }

    /// <summary>Формы на виду встают живыми на любом масштабе: и на десятой доле — не снимками.</summary>
    [AvaloniaFact]
    public async Task Forms_stand_live_at_any_zoom()
    {
        await using var studio = new LiveFormStudio(hideDelay: TimeSpan.Zero);
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var view = board.View!;
        var badge = Card(board, "Badge.axaml");

        board.Model!.Remove([badge]);

        await XamlStudio.UntilAsync(() => board.Forms!.SessionOf(badge) is null, "сессия убранного значка осталась");

        view.Sheet.ViewportZoom = 0.1;
        board.Model.Return([badge.Path]);

        await LiveFormStudio.UntilLiveAsync(board);

        Assert.True(board.Forms!.IsLive(Card(board, "Badge.axaml")), "значок на мелком масштабе встал снимком");
        Assert.True(board.Forms.IsLive(Card(board, "MainWindow.axaml")));
    }

    private static FormCard Card(BoardPanel board, string name) => board.Model!.Cards.Single(card => card.Name == name);

    /// <summary>
    /// Ставит вкладку формы рядом с доской — в том же окне, как вторую группу дока, — и ждёт, пока вкладка
    /// возьмёт форму.
    /// </summary>
    private static async Task<(LiveFormDocument Tab, ContentControl Place)> BesideAsync(LiveFormStudio studio, BoardPanel board, FormCard card)
    {
        var (view, error) = await studio.Editor().OpenAsync(card.Path.Value);

        Assert.Null(error);

        var tab = Assert.IsType<LiveFormDocument>(view);
        var place = Place(studio, board);

        place.Content = tab.Content;
        LiveFormStudio.Frame();

        await tab.Opening;
        await XamlStudio.UntilAsync(() => tab.Form.Root is not null, "вкладка не взяла форму");
        LiveFormStudio.Frame();

        return (tab, place);
    }

    /// <summary>Место вкладки справа от доски — в том же окне, а доска остаётся своего размера.</summary>
    /// <remarks>
    /// Окно одно. Контрол, перенесённый из окна в окно за один проход, Avalonia 12 оставляет в очереди раскладки
    /// прежнего окна, а <see cref="Back"/> ставит вид формы в пустое место, а не меняет содержимое, как группа
    /// дока: вкладка в чужом окне забрала бы форму у доски, окно доски дошло бы до неё раньше, чем её разложит
    /// окно вкладки, и упало бы «wrong LayoutManager». В студии переход держит порядок проходов дока — CLAUDE.md,
    /// у доски.
    /// </remarks>
    private static ContentControl Place(LiveFormStudio studio, BoardPanel board)
    {
        var place = new ContentControl();
        var both = new Grid { ColumnDefinitions = new ColumnDefinitions("1200,800") };

        Grid.SetColumn(place, 1);
        studio.Window.Content = null;
        both.Children.Add(board.Content);
        both.Children.Add(place);
        studio.Window.Width = 2000;
        studio.Window.Content = both;
        LiveFormStudio.Frame();

        return place;
    }

    /// <summary>Вкладка уходит с экрана — так её прячет док, выбрав в группе другую, — и проходит раскладка.</summary>
    private static void Away(ContentControl place)
    {
        place.Content = null;
        LiveFormStudio.Frame();
    }

    /// <summary>Вкладка снова на экране.</summary>
    private static void Back(ContentControl place, LiveFormDocument tab)
    {
        place.Content = tab.Content;
        LiveFormStudio.Frame();
    }

    /// <summary>Закрывает вкладку.</summary>
    private static async Task CloseAsync(LiveFormDocument tab, ContentControl place)
    {
        place.Content = null;
        await tab.DisposeAsync();
    }

    private static XamlElementPath PathTo(LiveFormDocument tab, string name) =>
        XamlElementPath.Of(tab.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name));

    private static UiDesignerFormItem Item(BoardPanel board, FormCard card) =>
        Assert.IsType<UiDesignerFormItem>(board.View!.Sheet.ContainerFromItem(card));

    private static Button Go(BoardPanel board) =>
        Item(board, Card(board, "MainWindow.axaml")).GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Go");

    private static string Text(FormSession session) => session.Document!.Syntax.SourceText.ToString();

    /// <summary>Диапазон элемента в тексте формы — от открывающего тега до закрывающего.</summary>
    private static AxCodeRange Range(FormSession session, string name)
    {
        var element = session.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);

        return new AxCodeRange(element.Span.Start, element.Span.Length);
    }

    private static Control Caption(UiDesignerFormItem item) =>
        item.GetVisualDescendants().OfType<Control>().Single(part => part.Name == "PART_Caption");

    private static Point Middle(LiveFormStudio studio, Visual part) =>
        part.TranslatePoint(new Point(part.Bounds.Width / 2, part.Bounds.Height / 2), studio.Window)!.Value;

    /// <summary>Нажимает и отпускает мышь посреди части — щелчок, как его даёт окно.</summary>
    private static void Press(LiveFormStudio studio, Visual part)
    {
        var at = Middle(studio, part);

        studio.Window.MouseDown(at, MouseButton.Left);
        studio.Window.MouseUp(at, MouseButton.Left);
        LiveFormStudio.Frame();
    }
}
