using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// SDK, набор контролов и иконки — сборки, которые плагин видит общими, и их поверхность записана.
/// </summary>
/// <remarks>
/// Плагин собирается против их типов, и студия отдаёт ему свои экземпляры этих сборок: снятый член
/// ломает уже собранный плагин на первом обращении, а добавленный — обещание, которое плагин
/// вправе потребовать через <c>sdk.min</c>. Правило «добавили — минор, сняли — мажор» записано в
/// CLAUDE.md давно, но держалось на памяти: запись 117 добавила в набор контрол, записи 172, 175 и 176
/// — публичные члены, и номер не двигался ни разу. Теперь поверхность сверяется с записанной, и
/// сдвиг мимо номера падает здесь.
/// </remarks>
public class SharedSurfaceTests
{
    /// <summary>Поверхность SDK двигается только вместе с номером SDK.</summary>
    /// <remarks>
    /// Строка самого номера в поверхность не входит: она менялась бы на каждом сдвиге, и запись
    /// расходилась бы с собой тем же движением, которое её и узаконивает.
    /// </remarks>
    [Fact]
    public void The_sdk_surface_moves_only_with_the_sdk_version() =>
        PublicSurface.AssertVersioned(
            Baseline("ArxisStudio.Sdk.txt"),
            [
                .. PublicSurface.Describe(typeof(StudioSdk).Assembly)
                    .Where(line => !line.StartsWith("ArxisStudio.Sdk.StudioSdk :: const System.String Version =", StringComparison.Ordinal)),
            ],
            StudioSdk.Version);

    /// <summary>Поверхность набора контролов двигается только вместе с номером SDK.</summary>
    [Fact]
    public void The_controls_surface_moves_only_with_the_sdk_version() =>
        PublicSurface.AssertVersioned(
            Baseline("ArxisStudio.Controls.txt"),
            PublicSurface.Describe(typeof(AxButton).Assembly),
            StudioSdk.Version);

    /// <summary>Поверхность иконок двигается только вместе с номером SDK.</summary>
    [Fact]
    public void The_icons_surface_moves_only_with_the_sdk_version() =>
        PublicSurface.AssertVersioned(
            Baseline("ArxisStudio.Icons.txt"),
            PublicSurface.Describe(typeof(AxIcon).Assembly),
            StudioSdk.Version);

    private static string Baseline(string file) => Repository.Path("tests", "ArxisStudio.Tests", "Surfaces", file);
}
