using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Куда встаёт вставленное из буфера: место выбирает выбор формы, а не указатель.
/// </summary>
/// <remarks>
/// <para>
/// Выбрана панель — в неё, в конец. Выбран не-панель — в ближайшую панель над ним, сразу после того его
/// предка, что стоит в этой панели: рамка и кнопка держат одно содержимое, и второе стало бы ошибкой
/// загрузки. Ничего не выбрано или выбран корень — в корень-панель, в панель, что стоит в корне, или в
/// пустой корень содержимым.
/// </para>
/// <para>
/// Элемент внутри элемента свойства — определение строки, ресурс, стиль — местом не служит: его соседи —
/// значения члена, а не дети панели, и номер среди них ничего не значит в содержимом владельца.
/// </para>
/// </remarks>
internal static class FormLanding
{
    /// <summary>Куда встанет новое.</summary>
    /// <param name="selection">Выбор формы, первый главный.</param>
    /// <param name="shown">Показ формы: панель ли элемент, знает его объект.</param>
    /// <param name="syntax">Текст формы.</param>
    /// <returns>Путь родителя и место среди его содержимого; null — встать некуда: корень держит одно содержимое, и оно занято.</returns>
    public static (XamlElementPath Parent, int Index)? For(
        IReadOnlyList<XamlElementPath> selection,
        IXamlDesignView shown,
        XamlDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(syntax);

        var primary = selection.FirstOrDefault();

        if (primary is not null && !primary.Equals(XamlElementPath.Root) && primary.Resolve(syntax) is { } selected)
        {
            if (IsPanel(shown, primary))
                return (primary, selected.ContentElements.Count());

            for (var child = primary; child.Parent is { } parent; child = parent)
            {
                if (child.Steps[^1].MemberName is not null)
                    continue;

                if (IsPanel(shown, parent) && child.Resolve(syntax) is { IndexInContent: >= 0 } placed)
                    return (parent, placed.IndexInContent + 1);
            }
        }

        if (syntax.Root is not { } root)
            return null;

        if (IsPanel(shown, XamlElementPath.Root))
            return (XamlElementPath.Root, root.ContentElements.Count());

        var content = root.ContentElements.ToList();

        if (content.Count == 0)
            return (XamlElementPath.Root, 0);

        if (content.Count == 1 && XamlElementPath.Of(content[0]) is { } inner && IsPanel(shown, inner))
            return (inner, content[0].ContentElements.Count());

        return null;
    }

    /// <summary>Держит ли родитель места сколько угодно, а не одно содержимое.</summary>
    /// <param name="shown">Показ формы.</param>
    /// <param name="parent">Путь родителя.</param>
    public static bool HoldsMany(IXamlDesignView shown, XamlElementPath parent)
    {
        ArgumentNullException.ThrowIfNull(shown);

        return IsPanel(shown, parent);
    }

    /// <summary>Построил ли элемент панель: детей у неё может быть сколько угодно.</summary>
    /// <remarks>Объект показа спрашивается и отпускается тут же: дольше вызова его не держат.</remarks>
    private static bool IsPanel(IXamlDesignView shown, XamlElementPath path) => shown.ObjectAt(path) is Panel;
}
