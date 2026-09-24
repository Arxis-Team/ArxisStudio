using System.Globalization;
using ArxisStudio.Sdk;

namespace Arxis.CodeViewer;

/// <summary>
/// Редактор документов, который берётся за текстовые файлы решения и показывает
/// их на просмотр.
/// </summary>
/// <remarks>
/// Правку он не умеет и не притворяется: контракт <see cref="DocumentEditor"/>
/// проверяется здесь целиком — отбор по типу, чтение, отказ с причиной и живое
/// представление во вкладке.
/// <para>
/// Двоичный файл с знакомым расширением встречается чаще, чем кажется: так
/// выглядит <c>.md</c>, сохранённый в UTF-16, или обрезанный файл. Просмотрщик
/// говорит об этом словами, а не показывает мусор.
/// </para>
/// </remarks>
public sealed class CodeViewer : DocumentEditor
{
    /// <summary>Сколько байт просмотрщик берётся прочитать.</summary>
    /// <remarks>
    /// Не ради памяти, а ради потока интерфейса: документ открывается по щелчку,
    /// и читать мегабайты, пока студия ждёт, нельзя. Файл крупнее получает отказ
    /// с причиной — её студия показывает в строке состояния.
    /// </remarks>
    internal const long Limit = 512 * 1024;

    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".axaml", ".xaml", ".csproj", ".props", ".targets", ".slnx",
        ".xml", ".json", ".md", ".txt", ".yml", ".yaml",
    };

    /// <inheritdoc/>
    public override bool CanOpen(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && Kinds.Contains(Path.GetExtension(filePath));

    /// <inheritdoc/>
    public override async Task<(DocumentView? View, string? Error)> OpenAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var file = new FileInfo(filePath);

        if (file.Length > Limit)
            return (null, Say("viewer.toobig", Limit / 1024));

        string text;

        try
        {
            text = await File.ReadAllTextAsync(filePath).ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, Say("viewer.unreadable", e.Message));
        }

        // Нулевой байт в тексте — верный признак, что файл не текстовый: в UTF-8
        // его не бывает, а в UTF-16 без метки порядка байт он в каждом символе.
        if (text.Contains('\0', StringComparison.Ordinal))
            return (null, Say("viewer.binary"));

        return (new CodeDocument(filePath, text), null);
    }

    private string Say(string key, params object[] values)
    {
        var text = Context.Strings[key];

        return values.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, values);
    }
}
