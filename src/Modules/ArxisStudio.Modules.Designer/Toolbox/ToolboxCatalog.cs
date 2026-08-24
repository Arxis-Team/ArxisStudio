using ArxisStudio.Markup.Xaml;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Modules.Designer;

/// <summary>Контрол, который палитра умеет вставлять в документ.</summary>
/// <param name="TypeName">Имя типа, как оно пишется в разметке.</param>
/// <param name="NamespaceUri">Пространство имён, из которого он берётся.</param>
/// <param name="Attributes">Атрибуты, с которыми он вставляется.</param>
/// <param name="NeedsSize">
/// Нужен ли ему размер, чтобы его было видно: пустой контейнер без размера
/// занимает ноль пикселей, и вставка выглядит как ничего не произошло.
/// </param>
public sealed record ToolboxItem(
    string TypeName,
    string NamespaceUri,
    string Attributes = "",
    bool NeedsSize = false);

/// <summary>Раздел палитры.</summary>
/// <param name="TitleKey">Ключ заголовка раздела.</param>
/// <param name="Items">Контролы раздела.</param>
public sealed record ToolboxGroup(string TitleKey, IReadOnlyList<ToolboxItem> Items)
{
    /// <summary>Заголовок раздела на языке интерфейса.</summary>
    public LocalizedString Title { get; } = Localizer.Instance.Track(TitleKey);
}

/// <summary>
/// Палитра контролов: что можно положить на канву открытого документа.
/// </summary>
/// <remarks>
/// Палитра показывает не всё, что есть в мире, а то, чем этот документ может
/// воспользоваться: контрол из библиотеки, которую документ не объявил, при
/// вставке дал бы неразрешимый тип. Поэтому раздел появляется, только если у
/// его пространства имён есть префикс в корне документа — у разметки Avalonia
/// он есть всегда, у библиотеки контролов студии только там, где её объявили.
/// </remarks>
public static class ToolboxCatalog
{
    /// <summary>Пространство имён разметки Avalonia.</summary>
    public const string AvaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>Пространство имён библиотеки контролов студии.</summary>
    public const string ControlsNamespace = "using:ArxisStudio.Controls";

    private static readonly ToolboxGroup[] All =
    [
        new("toolbox.group.layout",
        [
            new("Grid", AvaloniaNamespace, NeedsSize: true),
            new("StackPanel", AvaloniaNamespace, NeedsSize: true),
            new("DockPanel", AvaloniaNamespace, NeedsSize: true),
            new("WrapPanel", AvaloniaNamespace, NeedsSize: true),
            new("Canvas", AvaloniaNamespace, NeedsSize: true),
            new("Border", AvaloniaNamespace, "BorderThickness=\"1\"", NeedsSize: true),
            new("ScrollViewer", AvaloniaNamespace, NeedsSize: true),
        ]),

        new("toolbox.group.input",
        [
            new("Button", AvaloniaNamespace, "Content=\"Button\""),
            new("TextBox", AvaloniaNamespace, "Width=\"160\""),
            new("CheckBox", AvaloniaNamespace, "Content=\"CheckBox\""),
            new("RadioButton", AvaloniaNamespace, "Content=\"RadioButton\""),
            new("ToggleSwitch", AvaloniaNamespace),
            new("ComboBox", AvaloniaNamespace, "Width=\"160\""),
            new("Slider", AvaloniaNamespace, "Width=\"160\""),
        ]),

        new("toolbox.group.display",
        [
            new("TextBlock", AvaloniaNamespace, "Text=\"TextBlock\""),
            new("Image", AvaloniaNamespace, NeedsSize: true),
            new("ProgressBar", AvaloniaNamespace, "Width=\"160\" Value=\"40\""),
            new("ListBox", AvaloniaNamespace, NeedsSize: true),
            new("TabControl", AvaloniaNamespace, NeedsSize: true),
            new("TreeView", AvaloniaNamespace, NeedsSize: true),
        ]),

        new("toolbox.group.studio",
        [
            new("AxButton", ControlsNamespace, "Content=\"Кнопка\""),
            new("AxTextBox", ControlsNamespace, "Width=\"160\""),
            new("AxSearchField", ControlsNamespace, "Width=\"160\""),
            new("AxCheckBox", ControlsNamespace, "Content=\"Флажок\""),
            new("AxToggleSwitch", ControlsNamespace),
            new("AxComboBox", ControlsNamespace, "Width=\"160\""),
            new("AxSlider", ControlsNamespace, "Width=\"160\""),
            new("AxProgressBar", ControlsNamespace, "Width=\"160\""),
            new("AxBadge", ControlsNamespace, "Content=\"3\""),
            new("AxChip", ControlsNamespace, "Content=\"Метка\""),
            new("AxCard", ControlsNamespace, NeedsSize: true),
            new("AxDivider", ControlsNamespace),
        ]),
    ];

    /// <summary>Собирает палитру для документа.</summary>
    /// <param name="root">Корневой элемент документа.</param>
    /// <param name="filter">Строка поиска; пусто — показывать всё.</param>
    /// <returns>Разделы, в которых есть хоть один контрол.</returns>
    public static IReadOnlyList<ToolboxGroup> For(XamlElement? root, string? filter = null)
    {
        if (root is null)
            return [];

        var groups = new List<ToolboxGroup>();

        foreach (var group in All)
        {
            var items = group.Items
                .Where(item => root.NamespaceContext.LookupPrefix(item.NamespaceUri) is not null)
                .Where(item => Matches(item, filter))
                .ToList();

            if (items.Count > 0)
                groups.Add(group with { Items = items });
        }

        return groups;
    }

    /// <summary>
    /// Собирает разметку вставляемого контрола под тот документ, куда он идёт.
    /// </summary>
    /// <param name="item">Контрол палитры.</param>
    /// <param name="parent">Элемент, внутрь которого он вставляется.</param>
    /// <param name="placement">Атрибуты положения; пусто, если родитель кладёт сам.</param>
    /// <returns>Разметка элемента или null, если документ не объявил его пространство имён.</returns>
    public static string? Markup(ToolboxItem item, XamlElement parent, string placement = "")
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(parent);

        if (parent.NamespaceContext.LookupPrefix(item.NamespaceUri) is not { } prefix)
            return null;

        var name = prefix.Length > 0 ? $"{prefix}:{item.TypeName}" : item.TypeName;

        var attributes = string.Join(
            ' ',
            new[] { item.Attributes, item.NeedsSize ? "Width=\"120\" Height=\"80\"" : "", placement }
                .Where(part => part.Length > 0));

        return attributes.Length > 0 ? $"<{name} {attributes}/>" : $"<{name}/>";
    }

    private static bool Matches(ToolboxItem item, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        item.TypeName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
