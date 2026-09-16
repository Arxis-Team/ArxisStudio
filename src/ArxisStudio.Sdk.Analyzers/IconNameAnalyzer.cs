using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы у кнопки с одним значком было имя.
/// </summary>
/// <remarks>
/// Кнопка, у которой всё содержимое — глиф, для экранного диктора немая: он прочтёт «кнопка» и
/// замолчит. Подпись ей даёт <c>AutomationProperties.Name</c>, и ставить её приходится руками —
/// текста, из которого имя взялось бы само, у такой кнопки нет.
/// <para>
/// Подсказка под курсором этого не заменяет: она приходит по наведению мыши, а тот, кому имя
/// нужно, мышью не пользуется. Ставить надо оба, и правило спрашивает именно имя.
/// </para>
/// <para>
/// Входа два, как и у правила о виджетах: код и разметка. Разъехавшись, они означали бы, что
/// запрет обходится сменой места записи.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IconNameAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0013";

    private const string Markup = ".axaml";
    /// <summary>
    /// Основания кнопок Avalonia: от них ведут родословную все кнопки студии.
    /// </summary>
    /// <remarks>
    /// Одного основания не хватает: <c>SplitButton</c> в Avalonia кнопке не наследник, а
    /// самостоятельный контрол с содержимым.
    /// </remarks>
    private static readonly string[] Bases = ["Avalonia.Controls.Button", "Avalonia.Controls.SplitButton"];
    private const string Icon = "AxIcon";
    private const string Name = "AutomationProperties.Name";
    private const string Attached = "AutomationProperties";

    /// <summary>Имена элементов разметки, которые считаются кнопкой.</summary>
    /// <remarks>
    /// В разметке типов нет — есть имена, и разрешать их через ссылки проекта ради одного правила
    /// дороже, чем назвать кнопки студии списком. Список закрыт и короток: кнопкой с одним значком
    /// бывают эти пятеро.
    /// </remarks>
    private static readonly string[] Buttons =
    [
        "AxButton",
        "AxToggleButton",
        "AxDropDownButton",
        "AxSplitButton",
        "ToolBarButton",
    ];

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Кнопка со значком без имени для средств доступности",
        "{0} несёт только значок и не названа: экранный диктор прочтёт «кнопка» и замолчит — дайте ей AutomationProperties.Name",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Имя берётся из текста кнопки, а у кнопки со значком текста нет. " +
                     "Подсказка под курсором имени не заменяет: она приходит по наведению мыши, " +
                     "а тому, кому имя нужно, мышь не помощник.");

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
            var buttons = Bases
                .Select(start.Compilation.GetTypeByMetadataName)
                .Where(type => type is not null)
                .Select(type => type!)
                .ToImmutableArray();

            // Проект без Avalonia анализировать не о чем.
            if (buttons.IsEmpty)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Written(node, buttons), SyntaxKind.ObjectCreationExpression);
            start.RegisterAdditionalFileAction(Drawn);
        });
    }

    /// <summary>
    /// Кнопка, заведённая кодом: содержимое — значок, а имени нет.
    /// </summary>
    /// <remarks>
    /// Смотрится только инициализатор объекта: имя, поставленное позже вызовом
    /// <c>AutomationProperties.SetName</c>, правило засчитывает — искать его по всему методу
    /// значило бы гадать, той ли кнопке оно досталось, а промолчать на явном вызове хуже, чем
    /// смолчать на неявном.
    /// </remarks>
    private static void Written(SyntaxNodeAnalysisContext context, ImmutableArray<INamedTypeSymbol> buttons)
    {
        if (context.Node is not ObjectCreationExpressionSyntax creation ||
            context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor)
        {
            return;
        }

        var type = constructor.ContainingType;

        if (!Derives(type, buttons) || creation.Initializer is null)
        {
            return;
        }

        var assignments = creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>().ToList();
        var content = assignments.FirstOrDefault(one => one.Left.ToString().EndsWith("Content", StringComparison.Ordinal));

        // Значок в содержимом — и только он: кнопка с текстом называет себя сама.
        if (content is null || !IsIcon(content.Right, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (assignments.Any(one => one.Left.ToString().Contains(Name)) || Named(creation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Type.GetLocation(), type.Name));
    }

    /// <summary>
    /// Имя, данное кнопке рядом: вызовом <c>SetName</c>, присвоением свойства или чужой обёрткой.
    /// </summary>
    /// <remarks>
    /// Способов назвать кнопку в коде много — <c>AutomationProperties.SetName</c>, индексатор с
    /// <c>NameProperty</c>, свой помощник, которому передают и кнопку, и это свойство, — и
    /// перечислять их значило бы ловить на молчании тех, кто написал имя иначе. Поэтому правило
    /// ищет в том же блоке обращение, где рядом стоят имя переменной и <c>AutomationProperties.Name</c>:
    /// промолчать на честно названной кнопке хуже, чем не заметить хитро названную.
    /// </remarks>
    private static bool Named(ObjectCreationExpressionSyntax creation)
    {
        if (creation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } ||
            variable.Parent?.Parent?.Parent is not BlockSyntax block)
        {
            return false;
        }

        var target = variable.Identifier.ValueText;

        return block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(call =>
            {
                var text = call.ToString();

                // SetName, NameProperty, своя обёртка — все они называют и свойство, и кнопку.
                return text.Contains(Attached, StringComparison.Ordinal) &&
                       text.Contains("Name", StringComparison.Ordinal) &&
                       call.ArgumentList.Arguments.Any(argument => argument.Expression.ToString() == target);
            });
    }

    /// <summary>Значок ли это — <c>AxIcon</c> или его наследник.</summary>
    private static bool IsIcon(ExpressionSyntax expression, SemanticModel model, System.Threading.CancellationToken token)
    {
        if (model.GetSymbolInfo(expression, token).Symbol is not IMethodSymbol constructor)
        {
            return false;
        }

        for (INamedTypeSymbol? current = constructor.ContainingType; current is not null; current = current.BaseType)
        {
            if (current.Name == Icon)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Derives(INamedTypeSymbol type, ImmutableArray<INamedTypeSymbol> buttons)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var button in buttons)
            {
                if (SymbolEqualityComparer.Default.Equals(current, button))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Кнопка, записанная разметкой: единственный ребёнок — значок, а имени нет.</summary>
    private static void Drawn(AdditionalFileAnalysisContext context)
    {
        var file = context.AdditionalFile;

        if (!file.Path.EndsWith(Markup, StringComparison.OrdinalIgnoreCase) ||
            file.GetText(context.CancellationToken)?.ToString() is not { } text)
        {
            return;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            // Разметку, которую не разобрать, назовёт компилятор XAML — и лучше нас.
            return;
        }

        foreach (var element in document.Descendants())
        {
            if (!Buttons.Contains(element.Name.LocalName, StringComparer.Ordinal) || Named(element) || Part(element))
            {
                continue;
            }

            var children = element.Elements()
                .Where(child => !child.Name.LocalName.Contains('.'))
                .ToList();

            var property = element.Elements()
                .FirstOrDefault(child => child.Name.LocalName.EndsWith(".Content", StringComparison.Ordinal));

            var content = property is not null ? property.Elements().ToList() : children;

            if (content.Count != 1 || content[0].Name.LocalName != Icon)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, At(file, text, element), element.Name.LocalName));
        }
    }

    /// <summary>
    /// Часть шаблона: её называет хозяин шаблона, а не разметка.
    /// </summary>
    /// <remarks>
    /// Кнопка с именем <c>PART_*</c> принадлежит контролу, и подпись ей контрол ставит сам — чаще
    /// всего переведённую, которой в разметке и не написать. Так названа кнопка «скрыть» в шапке
    /// группы доков: имя ей даёт <c>DockGroupView</c>. Разметке отсюда видно только половину
    /// правды, и требовать имя здесь значило бы просить второе, мёртвое.
    /// </remarks>
    private static bool Part(XElement element) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" &&
                                              attribute.Value.StartsWith("PART_", StringComparison.Ordinal));

    /// <summary>
    /// Названа ли кнопка для средств доступности.
    /// </summary>
    /// <remarks>
    /// Имя приходит присоединённым свойством, и в разметке это атрибут <c>AutomationProperties.Name</c>
    /// — с префиксом или без него. Собственное имя элемента (<c>x:Name</c>) тут ни при чём: его
    /// экранный диктор не читает.
    /// </remarks>
    private static bool Named(XElement element) =>
        element.Attributes().Any(attribute =>
            attribute.Name.LocalName.EndsWith(Name, StringComparison.Ordinal) && attribute.Value.Length > 0);

    /// <summary>Место элемента в файле: без него замечание в разметке не найти.</summary>
    private static Location At(AdditionalText file, string text, XElement element)
    {
        var info = (IXmlLineInfo)element;
        var line = info.HasLineInfo() ? info.LineNumber - 1 : 0;
        var source = SourceText.From(text);
        var span = line >= 0 && line < source.Lines.Count ? source.Lines[line].Span : new TextSpan(0, 0);

        return Location.Create(file.Path, span, new LinePositionSpan(new LinePosition(line, 0), new LinePosition(line, span.Length)));
    }
}
