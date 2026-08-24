using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Панель «Иерархия»: дерево элементов активного документа.
/// </summary>
[Sdk.ToolWindow("designer.hierarchy")]
public sealed class HierarchyPanel : Sdk.ToolWindow
{
    private readonly AxTreeView _tree = new();
    private readonly TextBlock _empty = new()
    {
        Classes = { "dimmer" },
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <inheritdoc/>
    protected override Control Build()
    {
        _empty.Text = Shell.Localization.Localizer.Instance["panel.hierarchy.empty"];

        _tree.ItemTemplate = new FuncTreeDataTemplate<HierarchyNode>(
            (node, _) => Row(node),
            node => node.Children);

        _tree.SelectionChanged += OnTreeSelectionChanged;

        var state = DesignerState.Instance;

        state.ActiveChanged += ShowActive;
        state.SelectionChanged += OnSelectionChanged;

        ShowActive();

        return new Panel { Children = { _empty, _tree } };
    }

    private static Control Row(HierarchyNode node)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        row.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(HierarchyNode.DisplayName)),
        });
        row.Children.Add(new TextBlock
        {
            Classes = { "dimmer" },
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(HierarchyNode.TypeHint)),
        });

        return row;
    }

    private void ShowActive()
    {
        var document = DesignerState.Instance.Active?.Document;

        _tree.ItemsSource = document?.Nodes;
        _tree.IsVisible = document is not null;
        _empty.IsVisible = document is null || document.Nodes.Count == 0;

        if (document is not null)
            ExpandRoot(document);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tree.SelectedItem is not HierarchyNode node)
            return;

        DesignerState.Instance.Select(node, this);
        DesignerState.Instance.Active?.ShowSelection(node);
    }

    private void OnSelectionChanged(object? origin)
    {
        if (ReferenceEquals(origin, this))
            return;

        var state = DesignerState.Instance;

        if (state.Selected is not { } node || state.Active is not { } view)
        {
            _tree.SelectedItem = null;
            return;
        }

        Reveal(view.Document.FindPath(node.Control));
    }

    /// <summary>
    /// Показывает узел в дереве: раскрывает предков и выделяет сам узел.
    /// Контейнеры строк создаются по мере раскрытия, поэтому спускаться
    /// приходится шаг за шагом.
    /// </summary>
    private void Reveal(IReadOnlyList<HierarchyNode> path)
    {
        if (path.Count == 0)
        {
            _tree.SelectedItem = null;
            return;
        }

        ItemsControl parent = _tree;

        for (var i = 0; i < path.Count - 1; i++)
        {
            if (parent.ContainerFromItem(path[i]) is not TreeViewItem container)
                break;

            container.IsExpanded = true;
            container.UpdateLayout();
            parent = container;
        }

        _tree.SelectedItem = path[^1];
    }

    /// <summary>Раскрывает корень дерева документа.</summary>
    /// <remarks>
    /// Раскрывать можно только после того, как дерево создало контейнеры строк,
    /// поэтому не сразу.
    /// </remarks>
    private void ExpandRoot(DesignDocument document) => Dispatcher.UIThread.Post(
        () =>
        {
            if (document.Nodes.FirstOrDefault() is { } root &&
                _tree.TreeContainerFromItem(root) is TreeViewItem container)
            {
                container.IsExpanded = true;
            }
        },
        DispatcherPriority.Background);
}
