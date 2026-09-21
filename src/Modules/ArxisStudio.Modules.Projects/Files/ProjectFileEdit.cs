using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArxisStudio.Modules.Projects.Files;

/// <summary>
/// Файл проекта, открытый на правку: текст, в котором его прочли, и байты, которыми его вернуть.
/// </summary>
/// <remarks>
/// <para>
/// Тем же порядком, что у правки пакетов в библиотеке модели: документ читается с сохранением
/// пробелов, пишется без форматирования и в той же кодировке — с отметкой порядка байт, если она
/// была, и без неё, если не было. Объявление XML возвращается дословно. Иначе правка одной ссылки
/// переписывала бы весь файл, и человек не нашёл бы свою строку в разнице.
/// </para>
/// <para>
/// <b>Переводы строк читаются как есть.</b> Читатель, которого создаёт <see cref="XDocument.Parse(string, LoadOptions)"/>,
/// приводит их к LF — так велит спецификация XML, — и файл с CRLF после правки одной ссылки
/// менялся бы в каждой строке. Старый <see cref="XmlTextReader"/> приведение умеет выключить, и
/// файл возвращается таким, каким был: с CRLF, с LF или вперемешку.
/// </para>
/// <para>
/// Откат пишет исходные байты, а не текст: вернуть надо ровно то, что лежало, и ничего не
/// спрашивая у диска.
/// </para>
/// </remarks>
internal sealed class ProjectFileEdit
{
    private readonly byte[] _original;
    private readonly Encoding _encoding;

    private ProjectFileEdit(string path, byte[] original, Encoding encoding, XDocument document)
    {
        Path = path;
        _original = original;
        _encoding = encoding;
        Document = document;
    }

    /// <summary>Файл проекта.</summary>
    public string Path { get; }

    /// <summary>Документ на правку.</summary>
    public XDocument Document { get; }

    /// <summary>Открывает файл; null — его нет или это не XML.</summary>
    /// <param name="path">Полный путь.</param>
    public static ProjectFileEdit? Open(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);

            using var reader = new StreamReader(
                new MemoryStream(bytes, writable: false),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true);

            var text = reader.ReadToEnd();

            return new ProjectFileEdit(path, bytes, reader.CurrentEncoding, Parse(text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>Разбирает текст, не трогая ни пробелов, ни переводов строк.</summary>
    private static XDocument Parse(string text)
    {
        using var reader = new XmlTextReader(new StringReader(text))
        {
            Normalization = false,
            WhitespaceHandling = WhitespaceHandling.All,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>Пишет документ в файл.</summary>
    public void Save()
    {
        var body = new StringWriter();

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CheckCharacters = false,
        };

        using (var writer = XmlWriter.Create(body, settings))
            Document.Save(writer);

        var text = Document.Declaration is { } declaration ? declaration + body.ToString() : body.ToString();

        File.WriteAllText(Path, text, _encoding);
    }

    /// <summary>Возвращает файлу исходные байты.</summary>
    public void Restore() => File.WriteAllBytes(Path, _original);
}
