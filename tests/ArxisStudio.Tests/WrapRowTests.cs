using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ряд из ведущей и хвоста: одной строкой, пока части помещаются, и хвост под ведущей, когда нет.
/// </summary>
/// <remarks>
/// Части здесь — рамки известной ширины: у ведущей естественная ширина задана наименьшей, а
/// предел — наибольшей, у хвоста ширина прибита. Так видно, что решает ряд, а не содержимое.
/// </remarks>
public class WrapRowTests
{
    private const double Gap = 10;

    /// <summary>Помещаются — хвост у правого края, ведущая у левого и не шире предела.</summary>
    [AvaloniaFact]
    public void Parts_that_fit_share_a_line_with_the_trail_at_the_right_edge()
    {
        var (row, lead, trail, window) = Shown(600);

        Assert.False(row.IsWrapped);
        Assert.Equal(new Rect(0, 0, 200, 20), lead.Bounds);
        Assert.Equal(new Rect(450, 0, 150, 20), trail.Bounds);

        window.Close();
    }

    /// <summary>Места мало, но хватает — ведущая сжимается, а хвост не двигается с края.</summary>
    [AvaloniaFact]
    public void A_lead_short_of_room_gives_way_before_the_trail_moves()
    {
        var (row, lead, trail, window) = Shown(300);

        Assert.False(row.IsWrapped);
        Assert.Equal(new Rect(0, 0, 140, 20), lead.Bounds);
        Assert.Equal(new Rect(150, 0, 150, 20), trail.Bounds);

        window.Close();
    }

    /// <summary>
    /// Не помещаются и естественными ширинами — хвост уходит под ведущую, а ведущая берёт строку.
    /// </summary>
    /// <remarks>
    /// Ведущую ниже естественной ширины ряд не сжимает: двести сорок меньше суммы ста, зазора и
    /// ста пятидесяти, и ряд переносит хвост, а не отдаёт ведущей оставшиеся восемьдесят.
    /// </remarks>
    [AvaloniaFact]
    public void Parts_that_do_not_fit_even_at_their_natural_widths_wrap()
    {
        var (row, lead, trail, window) = Shown(240);

        Assert.True(row.IsWrapped);
        Assert.Equal(new Rect(0, 0, 200, 20), lead.Bounds);
        Assert.Equal(new Rect(0, 20 + Gap, 150, 20), trail.Bounds);
        Assert.Equal(20 + Gap + 20, row.Bounds.Height);

        window.Close();
    }

    /// <summary>Шире стало — хвост возвращается в строку.</summary>
    [AvaloniaFact]
    public void A_wrapped_trail_comes_back_when_there_is_room_again()
    {
        var (row, _, trail, window) = Shown(240);

        Assert.True(row.IsWrapped);

        // Размер окна применяется не сразу, а очередной задачей — как у экрана Welcome.
        window.Width = 600;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.False(row.IsWrapped);
        Assert.Equal(new Rect(450, 0, 150, 20), trail.Bounds);

        window.Close();
    }

    /// <summary>Скрытая часть места не занимает, и одна ведущая стоит одна.</summary>
    [AvaloniaFact]
    public void A_hidden_trail_leaves_the_lead_alone()
    {
        var (row, lead, trail, window) = Shown(240);

        trail.IsVisible = false;
        window.UpdateLayout();

        Assert.False(row.IsWrapped);
        Assert.Equal(new Rect(0, 0, 200, 20), lead.Bounds);
        Assert.Equal(20, row.Bounds.Height);

        window.Close();
    }

    /// <summary>
    /// Колонка ведущей задана — хвост встаёт сразу за ней, а не у правого края.
    /// </summary>
    /// <remarks>
    /// Так стоят контролы страницы настроек: линией возле подписей, как поля у Unity, а не у края
    /// широкого окна, куда глазу идти через пустоту.
    /// </remarks>
    [AvaloniaFact]
    public void A_fixed_lead_puts_the_tail_right_after_it()
    {
        var (row, lead, trail, window) = Shown(600, leadWidth: 250);

        Assert.False(row.IsWrapped);
        Assert.Equal(new Rect(0, 0, 200, 20), lead.Bounds);
        Assert.Equal(new Rect(250 + Gap, 0, 150, 20), trail.Bounds);
        Assert.Equal(250 + Gap + 150, row.DesiredSize.Width);

        window.Close();
    }

    /// <summary>Колонка задана, а хвосту за ней тесно — он уходит под ведущую, как и без колонки.</summary>
    [AvaloniaFact]
    public void A_fixed_lead_still_wraps_the_tail_when_it_does_not_fit()
    {
        var (row, _, trail, window) = Shown(380, leadWidth: 250);

        Assert.True(row.IsWrapped);
        Assert.Equal(new Rect(0, 20 + Gap, 150, 20), trail.Bounds);

        window.Close();
    }

    /// <summary>Подпись длиннее колонки переносится в ней, а не наезжает на хвост.</summary>
    [AvaloniaFact]
    public void A_label_longer_than_the_column_wraps_inside_it()
    {
        var label = new TextBlock
        {
            Text = "Показывать превью изображений на плитках окна проекта",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var trail = new Border { Width = 150, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var row = new WrapRow { Spacing = Gap, LeadWidth = 120, Children = { label, trail } };
        var window = new Window { Width = 600, Height = 200, Content = new StackPanel { Children = { row } } };

        window.Show();
        window.UpdateLayout();

        Assert.False(row.IsWrapped);
        Assert.True(label.Bounds.Width <= 120, $"подпись {label.Bounds.Width} шире колонки");
        Assert.True(label.Bounds.Height > label.FontSize * 2, "длинная подпись не перенеслась в колонке");
        Assert.Equal(120 + Gap, trail.Bounds.X);

        window.Close();
    }

    /// <summary>Третьей части ряд не раскладывает молча, а говорит, что её некуда поставить.</summary>
    [AvaloniaFact]
    public void A_third_part_is_refused_aloud()
    {
        var row = new WrapRow { Children = { new Border(), new Border(), new Border() } };

        var refusal = Assert.Throws<InvalidOperationException>(() => row.Measure(Size.Infinity));

        Assert.Contains(nameof(WrapRow), refusal.Message, StringComparison.Ordinal);
    }

    private static (WrapRow Row, Border Lead, Border Trail, Window Window) Shown(double width, double leadWidth = double.NaN)
    {
        var lead = new Border { MinWidth = 100, MaxWidth = 200, Height = 20 };
        var trail = new Border { Width = 150, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var row = new WrapRow { Spacing = Gap, LeadWidth = leadWidth, Children = { lead, trail } };

        // Стопка, а не окно: содержимое окна растягивается на всю высоту. Высота окна задана
        // затем, что без неё безголовое окно не принимает новую ширину и возвращает прежнюю.
        var window = new Window { Width = width, Height = 200, Content = new StackPanel { Children = { row } } };

        window.Show();
        window.UpdateLayout();

        return (row, lead, trail, window);
    }
}
