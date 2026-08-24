using System.Collections.ObjectModel;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Узел дерева документа: элемент разметки и объект, который он породил.
/// </summary>
/// <remarks>
/// Держим и то и другое: по элементу мы правим разметку, по объекту —
/// показываем выделение на канве. Элемент живёт ровно один разбор документа,
/// поэтому для долгих ссылок рядом лежит <see cref="Path"/>.
/// </remarks>
/// <param name="Element">Элемент разметки.</param>
/// <param name="Control">Живой контрол или null, если элемент не дал контрола.</param>
/// <param name="Path">Путь к элементу, переживающий переразбор документа.</param>
public sealed record HierarchyNode(XamlElement Element, Control? Control, XamlElementPath? Path)
{
    /// <summary>Дочерние узлы.</summary>
    public ObservableCollection<HierarchyNode> Children { get; } = [];

    /// <summary>Имя типа, как написано в разметке: <c>Button</c>, <c>Grid</c>.</summary>
    public string TypeName => Element.Name.LocalName;

    /// <summary>Значение <c>x:Name</c>, если оно есть.</summary>
    public string? Identity => Element.Identity;

    /// <summary>Что показывать в дереве: имя, если задано, иначе тип.</summary>
    public string DisplayName => Identity is { Length: > 0 } name ? name : TypeName;

    /// <summary>Подпись справа: тип, когда слева стоит имя.</summary>
    public string? TypeHint => Identity is { Length: > 0 } ? TypeName : null;
}
