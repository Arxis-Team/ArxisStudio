using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Сверка классов, помеченных атрибутом вклада, с элементами манифеста.
/// </summary>
/// <remarks>
/// Свой контрол полосы (<c>ARX0004</c>, <c>ARX0005</c>) и пункт создания с кодом (<c>ARX0016</c>,
/// <c>ARX0017</c>) связаны с кодом одинаково: манифест объявляет, атрибут связывает. Разойтись они
/// могут в обе стороны — элемент без класса и класс без элемента, — и сверяли их правила полосы и
/// пунктов двумя одинаковыми копиями.
/// <para>
/// Сверка возможна только в конце компиляции: раньше известна лишь одна из двух записей. Среда
/// покажет такую находку после полного разбора, а сборка — сразу.
/// </para>
/// </remarks>
internal static class MarkedClasses
{
    private const string Namespace = "ArxisStudio.Sdk";

    /// <summary>Заводит на компиляцию сверку атрибута вклада с манифестом.</summary>
    /// <param name="context">Начало компиляции.</param>
    /// <param name="attribute">Имя класса атрибута: <c>ToolBarItemAttribute</c> или <c>NewItemAttribute</c>.</param>
    /// <param name="declared">Элементы манифеста, которым нужен класс, — идентификатор и место.</param>
    /// <param name="missing">Правило «элемент объявлен, а класса нет».</param>
    /// <param name="undeclared">Правило «класс помечен, а элемента нет».</param>
    public static void Reconcile(
        CompilationStartAnalysisContext context,
        string attribute,
        Func<List<ManifestJson.Field>, IEnumerable<(string Id, TextSpan Span)>> declared,
        DiagnosticDescriptor missing,
        DiagnosticDescriptor undeclared)
    {
        var manifest = ManifestFiles.Find(context.Options);

        // Проекта без манифеста правило не касается: так собирают частную зависимость
        // расширения, и объявлять ей нечего.
        if (manifest?.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var marked = new ConcurrentDictionary<string, Location>(StringComparer.Ordinal);

        context.RegisterSymbolAction(symbol => Mark(symbol, attribute, marked), SymbolKind.NamedType);

        context.RegisterCompilationEndAction(end =>
        {
            var items = declared(ManifestJson.Strings(text.ToString())).ToList();

            foreach (var (id, span) in items.Where(item => item.Id.Length > 0 && !marked.ContainsKey(item.Id)))
            {
                end.ReportDiagnostic(Diagnostic.Create(missing, ManifestFiles.At(manifest.Path, text, span), id));
            }

            var known = new HashSet<string>(items.Select(item => item.Id), StringComparer.Ordinal);

            foreach (var pair in marked.Where(pair => !known.Contains(pair.Key)))
            {
                end.ReportDiagnostic(Diagnostic.Create(undeclared, pair.Value, pair.Key));
            }
        });
    }

    private static void Mark(SymbolAnalysisContext context, string name, ConcurrentDictionary<string, Location> marked)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { } found ||
                found.Name != name ||
                found.ContainingNamespace?.ToDisplayString() != Namespace ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string id)
            {
                continue;
            }

            // Место находки — сам атрибут: править нужно там или в манифесте, а не где-то в теле
            // класса.
            var location = attribute.ApplicationSyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : context.Symbol.Locations.FirstOrDefault() ?? Location.None;

            marked[id] = location;
        }
    }
}
