using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Строка окна проекта стоит там, где её поставило бы дерево темы.
/// </summary>
/// <remarks>
/// Окно проекта рисует дерево плоским виртуализованным списком, а не <see cref="AxTreeView"/>, и
/// повторяет геометрию его строки сам: отступ уровня, клетку шеврона, место значка и подписи,
/// высоту. Совпадение держит этот тест, а не глаз, — рядом кладётся настоящее дерево темы, и
/// сравниваются координаты в каждой плотности: ключи плотности у двух строк разные, и расхождение
/// в две точки видно только на компактной или просторной ступени.
/// <para>
/// Отличие одно и названо: у листа клетка шеврона остаётся пустой, и значки соседей стоят столбцом,
/// как в Rider, — дерево темы у листа клетку прячет.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowParityTests
{
    private static readonly string[] Chain = ["Hello", "src", "App", "Views", "MainWindow.axaml"];

    /// <summary>Шеврон, значок и подпись строки стоят там же, где у дерева темы, на каждом уровне.</summary>
    [AvaloniaTheory]
    [InlineData(StudioDensity.Compact)]
    [InlineData(StudioDensity.Normal)]
    [InlineData(StudioDensity.Comfortable)]
    public async Task A_row_stands_where_the_theme_tree_puts_it(StudioDensity density)
    {
        StudioTheming.Apply(density);

        var reference = new Window { Width = 520, Height = 720 };

        try
        {
            using var studio = new ProjectWindowStudio();

            await studio.Open();

            studio.Select("Views");
            studio.Press(studio.View.Tree, Key.Multiply);

            var tree = Reference();

            reference.Content = tree;
            reference.Show();
            reference.UpdateLayout();

            for (var level = 0; level < Chain.Length; level++)
            {
                var row = studio.Item(studio.Row(Chain[level]));
                var item = tree.GetVisualDescendants().OfType<AxTreeViewItem>().Single(node => Equals(node.Header, Chain[level]));

                Assert.Equal(Theirs(tree, item), Mine(studio.View.Tree, row, Chain[level]));
            }
        }
        finally
        {
            reference.Close();

            foreach (var tier in Application.Current!.Resources.MergedDictionaries
                         .OfType<ResourceInclude>()
                         .Where(include => include.Source?.OriginalString.Contains("/Density/", StringComparison.Ordinal) == true)
                         .ToList())
            {
                Application.Current.Resources.MergedDictionaries.Remove(tier);
            }
        }
    }

    /// <summary>
    /// Значок листа стоит в столбце значков своих соседей — в отличие от дерева темы, и нарочно.
    /// </summary>
    [AvaloniaFact]
    public async Task A_leaf_keeps_its_icon_in_the_column_of_its_siblings()
    {
        using var studio = new ProjectWindowStudio();

        await studio.Open();

        var leaf = Mine(studio.View.Tree, studio.Item(studio.Row("Models")), "Models");
        var folder = Mine(studio.View.Tree, studio.Item(studio.Row("Views")), "Views");

        Assert.Equal(folder.Icon, leaf.Icon);
        Assert.Equal(folder.Label, leaf.Label);
    }

    /// <summary>Где у строки окна шеврон, значок и подпись и какой она высоты.</summary>
    private static Place Mine(AxListBox list, AxListBoxItem item, string name)
    {
        var chevron = item.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "Chevron");
        var icon = item.GetVisualDescendants().OfType<AxIcon>().Single(glyph => !glyph.GetVisualAncestors().Contains(chevron));
        var label = item.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == name);

        return new Place(Left(chevron, list), Left(icon, list), Left(label, list), item.Bounds.Height);
    }

    /// <summary>То же у строки дерева темы.</summary>
    private static Place Theirs(AxTreeView tree, AxTreeViewItem item)
    {
        var parts = item.GetVisualDescendants().TakeWhile(part => part is not AxTreeViewItem).OfType<Control>().ToList();
        var chevron = parts.Single(part => part.Name == "PART_ExpandCollapseChevron");
        var icon = parts.Single(part => part.Name == "PART_Icon");
        var root = parts.Single(part => part.Name == "PART_Root");
        var label = parts.OfType<TextBlock>().First(text => text.Text == (string?)item.Header);

        return new Place(Left(chevron, tree), Left(icon, tree), Left(label, tree), root.Bounds.Height);
    }

    /// <summary>Дерево темы с той же цепочкой узлов, раскрытой до конца; у последнего есть лист.</summary>
    private static AxTreeView Reference()
    {
        var leaf = new AxTreeViewItem { Header = "MainWindow.axaml.cs", Icon = AxIcons.DocumentCode };
        var node = leaf;

        for (var level = Chain.Length - 1; level >= 0; level--)
            node = new AxTreeViewItem { Header = Chain[level], Icon = AxIcons.Folder, IsExpanded = true, ItemsSource = new[] { node } };

        return new AxTreeView { ItemsSource = new[] { node } };
    }

    private static double Left(Visual part, Visual origin) => part.TranslatePoint(default, origin)!.Value.X;

    /// <summary>Координаты строки от левого края списка.</summary>
    /// <param name="Chevron">Левый край клетки шеврона.</param>
    /// <param name="Icon">Левый край значка.</param>
    /// <param name="Label">Левый край подписи.</param>
    /// <param name="Height">Высота строки.</param>
    private sealed record Place(double Chevron, double Icon, double Label, double Height);
}
