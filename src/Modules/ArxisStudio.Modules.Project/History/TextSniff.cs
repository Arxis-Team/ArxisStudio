using System.Text;

namespace ArxisStudio.Modules.Project.History;

/// <summary>Байты файла — текстом, если это текст.</summary>
/// <remarks>
/// Двоичным считается файл с нулевым байтом в начале — как у git. Отметка порядка байт называет
/// кодировку сама; без неё текст читается как UTF-8, а не прочитавшийся — побайтно: разница строк
/// всё равно покажет, какие строки поменялись, пусть и с чужими буквами.
/// </remarks>
internal static class TextSniff
{
    /// <summary>Сколько первых байт смотреть в поисках нулевого.</summary>
    private const int Probe = 8000;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Текст файла; null — файл двоичный.</summary>
    /// <param name="bytes">Байты.</param>
    public static string? Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes is [0xFF, 0xFE, ..])
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes is [0xFE, 0xFF, ..])
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.AsSpan(0, Math.Min(bytes.Length, Probe)).Contains((byte)0))
            return null;

        try
        {
            return Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
