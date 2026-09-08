using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

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
/// Разбор регулярным выражением, а не JSON-разбором: анализатор живёт по
/// правилам Roslyn, на <c>netstandard2.0</c> и без своих зависимостей, — той
/// же дорогой, что и <c>ARX0002</c> рядом.
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

    private static readonly string[] Manifests = { "plugin.json", "module.json" };

    private static readonly Regex Section = new(@"""tags""\s*:\s*\[(?<body>[^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex Item = new(@"""(?<tag>[^""]*)""", RegexOptions.Compiled);

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

        if (!IsManifest(manifest.Path))
        {
            return;
        }

        var text = manifest.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        var source = text.ToString();
        var section = Section.Match(source);

        if (!section.Success)
        {
            return;
        }

        var body = section.Groups["body"];
        var seen = new HashSet<string>();
        var counted = 0;

        foreach (Match item in Item.Matches(body.Value))
        {
            var tag = item.Groups["tag"].Value;
            var span = new TextSpan(body.Index + item.Groups["tag"].Index, item.Groups["tag"].Length);

            counted++;

            foreach (var complaint in Complaints(tag, seen, counted))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    Location.Create(manifest.Path, span, text.Lines.GetLinePositionSpan(span)),
                    tag,
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

    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Манифест ли это — по имени файла.</summary>
    private static bool IsManifest(string path)
    {
        var name = FileName(path);

        foreach (var manifest in Manifests)
        {
            if (string.Equals(name, manifest, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FileName(string path)
    {
        var separator = path.LastIndexOfAny(Separators);

        return separator < 0 ? path : path.Substring(separator + 1);
    }
}
