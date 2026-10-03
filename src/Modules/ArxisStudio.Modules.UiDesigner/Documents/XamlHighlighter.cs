using ArxisStudio.Controls;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Роли текста разметки для просмотра кода — по синтаксическому дереву Markup, без загрузки и без
/// сборки.
/// </summary>
/// <remarks>
/// <para>
/// Два прохода, и они не пересекаются. Токены дают то, что видно по ним самим: скобки тегов, значения в
/// кавычках — расширение разметки, когда значение начинается с фигурной скобки, — комментарии, объявление
/// XML и то, что разбор пропустил. Дерево даёт имена: тег элемента, атрибут, директиву <c>x:</c> и
/// приставку перед двоеточием — токен имени один и тот же у всех, а чьё это имя, знает только дерево.
/// </para>
/// <para>
/// Отрезки идут в том порядке, в каком найдены, — сортирует и обрезает их сам просмотр кода.
/// </para>
/// </remarks>
internal static class XamlHighlighter
{
    /// <summary>Отрезки ролей документа.</summary>
    /// <param name="document">Документ.</param>
    public static IReadOnlyList<AxCodeSpan> Spans(XamlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var spans = new List<AxCodeSpan>();
        var text = document.SourceText;

        foreach (var token in document.Tokens)
        {
            if (token.Span.Length == 0 || RoleOf(token, text) is not { } role)
                continue;

            spans.Add(new AxCodeSpan(token.Span.Start, token.Span.Length, role));
        }

        Walk(document.Root, spans);

        return spans;
    }

    /// <summary>
    /// Элемент под кареткой — самый вложенный, чей текст её содержит; элемент свойства отвечает
    /// владельцем: выбирают контрол, а не его <c>Button.Content</c>.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="offset">Смещение каретки.</param>
    /// <returns>Элемент; null — каретка вне корня.</returns>
    public static XamlElement? ElementAt(XamlDocument document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);

        XamlElement? found = null;

        for (var element = document.Root; element is not null && Contains(element.Span, offset);)
        {
            found = element;
            element = element.Elements.FirstOrDefault(child => Contains(child.Span, offset));
        }

        while (found is { IsPropertyElementSyntax: true, Parent: XamlElement owner })
            found = owner;

        return found;
    }

    private static AxCodeRole? RoleOf(XamlToken token, SourceText text) => token.Kind switch
    {
        XamlTokenKind.LessThan or XamlTokenKind.LessThanSlash or XamlTokenKind.GreaterThan
            or XamlTokenKind.SlashGreaterThan => AxCodeRole.Tag,
        XamlTokenKind.Quote => AxCodeRole.String,
        XamlTokenKind.AttributeValueText => IsExtension(text, token.Span) ? AxCodeRole.Extension : AxCodeRole.String,
        XamlTokenKind.Comment => AxCodeRole.Comment,
        XamlTokenKind.CData => AxCodeRole.String,
        XamlTokenKind.ProcessingInstruction or XamlTokenKind.XmlDeclaration
            or XamlTokenKind.DocumentType => AxCodeRole.Directive,
        XamlTokenKind.EntityReference => AxCodeRole.Extension,
        XamlTokenKind.Skipped => AxCodeRole.Error,
        _ => null,
    };

    /// <summary>Значение — расширение разметки: начинается с фигурной скобки, и это не экранирование <c>{}</c>.</summary>
    private static bool IsExtension(SourceText text, TextSpan span)
    {
        var value = text.GetText(span).TrimStart();

        return value.StartsWith('{') && !value.StartsWith("{}", StringComparison.Ordinal);
    }

    private static void Walk(XamlElement? element, List<AxCodeSpan> spans)
    {
        if (element is null)
            return;

        Name(element.Name, element.NameSpan, AxCodeRole.Tag, spans);

        // Имя закрывающего тега стоит сразу за «</»: пробела там XML не допускает.
        if (element.EndTagName is { } closing && element.EndTagSpan is { } end)
            Name(closing, new TextSpan(end.Start + 2, closing.ToString().Length), AxCodeRole.Tag, spans);

        foreach (var attribute in element.Attributes)
        {
            var role = attribute.IsDirective ? AxCodeRole.Directive
                : IsNamespaceDeclaration(attribute.Name) ? AxCodeRole.Prefix
                : AxCodeRole.Attribute;

            Name(attribute.Name, attribute.NameSpan, role, spans);
        }

        foreach (var child in element.Elements)
            Walk(child, spans);
    }

    /// <summary>Имя с приставкой: приставка — своей ролью, двоеточие — текстом, остальное — ролью имени.</summary>
    private static void Name(XamlQualifiedName name, TextSpan span, AxCodeRole role, List<AxCodeSpan> spans)
    {
        if (role is AxCodeRole.Tag or AxCodeRole.Attribute && name.Prefix is { Length: > 0 } prefix && span.Length > prefix.Length + 1)
        {
            spans.Add(new AxCodeSpan(span.Start, prefix.Length, AxCodeRole.Prefix));
            spans.Add(new AxCodeSpan(span.Start + prefix.Length + 1, span.Length - prefix.Length - 1, role));

            return;
        }

        spans.Add(new AxCodeSpan(span.Start, span.Length, role));
    }

    private static bool IsNamespaceDeclaration(XamlQualifiedName name) =>
        name.Prefix == "xmlns" || (name.Prefix is null && name.LocalName == "xmlns");

    private static bool Contains(TextSpan span, int offset) => offset >= span.Start && offset < span.End;
}
