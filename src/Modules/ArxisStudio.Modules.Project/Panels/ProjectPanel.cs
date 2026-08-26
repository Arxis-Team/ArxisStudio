using ArxisStudio.Controls;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Панель «Проект»: дерево открытого решения.
/// </summary>
/// <remarks>
/// Панель знает, какие файлы есть, и не знает, что с ними делать: двойной щелчок
/// она передаёт студии словами «открой этот путь». Кто возьмётся — дизайнер
/// форм, редактор ресурсов или плагин — решают зарегистрированные редакторы, и
/// добавить новый тип файла можно, не тронув эту панель.
/// </remarks>
[Sdk.ToolWindow("project.tree")]
public sealed class ProjectPanel : Sdk.ToolWindow
{
    private readonly AxTreeView _tree = new();
    private readonly TextBlock _empty = new()
    {
        Classes = { "dimmer", "small" },
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <inheritdoc/>
    protected override Control Build()
    {
        _empty.Bind(
            TextBlock.TextProperty,
            new Avalonia.Data.Binding(nameof(LocalizedString.Value)) { Source = Localizer.Instance.Track("panel.project.empty") });

        _tree.ItemTemplate = new FuncTreeDataTemplate<ProjectNode>(
            (node, _) => Row(node),
            node => node.Children);

        _tree.DoubleTapped += OnDoubleTapped;

        if (Context.GetService<IProjectWorkspace>() is { } workspace)
        {
            workspace.SnapshotChanged += (_, _) => _ = ShowAsync(workspace.Snapshot);
            _ = ShowAsync(workspace.Snapshot);
        }

        return new Panel { Children = { _empty, _tree } };
    }

    private static Control Row(ProjectNode node) => new TextBlock
    {
        Text = node.Name,
        VerticalAlignment = VerticalAlignment.Center,
        [AutomationProperties.NameProperty] = node.Name,
    };

    /// <summary>
    /// Показывает дерево открытого решения.
    /// </summary>
    /// <remarks>
    /// Дерево спрашивает у диска, какие из объявленных файлов существуют, —
    /// на большом решении это заметно, поэтому строится оно в фоне.
    /// </remarks>
    private async Task ShowAsync(SolutionSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            _tree.ItemsSource = null;
            _tree.IsVisible = false;
            _empty.IsVisible = true;
            return;
        }

        var tree = await Task.Run(() => ProjectTree.Build(snapshot));

        _tree.ItemsSource = tree.Children;
        _tree.IsVisible = true;
        _empty.IsVisible = false;

        // Раскрывать узлы можно только после того, как дерево создало контейнеры.
        Dispatcher.UIThread.Post(
            () =>
            {
                foreach (var project in tree.Children)
                {
                    if (_tree.TreeContainerFromItem(project) is TreeViewItem container)
                        container.IsExpanded = true;
                }
            },
            DispatcherPriority.Background);
    }

    private async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tree.SelectedItem is not ProjectNode { IsFile: true } node)
            return;

        if (Context.GetService<IStudioDocuments>() is { } documents)
            await documents.OpenAsync(node.FullPath);
    }
}
