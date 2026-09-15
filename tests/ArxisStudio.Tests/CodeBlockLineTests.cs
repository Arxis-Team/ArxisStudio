using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Строка блока кода: двадцать при кегле темы, растёт вместе с кеглем, и текст стоит посередине.
/// </summary>
/// <remarks>
/// Высота строки была прибита числом 20, и при крупном кегле строки кода ложились одна на другую.
/// Теперь это кегль, умноженный на долю темы, — как безразмерный line-height в CSS. Мерится здесь, а
/// не в тестах темы: там рисование безголовое и настоящих метрик шрифта не знает, а шаг строки и
/// место текста в ней — это именно они.
/// </remarks>
public class CodeBlockLineTests
{
    private const string Sample = "<Button\n    Content=\"Отправить\"/>\n<!-- конец -->";

    /// <summary>При кегле темы шаг строки — двадцать, как в карточке, и блок не вырос ни на пиксель.</summary>
    /// <remarks>
    /// Доля кегля конечной дробью не записывается: 20 / 13. Округлённая вверх, она давала строку в
    /// 20,0005, а раскладка округляет размер вверх до пикселя — пять строк в витрине контролов
    /// заняли 101 вместо 100.
    /// </remarks>
    [AvaloniaFact]
    public void A_code_line_is_twenty_at_the_theme_size()
    {
        var (window, text) = Shown();
        var pitch = Pitch(text);

        Assert.True(Math.Abs(pitch - 20) <= 0.5, $"строка кода {pitch}, а должна быть 20");
        Assert.True(text.DesiredSize.Height == 60, $"три строки кода заняли {text.DesiredSize.Height}, а не 60");

        window.Close();
    }

    /// <summary>Кегль вырос втрое — строки не налезают друг на друга.</summary>
    [AvaloniaFact]
    public void Code_lines_do_not_overlap_when_the_type_grows()
    {
        var (window, text) = Shown();

        window.Resources["AxFontSize"] = 39d;
        window.UpdateLayout();

        var pitch = Pitch(text);

        Assert.Equal(39d, text.FontSize);
        Assert.True(pitch >= text.FontSize * 1.1, $"при кегле {text.FontSize} шаг строки {pitch} — строки налезают");

        window.Close();
    }

    /// <summary>Запас строки делится поровну: над текстом столько же, сколько под ним.</summary>
    /// <remarks>
    /// Так делит его карточка — это line-height в CSS, — и так делила прибитая высота. Расстояние
    /// между строками отдало бы весь запас под строку: текст поднялся бы к верху блока, а под
    /// последней строкой легла бы лишняя полоса. Шаг строки при этом тот же, и первые два теста
    /// такого сдвига не видят.
    /// </remarks>
    [AvaloniaFact]
    public void The_line_leading_is_split_evenly_above_and_below_the_text()
    {
        var (window, text) = Shown();

        var plain = new TextBlock { Text = Sample, FontFamily = text.FontFamily, FontSize = text.FontSize };
        plain.Measure(Size.Infinity);

        var line = text.TextLayout.TextLines[0];
        var natural = plain.TextLayout.TextLines[0];

        var above = line.Baseline - natural.Baseline;
        var below = line.Height - line.Baseline - (natural.Height - natural.Baseline);

        Assert.True(line.Height > natural.Height + 1, $"у строки кода нет запаса: {line.Height} при естественной {natural.Height}");
        Assert.True(Math.Abs(above - below) <= 0.5, $"запас строки над текстом {above:F2}, под ним {below:F2}");

        window.Close();
    }

    private static (Window Window, SelectableTextBlock Text) Shown()
    {
        var code = new AxCodeBlock { Text = Sample };
        var window = new Window { Width = 800, Height = 600, Content = code };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var text = code.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == "PART_Text");

        return (window, text);
    }

    /// <summary>Расстояние от верха первой строки до верха второй.</summary>
    private static double Pitch(TextBlock text)
    {
        var second = Sample.IndexOf('\n', StringComparison.Ordinal) + 1;

        return text.TextLayout.HitTestTextPosition(second).Y - text.TextLayout.HitTestTextPosition(0).Y;
    }
}
