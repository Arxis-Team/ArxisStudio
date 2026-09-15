using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Скругление в разметке студии числом не пишется.
/// </summary>
/// <remarks>
/// Строки навигации Welcome и строки-ссылки скруглялись шестёркой прямо в стилях оболочки — числом,
/// которого в шкале темы не было. Число было верное, неверным было место: теперь это
/// ключ темы <c>AxCornerRadiusMedium</c>, и литералов, кроме нуля, в разметке студии не осталось ни
/// одного. Поэтому здесь не храповик с потолком, как у отступов, а запрет.
/// </remarks>
public class LiteralRadiusTests
{
    /// <summary>Объявления скругления: атрибутом и сеттером.</summary>
    private static readonly Regex Radii = new(
        """(?:\bCornerRadius="([^"]*)")|(?:<Setter\s+Property="CornerRadius"\s+Value="([^"]*)")""",
        RegexOptions.Compiled);

    /// <summary>Место, где скругление вообще упомянуто: им меряется полнота счётчика.</summary>
    private static readonly Regex Declarations = new(
        """(?:\bCornerRadius=")|(?:\bProperty="CornerRadius")""",
        RegexOptions.Compiled);

    /// <summary>Ни одного скругления числом, кроме прямых углов.</summary>
    /// <remarks>
    /// Ноль не считается: это не выбранное скругление, а его отсутствие.
    /// </remarks>
    [Fact]
    public void No_radius_is_written_as_a_number()
    {
        var found = MarkupSources.All()
            .SelectMany(source => Radii.Matches(source.Text)
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(value => !value.Contains('{') && value.Any(symbol => symbol is >= '1' and <= '9'))
                .Select(value => $"{source.Name}: {value}"))
            .ToList();

        Assert.True(
            found.Count == 0,
            "скругление написано числом — его объявляют ключом AxCornerRadius* темы: " + string.Join(", ", found));
    }

    /// <summary>Счётчик видит каждое объявление скругления.</summary>
    /// <remarks>
    /// Запрет считает текстом, и форма записи, которой он не знает, прошла бы мимо молча.
    /// </remarks>
    [Fact]
    public void The_counter_sees_every_declaration()
    {
        var sources = MarkupSources.All().ToList();

        Assert.NotEmpty(sources);

        foreach (var (name, text) in sources)
        {
            Assert.True(
                Declarations.Count(text) == Radii.Count(text),
                $"{name}: объявлений скругления {Declarations.Count(text)}, а счётчик разобрал " +
                $"{Radii.Count(text)} — значит в файле форма записи, которой он не знает");
        }
    }
}
