using ArxisStudio.Docking;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Куда встаёт панель, чьей стороны в дереве нет.
/// </summary>
/// <remarks>
/// Размещение — чистая функция над деревом, и проверяется оно без окна. <see cref="StudioDockTests"/>
/// ставят панели в каркас, где все стороны на месте. Здесь — раскладка из файла, в которой стороны
/// нет: панель заводит группу у края, который назвала, а не у того, что попался первым.
/// </remarks>
public class DockPlacementTests
{
    /// <summary>Ушедшая сторона возвращается у своего края — перед домом документов.</summary>
    /// <param name="side">Сторона из манифеста.</param>
    /// <param name="orientation">Направление деления, в котором стоит дом.</param>
    /// <param name="neighbour">Сторона, которая в дереве осталась.</param>
    [Theory]
    [InlineData("left", DockOrientation.Horizontal, "right")]
    [InlineData("top", DockOrientation.Vertical, "bottom")]
    public void A_side_missing_from_the_tree_comes_back_at_its_own_edge(
        string side,
        DockOrientation orientation,
        string neighbour)
    {
        var root = new DockSplit
        {
            Orientation = orientation,
            Weights = [0.8, 0.2],
            Children = [new DockGroup { Id = StudioDock.Documents }, new DockGroup { Id = neighbour }],
        };

        var placed = DockPlacement.Place(
            root,
            "hello:tree",
            new PluginPlacement { Side = side },
            StudioDock.Documents,
            new HashSet<string>(StringComparer.Ordinal));

        var split = Assert.IsType<DockSplit>(placed);

        Assert.Equal(orientation, split.Orientation);
        Assert.Equal([side, StudioDock.Documents, neighbour], split.Children.Select(child => ((DockGroup)child).Id));
        Assert.Equal(["hello:tree"], ((DockGroup)split.Children[0]).Items);
    }
}
