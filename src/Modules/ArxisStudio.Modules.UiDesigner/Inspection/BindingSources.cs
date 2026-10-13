using System.Globalization;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>Из чего панель привязки выбирает источник: элементы формы и типы предков — по документу.</summary>
internal static class BindingSources
{
    /// <summary>Окно, в котором форма встанет: его в документе нет, а привязка к нему — обычное дело.</summary>
    private const string Window = "Window";

    /// <summary>
    /// Элементы формы, к которым можно привязать элемент: с именем — первыми, по имени; без имени — следом, в порядке
    /// документа, каждый со свободным именем, которое он получит той же правкой.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Источник по имени — контрол формы. Модель в данных, кисть в ресурсах, стиль и сам шаблон — объекты документа, но
    /// не элементы, к которым привязывают, и имя им давать незачем. Элемент шаблона — в своей области имён, и снаружи его
    /// имя не видно: такие не предлагаются.
    /// </para>
    /// <para>
    /// Свободное имя — camelCase типа с номером, если занято: имя элемента становится полем класса формы, и совпадение с
    /// другим именем документа — и у объекта, который не предлагается, — или с членом класса корня и его баз уронило бы
    /// сборку.
    /// </para>
    /// </remarks>
    /// <param name="document">Документ формы.</param>
    /// <param name="bound">Элемент, к члену которого привязывают: себе он не источник по имени — для этого есть «Сам».</param>
    /// <param name="rootMembers">Члены класса корня и его баз.</param>
    /// <param name="isControl">Построил ли элемент контрол.</param>
    public static IReadOnlyList<BindingEditor.ElementChoice> Elements(
        XamlDocument document, XamlElement bound, IEnumerable<string> rootMembers, Func<XamlElementPath, bool> isControl)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(rootMembers);
        ArgumentNullException.ThrowIfNull(isControl);

        if (document.Root is not { } root)
            return [];

        var scope = root.DescendantElements().Prepend(root)
            .Where(static element => !element.IsPropertyElementSyntax && !InTemplate(element))
            .ToList();
        var taken = new HashSet<string>(rootMembers, StringComparer.Ordinal);

        foreach (var element in scope)
        {
            if (element.Identity is { Length: > 0 } identity)
                taken.Add(identity);
        }

        var elements = scope
            .Where(element => !ReferenceEquals(element, bound) && !IsTemplate(element) && isControl(XamlElementPath.Of(element)))
            .ToList();

        var named = elements
            .Where(static element => element.Identity is { Length: > 0 })
            .Select(static element => new BindingEditor.ElementChoice(XamlElementPath.Of(element), element.Identity, element.Name.LocalName, null))
            .OrderBy(static choice => choice.Name, StringComparer.Ordinal);
        var unnamed = elements
            .Where(static element => element.Identity is not { Length: > 0 })
            .Select(element => new BindingEditor.ElementChoice(XamlElementPath.Of(element), null, element.Name.LocalName, FreeName(element.Name.LocalName, taken)));

        return [.. named, .. unnamed];
    }

    /// <summary>Типы предков элемента в документе — ближайший первым, — и окно, если формы в нём ещё нет.</summary>
    /// <param name="element">Элемент.</param>
    public static IReadOnlyList<string> Ancestors(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var types = new List<string>();

        for (var at = OwnerOf(element); at is not null; at = OwnerOf(at))
        {
            if (!types.Contains(at.Name.LocalName, StringComparer.Ordinal))
                types.Add(at.Name.LocalName);
        }

        if (!types.Contains(Window, StringComparer.Ordinal))
            types.Add(Window);

        return types;
    }

    /// <summary>Свободное имя: camelCase типа, а занятое — с номером.</summary>
    private static string FreeName(string type, HashSet<string> taken)
    {
        var stem = type.Length == 0 ? "element" : char.ToLowerInvariant(type[0]) + type[1..];

        for (var number = 1; ; number++)
        {
            var candidate = number == 1 ? stem : stem + number.ToString(CultureInfo.InvariantCulture);

            if (taken.Add(candidate))
                return candidate;
        }
    }

    /// <summary>Шаблон ли элемент — своя область имён для того, что в нём.</summary>
    private static bool IsTemplate(XamlElement element) =>
        !element.IsPropertyElementSyntax && element.Name.LocalName.EndsWith("Template", StringComparison.Ordinal);

    /// <summary>Лежит ли элемент в шаблоне.</summary>
    private static bool InTemplate(XamlElement element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement owner && IsTemplate(owner))
                return true;
        }

        return false;
    }

    /// <summary>Элемент-владелец: родитель в документе, минуя элемент свойства.</summary>
    private static XamlElement? OwnerOf(XamlElement element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement { IsPropertyElementSyntax: false } owner)
                return owner;
        }

        return null;
    }
}
