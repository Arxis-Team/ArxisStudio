using ArxisStudio.Controls;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Панель «Палитра»: контролы, которые можно положить в активный документ.
/// </summary>
[Sdk.ToolWindow("designer.toolbox")]
public sealed class ToolboxPanel : Sdk.ToolWindow
{
    private readonly AxSearchField _search = new() { Margin = new Avalonia.Thickness(8, 6) };
    private readonly ItemsControl _groups = new();
    private readonly DockPanel _body = new();
    private readonly TextBlock _empty = new()
    {
        Classes = { "dimmer" },
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Margin = new Avalonia.Thickness(20, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <inheritdoc/>
    protected override Control Build()
    {
        _search.PlaceholderText = Localizer.Instance["toolbox.search"];
        _empty.Text = Localizer.Instance["panel.toolbox.empty"];

        _search.TextChanged += (_, _) => ShowGroups();
        _groups.ItemTemplate = new FuncDataTemplate<ToolboxGroup>((group, _) => GroupView(group));

        DockPanel.SetDock(_search, Dock.Top);
        _body.Children.Add(_search);
        _body.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _groups,
        });

        DesignerState.Instance.ActiveChanged += ShowGroups;
        ShowGroups();

        return new Panel { Children = { _empty, _body } };
    }

    private void ShowGroups()
    {
        var root = DesignerState.Instance.Active?.Document.Document?.Root;

        _groups.ItemsSource = ToolboxCatalog.For(root, _search.Text);
        _body.IsVisible = root is not null;
        _empty.IsVisible = root is null;
    }

    private Control GroupView(ToolboxGroup group)
    {
        var items = new ItemsControl
        {
            ItemsSource = group.Items,
            Margin = new Avalonia.Thickness(0, 2, 0, 6),
            ItemTemplate = new FuncDataTemplate<ToolboxItem>((item, _) => ItemView(item)),
        };

        var header = new Border
        {
            Height = 24,
            Padding = new Avalonia.Thickness(12, 0),
            Child = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.TextProperty] = new Avalonia.Data.Binding("Title.Value") { Source = group },
            },
        };

        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("AxBg2Brush"));

        return new StackPanel { Children = { header, items } };
    }

    private Control ItemView(ToolboxItem item)
    {
        var icon = new AxIcon { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Data = AxIcons.Plus };

        icon.Bind(AxIcon.ForegroundProperty, icon.GetResourceObservable("AxFg3Brush"));

        var row = new Border
        {
            Classes = { "toolbox" },
            Padding = new Avalonia.Thickness(12, 4),
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    icon,
                    new TextBlock { Text = item.TypeName, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        ToolTip.SetTip(row, Localizer.Instance["toolbox.hint"]);
        row.PointerPressed += (sender, e) => OnItemPressed(item, sender as Control, e);

        return row;
    }

    /// <summary>
    /// Пускает контрол палитры в дело: двойным щелчком — в выделенный элемент,
    /// перетаскиванием — туда, куда его отпустят.
    /// </summary>
    /// <remarks>
    /// Оба способа начинаются здесь, потому что перетаскивание можно начать
    /// только с события нажатия, а начатое — оно забирает указатель себе, и
    /// второй щелчок до палитры уже не доходит. Поэтому нажатия и различаются
    /// по счётчику: двойное — вставка, одиночное — начало перетаскивания.
    /// </remarks>
    private static async void OnItemPressed(ToolboxItem item, Control? source, PointerPressedEventArgs e)
    {
        if (source is null || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
            return;

        var state = DesignerState.Instance;

        if (state.Active is not { } view)
            return;

        if (e.ClickCount >= 2)
        {
            await view.InsertFromToolboxAsync(item, state.Selected);
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DesignerState.ToolboxFormat, item));

        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
    }
}
