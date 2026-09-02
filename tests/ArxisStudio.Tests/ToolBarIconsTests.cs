using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значок элемента полосы по записи из манифеста.
/// </summary>
/// <remarks>
/// Запись читается без загрузки сборки плагина, поэтому и ошибка в ней должна
/// быть словом, а не исключением: полоса — не то место, из-за которого стоит
/// падать. Каждый отказ здесь называет причину.
/// </remarks>
public class ToolBarIconsTests
{
    /// <summary>Имя из набора даёт тот самый глиф — не копию.</summary>
    [AvaloniaFact]
    public void A_name_from_the_set_gives_the_very_same_geometry()
    {
        var drawn = ToolBarIcons.Resolve("arxis:Play", out var problem);

        Assert.Null(problem);
        Assert.Same(AxIcons.Play, drawn);
    }

    /// <summary>Свой контур рисуется как дан — в сетке 16.</summary>
    [AvaloniaFact]
    public void A_path_in_the_sixteen_grid_is_drawn_as_given()
    {
        var drawn = ToolBarIcons.Resolve("M3.5 8H12.5", out var problem);

        Assert.Null(problem);
        Assert.NotNull(drawn);
        Assert.Equal(9d, drawn!.Bounds.Width, 1);
    }

    /// <summary>
    /// Имя сверяется с набором строго: как записано в коде.
    /// </summary>
    /// <remarks>
    /// Имя копируют из <c>AxIcons.Play</c>, и снисхождение к регистру означало
    /// бы два написания одного значка в манифестах разных авторов.
    /// </remarks>
    [AvaloniaFact]
    public void An_unknown_name_is_reported_rather_than_drawn()
    {
        Assert.Null(ToolBarIcons.Resolve("arxis:Nope", out var problem));
        Assert.Contains("arxis:Nope", problem);

        Assert.Null(ToolBarIcons.Resolve("arxis:play", out problem));
        Assert.NotNull(problem);
    }

    /// <summary>Контур, который не разобрался или ничего не рисует, — отказ со словом.</summary>
    [AvaloniaFact]
    public void A_path_that_draws_nothing_is_reported()
    {
        Assert.Null(ToolBarIcons.Resolve("M oops", out var problem));
        Assert.NotNull(problem);

        Assert.Null(ToolBarIcons.Resolve("M8 8", out problem));
        Assert.NotNull(problem);
    }

    /// <summary>Значка не просили — и замечания нет.</summary>
    [AvaloniaFact]
    public void No_icon_is_not_a_problem()
    {
        Assert.Null(ToolBarIcons.Resolve(string.Empty, out var problem));
        Assert.Null(problem);

        Assert.Null(ToolBarIcons.Resolve(null, out problem));
        Assert.Null(problem);
    }
}
