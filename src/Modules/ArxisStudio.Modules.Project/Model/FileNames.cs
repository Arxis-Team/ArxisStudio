namespace ArxisStudio.Modules.Project.Model;

/// <summary>Что не так с именем.</summary>
internal enum NameProblem
{
    /// <summary>Имя годится.</summary>
    None,

    /// <summary>Имя то же, что было: переименовывать нечего.</summary>
    Unchanged,

    /// <summary>Имени нет.</summary>
    Empty,

    /// <summary>В имени знак, которого в имени файла быть не может.</summary>
    Invalid,

    /// <summary>Имя кончается точкой или пробелом — Windows их молча отрезает.</summary>
    Trailing,

    /// <summary>Имя устройства Windows: <c>CON</c>, <c>NUL</c>, <c>COM1</c> и соседи.</summary>
    Reserved,

    /// <summary>Такое имя в каталоге уже есть.</summary>
    Taken,

    /// <summary>Имя не годится именем типа C#: шаблон ставит его в код.</summary>
    NotIdentifier,

    /// <summary>Имя — ключевое слово C#: типом оно быть не может.</summary>
    Keyword,
}

/// <summary>Ответ проверки имени.</summary>
/// <param name="Problem">Что не так.</param>
/// <param name="Subject">О чём сказать: знак, зарезервированное слово, занятое имя.</param>
internal readonly record struct NameCheck(NameProblem Problem, string? Subject = null)
{
    /// <summary>Имя годится.</summary>
    public bool IsFine => Problem == NameProblem.None;
}

/// <summary>
/// Правила имени файла и каталога — общие у переименования и создания.
/// </summary>
/// <remarks>
/// <para>
/// Правила строже одной системы: знак, запрещённый хоть где-то, запрещён везде, и имя устройства
/// Windows запрещено и на Linux — решение, в котором лежит <c>con.cs</c>, не откроется у соседа с
/// Windows. Точка и пробел в конце — туда же: Windows их молча отрезает, и файл лёг бы под другим
/// именем.
/// </para>
/// <para>
/// Имя типа C# проверяется там, где шаблон ставит имя в код: <c>class $name$</c> с дефисом или
/// ключевым словом не соберётся. Правило то же, что у компилятора, без экранирования <c>@</c>: тип,
/// которого без собаки не назвать, человеку не нужен.
/// </para>
/// </remarks>
internal static class FileNames
{
    private static readonly char[] Forbidden = [.. Path.GetInvalidFileNameChars().Union(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])];

    private static readonly HashSet<string> Devices = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Ключевые слова C#, которые именем типа быть не могут.</summary>
    private static readonly HashSet<string> Keywords = new(
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ], StringComparer.Ordinal);

    /// <summary>Проверяет одно имя — без каталогов.</summary>
    /// <param name="name">Имя.</param>
    public static NameCheck Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new NameCheck(NameProblem.Empty);

        if (name.IndexOfAny(Forbidden) is var at and >= 0)
            return new NameCheck(NameProblem.Invalid, name[at].ToString());

        if (name is "." or "..")
            return new NameCheck(NameProblem.Invalid, name);

        if (name[^1] is '.' or ' ')
            return new NameCheck(NameProblem.Trailing);

        if (name.Split('.')[0].TrimEnd() is var device && Devices.Contains(device))
            return new NameCheck(NameProblem.Reserved, device);

        return new NameCheck(NameProblem.None);
    }

    /// <summary>Проверяет имя типа C#.</summary>
    /// <param name="name">Имя, уже годное именем файла.</param>
    public static NameCheck Identifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Keywords.Contains(name))
            return new NameCheck(NameProblem.Keyword, name);

        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') || !name.All(symbol => char.IsLetterOrDigit(symbol) || symbol == '_'))
            return new NameCheck(NameProblem.NotIdentifier, name);

        return new NameCheck(NameProblem.None);
    }
}
