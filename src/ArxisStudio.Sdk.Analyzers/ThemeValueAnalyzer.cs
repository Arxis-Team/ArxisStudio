using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы разметка расширения называла значения темы по имени.
/// </summary>
/// <remarks>
/// Правило репозитория — «значение цвета или размера объявляется в теме и
/// больше нигде» — до этого анализатора держалось только на обзоре, а у автора
/// плагина обзора нет. Число в разметке не переключается вместе с темой и не
/// сжимается вместе с плотностью: панель плагина остаётся тёмной в светлой
/// студии и просторной в плотной.
/// <para>
/// Два правила, и граница между ними — вопрос, а не место. <c>ARX0008</c>
/// спрашивает «почему здесь число»; <c>ARX0009</c> — «почему здесь не то
/// имя»: цвет там, где свойству нужна кисть, или имя, которого с SDK 6.0 в теме
/// нет, — такая ссылка не разрешится во что-то видимое.
/// </para>
/// <para>
/// Прежнее имя ARX0009 ищет и в коде, в любой строке: <c>GetResourceObservable("AxBg2Brush")</c>
/// ломается так же молча, как ссылка в разметке, а строка, совпавшая с именем из
/// <see cref="ThemeRenames"/>, ничем другим быть не может. Цвет вместо кисти в коде не
/// спрашивается: у строки нет свойства, по которому видно, что нужно.
/// </para>
/// <para>
/// Чего правило не спрашивает, и почему. Ширины и высоты: собственная канва
/// плагина в 137 пикселей — его дело, а совпади она с высотой строки — правило
/// приняло бы случайность за намерение. Цвет, которого в теме нет: своя палитра
/// графика законна. Отступ меньше двух: это поправка на пиксель, а не зазор.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThemeValueAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики: число вместо значения темы.</summary>
    public const string LiteralId = "ARX0008";

    /// <summary>Код диагностики: ресурс темы назван не тем именем.</summary>
    public const string FamilyId = "ARX0009";

    private const string Markup = ".axaml";

    private static readonly DiagnosticDescriptor Literal = new(
        LiteralId,
        "Число вместо значения темы в разметке расширения",
        "{0}=\"{1}\": {2}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Число не переключается вместе с темой и не сжимается вместе с плотностью. " +
                     "Отступы, кегли и цвета темы называют ресурсом: {DynamicResource AxSpace}.");

    private static readonly DiagnosticDescriptor Family = new(
        FamilyId,
        "Ресурс темы назван не тем именем",
        "{0}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Цвета (AxAccentColor) — значения типа Color, разметке нужны кисти (AxAccentBrush). " +
                     "Имён темы до SDK 6.0 (AxBg2Brush, AxFg3Brush) и ступеней шкал (AxBlue6) в теме нет: их заменили роли.");

    private static readonly Regex Resource = new(
        @"^\{\s*(?:[A-Za-z_][\w.]*:)?(?:Dynamic|Static)Resource\s+(?:ResourceKey\s*=\s*)?([A-Za-z_][\w.]*)\s*\}$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> Gaps = new(StringComparer.Ordinal) { "Margin", "Padding" };

    /// <summary>Свойства, которым нужна кисть, а не цвет.</summary>
    internal static readonly HashSet<string> Brushes = new(StringComparer.Ordinal)
    {
        "Foreground", "Background", "BorderBrush", "Fill", "Stroke",
        "CaretBrush", "SelectionBrush", "SelectionForegroundBrush",
    };

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Literal, Family);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            return;

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(Check);
        context.RegisterOperationAction(Named, OperationKind.Literal);
    }

    private static void Named(OperationAnalysisContext context)
    {
        if (context.Operation.ConstantValue is { HasValue: true, Value: string key } &&
            ThemeRenames.Retired(key, brush: false) is { } complaint)
            context.ReportDiagnostic(Diagnostic.Create(Family, context.Operation.Syntax.GetLocation(), complaint));
    }

    private static void Check(AdditionalFileAnalysisContext context)
    {
        if (!context.AdditionalFile.Path.EndsWith(Markup, StringComparison.OrdinalIgnoreCase))
            return;

        var text = context.AdditionalFile.GetText(context.CancellationToken);

        if (text is null)
            return;

        XDocument document;

        try
        {
            document = XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            // Недописанную разметку разберёт и отругает компилятор разметки.
            return;
        }

        var tokens = ThemeTokens.Instance;

        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName == "Setter")
            {
                if (element.Attribute("Property")?.Value is { } property && element.Attribute("Value") is { } value)
                    Inspect(context, text, Owned(property), value, tokens);

                continue;
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length > 0)
                    continue;

                Inspect(context, text, Owned(attribute.Name.LocalName), attribute, tokens);
            }
        }
    }

    private static void Inspect(
        AdditionalFileAnalysisContext context,
        SourceText text,
        string property,
        XAttribute attribute,
        ThemeTokens tokens)
    {
        var value = attribute.Value;
        var reference = Resource.Match(value);

        if (reference.Success)
        {
            if (Misnamed(property, reference.Groups[1].Value, tokens) is { } complaint)
                Report(context, text, attribute, Family, complaint);

            return;
        }

        if (value.StartsWith("{", StringComparison.Ordinal))
            return;

        var advice = property switch
        {
            "Spacing" => Number(value) is { } length ? ThemeAdvice.Length(length, tokens) : null,
            "FontSize" => Number(value) is { } size ? ThemeAdvice.FontSize(size, tokens) : null,
            _ when Gaps.Contains(property) => ThemeTokens.Sides(value) is { } sides ? ThemeAdvice.Thickness(sides, tokens) : null,
            _ when Brushes.Contains(property) || property == "Color" => ThemeAdvice.Colour(value, tokens),
            _ => null,
        };

        if (advice is not null)
            Report(context, text, attribute, Literal, property, value, advice);
    }

    /// <summary>
    /// Что не так с именем ресурса; <c>null</c> — всё так.
    /// </summary>
    internal static string? Misnamed(string property, string key, ThemeTokens tokens)
    {
        if (ThemeRenames.Retired(key, Brushes.Contains(property)) is { } retired)
            return retired;

        if (Brushes.Contains(property) && tokens.BrushByColourKey.TryGetValue(key, out var brush))
            return $"{key} — цвет, а свойству {property} нужна кисть: {brush}";

        return null;
    }

    /// <summary>Имя свойства без хозяина: <c>TextBlock.FontSize</c> — <c>FontSize</c>.</summary>
    private static string Owned(string name)
    {
        var dot = name.LastIndexOf('.');

        return dot < 0 ? name : name.Substring(dot + 1);
    }

    private static double? Number(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static void Report(
        AdditionalFileAnalysisContext context,
        SourceText text,
        XAttribute attribute,
        DiagnosticDescriptor rule,
        params object[] arguments) =>
        context.ReportDiagnostic(Diagnostic.Create(rule, Where(attribute, text, context.AdditionalFile.Path), arguments));

    /// <summary>Место атрибута в файле разметки.</summary>
    private static Location Where(XAttribute attribute, SourceText text, string path)
    {
        var info = (IXmlLineInfo)attribute;

        if (!info.HasLineInfo() || info.LineNumber - 1 >= text.Lines.Count)
            return Location.None;

        var line = info.LineNumber - 1;
        var column = info.LinePosition - 1;
        var length = attribute.Name.LocalName.Length;
        var start = text.Lines[line].Start + column;

        return Location.Create(
            path,
            new TextSpan(start, length),
            new LinePositionSpan(new LinePosition(line, column), new LinePosition(line, column + length)));
    }
}
