using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «значения темы — по имени» в разметке расширения.
/// </summary>
/// <remarks>
/// Проверяется не только то, что правило замечает число, но и то, что оно
/// говорит: замечание, называющее ключ, стоит одной правки, а «числа нельзя»
/// стоит поиска по словарям темы, которых у автора плагина под рукой нет.
/// </remarks>
public class ThemeValueAnalyzerTests
{
    private const string Studio = "https://github.com/Arxis-Team/ArxisStudio";
    private const string Avalonia = "https://github.com/avaloniaui";

    /// <summary>Отступ числом называет свою ступень.</summary>
    [Fact]
    public async Task A_gap_written_as_a_number_names_its_step()
    {
        var found = Assert.Single(await AnalyzeAsync("""<StackPanel Spacing="8"/>"""));

        Assert.Equal(ThemeValueAnalyzer.LiteralId, found.Id);
        Assert.Contains("8 — это AxSpace", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Направленный отступ называет ключ ровно своей формы.</summary>
    [Fact]
    public async Task A_shaped_gap_names_the_key_of_that_shape()
    {
        var found = Assert.Single(await AnalyzeAsync("""<Border Margin="0,0,6,0"/>"""));

        Assert.Contains("AxSpaceSnugTrailingThickness", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Отступ мимо шкалы называет ближайшие ступени.
    /// </summary>
    /// <remarks>
    /// Именно такой отступ и есть дрейф, с которым боролась миграция: десятка
    /// между шестёркой и двенадцатью. Промолчи правило о числе, у которого нет
    /// ключа, — оно пропустило бы как раз то, ради чего заведено.
    /// </remarks>
    [Fact]
    public async Task An_off_scale_gap_names_the_nearest_steps()
    {
        var found = Assert.Single(await AnalyzeAsync("""<StackPanel Spacing="10"/>"""));

        Assert.Contains("AxSpace (8) и AxSpaceWide (12)", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Поправка на пиксель — не зазор.
    /// </summary>
    /// <remarks>
    /// Единица под иконкой, сажающая её на строку текста, законна и в самой
    /// студии. Ступени меньше двух нет, назвать нечего, а под
    /// <c>TreatWarningsAsErrors</c> придирка стоила бы плагину сборки.
    /// </remarks>
    [Fact]
    public async Task A_pixel_nudge_is_not_a_gap()
    {
        Assert.Empty(await AnalyzeAsync("""<Panel><Border Margin="0,1,0,0"/><Border Margin="-3"/><Border Padding="0"/></Panel>"""));
    }

    /// <summary>Кегль числом называет кегль темы или ближайшие.</summary>
    [Fact]
    public async Task A_font_size_written_as_a_number_names_the_size()
    {
        var exact = Assert.Single(await AnalyzeAsync("""<TextBlock FontSize="13"/>"""));
        var drift = Assert.Single(await AnalyzeAsync("""<TextBlock FontSize="12.5"/>"""));

        Assert.Contains("13 — это AxFontSize", exact.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("AxFontSizeSmall (11.5) и AxFontSize (13)", drift.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Цвет темы, записанный числом, называет свою кисть — в любой записи.
    /// </summary>
    /// <remarks>
    /// Разметка принимает и строчные буквы, и короткую запись с прозрачностью;
    /// сверка по записи как она есть пропустила бы <c>#3574f0</c> мимо
    /// <c>#3574F0</c>.
    /// </remarks>
    [Theory]
    [InlineData("#3574F0")]
    [InlineData("#3574f0")]
    [InlineData("#FF3574F0")]
    public async Task A_theme_colour_written_as_a_number_names_its_brush(string colour)
    {
        var found = Assert.Single(await AnalyzeAsync($"""<TextBlock Foreground="{colour}"/>"""));

        Assert.Equal(ThemeValueAnalyzer.LiteralId, found.Id);
        Assert.Contains("AxAccBrush", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Цвета, которого в теме нет, правило не касается.</summary>
    /// <remarks>
    /// Своя палитра графика или схема терминала законны. Правилом цвет
    /// становится, когда совпал с цветом темы: значит, имелся в виду он.
    /// </remarks>
    [Fact]
    public async Task A_colour_the_theme_does_not_have_is_the_plugins_business()
    {
        Assert.Empty(await AnalyzeAsync("""<Border Background="#123456"/>"""));
    }

    /// <summary>Сеттер читается так же, как атрибут.</summary>
    /// <remarks>
    /// Иначе правило обходилось бы переносом числа в стиль — ровно так, как
    /// храповик отступов в теме однажды слеп на форме сеттера.
    /// </remarks>
    [Fact]
    public async Task A_setter_is_read_like_an_attribute()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """<Style Selector="Border"><Setter Property="Padding" Value="8"/></Style>"""));

        Assert.Contains("AxSpaceThickness", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Ступень шкалы палитры — внутренность темы.</summary>
    [Fact]
    public async Task A_scale_step_is_internal_to_the_theme()
    {
        var found = Assert.Single(await AnalyzeAsync(
            $"""<a:TextBlock xmlns:a="{Avalonia}" Foreground="{"{"}a:DynamicResource AxBlue6{"}"}"/>"""));

        Assert.Equal(ThemeValueAnalyzer.FamilyId, found.Id);
        Assert.Contains("AxBlue6 — ступень шкалы палитры", found.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("AxAccBrush", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Цвет там, где нужна кисть, замечен; там, где нужен цвет, — нет.
    /// </summary>
    /// <remarks>
    /// Ссылка на <c>AxAccColor</c> из <c>Foreground</c> не разрешится в кисть и
    /// не нарисует ничего, и узнать об этом без правила можно только глазами.
    /// А <c>SolidColorBrush.Color</c> цвета и ждёт.
    /// </remarks>
    [Fact]
    public async Task A_colour_where_a_brush_is_needed_is_noticed()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """
            <Panel>
              <TextBlock Foreground="{DynamicResource AxAccColor}"/>
              <SolidColorBrush Color="{DynamicResource AxAccColor}"/>
            </Panel>
            """));

        Assert.Equal(ThemeValueAnalyzer.FamilyId, found.Id);
        Assert.Contains("нужна кисть: AxAccBrush", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Чего правило не спрашивает: размеры канвы и правильно названные ресурсы.
    /// </summary>
    /// <remarks>
    /// Высота 24 совпадает с высотой строки, но картинка в 24 пикселя строкой не
    /// становится: правило приняло бы случайность за намерение.
    /// </remarks>
    [Fact]
    public async Task Sizes_of_the_plugins_own_and_named_resources_are_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            <StackPanel Spacing="{DynamicResource AxSpace}" Width="137" Height="24">
              <TextBlock Foreground="{DynamicResource AxAccBrush}" FontSize="{DynamicResource AxFontSize}"/>
            </StackPanel>
            """));
    }

    /// <summary>
    /// Таблица ключей — это нынешняя тема, а не её старая копия.
    /// </summary>
    /// <remarks>
    /// Ключ <c>AxSpaceHairAboveThickness</c> появился в теме этой же программой.
    /// Правило, называющее его, читает словарь, вшитый при сборке, а не список,
    /// переписанный однажды руками.
    /// </remarks>
    [Fact]
    public async Task The_table_is_the_current_theme()
    {
        var found = Assert.Single(await AnalyzeAsync("""<TextBlock Margin="0,2,0,0"/>"""));

        Assert.Contains("AxSpaceHairAboveThickness", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Замечание стоит на строке атрибута.</summary>
    [Fact]
    public async Task The_notice_stands_on_the_line_of_the_attribute()
    {
        var found = Assert.Single(await AnalyzeAsync(
            $"""
             <AxUserControl xmlns="{Studio}">
               <StackPanel
                   Spacing="8"/>
             </AxUserControl>
             """));

        Assert.Equal(2, found.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>Файл не из разметки и недописанная разметка правило не роняют.</summary>
    [Fact]
    public async Task Only_whole_markup_is_read()
    {
        Assert.Empty(await AnalyzeAsync("""<StackPanel Spacing="8"/>""", path: "C:/probe/plugin.json"));
        Assert.Empty(await AnalyzeAsync("""<StackPanel Spacing="8" """));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string markup, string path = "C:/probe/View.axaml")
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText("public sealed class Probe { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ThemeValueAnalyzer()),
            new AnalyzerOptions([new Given(path, markup)]));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Файл, переданный анализатору входом сборки.</summary>
    private sealed class Given(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }
}
