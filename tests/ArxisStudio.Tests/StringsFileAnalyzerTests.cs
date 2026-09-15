using System.Collections.Concurrent;
using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using ArxisStudio.Shell.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «словарь расширения студия прочтёт».
/// </summary>
/// <remarks>
/// Словарь, который не разобрался, студия берёт пустым, и подписи расширения становятся ключами.
/// Отказа нет, и заметить порчу можно только глазами — уже у пользователя. Пропущенную запятую
/// дешевле всего найти при сборке.
/// </remarks>
public class StringsFileAnalyzerTests
{
    private static readonly string Slash = ((char)92).ToString();

    private static readonly string Newline = ((char)10).ToString();

    /// <summary>Пропущенная запятая — находка, и стоит она там, где разбор споткнулся.</summary>
    [Fact]
    public async Task A_missing_comma_is_reported_where_the_reading_stumbles()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            {
              "panel.main": "Панель"
              "panel.side": "Сбоку"
            }
            """));

        Assert.Equal(StringsFileAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("нет запятой", diagnostic.GetMessage(), StringComparison.Ordinal);

        var at = diagnostic.Location.GetLineSpan();

        Assert.EndsWith("strings.json", at.Path, StringComparison.Ordinal);
        Assert.Equal(2, at.StartLinePosition.Line);
        Assert.Equal(2, at.StartLinePosition.Character);
    }

    /// <summary>
    /// Перевод проверяется так же: испорченный, он молча оставляет язык без перевода.
    /// </summary>
    [Fact]
    public async Task A_translation_is_checked_too()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""{ "panel.main": Panel }""", role: "translation"));

        Assert.Contains("не разобралось", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON, который сборка словарём не назвала, правило не трогает.
    /// </summary>
    /// <remarks>
    /// Рядом с манифестом вполне может лежать настройка другого анализатора — объект объектов, а не
    /// строк. Назови правило его испорченным словарём, сборка сорвалась бы на пустом месте.
    /// </remarks>
    [Fact]
    public async Task A_json_the_build_did_not_name_a_dictionary_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""{ "settings": { "indentation": 4 } }""", role: null));
    }

    /// <summary>Словарь с комментариями и висячими запятыми студия прочтёт — и правило молчит.</summary>
    [Fact]
    public async Task A_dictionary_with_comments_and_trailing_commas_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              // Подписи панели.
              "panel.main": "Панель",
              /* "panel.old": "Старая", */
            }
            """));
    }

    /// <summary>
    /// Правило и студия согласны: что студия не прочла, то правило называет, и только это.
    /// </summary>
    /// <remarks>
    /// Разбор у правила свой, у студии — разборщик .NET, и разойтись им есть где: висячая запятая,
    /// <c>null</c> вместо строки, повтор ключа, экранирование, комментарий после словаря. Каждый
    /// образец читается студией и проверяется правилом, и ответы обязаны совпасть.
    /// </remarks>
    [Fact]
    public async Task The_rule_and_the_studio_agree_on_what_reads()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-strings-{Guid.NewGuid():N}");
        var told = new ConcurrentBag<string>();

        void Listen(object? sender, StringFileProblem problem) => told.Add(problem.Path);

        Directory.CreateDirectory(folder);
        StringFile.Unreadable += Listen;

        try
        {
            foreach (var (name, text) in Samples())
            {
                var path = Path.Combine(folder, name + ".json");

                File.WriteAllText(path, text);
                StringFile.Read(path);

                var studio = told.Contains(path);
                var rule = (await AnalyzeAsync(text)).Any(diagnostic => diagnostic.Id == StringsFileAnalyzer.DiagnosticId);

                Assert.True(
                    studio == rule,
                    $"{name}: студия — {(studio ? "не прочла" : "прочла")}, правило — {(rule ? "находка" : "молчит")}");
            }
        }
        finally
        {
            StringFile.Unreadable -= Listen;
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Образцы словарей: целые и испорченные.</summary>
    private static IEnumerable<(string Name, string Text)> Samples()
    {
        yield return ("plain", """{ "a": "A" }""");
        yield return ("empty-object", "{}");
        yield return ("comments-and-trailing-comma", "{ // строкой" + Newline + " \"a\": \"A\", /* блоком */ }");
        yield return ("null-value", """{ "a": null }""");
        yield return ("escapes", "{ \"a\": \"" + Slash + "\"" + Slash + Slash + Slash + "/" + Slash + "n" + Slash + "u00e9\" }");
        yield return ("repeated-key", """{ "a": "A", "a": "B" }""");
        yield return ("surrogate-pair", "{ \"a\": \"" + Slash + "uD83D" + Slash + "uDE00\" }");
        yield return ("comments-around", "// голова" + Newline + "{ \"a\": \"A\" }" + Newline + "/* хвост */");

        yield return ("nothing", string.Empty);
        yield return ("only-comment", "// пусто");
        yield return ("missing-comma", """{ "a": "A" "b": "B" }""");
        yield return ("unquoted-name", """{ a: "A" }""");
        yield return ("single-quotes", "{ 'a': \"A\" }");
        yield return ("number-value", """{ "a": 1 }""");
        yield return ("boolean-value", """{ "a": true }""");
        yield return ("nested-object", """{ "a": { "b": "c" } }""");
        yield return ("array-root", """[ "a" ]""");
        yield return ("null-root", "null");
        yield return ("text-after", """{ "a": "A" } x""");
        yield return ("slash-after", """{ "a": "A" } /x""");
        yield return ("open-string", """{ "a": "A""");
        yield return ("open-object", """{ "a": "A" """);
        yield return ("bad-escape", "{ \"a\": \"" + Slash + "q\" }");
        yield return ("short-unicode-escape", "{ \"a\": \"" + Slash + "u12\" }");
        yield return ("newline-in-string", "{ \"a\": \"line" + Newline + "break\" }");
        yield return ("missing-colon", """{ "a" "A" }""");
        yield return ("double-comma", """{ "a": "A",, "b": "B" }""");
        yield return ("leading-comma", """{ , "a": "A" }""");
        yield return ("open-comment", """{ /* "a": "A" }""");
        yield return ("broken-literal", """{ "a": nul }""");
        yield return ("lone-high-surrogate", "{ \"a\": \"" + Slash + "uD800\" }");
        yield return ("lone-low-surrogate", "{ \"a\": \"" + Slash + "uDE00\" }");
        yield return ("high-surrogate-before-a-letter", "{ \"a\": \"" + Slash + "uD83D" + Slash + "u0041\" }");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string dictionary, string? role = "default")
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText("public sealed class Probe { }", cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        const string path = "C:/probe/lang/strings.json";

        var roles = new Dictionary<string, string>(StringComparer.Ordinal);

        if (role is not null)
            roles[path] = role;

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new StringsFileAnalyzer()),
            new AnalyzerOptions([new Given(path, dictionary)], new StringsRoles(roles)));

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
