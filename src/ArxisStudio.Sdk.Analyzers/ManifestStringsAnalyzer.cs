using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы <c>%ключ%</c> из манифеста нашёлся в словаре расширения.
/// </summary>
/// <remarks>
/// Ненайденный ключ студия показывает как <c>!ключ!</c> — пропуск виден, но
/// увидит его человек, а не автор, и не при сборке, а в чужой уже студии.
/// Опечатку в ключе дешевле поймать здесь.
/// <para>
/// Оба манифеста: <c>plugin.json</c> у внешнего плагина и <c>module.json</c> у
/// встроенного модуля. Правила у них одни, и проверяться они должны одинаково —
/// иначе код, переносимый между режимами, менял бы смысл при переносе. Прежде
/// проверка смотрела только на имя <c>plugin.json</c>, и у модулей ключ без
/// строки не ловился вовсе.
/// </para>
/// <para>
/// Словарём считается любой поданный сборкой файл, кроме самих манифестов: у
/// плагина это <c>lang/strings.json</c>, у модуля — словарь студии, её строки
/// он и показывает. Ни то, ни другое имя здесь не написано: расположение
/// словарей — дело того, кто их подаёт, а не проверки.
/// </para>
/// <para>
/// Сверяется словарь по умолчанию, а не переводы: перевод отсутствует у любого
/// языка, на который расширение ещё не переведено, и требовать полноты от
/// каждого файла значило бы запретить переводить по частям.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManifestStringsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0002";

    private static readonly string[] Manifests = { "plugin.json", "module.json" };

    private static readonly Regex Keys = new(@"%([A-Za-z0-9._-]+)%", RegexOptions.Compiled);
    private static readonly Regex Declared = new(@"""([^""]+)""\s*:", RegexOptions.Compiled);

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Ключ манифеста не найден в словаре расширения",
        "{0}: такого ключа нет в словаре — студия покажет !{0}!",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Текст, который студия показывает за расширение — заголовок панели, пункт меню, подпись настройки, — " +
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

        if (!IsManifest(manifest.Path))
        {
            return;
        }

        var text = manifest.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        var known = Known(context);
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

    /// <summary>
    /// Ключи, объявленные словарями сборки.
    /// </summary>
    /// <remarks>
    /// Словарём считается всё поданное, кроме самих манифестов: у плагина это
    /// <c>lang/strings.json</c>, у модуля — словарь студии. Имена здесь не
    /// написаны нарочно — где лежат словари, решает тот, кто их подаёт.
    /// </remarks>
    private static HashSet<string> Known(AdditionalFileAnalysisContext context)
    {
        var known = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var file in context.Options.AdditionalFiles)
        {
            if (IsManifest(file.Path) || file.GetText(context.CancellationToken) is not { } text)
            {
                continue;
            }

            // Словарь плоский: имя свойства — ключ, значение — строка. Разбирать
            // JSON целиком анализатору нечем, да и незачем: нужен только список
            // имён, а вложенности в этом файле не бывает.
            foreach (Match match in Declared.Matches(text.ToString()))
            {
                known.Add(match.Groups[1].Value);
            }
        }

        return known;
    }
}
