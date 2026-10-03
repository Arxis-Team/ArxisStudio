using System.Xml;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>Вид файла разметки по его корню — без сборки и без сессии.</summary>
/// <remarks>
/// Правило то же, что у доски форм: корни, которые формами не бывают, названы списком, окна и элементы
/// узнаются по окончанию имени, всё прочее — контрол. Читатель потоковый, DTD запрещён: файл приходит
/// из решения, то есть от человека, и сущность в нём не должна ни раздувать память, ни ходить на диск.
/// </remarks>
internal static class XamlFileKinds
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>Разметка ли это — по расширению: <c>.axaml</c> или <c>.xaml</c>.</summary>
    /// <param name="path">Файл.</param>
    public static bool IsMarkup(CanonicalPath path) =>
        path.Extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase)
        || path.Extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Вид по корню разобранного документа.</summary>
    /// <param name="document">Документ.</param>
    public static XamlFileKind Of(XamlDocument document) =>
        document.Root is { } root ? OfElement(root.Name.LocalName) : XamlFileKind.Unknown;

    /// <summary>Вид по корню файла на диске.</summary>
    /// <param name="path">Файл.</param>
    public static XamlFileKind Read(CanonicalPath path)
    {
        try
        {
            using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, Settings);

            return reader.MoveToContent() == XmlNodeType.Element ? OfElement(reader.LocalName) : XamlFileKind.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return XamlFileKind.Unknown;
        }
    }

    /// <summary>Вид по имени корня без приставки.</summary>
    /// <param name="element">Имя.</param>
    public static XamlFileKind OfElement(string element) => element switch
    {
        "Application" => XamlFileKind.Application,
        "Styles" or "Style" => XamlFileKind.Styles,
        "ResourceDictionary" => XamlFileKind.Resources,
        _ when element.EndsWith("Window", StringComparison.Ordinal) => XamlFileKind.Window,
        _ when element.EndsWith("UserControl", StringComparison.Ordinal) => XamlFileKind.UserControl,
        _ => XamlFileKind.Control,
    };
}
