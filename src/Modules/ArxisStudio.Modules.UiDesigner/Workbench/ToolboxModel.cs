using System.ComponentModel;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>Строка палитры: заголовок раздела или контрол.</summary>
internal abstract class ToolboxItem
{
    /// <summary>Строка ли это контрола — её выбирают и несут; заголовок — нет.</summary>
    public abstract bool IsEntry { get; }

    /// <summary>Что строку называет диктору: контрол или раздел.</summary>
    public abstract string Label { get; }
}

/// <summary>Заголовок раздела палитры.</summary>
/// <param name="title">Заголовок.</param>
internal sealed class ToolboxGroup(string title) : ToolboxItem
{
    /// <summary>Заголовок.</summary>
    public string Title { get; } = title;

    /// <inheritdoc/>
    public override bool IsEntry => false;

    /// <inheritdoc/>
    public override string Label => Title;
}

/// <summary>Контрол палитры: тип и разметка, которую напишет вставка.</summary>
/// <param name="type">Тип.</param>
/// <param name="tip">Подсказка: что встанет, а у несобранного — что бросок соберёт проект.</param>
internal sealed class ToolboxEntry(XamlSnippet type, string tip) : ToolboxItem
{
    /// <summary>Тип.</summary>
    public XamlSnippet Type { get; } = type;

    /// <summary>Имя контрола.</summary>
    public string Name => Type.Name;

    /// <summary>Подсказка.</summary>
    public string Tip { get; } = tip;

    /// <summary>Контрол проекта, класса которого ещё нет: бросок сначала соберёт проект.</summary>
    public bool IsUnbuilt => Type.Placeable is { IsBuilt: false };

    /// <inheritdoc/>
    public override bool IsEntry => true;

    /// <inheritdoc/>
    public override string Label => Name;
}

/// <summary>
/// Палитра: контролы проекта формы впереди — первыми, — и контролы Avalonia по разделам.
/// </summary>
/// <remarks>
/// <para>
/// Контролы Avalonia — разметка, а не типы: ставят строку файла, и разметка уже говорит, каким новый
/// контрол встаёт, — кнопка с надписью, список с тремя строками: пустой список невидим и ничего не
/// говорит о форме файла.
/// </para>
/// <para>
/// Контролы проекта — то, что служба типов перечисляет для формы (<see cref="IStudioXamlTypes"/>):
/// собранные и ещё не собранные — их бросок сначала соберёт проект.
/// </para>
/// </remarks>
internal sealed class ToolboxModel : INotifyPropertyChanged
{
    /// <summary>Разделы Avalonia и их контролы, по порядку: палитру читают и по месту, а не только по имени.</summary>
    private static readonly (string Group, (string Name, string Markup, string? Package)[] Entries)[] Catalogue =
    [
        ("layout",
        [
            ("Grid", """<Grid Width="200" Height="140" />""", null),
            ("StackPanel", """<StackPanel Width="180" Height="120" />""", null),
            ("DockPanel", """<DockPanel Width="200" Height="140" />""", null),
            ("WrapPanel", """<WrapPanel Width="200" Height="140" />""", null),
            // Голый Canvas не попадает под указатель вовсе: следующий брошенный контрол встал бы рядом, и
            // панель для свободного размещения была бы единственной, в которую ничего не поставить.
            ("Canvas", """<Canvas Width="200" Height="140" Background="#11FFFFFF" />""", null),
            ("Border", """<Border Width="160" Height="90" Background="#22FFFFFF" />""", null),
            ("ScrollViewer", """<ScrollViewer Width="200" Height="140" />""", null),
            ("TabControl", """<TabControl Width="220" Height="150"><TabItem Header="One"><StackPanel /></TabItem><TabItem Header="Two"><StackPanel /></TabItem></TabControl>""", null),
            ("Expander", """<Expander Header="Expander" IsExpanded="True"><StackPanel /></Expander>""", null),
        ]),
        ("buttons",
        [
            ("Button", """<Button Content="Button" />""", null),
            ("ToggleButton", """<ToggleButton Content="Toggle" />""", null),
            ("CheckBox", """<CheckBox Content="Check" />""", null),
            ("RadioButton", """<RadioButton Content="Option" />""", null),
            ("ToggleSwitch", """<ToggleSwitch />""", null),
        ]),
        ("input",
        [
            ("TextBox", """<TextBox Width="160" />""", null),
            ("ComboBox", """<ComboBox Width="160" SelectedIndex="0"><ComboBoxItem Content="One" /><ComboBoxItem Content="Two" /><ComboBoxItem Content="Three" /></ComboBox>""", null),
            ("Slider", """<Slider Width="160" Maximum="100" Value="40" />""", null),
            ("NumericUpDown", """<NumericUpDown Width="140" Value="0" />""", null),
            ("DatePicker", """<DatePicker />""", null),
            ("AutoCompleteBox", """<AutoCompleteBox Width="160" />""", null),
        ]),
        ("text",
        [
            ("TextBlock", """<TextBlock Text="Text" />""", null),
            ("Label", """<Label Content="Label" />""", null),
            ("SelectableTextBlock", """<SelectableTextBlock Text="Text" />""", null),
        ]),
        ("items",
        [
            ("ListBox", """<ListBox Width="180"><ListBoxItem Content="One" /><ListBoxItem Content="Two" /><ListBoxItem Content="Three" /></ListBox>""", null),
            ("TreeView", """<TreeView Width="180"><TreeViewItem Header="Root" IsExpanded="True"><TreeViewItem Header="Leaf" /><TreeViewItem Header="Leaf" /></TreeViewItem></TreeView>""", null),
            ("Menu", """<Menu><MenuItem Header="File"><MenuItem Header="New" /><MenuItem Header="Open" /></MenuItem><MenuItem Header="Edit" /></Menu>""", null),
            ("DataGrid", """<DataGrid Width="220" Height="140" />""", "Avalonia.Controls.DataGrid"),
            ("ProgressBar", """<ProgressBar Width="160" Value="40" />""", null),
            ("Separator", """<Separator Width="160" />""", null),
        ]),
        ("media",
        [
            ("Image", """<Image Width="120" Height="90" />""", null),
        ]),
        ("shapes",
        [
            ("Rectangle", """<Rectangle Width="80" Height="50" Fill="#7F7F7F" />""", null),
            ("Ellipse", """<Ellipse Width="60" Height="60" Fill="#7F7F7F" />""", null),
            ("Line", """<Line StartPoint="0,0" EndPoint="80,0" Stroke="#7F7F7F" StrokeThickness="1" />""", null),
            ("Path", """<Path Data="M3 12.5C5 6 11 10 13 3.5" Stroke="#7F7F7F" StrokeThickness="1.4" />""", null),
        ]),
    ];

    private readonly IStudioStrings _strings;
    private IReadOnlyList<ToolboxItem> _items = [];

    /// <summary>Модель палитры.</summary>
    /// <param name="strings">Словарь модуля.</param>
    public ToolboxModel(IStudioStrings strings) => _strings = strings;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Строки: заголовки разделов и контролы.</summary>
    public IReadOnlyList<ToolboxItem> Items
    {
        get => _items;
        private set
        {
            _items = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        }
    }

    /// <summary>Не нашлось ничего.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Строит строки: контролы проекта, затем Avalonia, — те, чьё имя содержит набранное.</summary>
    /// <param name="project">Контролы проекта формы впереди.</param>
    /// <param name="filter">Что набрано в поиске.</param>
    public void Show(IReadOnlyList<XamlPlaceable> project, string filter)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(filter);

        var typed = filter.Trim();
        var items = new List<ToolboxItem>();

        Section("project", project.Select(control => new ToolboxEntry(TypeMarkup.Of(control), Tip(control))));

        foreach (var (group, entries) in Catalogue)
        {
            Section(group, entries.Select(entry => new ToolboxEntry(
                new XamlSnippet(XamlSnippet.AvaloniaNamespace, entry.Name) { Markup = entry.Markup, Package = entry.Package },
                entry.Markup)));
        }

        Items = items;

        void Section(string group, IEnumerable<ToolboxEntry> entries)
        {
            var shown = entries.Where(entry => typed.Length == 0 || entry.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();

            if (shown.Count == 0)
                return;

            items.Add(new ToolboxGroup(_strings[$"toolbox.group.{group}"]));
            items.AddRange(shown);
        }
    }

    private string Tip(XamlPlaceable control) =>
        control.IsBuilt ? control.ClassName : $"{control.ClassName} — {_strings["toolbox.unbuilt"]}";
}
