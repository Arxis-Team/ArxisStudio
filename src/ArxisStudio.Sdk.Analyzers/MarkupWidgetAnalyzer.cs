using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы интерфейс расширения строился на контролах студии и в разметке.
/// </summary>
/// <remarks>
/// То же правило, что у <see cref="AvaloniaWidgetAnalyzer"/>, и по той же
/// причине. Отдельный анализатор нужен потому, что разметку компилятор XAML
/// разбирает уже после Roslyn: <c>&lt;TextBox/&gt;</c> в <c>.axaml</c> не видит
/// ни один анализатор кода, и правило, живущее только в коде, обходилось бы
/// сменой места записи.
/// <para>
/// Имена разрешаются так же, как их разрешает компилятор разметки: <c>using:</c>
/// и <c>clr-namespace:</c> — напрямую, адрес — по объявлениям
/// <c>XmlnsDefinition</c> в сборках, на которые сослался проект. Поэтому
/// правило одинаково видит и словарь студии, и словарь Avalonia, и любой
/// чужой, и не знает ни одного адреса наизусть.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MarkupWidgetAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0006";

    private const string Markup = ".axaml";
    private const string Using = "using:";
    private const string ClrNamespace = "clr-namespace:";
    private const string XmlnsDefinition = "XmlnsDefinitionAttribute";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Виджет Avalonia в разметке плагина",
        "{0} — виджет Avalonia; {1}",
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
            var templated = start.Compilation.GetTypeByMetadataName(StudioControls.TemplatedControl);

            // Проект без Avalonia анализировать не о чем.
            if (templated is null)
            {
                return;
            }

            // Словарь адресов собирается один раз на компиляцию: он один и тот
            // же для всех файлов разметки, а обход ссылок недёшев.
            var addresses = Addresses(start.Compilation);

            start.RegisterAdditionalFileAction(file => Check(file, templated, addresses));
        });
    }

    /// <summary>
    /// Собирает словарь «адрес xmlns → пространства имён» по ссылкам проекта.
    /// </summary>
    /// <remarks>
    /// Читаются объявления самих сборок, а не список известных адресов: так
    /// правило работает и со словарём студии, и со словарём Avalonia, и с
    /// чужим, о котором мы ничего не знаем.
    /// </remarks>
    private static Dictionary<string, List<string>> Addresses(Compilation compilation)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                Collect(assembly, found);
            }
        }

        // И собственные объявления проекта: библиотека контролов, собранная в
        // этом же решении, объявляет свой адрес про себя.
        Collect(compilation.Assembly, found);

        return found;
    }

    private static void Collect(IAssemblySymbol assembly, Dictionary<string, List<string>> found)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != XmlnsDefinition ||
                attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not string address ||
                attribute.ConstructorArguments[1].Value is not string space)
            {
                continue;
            }

            if (!found.TryGetValue(address, out var spaces))
            {
                found[address] = spaces = [];
            }

            if (!spaces.Contains(space))
            {
                spaces.Add(space);
            }
        }
    }

    private static void Check(
        AdditionalFileAnalysisContext context,
        INamedTypeSymbol templated,
        Dictionary<string, List<string>> addresses)
    {
        if (!context.AdditionalFile.Path.EndsWith(Markup, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = context.AdditionalFile.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            // Разметка не разбирается — об этом скажет компилятор разметки, и
            // повторять его двумя словами незачем.
            return;
        }

        foreach (var element in document.Descendants())
        {
            if (Forbidden(element, context.Compilation, templated, addresses) is not { } widget)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                Where(element, text, context.AdditionalFile.Path),
                widget.Name,
                StudioControls.Advice(context.Compilation, widget)));
        }
    }

    /// <summary>
    /// Тип виджета, если элемент — запрещённый контрол; иначе <c>null</c>.
    /// </summary>
    /// <remarks>
    /// За адресом стоит несколько пространств имён, и решает первое, в котором
    /// имя нашлось: так же выбирает и компилятор разметки — по порядку ссылок,
    /// молча. Правило обязано смотреть на тот же тип, что и он.
    /// <para>
    /// Элементы-свойства (<c>&lt;Grid.ColumnDefinitions&gt;</c>) отсеиваются
    /// здесь же и сами собой: точечному имени не отвечает ни один тип.
    /// </para>
    /// </remarks>
    private static INamedTypeSymbol? Forbidden(
        XElement element,
        Compilation compilation,
        INamedTypeSymbol templated,
        Dictionary<string, List<string>> addresses)
    {
        foreach (var space in Spaces(element.Name.NamespaceName, addresses))
        {
            foreach (var type in compilation.GetTypesByMetadataName(space + "." + element.Name.LocalName))
            {
                return StudioControls.IsForbidden(type, templated) ? type : null;
            }
        }

        return null;
    }

    /// <summary>Пространства имён, которые стоят за адресом xmlns.</summary>
    private static IEnumerable<string> Spaces(string address, Dictionary<string, List<string>> addresses)
    {
        if (address.StartsWith(Using, StringComparison.Ordinal))
        {
            return [address.Substring(Using.Length)];
        }

        if (address.StartsWith(ClrNamespace, StringComparison.Ordinal))
        {
            var space = address.Substring(ClrNamespace.Length);
            var assembly = space.IndexOf(';');

            return [assembly < 0 ? space : space.Substring(0, assembly)];
        }

        return addresses.TryGetValue(address, out var found) ? found : [];
    }

    /// <summary>Место элемента в файле разметки.</summary>
    private static Location Where(XElement element, SourceText text, string path)
    {
        var info = (IXmlLineInfo)element;

        if (!info.HasLineInfo() || info.LineNumber - 1 >= text.Lines.Count)
        {
            return Location.None;
        }

        var line = info.LineNumber - 1;
        var column = info.LinePosition - 1;
        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        var length = (prefix is null ? 0 : prefix.Length + 1) + element.Name.LocalName.Length;
        var start = text.Lines[line].Start + column;

        return Location.Create(
            path,
            new TextSpan(start, length),
            new LinePositionSpan(new LinePosition(line, column), new LinePosition(line, column + length)));
    }
}
