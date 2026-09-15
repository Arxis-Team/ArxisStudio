namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Прочтёт ли студия словарь — и если нет, что и где помешает.
/// </summary>
/// <remarks>
/// Разбор строгий ровно настолько, насколько строга студия: словарь она читает
/// разборщиком .NET в <c>Dictionary&lt;string, string&gt;</c> с пропуском
/// комментариев и висячими запятыми, а всё, что он не принял, берёт пустым.
/// Значит, словарь — объект, в нём имена в кавычках, после имени двоеточие,
/// значение — строка или <c>null</c>, между строками запятые, после объекта
/// ничего. Повтор ключа не порча: разборщик оставляет последнее вхождение.
/// <para>
/// Терпимый <see cref="ManifestJson"/> здесь не годится нарочно: он создан
/// читать то, что студия прочтёт, и потому пропускает порчу мимо. Этот — чтобы
/// назвать то, чего она не прочтёт. Разойтись с разборщиком студии им не дают
/// тесты, сверяющие оба на одних и тех же файлах.
/// </para>
/// </remarks>
internal static class StringsJson
{
    /// <summary>Что помешает студии прочесть словарь, и где.</summary>
    internal readonly struct Problem(string reason, int at)
    {
        /// <summary>Что не так — словами.</summary>
        public string Reason { get; } = reason;

        /// <summary>Место в тексте, где разбор споткнулся.</summary>
        public int At { get; } = at;
    }

    /// <summary>Порча словаря; null — студия его прочтёт.</summary>
    /// <param name="source">Текст словаря.</param>
    public static Problem? Check(string source) => new Reader(source).Dictionary();

    private sealed class Reader(string source)
    {
        private int _at;

        public Problem? Dictionary()
        {
            if (Skip() is { } comment)
            {
                return comment;
            }

            if (_at >= source.Length)
            {
                return new Problem("словаря в файле нет — он пуст", _at);
            }

            if (source[_at] != '{')
            {
                return new Problem("это не словарь: словарь начинается с {", _at);
            }

            _at++;

            if (Members() is { } member)
            {
                return member;
            }

            if (Skip() is { } tail)
            {
                return tail;
            }

            return _at < source.Length
                ? new Problem("после словаря лишний текст", _at)
                : null;
        }

        /// <summary>Строки словаря — до закрывающей скобки включительно.</summary>
        private Problem? Members()
        {
            // Открывающая скобка или запятая: дальше ждут имя, а закрыть можно и
            // здесь — пустой словарь или висячая запятая.
            var awaitingName = true;
            var afterComma = false;

            while (true)
            {
                if (Skip() is { } comment)
                {
                    return comment;
                }

                if (_at >= source.Length)
                {
                    return new Problem("словарь не закрыт — нет }", _at);
                }

                var symbol = source[_at];

                if (symbol == '}')
                {
                    _at++;
                    return null;
                }

                if (!awaitingName)
                {
                    if (symbol != ',')
                    {
                        return new Problem("между строками нет запятой", _at);
                    }

                    _at++;
                    awaitingName = true;
                    afterComma = true;
                    continue;
                }

                if (symbol == ',')
                {
                    return new Problem(afterComma ? "запятая лишняя — две подряд" : "запятая лишняя — перед ней нет строки", _at);
                }

                if (symbol != '"')
                {
                    return new Problem("имя строки без кавычек", _at);
                }

                if (Text() is { } name)
                {
                    return name;
                }

                if (Skip() is { } beforeColon)
                {
                    return beforeColon;
                }

                if (_at >= source.Length || source[_at] != ':')
                {
                    return new Problem("после имени строки нет двоеточия", _at);
                }

                _at++;

                if (Value() is { } value)
                {
                    return value;
                }

                awaitingName = false;
                afterComma = false;
            }
        }

        private Problem? Value()
        {
            if (Skip() is { } comment)
            {
                return comment;
            }

            if (_at >= source.Length)
            {
                return new Problem("у строки нет значения", _at);
            }

            var symbol = source[_at];

            if (symbol == '"')
            {
                return Text();
            }

            if (string.CompareOrdinal(source, _at, "null", 0, 4) == 0 && (_at + 4 >= source.Length || IsDelimiter(source[_at + 4])))
            {
                _at += 4;
                return null;
            }

            return symbol is '{' or '[' or '-' or 't' or 'f' or 'n' || char.IsDigit(symbol)
                ? new Problem("значение строки — не текст: словарь хранит только строки", _at)
                : new Problem("значение строки не разобралось", _at);
        }

        /// <summary>Строка в кавычках; разбор встаёт за закрывающую кавычку.</summary>
        private Problem? Text()
        {
            var start = _at;

            _at++;

            while (_at < source.Length)
            {
                var symbol = source[_at];

                if (symbol == '"')
                {
                    _at++;
                    return null;
                }

                if (symbol < ' ')
                {
                    return new Problem("в строке перенос или управляющий знак без экранирования", _at);
                }

                if (symbol != '\\')
                {
                    _at++;
                    continue;
                }

                if (_at + 1 >= source.Length)
                {
                    break;
                }

                var escaped = source[_at + 1];

                if (escaped is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')
                {
                    _at += 2;
                    continue;
                }

                if (escaped == 'u' && _at + 6 <= source.Length && IsHex(source, _at + 2, 4))
                {
                    // Половина суррогатной пары сама по себе — не знак: разборщик
                    // студии такую строку отвергает, а с ней и весь словарь.
                    var code = Convert.ToInt32(source.Substring(_at + 2, 4), 16);

                    if (code is >= 0xDC00 and <= 0xDFFF)
                    {
                        return new Problem("в строке младшая половина суррогатной пары без старшей", _at);
                    }

                    if (code is >= 0xD800 and <= 0xDBFF)
                    {
                        if (!IsLowSurrogate(_at + 6))
                        {
                            return new Problem("в строке старшая половина суррогатной пары без младшей", _at);
                        }

                        _at += 12;
                        continue;
                    }

                    _at += 6;
                    continue;
                }

                return new Problem("экранирование в строке не разобралось", _at);
            }

            return new Problem("строка не закрыта", start);
        }

        /// <summary>Пробелы и комментарии; ошибка — только незакрытый или ненастоящий комментарий.</summary>
        private Problem? Skip()
        {
            while (_at < source.Length)
            {
                var symbol = source[_at];

                if (symbol is ' ' or '\t' or '\n' or '\r')
                {
                    _at++;
                    continue;
                }

                if (symbol != '/')
                {
                    return null;
                }

                if (_at + 1 < source.Length && source[_at + 1] == '/')
                {
                    var line = source.IndexOf('\n', _at);

                    _at = line < 0 ? source.Length : line + 1;
                    continue;
                }

                if (_at + 1 < source.Length && source[_at + 1] == '*')
                {
                    var end = source.IndexOf("*/", _at + 2, System.StringComparison.Ordinal);

                    if (end < 0)
                    {
                        return new Problem("комментарий не закрыт — нет */", _at);
                    }

                    _at = end + 2;
                    continue;
                }

                return new Problem("одиночная косая — это не комментарий", _at);
            }

            return null;
        }

        /// <summary>Стоит ли с этого места экранированная младшая половина суррогатной пары.</summary>
        private bool IsLowSurrogate(int at) =>
            at + 6 <= source.Length &&
            source[at] == '\\' &&
            source[at + 1] == 'u' &&
            IsHex(source, at + 2, 4) &&
            Convert.ToInt32(source.Substring(at + 2, 4), 16) is >= 0xDC00 and <= 0xDFFF;

        private static bool IsDelimiter(char symbol) =>
            symbol is ' ' or '\t' or '\n' or '\r' or ',' or '}' or ']' or '/';

        private static bool IsHex(string text, int start, int length)
        {
            for (var at = start; at < start + length; at++)
            {
                if (!Uri.IsHexDigit(text[at]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
