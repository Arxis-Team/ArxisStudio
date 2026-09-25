using ArxisStudio.Modules.Terminal.Sessions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Бегунок полосы терминала: рисунок, попадание и протяжка из одного расчёта.
/// </summary>
/// <remarks>
/// Окно здесь не нужно: геометрия бегунка — числа, и проверяется она числами. Протяжку мышью по
/// живому виду держит <see cref="TerminalViewTests"/>.
/// </remarks>
public class TerminalScrollThumbTests
{
    private const double Inset = 6;
    private const double Height = 400;
    private const int Rows = 24;

    /// <summary>Истории нет — нет и бегунка.</summary>
    [Fact]
    public void Without_history_there_is_no_thumb() =>
        Assert.Null(ScrollThumb.Of(Inset, Height, Rows, max: 0, shown: 0));

    /// <summary>
    /// В начале истории бегунок стоит на верху дорожки, на живом краю — на её дне, при любой длине
    /// истории.
    /// </summary>
    /// <param name="max">Самая нижняя строка, на которую можно встать.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(100_000)]
    public void The_thumb_travels_the_whole_track(int max)
    {
        var first = Thumb(max, shown: 0);
        var last = Thumb(max, shown: max);

        Assert.Equal(Inset, first.Top, 6);
        Assert.Equal(Height - Inset, last.Top + last.Height, 6);
    }

    /// <summary>Длинная история не сводит бегунок в точку: короче своего предела он не бывает.</summary>
    [Fact]
    public void A_long_history_keeps_the_thumb_graspable() =>
        Assert.Equal(ScrollThumb.MinHeight, Thumb(max: 100_000, shown: 0).Height);

    /// <summary>
    /// Бегунок, взятый в любой своей точке и не сдвинутый, называет ту строку, на которой нарисован:
    /// протяжка — обратный ход рисунка, и под рукой он не прыгает.
    /// </summary>
    /// <param name="shown">Верхняя показанная строка.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(57)]
    [InlineData(200)]
    public void A_held_thumb_names_the_line_it_is_drawn_at(int shown)
    {
        var thumb = Thumb(max: 200, shown);

        foreach (var grab in new[] { 0, thumb.Height / 2, thumb.Height - 1 })
            Assert.Equal(shown, thumb.LineAt(thumb.Top + grab, grab, Inset));
    }

    /// <summary>Протяжка за край дорожки останавливается на краю истории.</summary>
    [Fact]
    public void Dragging_past_the_track_stops_at_the_ends_of_history()
    {
        var thumb = Thumb(max: 200, shown: 100);

        Assert.Equal(0, thumb.LineAt(-1000, 0, Inset));
        Assert.Equal(200, thumb.LineAt(Height + 1000, 0, Inset));
    }

    /// <summary>Нажатие приходится на бегунок только в его пределах; над ним и под ним — дорожка.</summary>
    [Fact]
    public void Only_the_thumb_itself_is_grabbed()
    {
        var thumb = Thumb(max: 200, shown: 100);

        Assert.True(thumb.Holds(thumb.Top), "верх бегунка — дорожка");
        Assert.True(thumb.Holds(thumb.Top + thumb.Height - 0.5), "низ бегунка — дорожка");
        Assert.False(thumb.Holds(thumb.Top - 0.5), "дорожка над бегунком берёт бегунок");
        Assert.False(thumb.Holds(thumb.Top + thumb.Height), "дорожка под бегунком берёт бегунок");
    }

    private static ScrollThumb Thumb(int max, int shown) =>
        ScrollThumb.Of(Inset, Height, Rows, max, shown) ?? throw new InvalidOperationException("бегунка нет, а история есть");
}
