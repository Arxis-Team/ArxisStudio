using System.Security.Cryptography;

namespace ArxisStudio.LocalHistory;

/// <summary>
/// Адрес содержимого в истории: SHA-256 его байт, шестьдесят четыре строчных шестнадцатеричных
/// знака.
/// </summary>
/// <remarks>
/// Адрес выводится из самих байт, поэтому одно и то же содержимое — снятое с разных файлов, в разные
/// дни — лежит в хранилище одним объектом. Хэш криптографический не ради защиты: у короткого хэша на
/// миллионе файлов совпадения уже не редкость, а совпавший адрес вернул бы человеку чужой файл.
/// </remarks>
public readonly record struct ContentId
{
    private const int Length = 64;

    private ContentId(string value) => Value = value;

    /// <summary>Шестьдесят четыре строчных шестнадцатеричных знака; у значения по умолчанию — null.</summary>
    public string Value { get; }

    /// <summary>Адрес этих байт.</summary>
    /// <param name="bytes">Содержимое.</param>
    public static ContentId Of(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));

    /// <summary>Разбирает записанный адрес.</summary>
    /// <param name="text">Строка из журнала или имени объекта.</param>
    /// <param name="id">Адрес, если строка им была.</param>
    /// <returns><c>false</c> — строка не адрес.</returns>
    public static bool TryParse(string? text, out ContentId id)
    {
        if (text is { Length: Length } && text.All(char.IsAsciiHexDigitLower))
        {
            id = new ContentId(text);
            return true;
        }

        id = default;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => Value ?? string.Empty;
}
