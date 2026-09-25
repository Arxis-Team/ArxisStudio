using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// ARX0007: тег манифеста написан не так, как студия его прочтёт.
/// </summary>
/// <remarks>
/// Тег — идентификатор, а не подпись: по нему ищут расширение и раскладывают
/// список по полкам. Студия чистит теги при чтении — нижний регистр, снятые
/// пробелы, без повторов, первые восемь, — и написанный иначе доедет другим
/// или не доедет вовсе. Автор об этом узнать обязан, и узнать при своей
/// сборке: в чужой студии его тег будет уже просто не тем.
/// <para>
/// Теги берутся у <see cref="ManifestJson"/> — там, где их читает студия: в голове
/// манифеста, <c>tags[N]</c>, и такими, какими она их прочтёт, с разобранным
/// экранированием. Прежде секция искалась регулярным выражением — первым
/// вхождением в тексте, вместе с комментариями: закомментированный старый список
/// судился вместо настоящего, поле с тем же именем глубже в манифесте — тоже, а
/// <c>\u0054ools</c> проходил без замечания о регистре.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManifestTagsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0007";

    /// <summary>Сколько тегов студия показывает.</summary>
    public const int Limit = 8;

    /// <summary>Насколько длинным тег быть перестаёт.</summary>
    public const int Longest = 24;

    /// <summary>Тег — строка в списке головы манифеста.</summary>
    private static readonly Regex Tag = new(@"^tags\[\d+\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Тег манифеста написан не так, как студия его прочтёт",
        "«{0}»: {1}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Тег — идентификатор для поиска и разбора по полкам, а не подпись. Студия приводит теги к нижнему " +
                     "регистру, снимает пробелы и повторы и берёт первые восемь: написанный иначе тег доедет другим.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            return;
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(Check);
    }

    private static void Check(AdditionalFileAnalysisContext context)
    {
        var manifest = context.AdditionalFile;

        if (!ManifestFiles.IsManifest(manifest.Path))
        {
            return;
        }

        var text = manifest.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        var seen = new HashSet<string>();
        var counted = 0;

        foreach (var field in ManifestJson.Strings(text.ToString()))
        {
            if (!Tag.IsMatch(field.Path))
            {
                continue;
            }

            counted++;

            foreach (var complaint in Complaints(field.Value, seen, counted))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    ManifestFiles.At(manifest.Path, text, field.Span),
                    field.Value,
                    complaint));
            }
        }
    }

    /// <summary>Что не так с этим тегом; пусто — всё так.</summary>
    private static IEnumerable<string> Complaints(string tag, HashSet<string> seen, int counted)
    {
        var trimmed = tag.Trim();

        if (trimmed.Length == 0)
        {
            yield return "тег пустой — студия его отбросит";
            yield break;
        }

        if (trimmed.Length != tag.Length)
        {
            yield return "по краям пробелы — студия их снимет";
        }

        if (trimmed != trimmed.ToLowerInvariant())
        {
            yield return "регистр верхний — студия прочтёт «" + trimmed.ToLowerInvariant() + "»";
        }

        if (trimmed.IndexOf(' ') >= 0)
        {
            yield return "внутри пробел — слова в теге разделяют дефисом: «"
                         + trimmed.ToLowerInvariant().Replace(' ', '-') + "»";
        }

        if (trimmed.Length > Longest)
        {
            yield return "длиннее " + Longest + " знаков — это уже не метка, а описание";
        }

        if (!seen.Add(trimmed.ToLowerInvariant()))
        {
            yield return "тег повторяется — студия оставит один";
        }

        if (counted > Limit)
        {
            yield return "тегов больше " + Limit + " — студия покажет первые " + Limit;
        }
    }
}
