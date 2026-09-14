using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Храповик: сколько отступов в разметке студии написано числом.
/// </summary>
/// <remarks>
/// Правило репозитория говорит прямо: значение цвета или размера объявляется в
/// теме и больше нигде — «это правило подмодуля Themes.Arxis, но нарушается оно
/// здесь». Нарушается сто одиннадцать раз, и до шкалы отступов иначе и быть не
/// могло: называть было нечем.
/// <para>
/// Заменить их разом нельзя: правка сотни отступов одним коммитом непроверяема
/// на обзоре, непригодна для бисекта, и её регрессии припишут следующему
/// коммиту. Значит — по экрану за раз, а между коммитами дверь держит этот
/// тест. Число ниже опускается коммитом миграции и не растёт никогда.
/// </para>
/// <para>
/// Сверка точная, а не «не больше». Потолок с запасом под ним — это не
/// храповик, а бюджет, и бюджет тратят: перенёс один отступ в токен, написал
/// числом другой — счёт сошёлся, а разметка осталась где была.
/// </para>
/// </remarks>
public class LiteralSpacingTests
{
    /// <summary>
    /// Сколько отступов студии сегодня написано числом.
    /// </summary>
    /// <remarks>
    /// Опускается коммитом миграции — вместе с экраном, из которого числа ушли,
    /// и записью в плане. Не поднимается ничем: отступу, которому не нашлось
    /// ступени, место либо в теме ключом, либо в разговоре о самой шкале.
    /// </remarks>
    private const int Ceiling = 15;

    /// <summary>Объявления отступа: атрибутом и сеттером.</summary>
    private static readonly Regex Spacings = new(
        """(?:\b(?:Margin|Padding|Spacing)="([^"]*)")|(?:<Setter\s+Property="(?:Margin|Padding|Spacing)"\s+Value="([^"]*)")""",
        RegexOptions.Compiled);

    /// <summary>
    /// Место, где отступ вообще упомянут: им меряется полнота счётчика.
    /// </summary>
    /// <remarks>
    /// Про форму сеттера здесь не сказано ничего — только что имя свойства
    /// названо. В этом весь смысл: мерить строгий счётчик его же
    /// предположениями бессмысленно, они слепнут вместе.
    /// </remarks>
    private static readonly Regex Declarations = new(
        """(?:\b(?:Margin|Padding|Spacing)=")|(?:\bProperty="(?:Margin|Padding|Spacing)")""",
        RegexOptions.Compiled);

    /// <summary>
    /// Сколько отступов в коде студии написано числом.
    /// </summary>
    /// <remarks>
    /// Одно, и оно не зазор: палитра команд встаёт в восьмидесяти точках от верхнего края окна.
    /// Это координата карточки, как координаты рисунка заставки в разметке, а не расстояние между
    /// вещами. Правило то же, что у разметки: опускается коммитом, который число убрал, и не
    /// поднимается ничем.
    /// </remarks>
    private const int CodeCeiling = 1;

    /// <summary>Отступ в коде: число в зазоре стопки и толщина из чисел.</summary>
    private static readonly Regex CodeSpacings = new(
        """\b(?:Spacing|ItemSpacing|LineSpacing|HorizontalSpacing|VerticalSpacing)\s*=\s*(-?[\d.]+)|\bnew\s+Thickness\(([^()]*)\)""",
        RegexOptions.Compiled);

    /// <summary>Толщина, собранная из одних чисел.</summary>
    private static readonly Regex Numbers = new("""^[\s\d.,\-]*$""", RegexOptions.Compiled);

    /// <summary>Литералов ровно столько, сколько стояло на прошлом коммите.</summary>
    [Fact]
    public void Literal_spacings_are_exactly_as_many_as_the_ceiling_says()
    {
        var counted = MarkupSources.All()
            .Select(source => (source.Name, Count: Literals(source.Text)))
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.Count)
            .ToList();

        var total = counted.Sum(row => row.Count);
        var worst = string.Join(", ", counted.Take(3).Select(row => $"{row.Name} — {row.Count}"));

        Assert.True(
            total == Ceiling,
            total > Ceiling
                ? $"литералов отступа стало {total} при потолке {Ceiling}: расстояние объявляют ступенью " +
                  $"шкалы из Spacing.axaml темы, а отступ внутрь контрола — ключом в Metrics.axaml. " +
                  $"Больше всего в {worst}"
                : $"литералов отступа осталось {total} при потолке {Ceiling}: опустите Ceiling до {total} " +
                  "тем же коммитом, которым их убрали");
    }

    /// <summary>В коде студии литералов отступа ровно столько, сколько стояло на прошлом коммите.</summary>
    /// <remarks>
    /// Отступ в коде берут привязкой к ключу темы — так же, как советует автору плагина ARX0010:
    /// <c>Bind(StackPanel.SpacingProperty, control.GetResourceObservable("AxGapControls"))</c>.
    /// Число не сжалось бы вместе с плотностью, и заглушка упавшей панели или кнопки диалога
    /// выдали бы себя рядом с соседями, которые сжались.
    /// </remarks>
    [Fact]
    public void Literal_spacings_in_studio_code_are_exactly_as_many_as_the_ceiling_says()
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
                ? $"литералов отступа в коде студии стало {total} при потолке {CodeCeiling}: отступ берут " +
                  $"привязкой к ключу темы, а не числом. Числа — в {where}"
                : $"литералов отступа в коде студии осталось {total} при потолке {CodeCeiling}: опустите " +
                  "CodeCeiling тем же коммитом, которым их убрали");
    }

    /// <summary>
    /// Счётчик видит каждое объявление отступа.
    /// </summary>
    /// <remarks>
    /// Храповик считает текстом, и любая форма записи, которой он не знает,
    /// становится дырой ровно того размера, сколько в неё влезет. Сеттер со
    /// значением-элементом или с переставленными атрибутами счётчик не
    /// разберёт — и здесь это видно как расхождение, а не как упавший до нуля
    /// счёт.
    /// </remarks>
    [Fact]
    public void The_counter_sees_every_declaration()
    {
        var sources = MarkupSources.All().ToList();

        Assert.NotEmpty(sources);

        foreach (var (name, text) in sources)
        {
            Assert.True(
                Declarations.Count(text) == Spacings.Count(text),
                $"{name}: объявлений отступа {Declarations.Count(text)}, а счётчик разобрал " +
                $"{Spacings.Count(text)} — значит в файле форма записи, которой он не знает");
        }
    }

    /// <summary>
    /// Считает отступы, написанные числом.
    /// </summary>
    /// <remarks>
    /// Ноль не считается: это не выбранное расстояние, а его отсутствие.
    /// Отрицательное считается — минус в разметке такое же выбранное руками
    /// число, как восьмёрка.
    /// </remarks>
    private static int Literals(string text) =>
        Spacings.Matches(text)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Count(value => !value.Contains('{') && value.Any(symbol => symbol is >= '1' and <= '9'));

    /// <summary>
    /// Считает отступы в коде, написанные числом.
    /// </summary>
    /// <remarks>
    /// Ноль, как и в разметке, не считается. Толщина считается, только когда собрана из одних
    /// чисел: <c>new Thickness(padding.Left, 0, 0, 0)</c> — это вычисление, а не выбранное руками
    /// расстояние.
    /// </remarks>
    private static int CodeLiterals(string text) =>
        CodeSpacings.Matches(text)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Count(value => Numbers.IsMatch(value) && value.Any(symbol => symbol is >= '1' and <= '9'));
}
