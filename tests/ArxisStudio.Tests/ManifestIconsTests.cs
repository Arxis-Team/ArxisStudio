using ArxisStudio.Icons;
using ArxisStudio.Shell;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значок по записи из манифеста — у кнопки полосы, панели и команды.
/// </summary>
/// <remarks>
/// Запись читается без загрузки сборки плагина, поэтому и ошибка в ней должна
/// быть словом, а не исключением: полоса, вкладка и меню — не те места, из-за
/// которых стоит падать. Каждый отказ здесь называет причину.
/// </remarks>
public class ManifestIconsTests
{
    /// <summary>Имя из набора даёт тот самый глиф — не копию.</summary>
    [AvaloniaFact]
    public void A_name_from_the_set_gives_the_very_same_geometry()
    {
        var drawn = ManifestIcons.Resolve("arxis:Play", out var problem);

        Assert.Null(problem);
        Assert.Same(AxIcons.Play, drawn);
    }

    /// <summary>Свой контур рисуется как дан — в сетке 16.</summary>
    [AvaloniaFact]
    public void A_path_in_the_sixteen_grid_is_drawn_as_given()
    {
        var drawn = ManifestIcons.Resolve("M3.5 8H12.5", out var problem);

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
        Assert.Null(ManifestIcons.Resolve("arxis:Nope", out var problem));
        Assert.Contains("arxis:Nope", problem);

        Assert.Null(ManifestIcons.Resolve("arxis:play", out problem));
        Assert.NotNull(problem);
    }

    /// <summary>Контур, который не разобрался или ничего не рисует, — отказ со словом.</summary>
    [AvaloniaFact]
    public void A_path_that_draws_nothing_is_reported()
    {
        Assert.Null(ManifestIcons.Resolve("M oops", out var problem));
        Assert.NotNull(problem);

        Assert.Null(ManifestIcons.Resolve("M8 8", out problem));
        Assert.NotNull(problem);
    }

    /// <summary>
    /// Пробелы по краям записи не мешают — ни имени, ни контуру.
    /// </summary>
    /// <remarks>
    /// Запись с пробелом впереди не узнавалась по приставке и уходила разбираться
    /// контуром, а контуром «arxis:Play» не бывает. Правило сборки читает запись так
    /// же, и пропустить то, чего студия не нарисует, ему было бы нечем объяснить.
    /// </remarks>
    [AvaloniaFact]
    public void Spaces_around_the_record_do_not_get_in_the_way()
    {
        Assert.Same(AxIcons.Play, ManifestIcons.Resolve("  arxis:Play ", out var problem));
        Assert.Null(problem);

        Assert.NotNull(ManifestIcons.Resolve(" M3.5 8H12.5  ", out problem));
        Assert.Null(problem);
    }

    /// <summary>Значка не просили — и замечания нет.</summary>
    [AvaloniaFact]
    public void No_icon_is_not_a_problem()
    {
        Assert.Null(ManifestIcons.Resolve(string.Empty, out var problem));
        Assert.Null(problem);

        Assert.Null(ManifestIcons.Resolve(null, out problem));
        Assert.Null(problem);
    }
}
