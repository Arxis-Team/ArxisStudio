using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Запрет: длина или толщина в разметке и в коде студии, написанная числом.
/// </summary>
/// <remarks>
/// Отступы держал храповик, скругления — запрет, а размеры не держал никто, и числа расползлись по
/// экранам: ширина колонки в одном окне, высота строки в другом, рамка в пиксель — в сорока местах.
/// Каждое из них — второе место, где живёт решение, и расходится оно с первым молча.
/// <para>
/// Правило то же, что у цвета: значение объявляется в теме и больше нигде. Два случая правилом же и
/// названы. Размер самого окна — в открывающем теге: это не вёрстка, а место, с которого окно
/// открывается, и ключа темы у него быть не может. Картинка релиза заставки — рисунок, и её числа
/// координаты внутри него.
/// </para>
/// <para>
/// Модули и плагины сюда не входят: они расширения, у них своё правило и свой анализатор, а ширина
/// их канвы — их дело.
/// </para>
/// </remarks>
public class LiteralSizeTests
{
    /// <summary>
    /// Сколько длин в разметке студии написано числом.
    /// </summary>
    /// <remarks>
    /// Ноль с самого начала: храповик заведён вехой, которая их и убрала. Не поднимается ничем —
    /// новой длине место в Metrics.axaml темы, и там у неё будет имя.
    /// </remarks>
    private const int Ceiling = 0;

    /// <summary>Свойства, которыми задают длину и толщину.</summary>
    private const string Properties =
        "Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|BorderThickness|StrokeThickness|LetterSpacing";

    /// <summary>Объявления длины: атрибутом и сеттером.</summary>
    private static readonly Regex Sizes = new(
        $"""(?:\b(?:{Properties})="([^"]*)")|(?:<(?:\w+:)?Setter\s+Property="(?:{Properties})"\s+Value="([^"]*)")""",
        RegexOptions.Compiled);

    /// <summary>Место, где длина вообще упомянута: им меряется полнота счётчика.</summary>
    private static readonly Regex Declarations = new(
        $"""(?:\b(?:{Properties})=")|(?:\bProperty="(?:{Properties})")""",
        RegexOptions.Compiled);

    /// <summary>Сколько длин в коде студии написано числом.</summary>
    /// <remarks>
    /// Ноль, как и в разметке. Опускается коммитом, который число убрал, и не поднимается ничем.
    /// </remarks>
    private const int CodeCeiling = 0;

    /// <summary>Длина в коде: число, присвоенное свойству длины, — кроме объявления константы.</summary>
    private static readonly Regex CodeSizes = new(
        $"""(?<!\bconst\s+[\w.<>?]+\s+)\b(?:{Properties})\s*=\s*(-?[\d.]+)""",
        RegexOptions.Compiled);

    /// <summary>Длин числом ровно столько, сколько говорит потолок.</summary>
    [Fact]
    public void Literal_sizes_are_exactly_as_many_as_the_ceiling_says()
    {
        var sources = MarkupSources.Own().Where(source => !MarkupSources.IsSplashArt(source.Name)).ToList();

        Assert.Contains(sources, source => source.Name.Equals("MainWindow.axaml", StringComparison.Ordinal));

        var counted = sources
            .Select(source => (source.Name, Count: Literals(source.Text)))
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.Count)
            .ToList();

        var total = counted.Sum(row => row.Count);
        var worst = string.Join(", ", counted.Take(3).Select(row => $"{row.Name} — {row.Count}"));

        Assert.True(
            total == Ceiling,
            total > Ceiling
                ? $"длин числом стало {total} при потолке {Ceiling}: длину берут ключом темы — размеры " +
                  $"контролов и хрома в Metrics.axaml, ширины экранов там же. Больше всего в {worst}"
                : $"длин числом осталось {total} при потолке {Ceiling}: опустите Ceiling до {total} " +
                  "тем же коммитом, которым их убрали");
    }

    /// <summary>Счётчик видит каждое объявление длины.</summary>
    [Fact]
    public void The_counter_sees_every_declaration()
    {
        var sources = MarkupSources.Own().ToList();

        Assert.NotEmpty(sources);

        foreach (var (name, text) in sources)
        {
            Assert.True(
                Declarations.Count(text) == Sizes.Count(text),
                $"{name}: объявлений длины {Declarations.Count(text)}, а счётчик разобрал " +
                $"{Sizes.Count(text)} — значит в файле форма записи, которой он не знает");
        }
    }

    /// <summary>В коде студии длин числом ровно столько, сколько говорит потолок.</summary>
    /// <remarks>
    /// Счёт разметки кода не видел, и числа ушли туда: предел строки вопроса и ширина поля имени в
    /// <c>StudioAsk</c> стояли числами, когда разметка давно дошла до нуля. Константа не в счёт: она
    /// называет число, а длину контролу задаёт присваивание, — ширина, до которой декодируется
    /// значок плагина, это размер растра, а не вёрстка.
    /// </remarks>
    [Fact]
    public void Literal_sizes_in_studio_code_are_exactly_as_many_as_the_ceiling_says()
    {
        var sources = CodeSources.All().ToList();

        Assert.Contains(sources, source => source.Name.EndsWith("StudioAsk.cs", StringComparison.Ordinal));

        var counted = sources
            .Select(source => (source.Name, Count: CodeLiterals(source.Text)))
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.Count)
            .ToList();

        var total = counted.Sum(row => row.Count);
        var where = string.Join(", ", counted.Select(row => $"{row.Name} — {row.Count}"));

        Assert.True(
            total == CodeCeiling,
            total > CodeCeiling
                ? $"длин числом в коде студии стало {total} при потолке {CodeCeiling}: длину берут " +
                  $"привязкой к ключу темы — размеры экранов в Metrics.axaml. Числа — в {where}"
                : $"длин числом в коде студии осталось {total} при потолке {CodeCeiling}: опустите " +
                  "CodeCeiling тем же коммитом, которым их убрали");
    }

    /// <summary>Считает длины в коде, написанные числом; ноль — не выбранная длина, а её нет.</summary>
    private static int CodeLiterals(string text) =>
        CodeSizes.Matches(text).Count(match => match.Groups[1].Value.Any(symbol => symbol is >= '1' and <= '9'));

    /// <summary>Считает длины, написанные числом; размер самого окна не в счёт.</summary>
    private static int Literals(string text)
    {
        var root = RootTagEnd(text);

        return Sizes.Matches(text)
            .Where(match => match.Index > root)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Count(value => !value.Contains('{') && value.Any(symbol => symbol is >= '1' and <= '9'));
    }

    /// <summary>
    /// Где кончается открывающий тег корневого элемента.
    /// </summary>
    /// <remarks>
    /// Размер окна объявлен там, и только там: <c>Width</c> и <c>MinHeight</c> у самого
    /// <c>Window</c> — это место, с которого окно открывается, а не вёрстка внутри него.
    /// Комментарий и объявление XML перед корнем пропускаются: файл темы студии начинается с них.
    /// </remarks>
    private static int RootTagEnd(string text)
    {
        var start = 0;

        while (true)
        {
            var open = text.IndexOf('<', start);

            if (open < 0)
                return 0;

            if (text.AsSpan(open).StartsWith("<!--"))
            {
                var comment = text.IndexOf("-->", open, StringComparison.Ordinal);

                if (comment < 0)
                    return 0;

                start = comment + 3;

                continue;
            }

            if (text.AsSpan(open).StartsWith("<?"))
            {
                var declaration = text.IndexOf("?>", open, StringComparison.Ordinal);

                if (declaration < 0)
                    return 0;

                start = declaration + 2;

                continue;
            }

            var close = text.IndexOf('>', open);

            return close < 0 ? 0 : close;
        }
    }
}
