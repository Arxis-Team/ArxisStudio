using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Строковые поля манифеста — вместе с дорогой к каждому.
/// </summary>
/// <remarks>
/// JSON-разборщика у анализатора нет: он живёт в netstandard2.0 и своих
/// зависимостей в плагин не тащит. Регулярного выражения здесь мало: одно и то
/// же поле <c>icon</c> в голове манифеста — путь к картинке карточки, а у
/// команды — значок, и различить их можно только по тому, где поле стоит.
/// Поэтому разбор структурный, но простой — скобки, строки, запятые и
/// комментарии. Студия читает манифест с комментариями и висячими запятыми, и
/// проверка не вправе быть строже чтения.
/// <para>
/// Испорченный манифест не роняет разбор: лишняя скобка закрывает меньше, чем
/// просили, недописанная строка кончается вместе с файлом. Что не разобрала
/// проверка, того не прочтёт и студия — и скажет об этом сама.
/// </para>
/// </remarks>
internal static class ManifestJson
{
    /// <summary>Строковое значение, дорога к нему и место в тексте.</summary>
    internal readonly struct Field(string path, string value, TextSpan span)
    {
        /// <summary>
        /// Дорога: имена через точку, номер в массиве — в скобках, например
        /// <c>contributions.commands[0].icon</c>.
        /// </summary>
        public string Path { get; } = path;

        /// <summary>Значение с разобранными экранированиями.</summary>
        public string Value { get; } = value;

        /// <summary>Где значение записано — между кавычками.</summary>
        public TextSpan Span { get; } = span;
    }

    /// <summary>Все строковые значения манифеста по порядку записи.</summary>
    /// <param name="source">Текст манифеста.</param>
    public static List<Field> Strings(string source)
    {
        var fields = new List<Field>();
        var frames = new List<Frame>();
        var at = 0;

        while (at < source.Length)
        {
            var symbol = source[at];

            if (char.IsWhiteSpace(symbol))
            {
                at++;
                continue;
            }

            if (symbol == '/' && at + 1 < source.Length && (source[at + 1] == '/' || source[at + 1] == '*'))
            {
                at = SkipComment(source, at);
                continue;
            }

            switch (symbol)
            {
                case '{':
                case '[':
                    Take(frames);
                    frames.Add(new Frame(symbol == '{'));
                    at++;
                    break;

                case '}':
                case ']':
                    if (frames.Count > 0)
                    {
                        frames.RemoveAt(frames.Count - 1);
                    }

                    Taken(frames);
                    at++;
                    break;

                case ',':
                    if (Top(frames) is { IsObject: true } separated)
                    {
                        separated.Key = null;
                        separated.Awaiting = false;
                    }

                    at++;
                    break;

                case ':':
                    if (Top(frames) is { IsObject: true } keyed)
                    {
                        keyed.Awaiting = true;
                    }

                    at++;
                    break;

                case '"':
                    var start = at + 1;
                    var value = ReadString(source, ref at);

                    // Строка в объекте до двоеточия — имя поля, после — значение.
                    if (Top(frames) is { IsObject: true, Awaiting: false } named)
                    {
                        named.Key = value;
                        break;
                    }

                    Take(frames);
                    fields.Add(new Field(Path(frames), value, new TextSpan(start, at - 1 - start)));
                    Taken(frames);
                    break;

                default:
                    // Число, true, false, null — или мусор: значение без кавычек
                    // идёт до разделителя.
                    Take(frames);

                    var began = at;

                    while (at < source.Length && !IsDelimiter(source[at]))
                    {
                        at++;
                    }

                    if (at == began)
                    {
                        at++;
                    }

                    Taken(frames);
                    break;
            }
        }

        return fields;
    }

    /// <summary>Уровень вложенности: объект или массив.</summary>
    private sealed class Frame(bool isObject)
    {
        public bool IsObject { get; } = isObject;

        /// <summary>Имя поля, значение которого сейчас читается; у массива — никогда.</summary>
        public string? Key { get; set; }

        /// <summary>Двоеточие прочитано: следующая строка — значение, а не имя.</summary>
        public bool Awaiting { get; set; }

        /// <summary>Номер текущего элемента массива; до первого — -1.</summary>
        public int Index { get; set; } = -1;
    }

    private static Frame? Top(List<Frame> frames) => frames.Count > 0 ? frames[frames.Count - 1] : null;

    /// <summary>Начинается значение: в массиве это следующий элемент.</summary>
    private static void Take(List<Frame> frames)
    {
        if (Top(frames) is { IsObject: false } array)
        {
            array.Index++;
        }
    }

    /// <summary>Значение прочитано: объект ждёт следующего имени.</summary>
    private static void Taken(List<Frame> frames)
    {
        if (Top(frames) is { IsObject: true } owner)
        {
            owner.Awaiting = false;
        }
    }

    private static string Path(List<Frame> frames)
    {
        var path = new StringBuilder();

        foreach (var frame in frames)
        {
            if (!frame.IsObject)
            {
                path.Append('[').Append(frame.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else if (frame.Key is not null)
            {
                if (path.Length > 0)
                {
                    path.Append('.');
                }

                path.Append(frame.Key);
            }
        }

        return path.ToString();
    }

    /// <summary>Читает строку от открывающей кавычки; <paramref name="at"/> встаёт за закрывающую.</summary>
    private static string ReadString(string source, ref int at)
    {
        var value = new StringBuilder();

        at++;

        while (at < source.Length)
        {
            var symbol = source[at];

            if (symbol == '"')
            {
                at++;
                return value.ToString();
            }

            if (symbol == '\\' && at + 1 < source.Length)
            {
                at++;
                value.Append(Unescape(source, ref at));
                continue;
            }

            value.Append(symbol);
            at++;
        }

        return value.ToString();
    }

    private static string Unescape(string source, ref int at)
    {
        var symbol = source[at];

        at++;

        switch (symbol)
        {
            case 'b': return "\b";
            case 'f': return "\f";
            case 'n': return "\n";
            case 'r': return "\r";
            case 't': return "\t";
            case 'u' when at + 4 <= source.Length &&
                          int.TryParse(source.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                at += 4;
                return ((char)code).ToString();
            default: return symbol.ToString();
        }
    }

    private static int SkipComment(string source, int at)
    {
        if (source[at + 1] == '/')
        {
            var line = source.IndexOf('\n', at);

            return line < 0 ? source.Length : line + 1;
        }

        var end = source.IndexOf("*/", at + 2, System.StringComparison.Ordinal);

        return end < 0 ? source.Length : end + 2;
    }

    private static bool IsDelimiter(char symbol) =>
        char.IsWhiteSpace(symbol) || symbol is ',' or ':' or '{' or '}' or '[' or ']' or '"' or '/';
}
