using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «ключ манифеста должен найтись в словаре плагина».
/// </summary>
/// <remarks>
/// Ненайденный ключ студия показывает как <c>!ключ!</c>. Пропуск при этом виден
/// — но человеку и в чужой уже студии; автору он должен быть виден при сборке,
/// пока опечатку исправить дешевле всего.
/// </remarks>
public class ManifestStringsAnalyzerTests
{
    private const string Manifest = """
        {
          "id": "arxis.probe",
          "name": "Проба",
          "contributions": {
            "toolWindows": [ { "id": "probe.panel", "title": "%panel.probe%", "zone": "left" } ]
          }
        }
        """;

    /// <summary>Ключа нет в словаре — о нём говорят при сборке.</summary>
    [Fact]
    public async Task A_key_missing_from_the_dictionary_is_reported()
    {
        var found = await AnalyzeAsync(Manifest, """{ "panel.other": "Другая" }""");

        var diagnostic = Assert.Single(found);

        Assert.Equal(ManifestStringsAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("panel.probe", diagnostic.GetMessage());

        // Место находки — сам манифест: править нужно там, а не в коде.
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Ключ на месте — правило молчит.</summary>
    [Fact]
    public async Task A_key_that_is_in_place_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(Manifest, """{ "panel.probe": "Проба" }"""));
    }

    /// <summary>
    /// Словаря нет вовсе, а ключи есть — это тот же пропуск.
    /// </summary>
    /// <remarks>
    /// Забытая папка <c>lang/</c> — обычная ошибка при первой локализации:
    /// строки в манифесте уже ключами, а взять их неоткуда.
    /// </remarks>
    [Fact]
    public async Task Keys_without_any_dictionary_are_reported()
    {
        Assert.Single(await AnalyzeAsync(Manifest, dictionary: null));
    }

    /// <summary>
    /// Манифест без ключей словаря не требует.
    /// </summary>
    /// <remarks>
    /// Локализация необязательна: плагин, написанный на один язык, пишет
    /// подписи прямо в манифест, и правило к нему отношения не имеет.
    /// </remarks>
    [Fact]
    public async Task A_manifest_without_keys_needs_no_dictionary()
    {
        var manifest = Manifest.Replace("%panel.probe%", "Проба", StringComparison.Ordinal);

        Assert.Empty(await AnalyzeAsync(manifest, dictionary: null));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string manifest, string? dictionary)
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

        var files = new List<AdditionalText> { new Given("C:/probe/plugin.json", manifest) };

        if (dictionary is not null)
            files.Add(new Given("C:/probe/lang/strings.json", dictionary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ManifestStringsAnalyzer()),
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
