using System.Collections.Immutable;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk.Analyzers;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Кнопка со значком называет себя и в самой студии.
/// </summary>
/// <remarks>
/// Расширениям это правило говорит при сборке — <c>ARX0013</c> едет с анализаторами SDK. Студия
/// анализаторов не подключает: у неё свои права на голые виджеты Avalonia, и включить один
/// анализатор из дюжины нельзя. Поэтому то же правило применяется здесь — тем же анализатором, а
/// не переписанным: разойдясь, они означали бы, что у студии правило мягче, чем у плагина.
/// </remarks>
public class IconNameTests
{
    /// <summary>Ни одной безымянной кнопки со значком в разметке студии.</summary>
    [Fact]
    public async Task No_icon_button_in_studio_markup_is_nameless()
    {
        var files = MarkupSources.Own()
            .Select(source => (AdditionalText)new AdditionalFile("C:/studio/" + source.Name, source.Text))
            .ToArray();

        Assert.NotEmpty(files);

        var found = await AnalyzeAsync([AnalyzerRun.Tree(AnalyzerRun.EmptyProbe)], files);

        Assert.True(
            found.IsEmpty,
            "кнопка со значком без имени: " + string.Join(", ", found.Select(Where)));
    }

    /// <summary>
    /// Ни одной безымянной кнопки со значком в коде студии.
    /// </summary>
    /// <remarks>
    /// Код студии разбирается одной компиляцией, а сборка самой студии из ссылок выброшена: её
    /// типы иначе объявлены дважды — исходниками и сборкой, — и разрешать имена стало бы не из
    /// чего. Ошибки компиляции анализатору не мешают: он читает то, что разобралось.
    /// </remarks>
    [Fact]
    public async Task No_icon_button_in_studio_code_is_nameless()
    {
        var sources = CodeSources.All().ToList();

        Assert.NotEmpty(sources);

        var found = await AnalyzeAsync(sources.Select(source => AnalyzerRun.Tree(source.Text, source.Name)), []);

        Assert.True(
            found.IsEmpty,
            "кнопка со значком без имени: " + string.Join(", ", found.Select(Where)));
    }

    private static string Where(Diagnostic diagnostic) =>
        $"{diagnostic.Location.GetLineSpan().Path}:{diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}";

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(IEnumerable<SyntaxTree> code, AdditionalText[] files) =>
        AnalyzerRun.Probe(
                code,
                AnalyzerRun.References(
                    [typeof(AxButton), typeof(AxIcon), typeof(Button)],
                    keep: assembly => assembly.GetName().Name != "ArxisStudio"))
            .RunAsync(new IconNameAnalyzer(), files);
}
