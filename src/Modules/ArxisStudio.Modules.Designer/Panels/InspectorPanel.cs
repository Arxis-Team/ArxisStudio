using System.Collections.ObjectModel;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Панель «Инспектор»: свойства выделенного элемента, история и сохранение.
/// </summary>
[Sdk.ToolWindow("designer.inspector")]
public sealed class InspectorPanel : Sdk.ToolWindow
{
    private readonly ObservableCollection<InspectorSection> _sections = [];
    private readonly ItemsControl _list = new();
    private readonly ContentControl _custom = new() { IsVisible = false };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _type = new() { Classes = { "mono", "dimmer", "small" } };
    private readonly AxButton _undo = new() { Classes = { "compact" }, IsEnabled = false };
    private readonly AxButton _redo = new() { Classes = { "compact" }, IsEnabled = false };
    private readonly AxButton _save = new() { Classes = { "compact", "accent" }, IsEnabled = false };
    private DockPanel? _body;
    private TextBlock? _empty;

    // Заполнение полей инспектора поднимает те же события, что и правка
    // человеком. Флаг отличает одно от другого.
    private bool _filling;

    // Кому принадлежат показанные строки. Правка пишется сюда, а не в текущее
    // выделение: уход фокуса из поля и смена выделения — одно и то же движение,
    // и выделение успевает смениться раньше, чем поле отдаст значение.
    private HierarchyNode? _shown;
    private DesignerDocumentView? _shownView;

    /// <inheritdoc/>
    protected override Control Build()
    {
        _list.ItemsSource = _sections;
        _list.ItemTemplate = new FuncDataTemplate<InspectorSection>((section, _) => SectionView(section));

        _undo.Content = Localizer.Instance["inspector.undo"];
        _redo.Content = Localizer.Instance["inspector.redo"];
        _save.Content = Localizer.Instance["inspector.save"];

        _undo.Click += async (_, _) => await StepAsync(undo: true);
        _redo.Click += async (_, _) => await StepAsync(undo: false);
        _save.Click += OnSave;

        var header = new Border
        {
            Padding = new Avalonia.Thickness(12, 10),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 1,
                Children = { _title, _type },
            },
        };
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("AxBrdBrush"));

        var footer = new Border
        {
            Padding = new Avalonia.Thickness(10, 8),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _undo, _redo, _save },
            },
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("AxBrdBrush"));

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);

        _body = new DockPanel
        {
            IsVisible = false,
            Children =
            {
                header,
                footer,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new Panel { Children = { _custom, _list } },
                },
            },
        };

        _empty = new TextBlock
        {
            Classes = { "dimmer", "small" },
            Text = Localizer.Instance["panel.inspector.empty"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var state = DesignerState.Instance;

        state.SelectionChanged += _ => Show(state.Selected);
        state.ActiveChanged += () => Show(state.Selected);
        state.Mutated += RefreshValues;

        return new Panel { Children = { _empty, _body } };
    }

    /// <summary>Показывает свойства узла; null очищает панель.</summary>
    private void Show(HierarchyNode? node)
    {
        _sections.Clear();
        _shown = node;
        _shownView = DesignerState.Instance.Active;

        // Панель плагина живёт ровно одно выделение: следующий элемент может
        // быть другого типа, и чужой инспектор о нём ничего не знает.
        _custom.Content = null;
        _custom.IsVisible = false;
        _list.IsVisible = true;

        if (node is null || DesignerState.Instance.Active is not { } view)
        {
            _body!.IsVisible = false;
            _empty!.IsVisible = true;
            return;
        }

        _filling = true;
        try
        {
            foreach (var section in InspectorModel.Build(node, view.Document.Session))
                _sections.Add(section);

            Draw(node);
        }
        finally
        {
            _filling = false;
        }

        _title.Text = node.DisplayName;
        _type.Text = node.Control?.GetType().FullName ?? node.TypeName;

        _body!.IsVisible = true;
        _empty!.IsVisible = false;

        UpdateButtons();
    }

    /// <summary>
    /// Отдаёт строки рисовальщикам плагинов.
    /// </summary>
    /// <remarks>
    /// Рисовальщик получает не копию значения, а саму строку: правка из его
    /// контрола идёт тем же путём, что и правка из поля ввода — через документ,
    /// с проверкой и в общую историю.
    /// </remarks>
    private void Draw(HierarchyNode node)
    {
        if (DesignerState.Instance.Contributions is not { } contributions)
            return;

        var contexts = new List<IPropertyContext>();

        foreach (var row in _sections.SelectMany(section => section.Rows))
        {
            var context = new RowPropertyContext(row, CommitAsync);

            contexts.Add(context);

            if (row.ValueType is { } type && contributions.DrawerFor(type) is { } drawer)
                row.Drawer = Safely(() => drawer.Build(context), row.Name);
        }

        ShowCustom(node, contexts, contributions);
    }

    /// <summary>
    /// Подменяет содержимое инспектора панелью плагина, если он взялся за этот
    /// тип контрола.
    /// </summary>
    /// <remarks>
    /// Рисовальщик меняет одну строку, а инспектор — весь разговор о выделенном
    /// элементе, поэтому общие разделы при этом не показываются: две панели об
    /// одном и том же рядом означали бы два места, где правится одно свойство.
    /// </remarks>
    private void ShowCustom(
        HierarchyNode node,
        IReadOnlyList<IPropertyContext> properties,
        Extensibility.PluginContributionRegistry contributions)
    {
        if (node.Control?.GetType() is not { } type || contributions.InspectorFor(type) is not { } match)
            return;

        if (DesignerState.Instance.Context is not { } context)
            return;

        // Контекст указывает на папку того плагина, чей это инспектор: свои
        // ресурсы он ищет рядом с собой.
        var studio = match.PluginDirectory is { } directory
            ? new PluginScopedContext(context, directory)
            : context;

        if (Safely(() => match.Editor.Build(new ElementInspectorContext(node, properties, studio)), node.TypeName) is not { } content)
            return;

        _custom.Content = content;
        _custom.IsVisible = true;
        _list.IsVisible = false;
    }

    /// <summary>
    /// Строит контрол плагина, не давая его сбою утащить с собой инспектор.
    /// </summary>
    private static Control? Safely(Func<Control> build, string what)
    {
        try
        {
            return build();
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            DesignerState.Instance.Log(StudioLogLevel.Error, $"Рисовальщик свойства {what} упал: {e.Message}");
            return null;
        }
    }

    private void RefreshValues()
    {
        if (_shown is not { } node || _shownView is not { } view)
        {
            UpdateButtons();
            return;
        }

        _filling = true;
        try
        {
            InspectorModel.Refresh(_sections, node, view.Document.Session);
        }
        finally
        {
            _filling = false;
        }

        UpdateButtons();
    }

    /// <summary>
    /// Доводит правку строки до документа и показывает, что из этого вышло.
    /// </summary>
    /// <remarks>
    /// Строки перечитываются в любом случае: и когда правка прошла — значение
    /// могло нормализоваться, — и когда нет, чтобы поле не осталось показывать
    /// то, чего в документе нет.
    /// </remarks>
    private async Task CommitAsync(InspectorRow row, string? text)
    {
        if (_shown is not { } node || _shownView is not { } view)
            return;

        var error = await view.Document.SetAttributeAsync(node, row.Name, text);

        DesignerState.Instance.Status(error is null
            ? $"{node.DisplayName}.{row.Name} = {text ?? "—"}"
            : error);

        RefreshValues();
    }

    private async Task StepAsync(bool undo)
    {
        if (DesignerState.Instance.Active is not { } view)
            return;

        var error = undo ? await view.Document.UndoAsync() : await view.Document.RedoAsync();

        if (error is not null)
            DesignerState.Instance.Status(error);

        RefreshValues();
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DesignerState.Instance.Active is not { } view)
            return;

        await view.Document.SaveAsync();
        DesignerState.Instance.Status($"{Localizer.Instance["inspector.saved"]}: {view.Document.FilePath}");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var document = DesignerState.Instance.Active?.Document;

        _undo.IsEnabled = document?.CanUndo ?? false;
        _redo.IsEnabled = document?.CanRedo ?? false;
        _save.IsEnabled = document?.IsModified ?? false;
    }

    private Control SectionView(InspectorSection section)
    {
        var header = new Border
        {
            Height = 26,
            Padding = new Avalonia.Thickness(12, 0),
            Child = new TextBlock
            {
                Text = section.Title,
                FontWeight = FontWeight.SemiBold,
                Classes = { "small" },
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("AxBg2Brush"));

        var rows = new ItemsControl
        {
            ItemsSource = section.Rows,
            Margin = new Avalonia.Thickness(12, 6, 12, 10),
            ItemTemplate = new FuncDataTemplate<InspectorRow>((row, _) => RowView(row)),
        };

        return new StackPanel { Children = { header, rows } };
    }

    private Control RowView(InspectorRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,*,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 5),
        };

        var name = new TextBlock
        {
            Classes = { "small" },
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            Text = row.Name,
        };
        SetDim();
        ToolTip.SetTip(name, row.Name);

        // Признак «задано в разметке» меняется правкой, а строка живёт дольше
        // одной правки — подпись следит за ним сама.
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InspectorRow.IsSet))
                SetDim();
        };

        void SetDim()
        {
            if (row.IsSet)
                name.Classes.Remove("dim");
            else if (!name.Classes.Contains("dim"))
                name.Classes.Add("dim");
        }

        var text = new AxTextBox
        {
            Classes = { "small" },
            [!AxTextBox.TextProperty] = Bind(row, nameof(InspectorRow.Value)),
            [!AxTextBox.PlaceholderTextProperty] = Bind(row, nameof(InspectorRow.Placeholder), twoWay: false),
            [!Avalonia.Visual.IsVisibleProperty] = Bind(row, nameof(InspectorRow.IsText), twoWay: false),
        };
        text.KeyDown += async (_, e) =>
        {
            if (e.Key is Key.Enter)
                await CommitFromRow(row, text.Text);
        };
        text.LostFocus += async (_, _) => await CommitFromRow(row, text.Text);

        var toggle = new AxCheckBox
        {
            [!AxCheckBox.IsCheckedProperty] = Bind(row, nameof(InspectorRow.IsChecked)),
            [!Avalonia.Visual.IsVisibleProperty] = Bind(row, nameof(InspectorRow.IsToggle), twoWay: false),
        };
        toggle.Click += async (_, _) => await CommitFromRow(row, row.Value);

        var choice = new AxComboBox
        {
            ItemsSource = row.Options,
            [!AxComboBox.SelectedItemProperty] = Bind(row, nameof(InspectorRow.Value)),
            [!AxComboBox.PlaceholderTextProperty] = Bind(row, nameof(InspectorRow.Placeholder), twoWay: false),
            [!Avalonia.Visual.IsVisibleProperty] = Bind(row, nameof(InspectorRow.IsChoice), twoWay: false),
        };
        choice.SelectionChanged += async (_, _) => await CommitFromRow(row, row.Value);

        // Редактор из плагина заменяет собой всё, чем строка правилась бы сама.
        var drawn = new ContentControl
        {
            [!ContentControl.ContentProperty] = Bind(row, nameof(InspectorRow.Drawer), twoWay: false),
            [!Avalonia.Visual.IsVisibleProperty] = Bind(row, nameof(InspectorRow.IsDrawn), twoWay: false),
        };

        var reset = new AxButton
        {
            Classes = { "icon" },
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            Content = new AxIcon { Data = AxIcons.Close, Width = 11, Height = 11 },
            [!Avalonia.Visual.IsVisibleProperty] = Bind(row, nameof(InspectorRow.IsSet), twoWay: false),
        };
        ToolTip.SetTip(reset, Localizer.Instance["inspector.reset"]);
        reset.Click += async (_, _) => await CommitFromRow(row, null);

        Grid.SetColumn(name, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(toggle, 1);
        Grid.SetColumn(choice, 1);
        Grid.SetColumn(drawn, 1);
        Grid.SetColumn(reset, 2);

        grid.Children.Add(name);
        grid.Children.Add(text);
        grid.Children.Add(toggle);
        grid.Children.Add(choice);
        grid.Children.Add(drawn);
        grid.Children.Add(reset);

        return grid;
    }

    private async Task CommitFromRow(InspectorRow row, string? text)
    {
        if (_filling)
            return;

        await CommitAsync(row, text);
    }

    private static Avalonia.Data.Binding Bind(InspectorRow row, string path, bool twoWay = true) => new(path)
    {
        Source = row,
        Mode = twoWay ? Avalonia.Data.BindingMode.TwoWay : Avalonia.Data.BindingMode.OneWay,
    };
}

/// <summary>
/// Контекст плагина с подменённой папкой: инспектор чужого плагина ищет свои
/// ресурсы рядом с собой, а не рядом с модулем дизайнера.
/// </summary>
/// <param name="inner">Контекст модуля.</param>
/// <param name="directory">Папка плагина-хозяина.</param>
internal sealed class PluginScopedContext(IStudioContext inner, string directory) : IStudioContext
{
    /// <inheritdoc/>
    public IStudioLog Log => inner.Log;

    /// <inheritdoc/>
    public IStudioCommands Commands => inner.Commands;

    /// <inheritdoc/>
    public string? ProjectPath => inner.ProjectPath;

    /// <inheritdoc/>
    public string PluginDirectory => directory;

    /// <inheritdoc/>
    public T? GetService<T>() where T : class => inner.GetService<T>();
}
