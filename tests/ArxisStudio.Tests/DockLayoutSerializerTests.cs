using ArxisStudio.Docking;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка в текст и обратно.
/// </summary>
public class DockLayoutSerializerTests
{
    /// <summary>Раскладка возвращается из текста такой же, какой ушла.</summary>
    [Fact]
    public void A_layout_comes_back_the_way_it_went()
    {
        var before = Sample();

        var text = DockLayoutSerializer.Write(before);
        var after = DockLayoutSerializer.Read(text, out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.NotNull(after);

        // Устойчивость записи ловит потерю любого поля целиком...
        Assert.Equal(text, DockLayoutSerializer.Write(after));

        // ...а точечные проверки не дают ей пройти на двух одинаково пустых.
        Assert.Equal("work", after.Active);
        Assert.Equal(["default", "work"], after.Layouts.Keys.Order());

        var workspace = after.Current;
        Assert.NotNull(workspace);
        Assert.Equal("centre", workspace.DocumentHome);

        var split = Assert.IsType<DockSplit>(workspace.Root);
        Assert.Equal(DockOrientation.Horizontal, split.Orientation);
        Assert.Equal([0.25, 0.75], split.Weights);

        var left = Assert.IsType<DockGroup>(split.Children[0]);
        Assert.Equal("left", left.Id);
        Assert.Equal(["solution", "structure"], left.Items);
        Assert.Equal("structure", left.Selected);

        var inner = Assert.IsType<DockSplit>(split.Children[1]);
        Assert.Equal(DockOrientation.Vertical, inner.Orientation);
        Assert.Equal("centre", ((DockGroup)inner.Children[0]).Id);

        var floating = Assert.Single(workspace.Floating);
        Assert.Equal(120, floating.X);
        Assert.Equal("torn", ((DockGroup)floating.Root).Id);
    }

    /// <summary>
    /// Вычисляемый набор в файл не едет.
    /// </summary>
    /// <remarks>
    /// <c>Current</c> — это один из уже записанных наборов. Уехав в файл своей
    /// копией, он при следующем чтении лёг бы рядом с оригиналом и разошёлся бы с
    /// ним при первой же правке.
    /// </remarks>
    [Fact]
    public void The_chosen_set_is_not_written_twice()
    {
        Assert.DoesNotContain("\"current\"", DockLayoutSerializer.Write(Sample()), StringComparison.Ordinal);
    }

    /// <summary>Стороны и направления пишутся словами, а не номерами.</summary>
    /// <remarks>
    /// Файл раскладки человек открывает руками чаще, чем кажется, — и номер
    /// направления в нём не говорит ничего, а порядок значений в перечислении
    /// вдобавок нельзя будет менять.
    /// </remarks>
    [Fact]
    public void Directions_are_written_in_words()
    {
        var text = DockLayoutSerializer.Write(Sample());

        Assert.Contains("\"horizontal\"", text, StringComparison.Ordinal);
        Assert.Contains("\"vertical\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Файл новее известного не читается — и это не то же самое, что испорченный.
    /// </summary>
    /// <remarks>
    /// Испорченный перезаписывают не думая, а новый трогать нельзя: человек,
    /// заглянувший в проект старой студией, иначе потеряет раскладку, собранную
    /// новой. Поэтому причина отказа и различается.
    /// </remarks>
    [Fact]
    public void A_file_from_a_newer_studio_is_left_alone()
    {
        var text = DockLayoutSerializer.Write(Sample())
            .Replace($"\"version\": {DockLayout.CurrentVersion}", "\"version\": 99", StringComparison.Ordinal);

        Assert.Null(DockLayoutSerializer.Read(text, out var problem));
        Assert.Equal(DockLayoutProblem.Newer, problem);
    }

    /// <summary>Всё, чему нельзя доверять, отказывается одинаково.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не json вовсе")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"version":1,"active":"a","layouts":{"a":{"root":null}}}""")]
    [InlineData("""{"version":1,"active":"a","layouts":{"a":{"root":{"kind":"tabs"}}}}""")]
    [InlineData("""{"version":1,"active":"a","layouts":{"a":{"root":{"kind":"group","items":null}}}}""")]
    [InlineData("""{"version":1,"active":"a","layouts":{"a":{"root":{"kind":"group","items":[null]}}}}""")]
    [InlineData("""
        {"version":1,"active":"a","layouts":{"a":{"root":
        {"kind":"split","orientation":"horizontal","children":[null],"weights":[1]}}}}
        """)]
    [InlineData("""
        {"version":1,"active":"a","layouts":{"a":{"root":
        {"kind":"split","orientation":"horizontal","children":[],"weights":[]}}}}
        """)]
    [InlineData("""
        {"version":1,"active":"a","layouts":{"a":{"root":{"kind":"group","id":"g"},
        "floating":[{"root":null}]}}}
        """)]
    [InlineData("""
        {"version":1,"active":"a","layouts":{"a":{"root":{"kind":"group","id":"g"},"floating":[{"root":
        {"kind":"split","orientation":"vertical","children":[],"weights":[]}}]}}}
        """)]
    public void Anything_untrustworthy_is_refused_the_same_way(string? text)
    {
        Assert.Null(DockLayoutSerializer.Read(text, out var problem));
        Assert.Equal(DockLayoutProblem.Unreadable, problem);
    }

    /// <summary>Скупой файл читается: чего нет, то берётся по умолчанию.</summary>
    /// <remarks>
    /// Отсутствие поля — не ложь о форме, в отличие от явного null. Раскладку,
    /// написанную руками, студия обязана открыть.
    /// </remarks>
    [Fact]
    public void A_sparse_file_still_opens()
    {
        var layout = DockLayoutSerializer.Read(
            """{"layouts":{"default":{"root":{"kind":"group","id":"root","items":["console"]}}}}""",
            out var problem);

        Assert.Equal(DockLayoutProblem.None, problem);
        Assert.NotNull(layout);
        Assert.Equal(DockLayout.CurrentVersion, layout.Version);
        Assert.Equal(DockLayout.DefaultName, layout.Active);
        Assert.Equal(["console"], ((DockGroup)layout.Current!.Root).Items);
    }

    private static DockLayout Sample() => new()
    {
        Active = "work",
        Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
        {
            ["default"] = new() { Root = new DockGroup { Id = "root", Items = ["console"], Selected = "console" } },
            ["work"] = new()
            {
                DocumentHome = "centre",
                Root = new DockSplit
                {
                    Orientation = DockOrientation.Horizontal,
                    Weights = [0.25, 0.75],
                    Children =
                    [
                        new DockGroup { Id = "left", Items = ["solution", "structure"], Selected = "structure" },
                        new DockSplit
                        {
                            Orientation = DockOrientation.Vertical,
                            Weights = [0.7, 0.3],
                            Children =
                            [
                                new DockGroup { Id = "centre", Items = ["form"], Selected = "form" },
                                new DockGroup { Id = "bottom", Items = ["errors"], Selected = "errors" },
                            ],
                        },
                    ],
                },
                Floating =
                [
                    new DockWindow
                    {
                        X = 120,
                        Y = 60,
                        Width = 500,
                        Height = 380,
                        Root = new DockGroup { Id = "torn", Items = ["properties"], Selected = "properties" },
                    },
                ],
            },
        },
    };
}
