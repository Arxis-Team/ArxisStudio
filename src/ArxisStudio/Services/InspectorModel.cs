using System.Collections.ObjectModel;
using System.ComponentModel;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>Чем правится значение в строке инспектора.</summary>
public enum InspectorRowKind
{
    /// <summary>Строка текста.</summary>
    Text,

    /// <summary>Флажок.</summary>
    Toggle,

    /// <summary>Выбор из списка — перечисление.</summary>
    Choice,
}

/// <summary>Одно свойство выделенного элемента.</summary>
public sealed class InspectorRow : INotifyPropertyChanged
{
    private string? _value;
    private string? _placeholder;
    private string? _source;
    private bool _isSet;

    /// <summary>Создаёт строку.</summary>
    /// <param name="name">Имя свойства, как оно пишется в разметке.</param>
    /// <param name="kind">Чем правится значение.</param>
    /// <param name="valueType">Тип значения свойства; null, если тип неизвестен.</param>
    /// <param name="options">Варианты для выбора из списка.</param>
    public InspectorRow(
        string name,
        InspectorRowKind kind,
        Type? valueType = null,
        IReadOnlyList<string>? options = null)
    {
        Name = name;
        Kind = kind;
        ValueType = valueType;
        Options = options ?? [];
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Строку перечитали из документа.</summary>
    internal event EventHandler? Refilled;

    /// <summary>Имя свойства.</summary>
    public string Name { get; }

    /// <summary>Чем правится значение.</summary>
    public InspectorRowKind Kind { get; }

    /// <summary>Тип значения свойства; null, если тип неизвестен.</summary>
    public Type? ValueType { get; }

    /// <summary>
    /// Редактор, который дал плагин; null, если строка правится сама.
    /// </summary>
    /// <remarks>
    /// Строка с рисовальщиком не показывает ни поля, ни флажка, ни списка: всё,
    /// чем правится значение, теперь внутри этого контрола.
    /// </remarks>
    public Control? Drawer { get; internal set; }

    /// <summary>Строку рисует плагин.</summary>
    public bool IsDrawn => Drawer is not null;

    /// <summary>Варианты для выбора из списка; пусто для остальных строк.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>Значение правится полем ввода.</summary>
    public bool IsText => Kind == InspectorRowKind.Text && !IsDrawn;

    /// <summary>Значение правится флажком.</summary>
    public bool IsToggle => Kind == InspectorRowKind.Toggle && !IsDrawn;

    /// <summary>Значение выбирается из списка.</summary>
    public bool IsChoice => Kind == InspectorRowKind.Choice && !IsDrawn;

    /// <summary>Значение, заданное в разметке; null, если свойство не задано.</summary>
    public string? Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    /// <summary>
    /// Действующее значение, когда своего у элемента нет: подсказка внутри поля.
    /// </summary>
    public string? Placeholder
    {
        get => _placeholder;
        private set => Set(ref _placeholder, value);
    }

    /// <summary>Откуда взялось действующее значение: стиль, шаблон, привязка.</summary>
    public string? Source
    {
        get => _source;
        private set => Set(ref _source, value);
    }

    /// <summary>Свойство задано прямо в разметке этого элемента.</summary>
    public bool IsSet
    {
        get => _isSet;
        private set => Set(ref _isSet, value);
    }

    /// <summary>Значение флажка; осмысленно только для <see cref="InspectorRowKind.Toggle"/>.</summary>
    public bool IsChecked
    {
        get => bool.TryParse(_value ?? _placeholder, out var flag) && flag;
        set => Value = value ? "True" : "False";
    }

    /// <summary>Заполняет строку тем, что видно у элемента сейчас.</summary>
    /// <param name="value">Значение из разметки или null.</param>
    /// <param name="placeholder">Действующее значение.</param>
    /// <param name="source">Откуда действующее значение взялось.</param>
    internal void Fill(string? value, string? placeholder, string? source)
    {
        _value = value;
        IsSet = value is not null;
        Placeholder = placeholder;
        Source = source;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));

        // Рисовальщик плагина о правках из разметки узнаёт только отсюда:
        // привязок к строке у него нет.
        Refilled?.Invoke(this, EventArgs.Empty);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Раздел инспектора: заголовок и строки под ним.</summary>
/// <param name="Title">Заголовок раздела.</param>
public sealed record InspectorSection(string Title)
{
    /// <summary>Строки раздела.</summary>
    public ObservableCollection<InspectorRow> Rows { get; } = [];
}

/// <summary>
/// Собирает содержимое инспектора для выделенного элемента.
/// </summary>
/// <remarks>
/// У живого объекта сотни свойств, и показать их списком — значит не показать
/// ничего. Поэтому разделы набираются по известному перечню, из которого
/// остаётся только то, что у этого типа действительно есть, а всё заданное в
/// разметке помимо перечня сходится в последний раздел: то, что автор написал
/// руками, спрятать нельзя.
/// </remarks>
public static class InspectorModel
{
    private static readonly string[] LayoutMembers =
    [
        "Width", "Height", "MinWidth", "MinHeight",
        "Margin", "Padding",
        "HorizontalAlignment", "VerticalAlignment",
        "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "DockPanel.Dock",
    ];

    private static readonly string[] AppearanceMembers =
    [
        "Background", "Foreground", "BorderBrush", "BorderThickness",
        "CornerRadius", "Opacity",
        "FontSize", "FontWeight", "FontStyle",
    ];

    private static readonly string[] ContentMembers =
    [
        "Content", "Text", "PlaceholderText",
        "IsEnabled", "IsVisible", "Classes", "ToolTip.Tip",
    ];

    /// <summary>
    /// Собирает разделы для узла дерева.
    /// </summary>
    /// <param name="node">Выделенный узел.</param>
    /// <param name="session">Сессия загрузки — по ней читаются живые значения.</param>
    /// <returns>Разделы, в которых есть хоть одна строка.</returns>
    public static IReadOnlyList<InspectorSection> Build(HierarchyNode node, XamlLoadSession? session)
    {
        ArgumentNullException.ThrowIfNull(node);

        var sections = new List<InspectorSection>
        {
            Section("Раскладка", LayoutMembers, node, session),
            Section("Вид", AppearanceMembers, node, session),
            Section("Содержимое", ContentMembers, node, session),
        };

        var known = sections
            .SelectMany(section => section.Rows.Select(row => row.Name))
            .ToHashSet(StringComparer.Ordinal);

        var rest = new InspectorSection("Прочее");

        foreach (var attribute in Authored(node.Element))
        {
            var name = attribute.Name.ToString();

            if (known.Contains(name))
                continue;

            var row = new InspectorRow(name, InspectorRowKind.Text);
            row.Fill(attribute.GetValueText(), null, null);
            rest.Rows.Add(row);
        }

        if (rest.Rows.Count > 0)
            sections.Add(rest);

        return [.. sections.Where(section => section.Rows.Count > 0)];
    }

    /// <summary>Перечитывает значения строк, не пересобирая разделы.</summary>
    /// <param name="sections">Разделы, собранные ранее.</param>
    /// <param name="node">Тот же узел после правки.</param>
    /// <param name="session">Сессия загрузки.</param>
    public static void Refresh(IEnumerable<InspectorSection> sections, HierarchyNode node, XamlLoadSession? session)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(node);

        foreach (var row in sections.SelectMany(section => section.Rows))
            Fill(row, node, session);
    }

    private static InspectorSection Section(string title, IEnumerable<string> members, HierarchyNode node, XamlLoadSession? session)
    {
        var section = new InspectorSection(title);

        foreach (var name in members)
        {
            if (Describe(name, node, session) is not { } row)
                continue;

            Fill(row, node, session);
            section.Rows.Add(row);
        }

        return section;
    }

    /// <summary>
    /// Решает, показывать ли свойство и чем его править.
    /// </summary>
    /// <remarks>
    /// Свойство попадает в инспектор, если оно есть у типа; заданное в разметке
    /// показывается и тогда, когда тип нам неизвестен, — иначе автор потерял бы
    /// из виду то, что сам написал.
    /// </remarks>
    private static InspectorRow? Describe(string name, HierarchyNode node, XamlLoadSession? session)
    {
        var authored = node.Element.GetAttribute(XamlQualifiedName.Parse(name)) is not null;

        if (session is null || node.Control is not { } target)
            return authored ? new InspectorRow(name, InspectorRowKind.Text) : null;

        var member = session.GetMember(target, name);

        if (!member.IsResolved || !member.CanWrite)
            return authored ? new InspectorRow(name, InspectorRowKind.Text) : null;

        var type = Nullable.GetUnderlyingType(member.ValueType) ?? member.ValueType;

        if (type == typeof(bool))
            return new InspectorRow(name, InspectorRowKind.Toggle, type);

        return type.IsEnum
            ? new InspectorRow(name, InspectorRowKind.Choice, type, Enum.GetNames(type))
            : new InspectorRow(name, InspectorRowKind.Text, type);
    }

    private static void Fill(InspectorRow row, HierarchyNode node, XamlLoadSession? session)
    {
        var attribute = node.Element.GetAttribute(XamlQualifiedName.Parse(row.Name));
        var value = attribute?.GetValueText();

        string? placeholder = null;
        string? source = null;

        if (session is not null && node.Control is { } target)
        {
            var member = session.GetMember(target, row.Name);

            if (member is { IsResolved: true, AvaloniaProperty: { } property })
            {
                var info = session.GetValueInfo(target, property);

                placeholder = Text(info.EffectiveValue);
                source = info.HasBinding ? "binding" : Describe(info.Source);
            }
        }

        row.Fill(value, placeholder, value is null ? source : null);
    }

    /// <summary>
    /// Как показать действующее значение подсказкой в поле.
    /// </summary>
    /// <remarks>
    /// Незаданный размер в Avalonia — это NaN, и печатать его как есть значило
    /// бы показывать «не число» там, где на деле написано «сколько попросит
    /// содержимое».
    /// </remarks>
    private static string? Text(object? value) => value switch
    {
        null => null,
        double number when double.IsNaN(number) => "auto",
        _ => value.ToString(),
    };

    private static string? Describe(XamlValueSource source) => source switch
    {
        XamlValueSource.Style => "style",
        XamlValueSource.StyleTrigger => "trigger",
        XamlValueSource.Template => "template",
        XamlValueSource.Inherited => "inherited",
        XamlValueSource.Animation => "animation",
        XamlValueSource.Binding => "binding",
        _ => null,
    };

    /// <summary>
    /// Атрибуты, написанные автором: без объявлений пространств имён, директив
    /// <c>x:</c>, атрибутов времени дизайна и совместимости разметки.
    /// </summary>
    private static IEnumerable<XamlAttribute> Authored(XamlElement element) =>
        element.Attributes.Where(attribute =>
            attribute is not XamlNamespaceDeclaration &&
            !attribute.IsDirective &&
            !attribute.IsDesignTime &&
            !attribute.IsMarkupCompatibility);
}
