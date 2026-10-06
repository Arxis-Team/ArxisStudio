using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Иерархия дизайнера: дерево формы, с которой работают, — у вкладки её форма, у доски форма главного выбранного.
/// Выбор общий с холстом, Delete и перестановка — правки документа, отмена — история того, кто держит холст.
/// </summary>
/// <remarks>
/// Иерархия стоит в том же окне, что вкладки и доска (<see cref="LiveFormStudio.Hierarchy"/>), — слева, как панель
/// дока.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class DesignerHierarchyTests
{
    private const string Main = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Window.Styles>
            <Style Selector="TextBlock.note" />
          </Window.Styles>
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" MinHeight="30" />
            <TextBox Name="Input" Text="поле" Width="200" Height="24" />
            <TextBlock Text="Подпись" />
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

    private static XamlElementPath Panel => XamlElementPath.Parse("/0");

    private static XamlElementPath First => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Second => XamlElementPath.Parse("/0/1");

    private static XamlElementPath Third => XamlElementPath.Parse("/0/2");

    /// <summary>
    /// Без дизайнера на экране иерархия говорит, что показать нечего; у вкладки — строки её формы: тип и имя, без
    /// элементов-свойств. Выбранное на холсте выбрано в дереве, выбранное в дереве — на холсте и в XAML.
    /// </summary>
    [AvaloniaFact]
    public async Task The_hierarchy_shows_the_form_in_the_tab_and_shares_its_selection()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var view = panel.View!;

        Assert.True(view.Empty.IsVisible, "пустая иерархия молчит");
        Assert.Equal(studio.Strings["hierarchy.empty"], view.Empty.Text);
        Assert.False(view.Header.IsVisible);

        var tab = await studio.OpenAsync("MainWindow.axaml", Main);

        await RowsAsync(panel);

        var root = Assert.Single(panel.Nodes);
        var stack = Assert.Single(root.Children);

        Assert.Same(tab.Canvas, panel.Canvas);
        Assert.Equal("Window", root.Type);
        Assert.Equal(("StackPanel", "Panel"), (stack.Type, stack.Name));
        Assert.Equal(["Button", "TextBox", "TextBlock"], stack.Children.Select(node => node.Type));
        Assert.Equal(new string?[] { "Go", "Input", null }, stack.Children.Select(node => node.Name));
        Assert.Equal("MainWindow.axaml", view.FormOf.Text);
        Assert.True(view.Header.IsVisible);
        Assert.False(view.Empty.IsVisible);

        // Выбранное на холсте раскрывает дорогу к своей строке, даже свёрнутую человеком.
        Node(panel, Panel).IsExpanded = false;

        Assert.True(tab.View.Sheet.SelectTarget(Live(tab, First)), "кнопку не выбрать на холсте");
        LiveFormStudio.Frame();

        Assert.Equal([First], Selected(panel));
        Assert.True(Node(panel, Panel).IsExpanded, "дорога к выбранному на холсте не раскрылась");

        view.Tree.SelectedItem = Node(panel, Second);
        LiveFormStudio.Frame();

        Assert.Equal([Second], tab.Selection);
        Assert.Same(Live(tab, Second), Assert.Single(tab.View.Sheet.SelectedTargets).Target);
        Assert.Equal(Range(tab, "Input"), tab.View.Code.Highlight);
    }

    /// <summary>
    /// Выбранная в дереве строка выбирает элемент на холсте, а клавиатура остаётся в дереве: следующая стрелка —
    /// шаг по строкам, а не сдвиг выбранного.
    /// </summary>
    [AvaloniaFact]
    public async Task Choosing_a_row_leaves_the_keyboard_in_the_tree()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        Assert.True(Row(panel, XamlElementPath.Root).Focus(), "строка дерева не взяла клавиатуру");

        tree.SelectedItem = Node(panel, Second);
        LiveFormStudio.Frame();

        Assert.Same(Live(tab, Second), Assert.Single(tab.View.Sheet.SelectedTargets).Target);
        Assert.True(tree.IsKeyboardFocusWithin, "выбор в дереве увёл клавиатуру на холст");
        Assert.False(tab.View.Sheet.IsKeyboardFocusWithin);
        Assert.Same(tab.Canvas, panel.Canvas);
    }

    /// <summary>
    /// Delete в дереве убирает выбранный элемент из текста, и выбор переходит к его родителю; корень Delete не
    /// берёт.
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_in_the_hierarchy_takes_the_element_out_of_the_text()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        tab.Select([XamlElementPath.Root]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(tree, Key.Delete);

        Assert.False(tab.Document!.CanUndo, "Delete по корню лёг в историю");
        Assert.Equal(Main, Text(tab));

        tab.Select([First]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(tree, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(tab).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка осталась в тексте");
        await XamlStudio.UntilAsync(() => Node(panel, Panel).Children.Count == 2, "дерево не перестроилось");
        await XamlStudio.UntilAsync(() => tab.Selection.SequenceEqual([Panel]), "выбор не перешёл к родителю");

        Assert.Equal([Panel], Selected(panel));
    }

    /// <summary>
    /// Ctrl со стрелкой переставляет элемент на одного соседа одной правкой, и выбор идёт за ним — и вверх, и вниз,
    /// и в конец; у края клавиша не делает ничего.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_and_an_arrow_move_the_element_among_its_siblings()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        tab.Select([Second]);
        LiveFormStudio.Frame();

        await MoveAsync(Key.Up, ["TextBox", "Button", "TextBlock"], First);

        // Выше некуда: клавиша не правит, и следующая идёт от того же места.
        LiveFormStudio.Press(tree, Key.Up, KeyModifiers.Control);

        await MoveAsync(Key.Down, ["Button", "TextBox", "TextBlock"], Second);
        await MoveAsync(Key.Down, ["Button", "TextBlock", "TextBox"], Third);

        LiveFormStudio.Press(tree, Key.Down, KeyModifiers.Control);

        await MoveAsync(Key.Up, ["Button", "TextBox", "TextBlock"], Second);

        async Task MoveAsync(Key key, string[] order, XamlElementPath selected)
        {
            LiveFormStudio.Press(tree, key, KeyModifiers.Control);

            await XamlStudio.UntilAsync(() => Types(panel).SequenceEqual(order), $"порядок не стал {string.Join(", ", order)}");
            await XamlStudio.UntilAsync(() => tab.Selection.SequenceEqual([selected]), $"выбор не пошёл за элементом на {selected}");

            Assert.Equal([selected], Selected(panel));
        }
    }

    /// <summary>
    /// Правка из дерева перестраивает его, а клавиатура возвращается выбранной строке: Ctrl со стрелкой дважды подряд
    /// переставляет элемент дважды.
    /// </summary>
    [AvaloniaFact]
    public async Task The_keyboard_stays_in_the_tree_across_its_own_edits()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        tab.Select([Third]);
        LiveFormStudio.Frame();

        Assert.True(Row(panel, Third).Focus(), "строка не взяла клавиатуру");

        LiveFormStudio.Press(Focused(tree), Key.Up, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Types(panel).SequenceEqual(["Button", "TextBlock", "TextBox"]), "подпись не поднялась");
        await XamlStudio.UntilAsync(
            () => Focused(tree) is TreeViewItem { DataContext: HierarchyNode { Type: "TextBlock" } },
            "клавиатура не вернулась строке поднятой подписи");

        LiveFormStudio.Press(Focused(tree), Key.Up, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Types(panel).SequenceEqual(["TextBlock", "Button", "TextBox"]), "вторая клавиша не дошла");
    }

    /// <summary>
    /// Esc в иерархии ведёт выбор к родителю, как на холсте, и клавиатура идёт за выбором; на корне выбор снимается.
    /// </summary>
    [AvaloniaFact]
    public async Task Esc_in_the_hierarchy_selects_the_parent_and_the_keyboard_follows()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        tab.Select([First]);
        LiveFormStudio.Frame();

        Assert.True(Row(panel, First).Focus(), "строка не взяла клавиатуру");

        LiveFormStudio.Press(Focused(tree), Key.Escape);

        Assert.Equal([Panel], tab.Selection);
        Assert.Equal([Panel], Selected(panel));
        Assert.Same(Node(panel, Panel), Assert.IsAssignableFrom<TreeViewItem>(Focused(tree)).DataContext);

        LiveFormStudio.Press(Focused(tree), Key.Escape);

        Assert.Equal([XamlElementPath.Root], tab.Selection);

        LiveFormStudio.Press(Focused(tree), Key.Escape);

        Assert.Empty(tab.Selection);
        Assert.Empty(Selected(panel));
    }

    /// <summary>Ctrl+Z в иерархии вкладки отменяет шаг документа, а Ctrl+Y возвращает его.</summary>
    [AvaloniaFact]
    public async Task Ctrl_z_in_the_hierarchy_of_a_tab_steps_its_document()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var tab = await studio.OpenAsync("MainWindow.axaml", Main);
        var tree = panel.View!.Tree;

        await RowsAsync(panel);

        tab.Select([First]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(tree, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(tab).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка не удалена");

        LiveFormStudio.Press(tree, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(tab) == Main, "Ctrl+Z в иерархии не вернул кнопку");

        LiveFormStudio.Press(tree, Key.Y, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => !Text(tab).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "Ctrl+Y в иерархии не повторил удаление");
    }

    /// <summary>
    /// На доске Ctrl+Z в иерархии — шаг истории доски: уборка формы, сделанная после правки, отменяется первой, а
    /// правка — следующей.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_ctrl_z_in_the_hierarchy_steps_the_boards_history()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var tree = panel.View!.Tree;
        var main = Slot(board, "MainWindow.axaml");

        Assert.True(board.View!.Sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");

        await RowsAsync(panel);

        LiveFormStudio.Press(tree, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(main).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка не удалена");

        board.History!.Push(board.Model!.Remove([Card(board, "Badge.axaml")])!);

        Assert.DoesNotContain(board.Model.Cards, card => card.Name == "Badge.axaml");

        LiveFormStudio.Press(tree, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => board.Model.Cards.Any(card => card.Name == "Badge.axaml"), "Ctrl+Z в иерархии не вернул форму на доску");

        Assert.DoesNotContain("x:Name=\"Go\"", Text(main), StringComparison.Ordinal);

        LiveFormStudio.Press(tree, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(main) == Main, "второй Ctrl+Z в иерархии не вернул кнопку");
    }

    /// <summary>
    /// Свёрнутое человеком остаётся свёрнутым, когда правка перестраивает дерево и когда к форме возвращаются от
    /// другой; у другой формы те же пути своего свёрнутого не наследуют.
    /// </summary>
    [AvaloniaFact]
    public async Task What_was_collapsed_stays_collapsed_across_edits_and_forms()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var canvas = board.Canvas!;
        var main = Slot(board, "MainWindow.axaml");

        canvas.Select(main, [XamlElementPath.Root]);
        LiveFormStudio.Frame();

        await RowsAsync(panel);

        Node(panel, Panel).IsExpanded = false;

        var before = panel.Nodes[0];

        await main.Document!.EditAsync(
            "Ширина", editor => editor.SetAttribute(editor.Document.Root!, XamlQualifiedName.Unprefixed("Width"), "420"),
            TestContext.Current.CancellationToken);

        await XamlStudio.UntilAsync(() => !ReferenceEquals(panel.Nodes[0], before), "правка не перестроила дерево");
        LiveFormStudio.Frame();

        Assert.False(Node(panel, Panel).IsExpanded, "свёрнутая панель раскрылась после правки");
        Assert.True(Node(panel, XamlElementPath.Root).IsExpanded);

        canvas.Select(Slot(board, "Badge.axaml"), [XamlElementPath.Root]);
        LiveFormStudio.Frame();

        Assert.Equal("Badge.axaml", panel.View!.FormOf.Text);
        Assert.True(Node(panel, Panel).IsExpanded, "свёрнутое одной формы свернуло тот же путь в другой");

        canvas.Select(main, [XamlElementPath.Root]);
        LiveFormStudio.Frame();

        Assert.Equal("MainWindow.axaml", panel.View.FormOf.Text);
        Assert.False(Node(panel, Panel).IsExpanded, "свёрнутое забылось, когда к форме вернулись");
    }

    /// <summary>
    /// На доске иерархия — формы, с которой работают: пока её нет, подсказка, как её выбрать; выбрали на форме —
    /// её дерево и имя над ним; выбрали на другой — дерево другой. Выбор в дереве остаётся в форме иерархии.
    /// </summary>
    [AvaloniaFact]
    public async Task On_the_board_the_hierarchy_follows_the_form_being_worked_on()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var view = panel.View!;

        await XamlStudio.UntilAsync(() => ReferenceEquals(panel.Canvas, board.Canvas), "иерархия не пошла за доской");

        Assert.Empty(panel.Nodes);
        Assert.True(view.Empty.IsVisible, "без формы, с которой работают, подсказки нет");
        Assert.Equal(studio.Strings["hierarchy.pick"], view.Empty.Text);
        Assert.False(view.Header.IsVisible);

        Assert.True(sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");
        LiveFormStudio.Frame();

        Assert.Equal("MainWindow.axaml", view.FormOf.Text);
        Assert.Equal("Window", Assert.Single(panel.Nodes).Type);
        Assert.Equal([First], Selected(panel));
        Assert.False(view.Empty.IsVisible);

        Assert.True(sheet.SelectTarget(FrameOf(board)), "рамку значка не выбрать");
        LiveFormStudio.Frame();

        Assert.Equal("Badge.axaml", view.FormOf.Text);
        Assert.Equal("UserControl", Assert.Single(panel.Nodes).Type);
        Assert.Equal([Panel], Selected(panel));

        view.Tree.SelectedItem = panel.Nodes[0];
        LiveFormStudio.Frame();

        Assert.Same(Slot(board, "Badge.axaml"), board.Canvas!.Active);
        Assert.Equal([XamlElementPath.Root], board.Canvas.Selection);
    }

    /// <summary>
    /// Иерархия идёт за холстом впереди: только что открытая вкладка — впереди, дальше — тот, с которым работали
    /// последним, пока его видно. Щелчок в самой иерархии этого не меняет, перестройка дока за один проход —
    /// тоже; ушло с экрана всё — дерево пусто и говорит почему.
    /// </summary>
    [AvaloniaFact]
    public async Task The_hierarchy_follows_the_canvas_in_front()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var front = FormFront.Of(studio.Context);
        var changes = 0;

        await XamlStudio.UntilAsync(() => ReferenceEquals(panel.Canvas, board.Canvas), "иерархия не пошла за доской");

        Assert.True(board.View!.Sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");

        var tab = await BesideAsync(studio, "Badge.axaml");

        try
        {
            await XamlStudio.UntilAsync(() => ReferenceEquals(panel.Canvas, tab.Canvas), "иерархия не пошла за открытой вкладкой");

            Assert.Equal("Badge.axaml", panel.View!.FormOf.Text);
            Assert.Equal(("Border", "Frame"), (Node(panel, Panel).Type, Node(panel, Panel).Name));

            // Клавиатура так и стоит на доске, пока рядом открывали вкладку: док не уводит её из группы, которая её
            // не держала. К доске возвращаются щелчком по пустому месту — фокус при этом никуда не переходит, и о
            // работе с холстом говорит само нажатие.
            var sheet = board.View.Sheet;
            var focused = Focused(sheet);
            var empty = new Point(sheet.Bounds.Width - 12, 12);

            Assert.True(sheet.IsKeyboardFocusWithin, "выбор на доске не отдал ей клавиатуру");
            Assert.Null((sheet.InputHitTest(empty) as Visual)?.FindAncestorOfType<UiDesignerFormItem>(includeSelf: true));

            Click(studio, sheet, empty);

            Assert.Same(focused, Focused(sheet));
            Assert.Same(board.Canvas, panel.Canvas);
            Assert.Equal("MainWindow.axaml", panel.View.FormOf.Text);

            Assert.True(Row(panel, Panel).Focus(), "строка иерархии не взяла клавиатуру");
            LiveFormStudio.Frame();

            Assert.Same(board.Canvas, panel.Canvas);

            Assert.True(tab.View.Sheet.Focus(), "вкладка не взяла клавиатуру");
            LiveFormStudio.Frame();

            Assert.Same(tab.Canvas, panel.Canvas);

            front.CanvasChanged += Count;
            studio.Beside!.Content = null;
            studio.Beside.Content = tab.Content;
            LiveFormStudio.Frame();
            front.CanvasChanged -= Count;

            Assert.Equal(0, changes);
            Assert.Same(tab.Canvas, panel.Canvas);

            studio.Beside.Content = null;
            LiveFormStudio.Frame();

            Assert.Same(board.Canvas, panel.Canvas);

            studio.Show(null);
            LiveFormStudio.Frame();

            Assert.Null(panel.Canvas);
            Assert.Empty(panel.Nodes);
            Assert.True(panel.View.Empty.IsVisible, "без холста на экране иерархия молчит");
            Assert.Equal(studio.Strings["hierarchy.empty"], panel.View.Empty.Text);
            Assert.False(panel.View.Header.IsVisible);
        }
        finally
        {
            studio.Beside!.Content = null;
            await tab.DisposeAsync();
        }

        void Count(object? sender, EventArgs e) => changes++;
    }

    /// <summary>Файл формы переписали снаружи, как его пишет Rider, — дерево встаёт по новому тексту само.</summary>
    [AvaloniaFact]
    public async Task A_file_written_outside_rebuilds_the_tree()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();

        await studio.OpenAsync("MainWindow.axaml", Main);
        await RowsAsync(panel);

        File.WriteAllText(
            studio.Xaml.PathOf("MainWindow.axaml").Value,
            Main.Replace("<TextBlock Text=\"Подпись\" />", "<TextBlock Text=\"Подпись\" />\n    <CheckBox x:Name=\"Agree\" />", StringComparison.Ordinal));

        await XamlStudio.UntilAsync(() => Node(panel, Panel).Children.Count == 4, "дерево не встало по тексту с диска");

        Assert.Equal(("CheckBox", "Agree"), (Node(panel, Panel).Children[3].Type, Node(panel, Panel).Children[3].Name));
    }

    /// <summary>
    /// Меню иерархии — пункты холста для формы, без пунктов доски для карточек, и его пункт правит форму
    /// иерархии; Enter ставит выбранное в середину холста, не меняя масштаба.
    /// </summary>
    [AvaloniaFact]
    public async Task The_menu_and_enter_of_the_hierarchy_are_the_canvas_ones()
    {
        await using var studio = new LiveFormStudio();
        var panel = studio.Hierarchy();
        var board = await studio.OpenBoardAsync(("MainWindow.axaml", Main), ("Badge.axaml", Badge));
        var sheet = board.View!.Sheet;
        var main = Slot(board, "MainWindow.axaml");

        Assert.True(sheet.SelectTarget(Go(board)), "кнопку на доске не выбрать");

        await RowsAsync(panel);

        var items = panel.MenuItems().OfType<AxMenuItem>().ToList();
        var headers = items.Select(item => (string)item.Header!).ToList();

        Assert.Contains(studio.Strings["board.remove"], Headers(board.Canvas!.MenuItems()));
        Assert.DoesNotContain(studio.Strings["board.remove"], headers);
        Assert.Contains(studio.Strings["form.menu.delete"], headers);

        // Подписи клавиш — те, что работают в дереве: Esc к родителю ловит и иерархия, а буква «Вписать всё» там —
        // поиск набором, и подписи у пункта нет.
        Assert.Equal(FormKeys.Parent, Row(items, studio.Strings["form.menu.parent"]).InputGesture);
        Assert.Null(Row(items, studio.Strings["form.menu.frame"]).InputGesture);
        Assert.Equal(
            BoardMenu.FrameKey,
            Row([.. board.Canvas.MenuItems().OfType<AxMenuItem>()], studio.Strings["form.menu.frame"]).InputGesture);

        var zoom = sheet.ViewportZoom;

        Assert.True(Distance(sheet.SelectionBounds.Center, Middle(sheet)) > 1, "кнопка уже в середине холста — Enter нечего проверять");

        LiveFormStudio.Press(panel.View!.Tree, Key.Enter);
        LiveFormStudio.Frame();

        Assert.Equal(zoom, sheet.ViewportZoom);
        Assert.True(
            Distance(sheet.SelectionBounds.Center, Middle(sheet)) < 1,
            $"Enter не поставил выбранное в середину: {sheet.SelectionBounds.Center} против {Middle(sheet)}");

        Row(items, studio.Strings["form.menu.delete"]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await XamlStudio.UntilAsync(() => !Text(main).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "пункт меню иерархии не удалил кнопку");
    }

    /// <summary>Ждёт, пока иерархия покажет форму, — корнем одной строкой.</summary>
    private static Task RowsAsync(HierarchyPanel panel) =>
        XamlStudio.UntilAsync(() => panel.Nodes.Count == 1, "иерархия не показала форму");

    /// <summary>Вкладка формы рядом с тем, что стоит в окне, — второй группой дока; взявшая форму.</summary>
    private static async Task<LiveFormDocument> BesideAsync(LiveFormStudio studio, string name)
    {
        var (view, error) = await studio.Editor().OpenAsync(studio.Xaml.PathOf(name).Value);

        Assert.Null(error);

        var tab = Assert.IsType<LiveFormDocument>(view);

        studio.Beside!.Content = tab.Content;
        LiveFormStudio.Frame();

        await tab.Opening;
        await XamlStudio.UntilAsync(() => tab.Form.Root is not null, "вкладка не взяла форму");
        LiveFormStudio.Frame();

        return tab;
    }

    private static HierarchyNode Node(HierarchyPanel panel, XamlElementPath path) =>
        panel.Nodes.SelectMany(node => node.SelfAndDescendants()).Single(node => node.Path.Equals(path));

    private static TreeViewItem Row(HierarchyPanel panel, XamlElementPath path) =>
        panel.View!.Tree.GetVisualDescendants().OfType<TreeViewItem>().First(item => item.DataContext is HierarchyNode node && node.Path.Equals(path));

    private static List<XamlElementPath> Selected(HierarchyPanel panel) =>
        [.. (panel.View!.Tree.SelectedItems ?? Array.Empty<object>()).OfType<HierarchyNode>().Select(node => node.Path)];

    private static string[] Types(HierarchyPanel panel) => [.. Node(panel, Panel).Children.Select(node => node.Type)];

    private static List<string> Headers(IReadOnlyList<Control> menu) => [.. menu.OfType<AxMenuItem>().Select(item => (string)item.Header!)];

    private static AxMenuItem Row(IReadOnlyList<AxMenuItem> menu, string header) => menu.Single(item => (string)item.Header! == header);

    /// <summary>Где сейчас клавиатура окна: туда платформа и доставит нажатие.</summary>
    private static Control Focused(Control where) =>
        Assert.IsAssignableFrom<Control>(TopLevel.GetTopLevel(where)?.FocusManager?.GetFocusedElement());

    private static string Text(LiveFormDocument tab) => tab.Document!.Syntax.SourceText.ToString();

    private static string Text(FormSlot slot) => slot.Document!.Syntax.SourceText.ToString();

    private static Control Live(LiveFormDocument tab, XamlElementPath path) =>
        Assert.IsAssignableFrom<Control>(tab.Shown!.ObjectAt(path));

    /// <summary>Диапазон элемента в тексте формы — от открывающего тега до закрывающего.</summary>
    private static AxCodeRange Range(LiveFormDocument tab, string name)
    {
        var element = tab.Document!.Syntax.Root!.DescendantElements().Single(element => element.Identity == name);

        return new AxCodeRange(element.Span.Start, element.Span.Length);
    }

    private static FormCard Card(BoardPanel board, string name) => board.Model!.Cards.Single(card => card.Name == name);

    private static FormSlot Slot(BoardPanel board, string name) =>
        board.Canvas!.Slots.Single(slot => slot.Session.Path.FileName == name);

    private static UiDesignerFormItem Item(BoardPanel board, string name) =>
        Assert.IsType<UiDesignerFormItem>(board.View!.Sheet.ContainerFromItem(Card(board, name)));

    private static Button Go(BoardPanel board) =>
        Item(board, "MainWindow.axaml").GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Go");

    private static Border FrameOf(BoardPanel board) =>
        Item(board, "Badge.axaml").GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Frame");

    /// <summary>Нажимает и отпускает мышь в точке части — щелчок, как его даёт окно.</summary>
    private static void Click(LiveFormStudio studio, Visual part, Point point)
    {
        var at = part.TranslatePoint(point, studio.Window)!.Value;

        studio.Window.MouseDown(at, MouseButton.Left);
        studio.Window.MouseUp(at, MouseButton.Left);
        LiveFormStudio.Frame();
    }

    /// <summary>Середина видимой области холста — в координатах холста.</summary>
    private static Point Middle(UiDesignerView sheet) =>
        sheet.ViewportLocation + new Vector(sheet.Bounds.Width, sheet.Bounds.Height) / (2 * sheet.ViewportZoom);

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
