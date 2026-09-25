using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ArxisStudio.Sdk.Analyzers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.CodeAnalysis;
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
        var place = found.Location.GetLineSpan();

        Assert.Equal(ThemeValueAnalyzer.LiteralId, found.Id);
        Assert.Contains("8 — это AxSpace", found.GetMessage(), StringComparison.Ordinal);

        // Отмечено имя атрибута: число правят там, где оно названо.
        Assert.Equal("Spacing".Length, place.EndLinePosition.Character - place.StartLinePosition.Character);
        Assert.Equal("Spacing".Length, found.Location.SourceSpan.Length);
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
            code: """public sealed class Probe { public object Key => "AxSelBrush"; public string Name => "AxSelectionActiveBrush"; }"""));

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
    /// Ключ с приставкой темы, которого в ней нет, — опечатка, и замечание называет похожий.
    /// </summary>
    /// <remarks>
    /// Такая ссылка не валит ни сборку, ни показ: кисть молча прозрачна, зазор — ноль. Похожим
    /// считается ключ в букву-две от названного и роль без окончания — там, где нужна кисть,
    /// называется кисть.
    /// </remarks>
    [Fact]
    public async Task A_misspelt_key_names_the_key_it_looks_like()
    {
        var found = await AnalyzeAsync(
            """
            <Panel>
              <Border Background="{DynamicResource AxSurfacePanelBrsh}"/>
              <TextBlock Foreground="{DynamicResource AxAccent}"/>
              <StackPanel Spacing="{DynamicResource AxGapFromRow}"/>
              <Border Tag="{DynamicResource AxNothingLikeIt}"/>
            </Panel>
            """);

        Assert.Equal(4, found.Length);
        Assert.All(found, notice => Assert.Equal(ThemeValueAnalyzer.FamilyId, notice.Id));
        Assert.Equal("AxSurfacePanelBrsh — такого ключа в теме нет; похоже на AxSurfacePanelBrush", Message(found, line: 1));
        Assert.Equal("AxAccent — такого ключа в теме нет; похоже на AxAccentBrush", Message(found, line: 2));
        Assert.Equal("AxGapFromRow — такого ключа в теме нет; похоже на AxGapFormRow", Message(found, line: 3));
        Assert.Equal("AxNothingLikeIt — такого ключа в теме нет", Message(found, line: 4));
    }

    /// <summary>Ключ, названный элементом или внутри привязки, спрашивается так же.</summary>
    [Fact]
    public async Task A_key_named_by_an_element_or_inside_a_binding_is_asked_too()
    {
        var found = await AnalyzeAsync(
            """
            <Panel>
              <StaticResource ResourceKey="AxAccentBrsh"/>
              <Border Padding="{Binding Gap, FallbackValue={StaticResource AxSpaceThicknes}}"/>
            </Panel>
            """);

        Assert.Equal(2, found.Length);
        Assert.EndsWith("похоже на AxAccentBrush", Message(found, line: 1), StringComparison.Ordinal);
        Assert.EndsWith("похоже на AxSpaceThickness", Message(found, line: 2), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ключ, который расширение объявило само — в любом своём файле, — и ключ не из пространства
    /// темы правило не трогает.
    /// </summary>
    /// <remarks>
    /// Словарь плагина с его кистями и экран, который их берёт, — разные файлы, и ссылка из одного
    /// на ключ другого законна. Ключ без приставки <c>Ax</c> — ресурс самого расширения: объявлен он
    /// может быть и в коде, и в чужой сборке, и спрашивать о нём правилу нечем.
    /// </remarks>
    [Fact]
    public async Task Keys_the_extension_declares_and_keys_outside_the_theme_are_left_alone()
    {
        var found = await AnalyzeFilesAsync(
        [
            ("C:/probe/View.axaml",
             """<Panel><Border Background="{DynamicResource AxChartLineBrush}"/><Border Background="{DynamicResource ChartGrid}"/></Panel>"""),
            ("C:/probe/Chart.axaml",
             """<ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><SolidColorBrush x:Key="AxChartLineBrush" Color="#123456"/></ResourceDictionary>"""),
        ]);

        Assert.Empty(found);
    }

    /// <summary>
    /// Незнакомый ключ замечен и в коде, в любой строке, — кроме куска строки, собранной на ходу.
    /// </summary>
    [Fact]
    public async Task A_misspelt_key_is_noticed_in_code_but_not_a_piece_of_a_built_one()
    {
        var found = Assert.Single(await AnalyzeAsync(
            "<Panel/>",
            code: """
                  public sealed class Probe
                  {
                      public object Gap => "AxGapFromRow";
                      public object Row => "AxGapFormRow";
                      public string Tint(string name) => $"AxTint{name}Brush";
                      public string Glued(string name) => "AxTint" + name + "Brush";
                      public string Word => "Axis";
                  }
                  """));

        Assert.Equal(ThemeValueAnalyzer.FamilyId, found.Id);
        Assert.Equal("AxGapFromRow — такого ключа в теме нет; похоже на AxGapFormRow", found.GetMessage());
    }

    /// <summary>
    /// Ключи всех словарей темы правилу знакомы — метрики, строки и именованные шаблоны, а не только
    /// палитра и шкалы, откуда правило берёт значения.
    /// </summary>
    [Fact]
    public async Task Every_dictionary_of_the_theme_is_known()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            <Panel>
              <Border Width="{DynamicResource AxTileGlyphSizeStep}" Height="{DynamicResource AxRowMarkerHeight}"/>
              <TextBlock Text="{DynamicResource AxTextBreadcrumbOverflow}"/>
              <Button Theme="{StaticResource AxInlineIconButton}"/>
            </Panel>
            """));
    }

    /// <summary>
    /// Каждый ключ, который называют разметка и код самой студии, модулей, плагинов и шаблона,
    /// правило знает.
    /// </summary>
    /// <remarks>
    /// Модули и плагины собираются с анализатором и промах уронили бы сами, а студия, оболочка,
    /// докинг, контролы и значки его не подключают — ключи же у них те же, темы. С живой темой их
    /// сверяют <see cref="MarkupResourceKeysTests"/> и <see cref="CodeResourceKeysTests"/>; здесь —
    /// что анализатор с живой темой согласен: словарь, забытый при вшивании, дал бы автору плагина
    /// замечание на честный ключ, и под <c>TreatWarningsAsErrors</c> — упавшую сборку.
    /// </remarks>
    [Fact]
    public async Task Every_key_the_studio_names_is_known()
    {
        var markup = MarkupSources.All().Select(source => ("C:/probe/" + source.Name, source.Text)).ToList();
        var named = CodeSources.Everywhere()
            .SelectMany(source => source.Text.Split('\n'))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(line => Keyed.Matches(line).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("AxScrollThumbColor", named);
        Assert.Contains(markup, source => source.Item1.EndsWith("ProjectPanelView.axaml", StringComparison.Ordinal));

        var code = "public static class Probe { public static readonly string[] Keys = { " +
                   string.Join(", ", named.Select(key => $"\"{key}\"")) + " }; }";
        var wrong = (await AnalyzeFilesAsync(markup, code))
            .Where(diagnostic => diagnostic.Id == ThemeValueAnalyzer.FamilyId)
            .Select(diagnostic => $"{diagnostic.Location.GetLineSpan().Path}: {diagnostic.GetMessage()}")
            .ToList();

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

    /// <summary>Ключ темы строкой: кавычка, приставка <c>Ax</c> и имя — как у <see cref="CodeResourceKeysTests"/>.</summary>
    private static readonly Regex Keyed = new(@"""(Ax[A-Z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    private static bool Resolves(Application application, string key) =>
        application.TryFindResource(key, ThemeVariant.Dark, out _) && application.TryFindResource(key, ThemeVariant.Light, out _);

    private static string Message(ImmutableArray<Diagnostic> found, int line) =>
        Assert.Single(found, diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line == line).GetMessage();

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string markup, string path = "C:/probe/View.axaml", string code = AnalyzerRun.EmptyProbe) =>
        AnalyzeFilesAsync([(path, markup)], code);

    private static Task<ImmutableArray<Diagnostic>> AnalyzeFilesAsync(
        IEnumerable<(string Path, string Text)> files, string code = AnalyzerRun.EmptyProbe) =>
        AnalyzerRun.Probe(code, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)])
            .RunAsync(new ThemeValueAnalyzer(), files.Select(file => (AdditionalText)new AdditionalFile(file.Path, file.Text)));
}
