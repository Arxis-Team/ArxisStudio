using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Разделитель областей в разметке студии — <c>AxDivider</c>, а не край рамки.
/// </summary>
/// <remarks>
/// Край толщиной в раскладочную единицу при 150 и 175 % округляется до двух пикселей, а разделитель
/// — ровно один пиксель устройства при любом масштабе: это держит <c>HairlineTests</c> темы. Рядом
/// они читались линиями разной толщины — боковая полоса Welcome и строки настроек против границ
/// дока. Рамка остаётся тому, что обводит контрол целиком; одна её сторона — это разделитель, и ему
/// место в <c>AxDivider</c>.
/// <para>
/// Правило — по всей разметке студии, модулей, плагинов и шаблона, а не по списку окон: новый экран
/// попадает под него сам.
/// </para>
/// </remarks>
public class DividerRuleTests
{
    private static readonly Regex OneSided = new(@"AxHairline(?:Top|Bottom|Leading|Trailing)Thickness", RegexOptions.Compiled);

    /// <summary>Односторонней волосяной рамки в разметке нет: разделитель рисует разделитель.</summary>
    [Fact]
    public void A_separator_is_a_divider_and_not_the_edge_of_a_border()
    {
        var sources = MarkupSources.All().ToList();
        var sided = sources
            .SelectMany(source => OneSided.Matches(source.Text).Select(match => $"{source.Name}: {match.Value}"))
            .ToList();

        Assert.Contains(sources, source => source.Name == "WelcomeWindow.axaml" && source.Text.Contains("AxDivider", StringComparison.Ordinal));
        Assert.True(sided.Count == 0, "край рамки вместо разделителя: " + string.Join("; ", sided));
    }
}
