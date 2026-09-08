using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «тег манифеста написан так, как студия его прочтёт».
/// </summary>
/// <remarks>
/// Студия чистит теги при чтении и делает это молча: нижний регистр, снятые
/// пробелы, без повторов, первые восемь. Молчание тут правильное — человеку,
/// поставившему плагин, до чужого манифеста дела нет. А вот автору знать надо,
/// и знать при своей сборке: в чужой студии его тег будет уже просто не тем.
/// </remarks>
public class ManifestTagsAnalyzerTests
{
    /// <summary>Верхний регистр — тег доедет другим, и об этом говорят.</summary>
    [Fact]
    public async Task An_upper_case_tag_is_reported()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "id": "arxis.probe", "tags": [ "Tools" ] }
            """));

        Assert.Equal(ManifestTagsAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("tools", diagnostic.GetMessage(), StringComparison.Ordinal);

        // Место находки — сам манифест: править нужно там, а не в коде.
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Повтор после чистки — тоже находка.</summary>
    [Fact]
    public async Task A_repeated_tag_is_reported()
    {
        var found = await AnalyzeAsync("""
            { "id": "arxis.probe", "tags": [ "tools", " tools " ] }
            """);

        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("повторяется", StringComparison.Ordinal));
    }

    /// <summary>Пробел внутри тега: студия оставит его как есть, а искать станут через дефис.</summary>
    [Fact]
    public async Task A_tag_with_a_space_inside_is_reported()
    {
        var found = await AnalyzeAsync("""
            { "id": "arxis.probe", "tags": [ "code style" ] }
            """);

        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("code-style", StringComparison.Ordinal));
    }

    /// <summary>Девятый тег виден только в манифесте — студия его не покажет.</summary>
    [Fact]
    public async Task A_ninth_tag_is_reported()
    {
        var found = await AnalyzeAsync("""
            { "id": "arxis.probe",
              "tags": [ "a", "b", "c", "d", "e", "f", "g", "h", "i" ] }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Contains("«i»", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Чистые теги правило не трогает.</summary>
    [Fact]
    public async Task Tags_written_the_way_the_studio_reads_them_are_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""
            { "id": "arxis.probe", "tags": [ "tools", "terminal" ] }
            """));
    }

    /// <summary>Манифест без тегов правилу неинтересен.</summary>
    [Fact]
    public async Task A_manifest_without_tags_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""{ "id": "arxis.probe" }"""));
    }

    /// <summary>Манифест встроенного модуля проверяется той же дорогой.</summary>
    /// <remarks>
    /// Секция манифеста у модуля и у плагина одна, и правила у них одни: код,
    /// переносимый между режимами, не должен менять смысл при переносе.
    /// </remarks>
    [Fact]
    public async Task The_manifest_of_a_built_in_module_is_checked_too()
    {
        var found = await AnalyzeAsync(
            """{ "id": "arxis.probe", "tags": [ "Tools" ] }""",
            manifestName: "module.json");

        Assert.EndsWith("module.json", Assert.Single(found).Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string manifest, string manifestName = "plugin.json")
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText("public sealed class Probe { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ManifestTagsAnalyzer()),
            new AnalyzerOptions([new Given($"C:/probe/{manifestName}", manifest)]));

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
