using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы расширение не правило стили и ресурсы всего приложения.
/// </summary>
/// <remarks>
/// <c>Application.Current.Styles</c> и <c>Application.Current.Resources</c> — общее на процесс.
/// Стиль, добавленный туда расширением, перестилизует всё дерево студии и все панели соседей, а
/// ресурс с чужим ключом молча подменит значение темы. Хуже того, ни то, ни другое не уходит с
/// выгрузкой расширения: выключенный плагин продолжает красить окно, пока студию не перезапустят.
/// <para>
/// Своё оформление у расширения есть где объявить: словарь и стили самой панели, её
/// <c>Styles</c> и <c>Resources</c>, — они живут ровно столько, сколько живёт панель.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApplicationStylesAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0014";

    private const string Application = "Avalonia.Application";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Расширение правит стили или ресурсы всего приложения",
        "Application.{0} — общее на процесс: правка отсюда перекрасит всю студию и переживёт выгрузку расширения",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Стиль, добавленный в приложение, оценивается на каждом контроле студии и всех соседних панелей, " +
                     "а ресурс с чужим ключом подменяет значение темы. С выгрузкой расширения ни то, ни другое не " +
                     "уходит. Своё оформление объявляют у своей панели: её Styles и Resources живут столько же, сколько она.");

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

        context.RegisterCompilationStartAction(start =>
        {
            var application = start.Compilation.GetTypeByMetadataName(Application);

            // Проект без Avalonia анализировать не о чем.
            if (application is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Check(node, application), SyntaxKind.SimpleMemberAccessExpression);
        });
    }

    /// <summary>
    /// Обращение к стилям или ресурсам приложения.
    /// </summary>
    /// <remarks>
    /// Правило смотрит на само обращение, а не на добавление в список: до списка добраться можно
    /// многими дорогами — сохранить в переменную, передать в метод, — и перечислять их значило бы
    /// оставить дырой каждую неназванную. Читать эти списки расширению тоже незачем: то, что оно
    /// там найдёт, принадлежит студии и меняется без его ведома.
    /// </remarks>
    private static void Check(SyntaxNodeAnalysisContext context, INamedTypeSymbol application)
    {
        if (context.Node is not MemberAccessExpressionSyntax access ||
            access.Name.Identifier.ValueText is not ("Styles" or "Resources"))
        {
            return;
        }

        var owner = context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type;

        if (owner is null || !Derives(owner, application))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, access.GetLocation(), access.Name.Identifier.ValueText));
    }

    private static bool Derives(ITypeSymbol type, INamedTypeSymbol application)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, application))
            {
                return true;
            }
        }

        return false;
    }
}
