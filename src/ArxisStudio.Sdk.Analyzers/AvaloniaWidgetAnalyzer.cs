using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы интерфейс плагина строился на контролах студии.
/// </summary>
/// <remarks>
/// Панели студии и панели плагинов стоят в одном окне, и виджет Avalonia
/// приносит с собой чужую тему — разнобой видно сразу. Поэтому виджет из
/// Avalonia в коде плагина — повод сказать об этом при сборке, а не при первом
/// взгляде на готовую панель.
/// <para>
/// Черта проведена по <c>TemplatedControl</c>: контрол с шаблоном приходит со
/// своим оформлением, а панель раскладки, рамка, текст, картинка и фигуры
/// оформления не несут и разрешены. Контролы <c>Ax*</c> — тоже наследники
/// <c>TemplatedControl</c>, но живут в библиотеке студии, и правило их не
/// касается.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvaloniaWidgetAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0001";

    /// <summary>
    /// Сборки, чьи контролы считаются контролами студии.
    /// </summary>
    /// <remarks>
    /// Их две: виджеты и набор иконок. <c>AxIcon</c> живёт отдельной
    /// библиотекой, но остаётся контролом студии: наследника от него
    /// правило трогать не должно по той же причине, что и наследника <c>AxButton</c>.
    /// </remarks>
    private static readonly string[] StudioAssemblies = ["ArxisStudio.Controls", "ArxisStudio.Icons"];
    private const string TemplatedControl = "Avalonia.Controls.Primitives.TemplatedControl";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Виджет Avalonia в интерфейсе плагина",
        "{0} — виджет Avalonia; интерфейс плагина строится на контролах ArxisStudio.Controls",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Панели плагина и панели студии стоят рядом, и виджет со своей темой выбивается из общего вида. " +
                     "Панели раскладки, рамки, текст и фигуры разрешены — правило касается контролов с шаблоном.");

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
            var templated = start.Compilation.GetTypeByMetadataName(TemplatedControl);

            // Проект без Avalonia анализировать не о чем.
            if (templated is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Check(node, templated), SyntaxKind.ObjectCreationExpression);
            start.RegisterSyntaxNodeAction(node => Check(node, templated), SyntaxKind.ImplicitObjectCreationExpression);
        });
    }

    private static void Check(SyntaxNodeAnalysisContext context, INamedTypeSymbol templated)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IMethodSymbol constructor)
        {
            return;
        }

        var type = constructor.ContainingType;

        if (!IsForbidden(type, templated))
        {
            return;
        }

        var location = context.Node is BaseObjectCreationExpressionSyntax creation
            ? (creation as ObjectCreationExpressionSyntax)?.Type.GetLocation() ?? creation.GetLocation()
            : context.Node.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
    }

    /// <summary>
    /// Виджет ли это, которого в плагине быть не должно.
    /// </summary>
    /// <remarks>
    /// Наследник контрола студии разрешён, каким бы глубоким он ни был: своя
    /// кнопка поверх <c>AxButton</c> — это по-прежнему контрол студии.
    /// </remarks>
    private static bool IsForbidden(INamedTypeSymbol type, INamedTypeSymbol templated)
    {
        var isTemplated = false;

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, templated))
            {
                isTemplated = true;
                break;
            }

            // Дошли до контрола студии раньше, чем до шаблонного контрола
            // Avalonia, — значит это контрол студии.
            if (IsStudioControl(current))
            {
                return false;
            }
        }

        return isTemplated && IsAvalonia(type);
    }

    private static bool IsStudioControl(ISymbol type) =>
        type.ContainingAssembly?.Name is { } name && System.Array.IndexOf(StudioAssemblies, name) >= 0;

    private static bool IsAvalonia(ISymbol type) =>
        type.ContainingAssembly?.Name is { } name &&
        (name == "Avalonia" || name.StartsWith("Avalonia.", System.StringComparison.Ordinal));
}
