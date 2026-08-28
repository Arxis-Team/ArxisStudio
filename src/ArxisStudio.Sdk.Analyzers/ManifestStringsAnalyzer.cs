using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы <c>%ключ%</c> из манифеста нашёлся в словаре плагина.
/// </summary>
/// <remarks>
/// Ненайденный ключ студия показывает как <c>!ключ!</c> — пропуск виден, но
/// увидит его человек, а не автор, и не при сборке, а в чужой уже студии.
/// Опечатку в ключе дешевле поймать здесь.
/// <para>
/// Сверяется словарь по умолчанию (<c>lang/strings.json</c>), а не переводы:
/// перевод отсутствует у любого языка, на который плагин ещё не переведён, и
/// требовать полноты от каждого файла значило бы запретить переводить по частям.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManifestStringsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0002";

    private const string DefaultDictionary = "strings.json";

    private static readonly Regex Keys = new(@"%([A-Za-z0-9._-]+)%", RegexOptions.Compiled);
    private static readonly Regex Declared = new(@"""([^""]+)""\s*:", RegexOptions.Compiled);

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Ключ манифеста не найден в словаре плагина",
        "{0}: такого ключа нет в lang/strings.json — студия покажет !{0}!",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Текст, который студия показывает за плагин — заголовок панели, пункт меню, подпись настройки, — " +
                     "берётся из его словарей. Ключа нет в словаре по умолчанию — человек увидит !ключ! вместо текста.");

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

        // Проверка не про код, а про файл рядом с ним: манифест приходит
        // отдельным входом сборки, и разбирать его надо на нём самом, а не на
        // каждом синтаксическом узле и не в конце компиляции — иначе среда
        // покажет находку только после полного разбора решения.
        context.RegisterAdditionalFileAction(Check);
    }

    private static void Check(AdditionalFileAnalysisContext context)
    {
        var manifest = context.AdditionalFile;

        if (!string.Equals(FileName(manifest.Path), "plugin.json", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = manifest.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        var known = Known(Find(context.Options.AdditionalFiles, DefaultDictionary), context);
        var source = text.ToString();

        foreach (Match match in Keys.Matches(source))
        {
            var key = match.Groups[1].Value;

            if (known.Contains(key))
            {
                continue;
            }

            var span = new TextSpan(match.Index, match.Length);

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                Location.Create(manifest.Path, span, text.Lines.GetLinePositionSpan(span)),
                key));
        }
    }

    private static readonly char[] Separators = { '/', '\\' };

    private static AdditionalText? Find(ImmutableArray<AdditionalText> files, string name) =>
        files.FirstOrDefault(file => string.Equals(FileName(file.Path), name, System.StringComparison.OrdinalIgnoreCase));

    private static string FileName(string path)
    {
        var separator = path.LastIndexOfAny(Separators);

        return separator < 0 ? path : path.Substring(separator + 1);
    }

    private static HashSet<string> Known(AdditionalText? dictionary, AdditionalFileAnalysisContext context)
    {
        var known = new HashSet<string>(System.StringComparer.Ordinal);

        if (dictionary?.GetText(context.CancellationToken) is not { } text)
        {
            return known;
        }

        // Словарь плоский: имя свойства — ключ, значение — строка. Разбирать
        // JSON целиком анализатору нечем, да и незачем: нужен только список
        // имён, а вложенности в этом файле не бывает.
        foreach (Match match in Declared.Matches(text.ToString()))
        {
            known.Add(match.Groups[1].Value);
        }

        return known;
    }
}
