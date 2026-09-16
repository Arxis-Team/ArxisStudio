using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ArxisStudio.Sdk.Analyzers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
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
        Assert.Contains("AxFontSizeSmall (12) и AxFontSize (13)", drift.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Цвет темы, записанный числом, называет свою кисть — в любой записи.
    /// </summary>
    /// <remarks>
    /// Разметка принимает и строчные буквы, и короткую запись с прозрачностью;
    /// сверка по записи как она есть пропустила бы <c>#5a8ff3</c> мимо
    /// <c>#5A8FF3</c>.
    /// </remarks>
    [Theory]
    [InlineData("#5A8FF3")]
    [InlineData("#5a8ff3")]
    [InlineData("#FF5A8FF3")]
    public async Task A_theme_colour_written_as_a_number_names_its_brush(string colour)
    {
        var found = Assert.Single(await AnalyzeAsync($"""<TextBlock Foreground="{colour}"/>"""));

        Assert.Equal(ThemeValueAnalyzer.LiteralId, found.Id);
        Assert.Contains("AxAccentBrush", found.GetMessage(), StringComparison.Ordinal);
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

    /// <summary>
    /// Имя темы до SDK 6.0 называет роль, которая его заменила.
    /// </summary>
    /// <remarks>
    /// Значения в 6.0 не сдвинулись, сдвинулись имена, и разметка плагина, собранного под 5.x, в
    /// новой студии получила бы пустую кисть без единой ошибки. Цвет, названный там, где нужна
    /// кисть, называется кистью: иначе правка по совету принесла бы следующее замечание.
    /// </remarks>
    [Fact]
    public async Task A_name_from_before_six_names_the_role_that_replaced_it()
    {
        var found = await AnalyzeAsync(
            $"""
             <Panel>
               <Border Background="{"{"}DynamicResource AxBg1Brush{"}"}"/>
               <a:TextBlock xmlns:a="{Avalonia}" Foreground="{"{"}a:DynamicResource AxFg3Color{"}"}"/>
               <Border Background="{"{"}DynamicResource AxBg3Brush{"}"}"/>
               <Border Background="{"{"}DynamicResource AxBlue6{"}"}"/>
             </Panel>
             """);

        Assert.Equal(4, found.Length);
        Assert.All(found, notice => Assert.Equal(ThemeValueAnalyzer.FamilyId, notice.Id));
        Assert.Contains("теперь это AxSurfaceBaseBrush", Message(found, line: 1), StringComparison.Ordinal);
        Assert.Contains("теперь это AxTextTertiaryBrush", Message(found, line: 2), StringComparison.Ordinal);
        Assert.Contains("AxHoverBrush или AxSurfaceRaisedBrush", Message(found, line: 3), StringComparison.Ordinal);
        Assert.Contains("AxBlue6 — ступень шкалы палитры", Message(found, line: 4), StringComparison.Ordinal);
    }

    /// <summary>
    /// Прежнее имя замечено и в коде: строка с ключом ломается так же молча, как ссылка в разметке.
    /// </summary>
    [Fact]
    public async Task A_name_from_before_six_is_noticed_in_code()
    {
        var found = Assert.Single(await AnalyzeAsync(
            "<Panel/>",
            code: """public sealed class Probe { public object Key => "AxSelBrush"; public string Name => "AxSelection"; }"""));

        Assert.Equal(ThemeValueAnalyzer.FamilyId, found.Id);
        Assert.Contains("теперь это AxSelectionActiveBrush", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Каждое имя палитры 5.x либо осталось в теме, либо правило называет ему замену, и замена в теме есть.
    /// </summary>
    /// <remarks>
    /// Таблица прежних имён в анализаторе записана руками, и забытое в ней имя молчало бы ровно так,
    /// как молчала бы тема без правила. Имена перечислены здесь такими, какими палитра была до
    /// переименования, и обе стороны сверяются с живой темой, а не друг с другом: имя, которое в
    /// теме есть, замечать нельзя, а замена, которой в теме нет, — совет в пустоту.
    /// </remarks>
    [AvaloniaFact]
    public async Task Every_name_of_the_five_palette_resolves_or_is_named_with_a_real_role()
    {
        string[] names =
        [
            .. FivePalette.SelectMany(name => new[] { name + "Color", name + "Brush" }),
            "AxPopupShadow", "AxModalShadow", "AxGray1", "AxBlue13", "AxTeal7",
        ];

        var found = await AnalyzeAsync(
            "<Panel>\n" + string.Join("\n", names.Select(name => $"<Border Tag=\"{{DynamicResource {name}}}\"/>")) + "\n</Panel>");
        var application = Application.Current!;
        var wrong = new List<string>();

        for (var line = 1; line <= names.Length; line++)
        {
            var name = names[line - 1];
            var notice = found.SingleOrDefault(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line == line);

            if (Resolves(application, name) == notice is not null)
                wrong.Add(notice is null ? $"{name}: в теме нет, а правило промолчало" : $"{name}: в теме есть, а правило заметило");
            else if (notice is not null)
                wrong.AddRange(Roles.Matches(notice.GetMessage())
                    .Select(match => match.Value)
                    .Where(role => role != name && !Resolves(application, role))
                    .Select(role => $"{name}: совет называет {role}, которого в теме нет"));
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// Цвет там, где нужна кисть, замечен; там, где нужен цвет, — нет.
    /// </summary>
    /// <remarks>
    /// Ссылка на <c>AxAccentColor</c> из <c>Foreground</c> не разрешится в кисть и
    /// не нарисует ничего, и узнать об этом без правила можно только глазами.
    /// А <c>SolidColorBrush.Color</c> цвета и ждёт.
    /// </remarks>
    [Fact]
    public async Task A_colour_where_a_brush_is_needed_is_noticed()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """
            <Panel>
              <TextBlock Foreground="{DynamicResource AxAccentColor}"/>
              <SolidColorBrush Color="{DynamicResource AxAccentColor}"/>
            </Panel>
            """));

        Assert.Equal(ThemeValueAnalyzer.FamilyId, found.Id);
        Assert.Contains("нужна кисть: AxAccentBrush", found.GetMessage(), StringComparison.Ordinal);
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
              <TextBlock Foreground="{DynamicResource AxAccentBrush}" FontSize="{DynamicResource AxFontSize}"/>
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

    /// <summary>Палитра 5.x без суффиксов: у каждого имени были цвет и кисть.</summary>
    private static readonly string[] FivePalette =
    [
        "AxBgSunken", "AxBg1", "AxBg2", "AxBg3", "AxBg4", "AxBrd", "AxBrd2",
        "AxFg", "AxFg2", "AxFg3", "AxFgDisabled", "AxOnAcc",
        "AxAcc", "AxAccHover", "AxAccPressed", "AxAccStrong", "AxAccStrongHover",
        "AxSel", "AxSelInactive", "AxCanvas", "AxDot", "AxInp", "AxInpDisabled",
        "AxGrn", "AxRed", "AxYel", "AxOrg", "AxPur", "AxGreenText", "AxRedText", "AxYellowText",
        "AxLink", "AxLinkHover", "AxLinkVisited", "AxLinkOn",
        "AxOutlineFocused", "AxOutlineError", "AxOutlineWarning",
        "AxInfoBackground", "AxSuccessBackground", "AxWarningBackground", "AxErrorBackground",
        "AxInfoBorder", "AxSuccessBorder", "AxWarningBorder", "AxErrorBorder",
        "AxMonogramOrange", "AxMonogramGreen", "AxMonogramPurple", "AxMonogramRed",
        "AxScrollThumb", "AxScrollThumbHover", "AxTooltipBackground", "AxTooltipBorder",
        "AxCodeFg", "AxCodeTag", "AxCodeAttr", "AxCodeString", "AxCodeComment",
        "AxShadow", "AxAbShadow",
    ];

    private static readonly Regex Roles = new(@"\bAx[A-Z]\w*", RegexOptions.Compiled);

    private static bool Resolves(Application application, string key) =>
        application.TryFindResource(key, ThemeVariant.Dark, out _) && application.TryFindResource(key, ThemeVariant.Light, out _);

    private static string Message(ImmutableArray<Diagnostic> found, int line) =>
        Assert.Single(found, diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line == line).GetMessage();

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string markup, string path = "C:/probe/View.axaml", string code = "public sealed class Probe { }")
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(code)],
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
