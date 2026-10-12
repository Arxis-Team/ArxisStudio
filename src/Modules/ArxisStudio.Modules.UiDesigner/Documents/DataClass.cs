using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Класс, которым форма задаёт данные, — так, как его несёт запись инспектора: элемент со своим объявлением,
/// <c>&lt;vm:MainViewModel xmlns:vm="using:App.ViewModels" /&gt;</c>.
/// </summary>
/// <param name="XmlNamespace">Пространство разметки класса.</param>
/// <param name="Name">Имя класса.</param>
/// <param name="Prefix">Префикс, которым запись его называет; пусто — пространство по умолчанию.</param>
/// <remarks>
/// <para>
/// Запись стоит сама по себе: префикс объявлен в ней же, и «Копировать» в одной форме с «Вставить» в другой несут
/// тот же класс, как бы ни звали его пространство там. Правка объявляет пространство на корне формы, если его там
/// нет (<see cref="FormEdits"/>).
/// </para>
/// <para>
/// Только пустой элемент: свойства модели, заданные в разметке, — уже не выбор класса, и инспектор их не пишет, а
/// написанные — показывает и сохраняет, пока класс не сменят.
/// </para>
/// </remarks>
internal sealed record DataClass(string XmlNamespace, string Name, string Prefix)
{
    private const string Using = "using:";
    private const string ClrNamespace = "clr-namespace:";

    /// <summary>Текст записи: элемент с объявлением своего пространства.</summary>
    public string Text => Prefix.Length == 0
        ? $"<{Name} xmlns=\"{Escape(XmlNamespace)}\" />"
        : $"<{Prefix}:{Name} xmlns:{Prefix}=\"{Escape(XmlNamespace)}\" />";

    /// <summary>Полное имя CLR, если пространство его называет (<c>using:</c>, <c>clr-namespace:</c>); иначе null.</summary>
    public string? ClrName => ClrNamespaceOf(XmlNamespace) is { } clr ? clr + "." + Name : null;

    /// <summary>Читает запись; null — это не пустой элемент класса со своим объявлением.</summary>
    /// <param name="text">Текст записи.</param>
    public static DataClass? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('<'))
            return null;

        var fragment = XamlFragment.Parse(text);

        if (fragment.Root is not { } root || !fragment.Document.IsWellFormed || root.IsPropertyElementSyntax ||
            root.Elements.Any() || root.Attributes.Any(static attribute => attribute is not XamlNamespaceDeclaration) ||
            root.NamespaceUri is not { Length: > 0 } xmlNamespace)
        {
            return null;
        }

        return new DataClass(xmlNamespace, root.Name.LocalName, root.Name.Prefix ?? string.Empty);
    }

    /// <summary>Класс, который элемент документа создаёт; null — элемент не пустой или его пространство не объявлено.</summary>
    /// <param name="instance">Элемент внутри элемента свойства: <c>&lt;vm:Main /&gt;</c>.</param>
    public static DataClass? Of(XamlElement instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.IsPropertyElementSyntax || instance.Elements.Any() || instance.NamespaceUri is not { Length: > 0 } xmlNamespace ||
            instance.Attributes.Any(static attribute => attribute is not XamlNamespaceDeclaration))
        {
            return null;
        }

        return new DataClass(xmlNamespace, instance.Name.LocalName, instance.Name.Prefix ?? string.Empty);
    }

    /// <summary>
    /// Префикс для записи класса: тот, что предлагает его сборка, а без него — <c>vm</c> у моделей представления и
    /// последнее звено пространства у остальных.
    /// </summary>
    /// <param name="xmlNamespace">Пространство разметки.</param>
    /// <param name="suggested">Префикс сборки; null — не предлагает.</param>
    public static string PrefixFor(string xmlNamespace, string? suggested)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);

        if (suggested is { Length: > 0 })
            return suggested;

        var last = ClrNamespaceOf(xmlNamespace) is { } clr
            ? clr[(clr.LastIndexOf('.') + 1)..]
            : xmlNamespace.TrimEnd('/').Split('/', ':').LastOrDefault(static part => part.Length > 0);

        if (last is null || last.Length == 0)
            return "vm";

        if (last.EndsWith("ViewModels", StringComparison.Ordinal) || last.EndsWith("ViewModel", StringComparison.Ordinal))
            return "vm";

        var letters = new string([.. last.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

        return letters.Length == 0 || !char.IsLetter(letters[0]) || letters.StartsWith("xml", StringComparison.Ordinal) ? "vm" : letters;
    }

    /// <summary>Пространство CLR, которое называет пространство разметки; null — оно его не называет.</summary>
    internal static string? ClrNamespaceOf(string xmlNamespace)
    {
        var clr = xmlNamespace.StartsWith(Using, StringComparison.Ordinal)
            ? xmlNamespace[Using.Length..]
            : xmlNamespace.StartsWith(ClrNamespace, StringComparison.Ordinal)
                ? xmlNamespace[ClrNamespace.Length..].Split(';')[0]
                : null;

        return clr is { Length: > 0 } ? clr : null;
    }

    /// <summary>Значение атрибута в двойных кавычках.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
}
