using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Emulator;
using ArxisStudio.Modules.Terminal.Sessions;
using ArxisStudio.Modules.Terminal.Shells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Текст терминала читается на своём фоне в обеих темах студии.
/// </summary>
/// <remarks>
/// Campbell подобрана под тёмный фон: в светлой теме жёлтый, белый и яркие цвета ложились на
/// светлый фон почти невидимыми — десять из шестнадцати ниже 4,5:1, — а в тёмной темнели синий и
/// чёрный. Контраст правится на рисовании, как в VS Code, и проверяется здесь по цветам темы, а не
/// по переписанным числам: сменится фон терминала в теме — проверка спросит новый.
/// </remarks>
public class TerminalContrastTests
{
    private static readonly ShellProfile Probe = new("probe", "Проба", "probe", []);

    /// <summary>
    /// Каждый из шестнадцати цветов читается на фоне терминала и на подложке выделения, а цвет,
    /// который читался и так, остаётся своим.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Every_named_colour_reads_on_the_terminal_background(string variant)
    {
        var (background, foreground, selection) = Surface(variant);
        var theme = TerminalTheme.Campbell(background, foreground, selection);
        string?[] named =
        [
            theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
            theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow,
            theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
        ];

        var faint = new List<string>();

        foreach (var name in named)
        {
            var colour = Rgb(Color.Parse(Assert.IsType<string>(name)));

            foreach (var under in new[] { Rgb(background), Rgb(selection) })
            {
                var ink = TerminalTheme.Legible(colour, under);

                if (TerminalTheme.Contrast(ink, under) < TerminalTheme.MinimumContrast)
                    faint.Add($"{name} на #{under:X6}: {TerminalTheme.Contrast(ink, under):0.00}:1");

                if (TerminalTheme.Contrast(colour, under) >= TerminalTheme.MinimumContrast && ink != colour)
                    faint.Add($"{name} читался и так, а его поменяли");
            }
        }

        Assert.True(faint.Count == 0, variant + ": " + string.Join("; ", faint));
    }

    /// <summary>
    /// Жёлтый, выведенный программой на фон по умолчанию, рисуется читаемым — той дорогой, какой
    /// рисует вид, — и остаётся жёлтым, а не серым.
    /// </summary>
    [AvaloniaFact]
    public void A_yellow_line_on_a_light_terminal_is_drawn_legible_and_still_yellow()
    {
        var (background, foreground, selection) = Surface("Light");
        using var pty = new FakePty();
        using var session = new TerminalSession(
            Probe, pty, TerminalSession.Options(TerminalSettings.Default, 40, 10), post: action => action());

        session.Terminal.Colors.ApplyTheme(TerminalTheme.Campbell(background, foreground, selection));
        pty.Emit("\u001b[33mжёлтый");

        Assert.True(SpinWait.SpinUntil(() => session.Terminal.Buffer.X > 0, TimeSpan.FromSeconds(5)), "вывод не дошёл");

        var cell = session.Terminal.Buffer.Lines[session.Terminal.Buffer.YBase]![0];
        var (ink, under) = TerminalTheme.Resolve(cell.Attributes, session.Terminal.Colors, false);
        var drawn = TerminalTheme.ToColor(ink);

        Assert.True(TerminalTheme.Contrast(ink, under) >= TerminalTheme.MinimumContrast,
            $"жёлтый на светлом фоне — {TerminalTheme.Contrast(ink, under):0.00}:1");
        Assert.True(drawn.R > drawn.B && drawn.G > drawn.B, $"жёлтый стал не жёлтым: #{ink:X6}");

        // Выделенный — на подложке выделения: читаемым он обязан быть на ней, а не на своём фоне.
        var chosen = Rgb(selection);
        var (inked, _) = TerminalTheme.Resolve(cell.Attributes, session.Terminal.Colors, false, chosen);

        Assert.True(TerminalTheme.Contrast(inked, chosen) >= TerminalTheme.MinimumContrast,
            $"выделенный жёлтый — {TerminalTheme.Contrast(inked, chosen):0.00}:1");
    }

    /// <summary>Фон, текст и выделение терминала — те, что вид берёт у темы.</summary>
    private static (Color Background, Color Foreground, Color Selection) Surface(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        return (Resource("AxSurfaceBaseColor", theme), Resource("AxTextPrimaryColor", theme), Resource("AxSelectionActiveColor", theme));
    }

    private static Color Resource(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryFindResource(key, theme, out var value), key);

        return (Color)value!;
    }

    private static int Rgb(Color color) => (color.R << 16) | (color.G << 8) | color.B;
}
