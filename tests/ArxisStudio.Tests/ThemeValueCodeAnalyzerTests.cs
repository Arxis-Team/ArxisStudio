using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Sdk.Analyzers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «значения темы — по имени» в коде расширения.
/// </summary>
/// <remarks>
/// Близнец правила разметки, и заведён по уроку пары ARX0001 и ARX0006: правило,
/// живущее в одном месте, обходится сменой места записи. Решение у них общее, и
/// здесь проверяется не оно, а то, какие записи кода правило узнаёт.
/// </remarks>
public class ThemeValueCodeAnalyzerTests
{
    /// <summary>Отступ, собранный из чисел, называет ключ своей формы.</summary>
    [Fact]
    public async Task A_thickness_made_of_numbers_names_its_key()
    {
        var uniform = Assert.Single(await AnalyzeAsync("var gap = new Avalonia.Thickness(8);"));
        var shaped = Assert.Single(await AnalyzeAsync("var gap = new Avalonia.Thickness(0, 0, 6, 0);"));

        Assert.Equal(ThemeValueCodeAnalyzer.DiagnosticId, uniform.Id);
        Assert.Contains("AxSpaceThickness", uniform.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("AxSpaceSnugTrailingThickness", shaped.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Число в <c>Spacing</c> и <c>FontSize</c> контрола замечено.</summary>
    [Fact]
    public async Task A_number_given_to_spacing_or_font_size_is_noticed()
    {
        var found = await AnalyzeAsync(
            """
            var panel = new Avalonia.Controls.StackPanel { Spacing = 10 };
            var text = new Avalonia.Controls.TextBlock();
            text.FontSize = 12.5;
            """);

        Assert.Equal(2, found.Length);
        Assert.Contains(found, notice => notice.GetMessage().Contains("AxSpace (8) и AxSpaceWide (12)", StringComparison.Ordinal));
        Assert.Contains(found, notice => notice.GetMessage().Contains("AxFontSizeSmall (11.5) и AxFontSize (13)", StringComparison.Ordinal));
    }

    /// <summary>Цвет темы, разобранный из строки, называет свою кисть.</summary>
    [Fact]
    public async Task A_theme_colour_parsed_from_a_string_names_its_brush()
    {
        var found = await AnalyzeAsync(
            """
            var colour = Avalonia.Media.Color.Parse("#5A8FF3");
            var brush = Avalonia.Media.Brush.Parse("#5a8ff3");
            """);

        Assert.Equal(2, found.Length);
        Assert.All(found, notice => Assert.Contains("AxAccentBrush", notice.GetMessage(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Значение, вычисленное на ходу, правило не касается.
    /// </summary>
    /// <remarks>
    /// Кегль из настройки терминала или отступ из поля — не число, записанное
    /// руками, а решение человека, и назвать вместо него ключ темы было бы
    /// ошибкой правила.
    /// </remarks>
    [Fact]
    public async Task A_value_computed_at_run_time_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            double gap = System.Environment.ProcessorCount;
            string text = System.Environment.MachineName;
            var margin = new Avalonia.Thickness(gap);
            var panel = new Avalonia.Controls.StackPanel { Spacing = gap };
            var colour = Avalonia.Media.Color.Parse(text);
            """));
    }

    /// <summary>
    /// Своё свойство расширения с тем же именем — не свойство контрола.
    /// </summary>
    [Fact]
    public async Task A_property_of_the_extensions_own_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(
            "var settings = new Settings { FontSize = 13, Spacing = 8 };",
            "public sealed class Settings { public double FontSize { get; set; } public double Spacing { get; set; } }"));
    }

    /// <summary>
    /// Цвет, собранный из байтов, и поправка на пиксель правило не касаются.
    /// </summary>
    /// <remarks>
    /// Из байтов строятся палитры со своей жизнью: схема терминала, запасные цвета
    /// на случай темы без ключа. По одному значению не отличить запасной цвет,
    /// нарочно равный теме, от темы, переписанной числом, — и правило молчит,
    /// а не гадает.
    /// </remarks>
    [Fact]
    public async Task Bytes_and_pixel_nudges_are_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            var fallback = Avalonia.Media.Color.FromRgb(0x1E, 0x1F, 0x22);
            var nudge = new Avalonia.Thickness(0, 1, 0, 0);
            """));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string statements, string types = "")
    {
        // Сборки грузятся лениво, а ссылки собираются по загруженным: без касания
        // типов Avalonia в списке может не оказаться вовсе, и семантической модели
        // нечего будет разрешать.
        Assembly[] anchors =
        [
            typeof(Thickness).Assembly,
            typeof(StackPanel).Assembly,
            typeof(Color).Assembly,
        ];

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(anchors)
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var source = $$"""
            public static class Probe
            {
                public static void Run()
                {
                    {{statements}}
                }
            }

            {{types}}
            """;

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzed = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ThemeValueCodeAnalyzer()));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
