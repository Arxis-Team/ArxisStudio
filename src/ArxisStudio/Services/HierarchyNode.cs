using System.Collections.ObjectModel;
using System.ComponentModel;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Узел дерева документа: элемент разметки и объект, который он породил.
/// </summary>
/// <remarks>
/// Держим и то и другое: по элементу мы правим разметку, по объекту —
/// показываем выделение на канве. Элемент живёт ровно один разбор документа, и
/// после каждой правки на его месте оказывается другой объект, поэтому рядом
/// лежит <see cref="Path"/> — ссылка, переживающая переразбор, — а сам элемент
/// узел меняет на новый, не теряя ни раскрытия дерева, ни выделения.
/// </remarks>
public sealed class HierarchyNode : INotifyPropertyChanged
{
    private XamlElement _element;

    /// <summary>Создаёт узел.</summary>
    /// <param name="element">Элемент разметки.</param>
    /// <param name="control">Живой контрол или null, если элемент не дал контрола.</param>
    /// <param name="path">Путь к элементу, переживающий переразбор документа.</param>
    public HierarchyNode(XamlElement element, Control? control, XamlElementPath? path)
    {
        _element = element;
        Control = control;
        Path = path;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Элемент разметки в текущем разборе документа.</summary>
    public XamlElement Element => _element;

    /// <summary>Живой контрол или null, если элемент не дал контрола.</summary>
    public Control? Control { get; private set; }

    /// <summary>Путь к элементу, переживающий переразбор документа.</summary>
    public XamlElementPath? Path { get; }

    /// <summary>Дочерние узлы.</summary>
    public ObservableCollection<HierarchyNode> Children { get; } = [];

    /// <summary>Имя типа, как написано в разметке: <c>Button</c>, <c>Grid</c>.</summary>
    public string TypeName => _element.Name.LocalName;

    /// <summary>Значение <c>x:Name</c>, если оно есть.</summary>
    public string? Identity => _element.Identity;

    /// <summary>Что показывать в дереве: имя, если задано, иначе тип.</summary>
    public string DisplayName => Identity is { Length: > 0 } name ? name : TypeName;

    /// <summary>Подпись справа: тип, когда слева стоит имя.</summary>
    public string? TypeHint => Identity is { Length: > 0 } ? TypeName : null;

    /// <summary>Переводит узел на новый разбор документа.</summary>
    /// <param name="element">Элемент в новом разборе.</param>
    /// <param name="control">Живой контрол; null оставляет прежний.</param>
    internal void Retarget(XamlElement element, Control? control)
    {
        _element = element;

        if (control is not null)
            Control = control;

        foreach (var name in new[] { nameof(Element), nameof(TypeName), nameof(Identity), nameof(DisplayName), nameof(TypeHint) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
