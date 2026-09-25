using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
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
/// имя»: цвет там, где свойству нужна кисть, имя, которого с SDK 6.0 в теме
/// нет, или имя, которого в ней не было никогда, — опечатка. Такая ссылка не
/// разрешится во что-то видимое.
/// </para>
/// <para>
/// Прежнее и незнакомое имя ARX0009 ищет и в коде, в любой строке:
/// <c>GetResourceObservable("AxBg2Brush")</c> ломается так же молча, как ссылка в разметке, а
/// строка формы <c>AxИмя</c> ничем, кроме ключа темы, не бывает — имена типов берут через
/// <c>nameof</c>. Кусок строки, склеенной из частей, — не ключ: <c>$"AxTint{name}Brush"</c>
/// собирает его на ходу. Цвет вместо кисти в коде не спрашивается: у строки нет свойства, по
/// которому видно, что нужно.
/// </para>
/// <para>
/// Ключ, который расширение объявило в своей разметке, правило знает: словари плагина читаются
/// все до одного, прежде чем спрашивать ссылки, — ссылка из одного файла на ключ другого законна.
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
                     "Имён темы до SDK 6.0 (AxBg2Brush, AxFg3Brush) и ступеней шкал (AxBlue6) в теме нет: их заменили роли. " +
                     "Имя с приставкой Ax, которого в теме нет, — опечатка: ресурс разрешится в пустоту.");

    private static readonly Regex Resource = new(
        @"^\{\s*(?:[A-Za-z_][\w.]*:)?(?:Dynamic|Static)Resource\s+(?:ResourceKey\s*=\s*)?([A-Za-z_][\w.]*)\s*\}$",
        RegexOptions.Compiled);

    /// <summary>Ссылка на ресурс внутри другого расширения разметки: <c>Converter={StaticResource …}</c>.</summary>
    private static readonly Regex Nested = new(
        @"\{\s*(?:[A-Za-z_][\w.]*:)?(?:Dynamic|Static)Resource\s+(?:ResourceKey\s*=\s*)?([A-Za-z_][\w.]*)\s*\}",
        RegexOptions.Compiled);

    /// <summary>Ключ, объявленный разметкой: <c>x:Key="…"</c>.</summary>
    private static readonly Regex Declared = new(
        @"\bx:Key\s*=\s*""([A-Za-z_][\w.]*)""",
        RegexOptions.Compiled);

    /// <summary>Элементы, которые называют ресурс атрибутом <c>ResourceKey</c>.</summary>
    private static readonly HashSet<string> Lookups = new(StringComparer.Ordinal) { "StaticResource", "DynamicResource" };

    private static readonly HashSet<string> Gaps = new(StringComparer.Ordinal) { "Margin", "Padding" };

    /// <summary>Свойства, которым нужна кисть, а не цвет.</summary>
    private static readonly HashSet<string> Brushes = new(StringComparer.Ordinal)
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

        context.RegisterCompilationStartAction(start =>
        {
            var own = Own(start.Options.AdditionalFiles, start.CancellationToken);

            start.RegisterAdditionalFileAction(file => Check(file, own));
            start.RegisterOperationAction(operation => Named(operation, own), OperationKind.Literal);
        });
    }

    /// <summary>Ключи, которые расширение объявило в своей разметке, — во всех её файлах.</summary>
    /// <remarks>
    /// Файл читается образцом, а не разбором: недописанный файл в редакторе не должен лишить
    /// правило ключей, которые в нём уже есть.
    /// </remarks>
    private static HashSet<string> Own(ImmutableArray<AdditionalText> files, System.Threading.CancellationToken cancellation)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!MarkupFiles.IsMarkup(file.Path) || file.GetText(cancellation) is not { } text)
                continue;

            foreach (Match declared in Declared.Matches(text.ToString()))
                keys.Add(declared.Groups[1].Value);
        }

        return keys;
    }

    private static void Named(OperationAnalysisContext context, HashSet<string> own)
    {
        if (context.Operation.ConstantValue is not { HasValue: true, Value: string key })
            return;

        var complaint = ThemeRenames.Retired(key, brush: false) ??
                        (Piece(context.Operation) ? null : ThemeAdvice.Absent(key, ThemeTokens.Instance, own, brush: false));

        if (complaint is not null)
            context.ReportDiagnostic(Diagnostic.Create(Family, context.Operation.Syntax.GetLocation(), complaint));
    }

    /// <summary>Часть строки, склеенной из кусков: ключ целиком она не называет.</summary>
    private static bool Piece(IOperation literal) =>
        literal.Parent is IInterpolatedStringTextOperation or IBinaryOperation { OperatorKind: BinaryOperatorKind.Add };

    private static void Check(AdditionalFileAnalysisContext context, HashSet<string> own)
    {
        if (MarkupFiles.Read(context.AdditionalFile, context.CancellationToken) is not { } markup)
            return;

        var (text, document) = markup;
        var tokens = ThemeTokens.Instance;

        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName == "Setter")
            {
                if (element.Attribute("Property")?.Value is { } property && element.Attribute("Value") is { } value)
                    Inspect(context, text, Owned(property), value, tokens, own);

                continue;
            }

            // <StaticResource ResourceKey="…"/> называет ключ без фигурных скобок.
            if (Lookups.Contains(element.Name.LocalName) && element.Attribute("ResourceKey") is { } named)
            {
                if (Misnamed(string.Empty, named.Value, tokens, own) is { } complaint)
                    Report(context, text, named, Family, complaint);

                continue;
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length > 0)
                    continue;

                Inspect(context, text, Owned(attribute.Name.LocalName), attribute, tokens, own);
            }
        }
    }

    private static void Inspect(
        AdditionalFileAnalysisContext context,
        SourceText text,
        string property,
        XAttribute attribute,
        ThemeTokens tokens,
        HashSet<string> own)
    {
        var value = attribute.Value;
        var reference = Resource.Match(value);

        if (reference.Success)
        {
            if (Misnamed(property, reference.Groups[1].Value, tokens, own) is { } complaint)
                Report(context, text, attribute, Family, complaint);

            return;
        }

        if (value.StartsWith("{", StringComparison.Ordinal))
        {
            // Ссылка внутри привязки или другого расширения: свойство ей не хозяин, и спросить
            // можно только имя.
            foreach (Match nested in Nested.Matches(value))
            {
                if (Misnamed(string.Empty, nested.Groups[1].Value, tokens, own) is { } complaint)
                    Report(context, text, attribute, Family, complaint);
            }

            return;
        }

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
    /// <param name="property">Свойство, которому ресурс отдан; пусто — неизвестно.</param>
    /// <param name="key">Ключ, как его назвали.</param>
    /// <param name="tokens">Тема.</param>
    /// <param name="own">Ключи, которые расширение объявило само.</param>
    private static string? Misnamed(string property, string key, ThemeTokens tokens, HashSet<string> own)
    {
        var brush = Brushes.Contains(property);

        if (ThemeRenames.Retired(key, brush) is { } retired)
            return retired;

        if (brush && tokens.BrushByColourKey.TryGetValue(key, out var named))
            return $"{key} — цвет, а свойству {property} нужна кисть: {named}";

        return ThemeAdvice.Absent(key, tokens, own, brush);
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
        context.ReportDiagnostic(Diagnostic.Create(
            rule,
            MarkupFiles.At(context.AdditionalFile.Path, text, attribute, attribute.Name.LocalName.Length),
            arguments));
}
