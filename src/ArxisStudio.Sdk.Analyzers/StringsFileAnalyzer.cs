using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// ARX0012: словарь расширения студия не прочтёт.
/// </summary>
/// <remarks>
/// Словарь, который не разобрался, студия берёт пустым — отказа нет, есть
/// подписи, ставшие ключами: <c>!panel.main!</c> вместо «Панели». Студия скажет
/// об этом в журнал, но прочтёт его уже пользователь. Пропущенная запятая стоит
/// дешевле всего при сборке, и правило называет место, где разбор споткнулся.
/// <para>
/// Словарём правило считает только то, что сборка сама назвала словарём, —
/// метаданными <c>AxStrings</c> у входа сборки: <c>default</c> у словаря по
/// умолчанию, <c>translation</c> у перевода. Угадывать по расширению нельзя: у
/// проекта рядом с манифестом вполне может лежать чужой JSON, например настройки
/// другого анализатора, и назвать его испорченным словарём значило бы сорвать
/// сборку на пустом месте. Помечает их сборка сама: плагину — таргет SDK, модулю —
/// общий таргет каталога модулей.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringsFileAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0012";

    /// <summary>Под каким именем метаданные входа сборки видны анализатору.</summary>
    public const string RoleKey = "build_metadata.AdditionalFiles.AxStrings";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Словарь расширения студия не прочтёт",
        "Словарь не прочтётся: {0} — студия возьмёт его пустым, и его строки покажутся ключами",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Словарь студия читает как JSON-объект строк, с комментариями и висячими запятыми. Словарь, который " +
                     "так не читается, становится пустым: подписи расширения показываются как !ключ!, а студия говорит об " +
                     "этом только в журнал.");

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
        var file = context.AdditionalFile;

        if (ManifestFiles.Role(context.Options, file) is not (ManifestFiles.Default or ManifestFiles.Translation) ||
            file.GetText(context.CancellationToken) is not { } text ||
            StringsJson.Check(text.ToString()) is not { } problem)
        {
            return;
        }

        var span = new TextSpan(Math.Min(problem.At, text.Length), problem.At < text.Length ? 1 : 0);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            ManifestFiles.At(file.Path, text, span),
            problem.Reason));
    }
}
