using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk.Analyzers;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
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
            .Select(source => (AdditionalText)new Given("C:/studio/" + source.Name, source.Text))
            .ToArray();

        Assert.NotEmpty(files);

        var found = await AnalyzeAsync("public sealed class Probe { }", files);

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

        var found = await AnalyzeAsync(sources.Select(source => source.Text), [], sources.Select(source => source.Name));

        Assert.True(
            found.IsEmpty,
            "кнопка со значком без имени: " + string.Join(", ", found.Select(Where)));
    }

    private static string Where(Diagnostic diagnostic) =>
        $"{diagnostic.Location.GetLineSpan().Path}:{diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}";

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string code, AdditionalText[] files) =>
        AnalyzeAsync([code], files, null);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        IEnumerable<string> code, AdditionalText[] files, IEnumerable<string>? names)
    {
        Assembly[] anchors = [typeof(AxButton).Assembly, typeof(AxIcon).Assembly, typeof(Button).Assembly];

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(anchors)
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Where(assembly => assembly.GetName().Name != "ArxisStudio")
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        // Имя файла отдаётся дереву: без него замечание в коде не найти — путь в нём пуст.
        var titles = names?.ToList();
        var trees = code
            .Select((text, at) => CSharpSyntaxTree.ParseText(text, path: titles is null ? string.Empty : titles[at]))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new IconNameAnalyzer()),
            new AnalyzerOptions([.. files]));

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
