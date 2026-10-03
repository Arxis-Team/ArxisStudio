using System.ComponentModel;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>Строка иерархии: элемент документа формы — тип, имя и дети его содержимого.</summary>
/// <remarks>
/// Строка — сведение о тексте, а не о живом дереве: путь, имя типа, как его написал документ, и
/// <c>x:Name</c>. Поэтому иерархия не держит поколения и одинаково читается до сборки, после замены
/// типов и у формы, текст которой отстал от разметки.
/// </remarks>
internal sealed class HierarchyNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    /// <summary>Строит строку.</summary>
    /// <param name="path">Путь элемента.</param>
    /// <param name="type">Имя элемента, как написано: <c>Button</c>, <c>local:Badge</c>.</param>
    /// <param name="name"><c>x:Name</c>; null — элемент без имени.</param>
    /// <param name="children">Строки элементов его содержимого.</param>
    /// <param name="isExpanded">Раскрыта ли.</param>
    public HierarchyNode(XamlElementPath path, string type, string? name, IReadOnlyList<HierarchyNode> children, bool isExpanded)
    {
        Path = path;
        Type = type;
        Name = name;
        Children = children;
        _isExpanded = isExpanded;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Путь элемента в тексте формы.</summary>
    public XamlElementPath Path { get; }

    /// <summary>Имя элемента, как его написал документ.</summary>
    public string Type { get; }

    /// <summary><c>x:Name</c>; null — элемент без имени.</summary>
    public string? Name { get; }

    /// <summary>Есть ли имя: показать его рядом с типом.</summary>
    public bool HasName => Name is not null;

    /// <summary>Что читает диктор: тип и имя.</summary>
    public string Label => Name is null ? Type : $"{Type} {Name}";

    /// <summary>Элементы содержимого.</summary>
    public IReadOnlyList<HierarchyNode> Children { get; }

    /// <summary>Раскрыта ли строка.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>Строки иерархии документа: корень и всё его содержимое.</summary>
    /// <param name="document">Текст формы.</param>
    /// <param name="collapsed">Пути, которые человек свернул: после правки они остаются свёрнутыми.</param>
    /// <returns>Корень одной строкой; пусто — у документа нет корня.</returns>
    /// <remarks>
    /// Элементы свойств — <c>&lt;Grid.RowDefinitions&gt;</c>, <c>&lt;Window.Styles&gt;</c> — не строки: это
    /// значения членов, а не дерево формы, и правят их инспектором.
    /// </remarks>
    public static IReadOnlyList<HierarchyNode> Of(XamlDocument document, IReadOnlySet<XamlElementPath> collapsed)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(collapsed);

        return document.Root is { } root ? [Node(root, collapsed)] : [];
    }

    private static HierarchyNode Node(XamlElement element, IReadOnlySet<XamlElementPath> collapsed)
    {
        var path = XamlElementPath.Of(element);
        var children = element.ContentElements.Select(child => Node(child, collapsed)).ToList();

        return new HierarchyNode(path, element.Name.ToString(), element.GetDirective("Name"), children, !collapsed.Contains(path));
    }

    /// <summary>Эта строка и все под ней — по порядку документа.</summary>
    public IEnumerable<HierarchyNode> SelfAndDescendants()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var node in child.SelfAndDescendants())
                yield return node;
        }
    }
}
