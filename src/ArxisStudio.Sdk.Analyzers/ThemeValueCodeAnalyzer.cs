using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// То же правило о значениях темы, что у <see cref="ThemeValueAnalyzer"/>, — в коде.
/// </summary>
/// <remarks>
/// Урок пары <see cref="AvaloniaWidgetAnalyzer"/> и <see cref="MarkupWidgetAnalyzer"/>
/// записан в шапке второго: правило, живущее в одном месте, обходится сменой места
/// записи. <c>Spacing="10"</c> в разметке и <c>Spacing = 10</c> в коде — одно и то
/// же число, и панель, построенная кодом, так же не сжимается вместе с плотностью.
/// <para>
/// Что правило спрашивает: <c>new Thickness</c> из чисел, присвоение числа
/// свойствам <c>Spacing</c> и <c>FontSize</c> контролов Avalonia и разбор цвета из
/// строки — <c>Color.Parse</c>, <c>Brush.Parse</c>. Последнее — прямой близнец
/// атрибута <c>Foreground="#3574F0"</c>.
/// </para>
/// <para>
/// Чего не спрашивает, и почему. Цвет, собранный из байтов, — <c>Color.FromRgb</c>:
/// так строятся палитры, у которых своя жизнь, — схема терминала, запасные цвета на
/// случай темы без нужного ключа, — и по одному значению не отличить запасной цвет,
/// нарочно равный теме, от темы, переписанной числом. Своё свойство расширения с
/// именем <c>FontSize</c>: правило смотрит на контролы Avalonia, а не на имена.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThemeValueCodeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0010";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Число вместо значения темы в коде расширения",
        "{0}: {1}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Число не переключается вместе с темой и не сжимается вместе с плотностью. " +
                     "В коде значение темы берут привязкой: control.Bind(property, control.GetResourceObservable(\"AxSpace\")).");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            return;

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var thickness = start.Compilation.GetTypeByMetadataName("Avalonia.Thickness");

            // Проект без Avalonia анализировать не о чем.
            if (thickness is null)
                return;

            var parsers = new[] { "Avalonia.Media.Color", "Avalonia.Media.Brush", "Avalonia.Media.SolidColorBrush" }
                .Select(start.Compilation.GetTypeByMetadataName)
                .OfType<INamedTypeSymbol>()
                .ToImmutableArray();

            var tokens = ThemeTokens.Instance;

            start.RegisterOperationAction(
                operation => Created(operation, thickness, tokens),
                OperationKind.ObjectCreation);
            start.RegisterOperationAction(
                operation => Assigned(operation, tokens),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                operation => Parsed(operation, parsers, tokens),
                OperationKind.Invocation);
        });
    }

    private static void Created(OperationAnalysisContext context, INamedTypeSymbol thickness, ThemeTokens tokens)
    {
        var creation = (IObjectCreationOperation)context.Operation;

        if (!SymbolEqualityComparer.Default.Equals(creation.Type, thickness) || creation.Arguments.Length == 0)
            return;

        var numbers = creation.Arguments.Select(argument => Number(argument.Value)).ToArray();

        if (numbers.Any(number => number is null))
            return;

        var sides = ThemeTokens.Sides(string.Join(",", numbers.Select(number => number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        if (sides is not null && ThemeAdvice.Thickness(sides, tokens) is { } advice)
            Report(context, creation.Syntax, advice);
    }

    private static void Assigned(OperationAnalysisContext context, ThemeTokens tokens)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        if (assignment.Target is not IPropertyReferenceOperation { Property: var property } ||
            !IsAvalonia(property.ContainingType) ||
            Number(assignment.Value) is not { } number)
            return;

        var advice = property.Name switch
        {
            "Spacing" => ThemeAdvice.Length(number, tokens),
            "FontSize" => ThemeAdvice.FontSize(number, tokens),
            _ => null,
        };

        if (advice is not null)
            Report(context, assignment.Syntax, advice);
    }

    private static void Parsed(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> parsers, ThemeTokens tokens)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (invocation.TargetMethod.Name != "Parse" ||
            !parsers.Contains(invocation.TargetMethod.ContainingType, SymbolEqualityComparer.Default) ||
            invocation.Arguments.Length == 0 ||
            invocation.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: string text})
            return;

        if (ThemeAdvice.Colour(text, tokens) is { } advice)
            Report(context, invocation.Syntax, advice);
    }

    /// <summary>
    /// Число, если выражение — постоянная; иначе <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>new Thickness(8)</c> передаёт <c>int</c> туда, где ждут <c>double</c>, и
    /// постоянной оказывается уже преобразованное значение — поэтому спрашивается
    /// оно, а не запись в исходнике.
    /// </remarks>
    private static double? Number(IOperation value) =>
        value.ConstantValue is { HasValue: true, Value: { } constant } && constant is not string and not bool and not char
            ? Convert.ToDouble(constant, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Объявлено ли свойство контролом Avalonia, а не самим расширением.</summary>
    private static bool IsAvalonia(INamedTypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString().StartsWith("Avalonia", StringComparison.Ordinal) == true;

    private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string advice) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.GetLocation(), Shorten(syntax.ToString()), advice));

    /// <summary>Запись из исходника в одну строку и не длиннее разумного.</summary>
    private static string Shorten(string code)
    {
        var line = string.Join(" ", code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));

        return line.Length <= 60 ? line : line.Substring(0, 57) + "...";
    }
}
