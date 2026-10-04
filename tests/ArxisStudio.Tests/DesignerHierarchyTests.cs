using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Modules.UiDesigner.Workbench;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Иерархия формы: дерево из текста формы впереди, выбор — общий с холстом, Delete и перестановка
/// клавиатурой — правками документа.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class DesignerHierarchyTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <StackPanel x:Name="Panel">
            <Button x:Name="Go" Content="Пуск" Width="120" MinHeight="30" />
            <TextBox x:Name="Input" Text="поле" Width="200" Height="24" />
            <TextBlock Text="Подпись" />
          </StackPanel>
        </Window>
        """;

    private static XamlElementPath Go => XamlElementPath.Parse("/0/0");

    private static XamlElementPath Input => XamlElementPath.Parse("/0/1");

    /// <summary>
    /// Дерево — элементы формы впереди с типами и именами; выбранное на холсте выбрано в дереве, а
    /// выбранное в дереве — на холсте.
    /// </summary>
    [AvaloniaFact]
    public async Task The_hierarchy_shows_the_form_in_front_and_shares_its_selection()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();
        var tree = panel.View!.Tree;

        var root = Assert.Single(panel.Nodes);
        var stack = Assert.Single(root.Children);

        Assert.Equal("Window", root.Type);
        Assert.Equal(["Button", "TextBox", "TextBlock"], stack.Children.Select(node => node.Type));
        Assert.Equal("Go", stack.Children[0].Name);
        Assert.False(panel.View.Empty.IsVisible);

        Assert.True(document.View.Sheet.SelectTarget(Live(document, Go)), "кнопку не выбрать на холсте");
        LiveFormStudio.Frame();

        Assert.Equal(Go, Assert.IsType<HierarchyNode>(Assert.Single(tree.SelectedItems)).Path);

        tree.SelectedItem = stack.Children[1];
        LiveFormStudio.Frame();

        Assert.Equal(Input, Assert.Single(document.Selection));
        Assert.Same(Live(document, Input), Assert.Single(document.View.Sheet.SelectedTargets).Target);
    }

    /// <summary>
    /// Выбранная в дереве строка выбирает элемент на холсте, а клавиатура остаётся в дереве: следующая
    /// стрелка — шаг по строкам, а не сдвиг выбранного.
    /// </summary>
    [AvaloniaFact]
    public async Task Choosing_a_row_leaves_the_keyboard_in_the_tree()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();
        var tree = panel.View!.Tree;

        // Каретку держит строка дерева, а не само дерево.
        var row = Assert.IsAssignableFrom<Control>(tree.ContainerFromItem(panel.Nodes[0]));

        Assert.True(row.Focus(), "строка дерева не взяла клавиатуру");

        tree.SelectedItem = panel.Nodes[0].Children[0].Children[1];
        LiveFormStudio.Frame();

        Assert.Same(Live(document, Input), Assert.Single(document.View.Sheet.SelectedTargets).Target);
        Assert.True(tree.IsKeyboardFocusWithin, "выбор в дереве увёл клавиатуру на холст");
        Assert.False(document.View.Sheet.IsKeyboardFocusWithin);
    }

    /// <summary>Delete в дереве убирает выбранный элемент из текста, и выбор переходит к его родителю.</summary>
    [AvaloniaFact]
    public async Task Delete_in_the_hierarchy_takes_the_element_out_of_the_text()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();

        document.Select([Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(panel.View!.Tree, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(document).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка осталась в тексте");
        await XamlStudio.UntilAsync(() => panel.Nodes[0].Children[0].Children.Count == 2, "дерево не перестроилось");

        Assert.Equal(XamlElementPath.Parse("/0"), Assert.Single(document.Selection));
    }

    /// <summary>Ctrl+Z в иерархии отменяет шаг формы, а Ctrl+Y возвращает его: история у формы одна.</summary>
    [AvaloniaFact]
    public async Task Ctrl_z_in_the_hierarchy_takes_back_the_forms_last_step()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();
        var tree = panel.View!.Tree;

        document.Select([Go]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(tree, Key.Delete);

        await XamlStudio.UntilAsync(() => !Text(document).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "кнопка не удалена");

        LiveFormStudio.Press(tree, Key.Z, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => Text(document).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "Ctrl+Z в иерархии не вернул кнопку");

        LiveFormStudio.Press(tree, Key.Y, KeyModifiers.Control);

        await XamlStudio.UntilAsync(() => !Text(document).Contains("x:Name=\"Go\"", StringComparison.Ordinal), "Ctrl+Y в иерархии не повторил удаление");
    }

    /// <summary>
    /// Ctrl со стрелкой переставляет элемент среди соседей одной правкой, и выбор идёт за ним; отмена
    /// возвращает порядок.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_and_an_arrow_move_the_element_among_its_siblings()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();

        document.Select([Input]);
        LiveFormStudio.Frame();
        LiveFormStudio.Press(panel.View!.Tree, Key.Up, KeyModifiers.Control);

        await XamlStudio.UntilAsync(
            () => Text(document).IndexOf("x:Name=\"Input\"", StringComparison.Ordinal) < Text(document).IndexOf("x:Name=\"Go\"", StringComparison.Ordinal),
            "поле не встало перед кнопкой");

        Assert.Equal(Go, Assert.Single(document.Selection));
        Assert.Equal(["TextBox", "Button", "TextBlock"], panel.Nodes[0].Children[0].Children.Select(node => node.Type));

        Assert.True(document.Document!.CanUndo);
        await document.Document.UndoAsync();
        await XamlStudio.UntilAsync(() => panel.Nodes[0].Children[0].Children[0].Type == "Button", "отмена не вернула порядок");
    }

    /// <summary>
    /// Правка из дерева перестраивает его, а клавиатура остаётся у выбранной строки: Ctrl со стрелкой дважды
    /// подряд переставляет элемент дважды.
    /// </summary>
    [AvaloniaFact]
    public async Task The_keyboard_stays_in_the_tree_across_its_own_edits()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();
        var tree = panel.View!.Tree;

        document.Select([XamlElementPath.Parse("/0/2")]);
        LiveFormStudio.Frame();

        var row = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .First(item => item.DataContext is HierarchyNode { Type: "TextBlock" });

        Assert.True(row.Focus(), "строка не взяла клавиатуру");

        LiveFormStudio.Press(Focused(tree), Key.Up, KeyModifiers.Control);
        await XamlStudio.UntilAsync(() => panel.Nodes[0].Children[0].Children[1].Type == "TextBlock", "подпись не поднялась");
        LiveFormStudio.Frame();

        Assert.True(tree.IsKeyboardFocusWithin, "перестроенное дерево потеряло клавиатуру");

        LiveFormStudio.Press(Focused(tree), Key.Up, KeyModifiers.Control);
        await XamlStudio.UntilAsync(() => panel.Nodes[0].Children[0].Children[0].Type == "TextBlock", "вторая клавиша не дошла");
    }

    /// <summary>Свёрнутое человеком остаётся свёрнутым, когда правка перестраивает дерево.</summary>
    [AvaloniaFact]
    public async Task What_was_collapsed_stays_collapsed_across_an_edit()
    {
        await using var studio = new LiveFormStudio();
        var document = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();

        panel.Nodes[0].Children[0].IsExpanded = false;

        await document.Edits!.SetAsync([Go], "Width", "150");
        await XamlStudio.UntilAsync(() => Text(document).Contains("Width=\"150\"", StringComparison.Ordinal), "ширина не записана");

        Assert.False(panel.Nodes[0].Children[0].IsExpanded, "свёрнутая панель раскрылась после правки");
        Assert.True(panel.Nodes[0].IsExpanded);
    }

    /// <summary>Впереди другая форма — дерево её; впереди не форма — дерево пусто и говорит почему.</summary>
    [AvaloniaFact]
    public async Task The_hierarchy_follows_the_form_in_front_and_empties_without_one()
    {
        await using var studio = new LiveFormStudio();
        var first = await studio.OpenAsync("MainWindow.axaml", Form);
        var panel = studio.Panel<HierarchyPanel>();

        var second = await studio.OpenAsync("Other.axaml", Form.Replace("<TextBlock Text=\"Подпись\" />", string.Empty, StringComparison.Ordinal));

        Assert.Equal(2, panel.Nodes[0].Children[0].Children.Count);

        second.OnDeactivated();
        LiveFormStudio.Frame();

        Assert.Empty(panel.Nodes);
        Assert.True(panel.View!.Empty.IsVisible);

        studio.Show(first);

        Assert.Equal(3, panel.Nodes[0].Children[0].Children.Count);
    }

    private static string Text(LiveFormDocument document) => document.Document!.Syntax.SourceText.ToString();

    /// <summary>Где сейчас клавиатура окна: туда платформа и доставит нажатие.</summary>
    private static Control Focused(Control where) =>
        Assert.IsAssignableFrom<Control>(TopLevel.GetTopLevel(where)?.FocusManager?.GetFocusedElement());

    private static Control Live(LiveFormDocument document, XamlElementPath path) =>
        Assert.IsAssignableFrom<Control>(document.Shown!.ObjectAt(path));
}
