using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Разметка расширения, какой её подаёт сборка: файл <c>.axaml</c>, разобранный с номерами строк.
/// </summary>
/// <remarks>
/// Три правила разметки — о виджетах, о значениях темы и об имени кнопки — разбирали файл каждое
/// своей копией, и два из них одинаково считали место имени в строке.
/// </remarks>
internal static class MarkupFiles
{
    private const string Extension = ".axaml";

    /// <summary>Разметка ли это — по расширению файла.</summary>
    /// <param name="path">Путь входа сборки.</param>
    public static bool IsMarkup(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Текст и разобранный документ; null — не разметка, текста нет или он не разбирается.
    /// </summary>
    /// <remarks>
    /// Недописанную разметку правило пропускает: её назовёт компилятор разметки, и лучше, чем
    /// повторённая двумя словами ошибка разбора.
    /// </remarks>
    /// <param name="file">Вход сборки.</param>
    /// <param name="cancellation">Отмена анализа.</param>
    public static (SourceText Text, XDocument Document)? Read(AdditionalText file, CancellationToken cancellation)
    {
        if (!IsMarkup(file.Path) || file.GetText(cancellation) is not { } text)
        {
            return null;
        }

        try
        {
            return (text, XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Место имени, с которого начинается элемент или атрибут; <see cref="Location.None"/> — номера строки нет.
    /// </summary>
    /// <param name="path">Путь файла.</param>
    /// <param name="text">Его текст.</param>
    /// <param name="info">Номер строки и позиция элемента или атрибута.</param>
    /// <param name="length">Сколько знаков занимает имя.</param>
    public static Location At(string path, SourceText text, IXmlLineInfo info, int length)
    {
        if (!info.HasLineInfo() || info.LineNumber - 1 >= text.Lines.Count)
        {
            return Location.None;
        }

        var line = info.LineNumber - 1;
        var column = info.LinePosition - 1;
        var start = text.Lines[line].Start + column;

        return Location.Create(
            path,
            new TextSpan(start, length),
            new LinePositionSpan(new LinePosition(line, column), new LinePosition(line, column + length)));
    }
}
