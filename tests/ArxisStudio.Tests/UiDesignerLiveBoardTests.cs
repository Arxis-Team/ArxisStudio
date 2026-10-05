using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Surface.UiDesigner;
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

    private static FormCard Card(BoardPanel board, string name) => board.Model!.Cards.Single(card => card.Name == name);

    private static UiDesignerFormItem Item(BoardPanel board, FormCard card) =>
        Assert.IsType<UiDesignerFormItem>(board.View!.Sheet.ContainerFromItem(card));

    private static Button Go(BoardPanel board) =>
        Item(board, Card(board, "MainWindow.axaml")).GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Go");

    private static string Text(FormSession session) => session.Document!.Syntax.SourceText.ToString();

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
