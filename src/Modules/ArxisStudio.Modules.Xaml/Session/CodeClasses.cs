namespace ArxisStudio.Modules.Xaml.Session;

/// <summary>Класс верхнего уровня, объявленный файлом кода, — как его прочёл текст, без компилятора.</summary>
/// <param name="Namespace">Пространство; пусто — глобальное.</param>
/// <param name="Name">Имя.</param>
/// <param name="IsStatic">Статический: экземпляра у него нет.</param>
/// <param name="IsAbstract">Абстрактный: экземпляр создаёт только наследник.</param>
/// <param name="IsGeneric">Обобщённый: имени для разметки без аргументов у него нет.</param>
internal sealed record DeclaredClass(string Namespace, string Name, bool IsStatic, bool IsAbstract, bool IsGeneric)
{
    /// <summary>Полное имя: <c>App.ViewModels.MainViewModel</c>.</summary>
    public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
}

/// <summary>
/// Открытые классы и записи верхнего уровня, которые объявляет текст C#, — тем, кто принимает файл модели из окна
/// проекта.
/// </summary>
/// <remarks>
/// <para>
/// Компилятора в студии нет, и для этого вопроса он не нужен: что файл объявляет, видно из лексем. Строки, символы,
/// комментарии и директивы препроцессора пропускаются — скобка в строке не открывает тела, — а классы внутри тел типов
/// и методов не в счёт: вложенный класс разметка не назовёт. Код обеих ветвей <c>#if</c> читается: объявление в
/// любой из них — объявление файла.
/// </para>
/// <para>
/// Пространство — блочное и файловое, вложенные складываются. Только открытые (<c>public</c>): закрытый класс
/// каталог типов не перечисляет, и разметке его не предложить.
/// </para>
/// </remarks>
internal static class CodeClasses
{
    private static readonly HashSet<string> Modifiers =
        ["public", "internal", "private", "protected", "static", "abstract", "sealed", "partial", "file", "unsafe", "new", "readonly", "ref"];

    /// <summary>Читает классы из текста файла.</summary>
    /// <param name="text">Текст C#.</param>
    /// <returns>Открытые классы и записи верхнего уровня в порядке текста, каждый один раз.</returns>
    public static IReadOnlyList<DeclaredClass> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var found = new List<DeclaredClass>();
        var reader = new Reader(Tokens(text).ToList());

        reader.Run(found);

        return found.DistinctBy(static declared => declared.FullName, StringComparer.Ordinal).ToList();
    }

    /// <summary>Лексемы текста: имена и знаки препинания, без строк, символов, комментариев и директив.</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var at = 0;
        var lineStart = true;

        while (at < text.Length)
        {
            var c = text[at];

            if (c == '\n')
            {
                lineStart = true;
                at++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                at++;
                continue;
            }

            // Директива — до конца строки: #if, #region, #nullable.
            if (c == '#' && lineStart)
            {
                at = EndOfLine(text, at);
                continue;
            }

            lineStart = false;

            if (c == '/' && At(text, at + 1) == '/')
            {
                at = EndOfLine(text, at);
                continue;
            }

            if (c == '/' && At(text, at + 1) == '*')
            {
                var end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (StringAt(text, at) is { } afterString)
            {
                at = afterString;
                continue;
            }

            if (c == '\'')
            {
                at = SkipChar(text, at);
                continue;
            }

            if (char.IsLetter(c) || c == '_' || (c == '@' && (char.IsLetter(At(text, at + 1)) || At(text, at + 1) == '_')))
            {
                var start = c == '@' ? at + 1 : at;
                var end = start;

                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    end++;

                yield return text[start..end];
                at = end;
                continue;
            }

            if ("{};<>()[]=.,:".Contains(c, StringComparison.Ordinal))
                yield return c.ToString();

            at++;
        }
    }

    /// <summary>Конец строки (её перевод не съеден).</summary>
    private static int EndOfLine(string text, int at)
    {
        var end = text.IndexOf('\n', at);

        return end < 0 ? text.Length : end;
    }

    private static char At(string text, int at) => at < text.Length ? text[at] : '\0';

    /// <summary>Если на месте начинается строка — где она кончается; иначе null.</summary>
    /// <remarks>
    /// Обычная, дословная (<c>@"…"</c>), сырая (<c>"""…"""</c>) и интерполированная (<c>$"…{x}…"</c>): в дырах
    /// интерполяции бывают свои строки и скобки, и они пропускаются вместе с ней.
    /// </remarks>
    private static int? StringAt(string text, int at)
    {
        var start = at;
        var interpolated = false;
        var verbatim = false;

        while (At(text, at) is '$' or '@')
        {
            interpolated |= text[at] == '$';
            verbatim |= text[at] == '@';
            at++;
        }

        if (At(text, at) != '"' || (at > start && !interpolated && !verbatim))
            return null;

        // Сырая строка: три кавычки и больше, кончается тем же числом.
        var quotes = 0;

        while (At(text, at + quotes) == '"')
            quotes++;

        if (quotes >= 3)
        {
            var close = new string('"', quotes);
            var end = text.IndexOf(close, at + quotes, StringComparison.Ordinal);

            return end < 0 ? text.Length : end + quotes;
        }

        at++;

        while (at < text.Length)
        {
            var c = text[at];

            if (verbatim && c == '"' && At(text, at + 1) == '"')
            {
                at += 2;
                continue;
            }

            if (c == '"')
                return at + 1;

            if (!verbatim && c == '\\')
            {
                at += 2;
                continue;
            }

            if (!verbatim && c == '\n')
                return at;

            if (interpolated && c == '{')
            {
                if (At(text, at + 1) == '{')
                {
                    at += 2;
                    continue;
                }

                at = SkipHole(text, at + 1);
                continue;
            }

            at++;
        }

        return text.Length;
    }

    /// <summary>Дыра интерполяции: до своей закрывающей скобки, со своими строками внутри.</summary>
    private static int SkipHole(string text, int at)
    {
        var depth = 0;

        while (at < text.Length)
        {
            if (StringAt(text, at) is { } afterString)
            {
                at = afterString;
                continue;
            }

            switch (text[at])
            {
                case '\'':
                    at = SkipChar(text, at);
                    continue;
                case '{':
                    depth++;
                    break;
                case '}' when depth == 0:
                    return at + 1;
                case '}':
                    depth--;
                    break;
            }

            at++;
        }

        return text.Length;
    }

    /// <summary>Символ: <c>'x'</c>, <c>'\''</c>, <c>'A'</c>.</summary>
    private static int SkipChar(string text, int at)
    {
        at++;

        while (at < text.Length && text[at] != '\'' && text[at] != '\n')
            at += text[at] == '\\' ? 2 : 1;

        return Math.Min(at + 1, text.Length);
    }

    /// <summary>Что открыла фигурная скобка: пространство, тело типа или всё остальное.</summary>
    private enum Scope
    {
        Namespace,
        Type,
        Other,
    }

    /// <summary>Проход по лексемам: какие тела открыты, в каком пространстве и что объявлено.</summary>
    private sealed class Reader(List<string> tokens)
    {
        private readonly Stack<(Scope Kind, string Name)> _scopes = new();
        private readonly HashSet<string> _modifiers = new(StringComparer.Ordinal);
        private string _fileNamespace = string.Empty;
        private int _at;

        public void Run(List<DeclaredClass> found)
        {
            while (_at < tokens.Count)
            {
                var token = tokens[_at];

                switch (token)
                {
                    case "[" when TopLevel:
                        SkipBrackets();
                        _modifiers.Clear();
                        continue;
                    case "{":
                        _scopes.Push((Scope.Other, string.Empty));
                        _modifiers.Clear();
                        break;
                    case "}":
                        if (_scopes.Count > 0)
                            _scopes.Pop();

                        _modifiers.Clear();
                        break;
                    case ";":
                        _modifiers.Clear();
                        break;
                    case "namespace" when TopLevel:
                        ReadNamespace();
                        continue;
                    case "class" or "record" or "struct" or "interface" or "enum" when TopLevel:
                        ReadType(token, found);
                        continue;
                    case "delegate" when TopLevel:
                        SkipTo(";");
                        _modifiers.Clear();
                        continue;
                    default:
                        if (TopLevel && Modifiers.Contains(token))
                            _modifiers.Add(token);

                        break;
                }

                _at++;
            }
        }

        /// <summary>Открыты только пространства: объявленное здесь — верхнего уровня.</summary>
        private bool TopLevel => _scopes.All(static scope => scope.Kind == Scope.Namespace);

        private string Namespace
        {
            get
            {
                var blocks = _scopes.Reverse().Select(static scope => scope.Name).Where(static name => name.Length > 0);

                return string.Join('.', new[] { _fileNamespace }.Concat(blocks).Where(static name => name.Length > 0));
            }
        }

        /// <summary><c>namespace A.B;</c> — файловое, <c>namespace A.B {</c> — блочное.</summary>
        private void ReadNamespace()
        {
            _at++;

            var name = new List<string>();

            while (_at < tokens.Count && tokens[_at] is not ("{" or ";"))
            {
                if (tokens[_at] != ".")
                    name.Add(tokens[_at]);

                _at++;
            }

            var joined = string.Join('.', name);

            if (_at < tokens.Count && tokens[_at] == "{")
                _scopes.Push((Scope.Namespace, joined));
            else
                _fileNamespace = string.Join('.', new[] { _fileNamespace, joined }.Where(static part => part.Length > 0));

            _at++;
            _modifiers.Clear();
        }

        /// <summary>
        /// Объявление типа: имя, обобщённость — и тело, в котором всё уже вложенное, или <c>;</c> записи без тела.
        /// </summary>
        private void ReadType(string keyword, List<DeclaredClass> found)
        {
            _at++;

            // record struct — структура, record class — класс.
            if (keyword == "record" && _at < tokens.Count && tokens[_at] is "struct" or "class")
            {
                keyword = tokens[_at] == "struct" ? "struct" : "record";
                _at++;
            }

            var name = _at < tokens.Count ? tokens[_at] : string.Empty;

            _at++;

            var generic = _at < tokens.Count && tokens[_at] == "<";
            var isPublic = _modifiers.Contains("public");
            var isStatic = _modifiers.Contains("static");
            var isAbstract = _modifiers.Contains("abstract");
            var ns = Namespace;

            _modifiers.Clear();

            // До тела или до «;»: параметры записи, базы и ограничения обобщённых — со своими class и struct.
            var parentheses = 0;

            while (_at < tokens.Count)
            {
                var token = tokens[_at];

                if (token == "(")
                    parentheses++;
                else if (token == ")")
                    parentheses--;
                else if (parentheses == 0 && token is "{" or ";")
                    break;

                _at++;
            }

            if (keyword is "class" or "record" && isPublic && name.Length > 0)
                found.Add(new DeclaredClass(ns, name, isStatic, isAbstract, generic));

            if (_at < tokens.Count && tokens[_at] == "{")
            {
                _scopes.Push((Scope.Type, string.Empty));
                _at++;
            }
            else
            {
                _at++;
            }
        }

        private void SkipBrackets()
        {
            var depth = 0;

            while (_at < tokens.Count)
            {
                if (tokens[_at] == "[")
                    depth++;
                else if (tokens[_at] == "]" && --depth == 0)
                {
                    _at++;
                    return;
                }

                _at++;
            }
        }

        private void SkipTo(string token)
        {
            while (_at < tokens.Count && tokens[_at] != token)
                _at++;

            _at++;
        }
    }
}
