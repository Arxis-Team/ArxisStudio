using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Следит за тем, чтобы полоса, обещанная манифестом, совпадала с кодом.
/// </summary>
/// <remarks>
/// Манифест и код — две записи об одном, и разойтись они могут молча: класс
/// переименовали, а в манифесте забыли — и человек увидит пустое место вместо
/// кнопки. Студия скажет об этом в журнал, но увидит журнал уже пользователь,
/// а не автор. Поймать это дешевле здесь.
/// <para>
/// Проверок три и они разной природы. Про команду кнопки видно по одному
/// манифесту, поэтому она и разбирается на нём — среда покажет находку сразу.
/// Про свой контрол одного манифеста мало: класс живёт в сборке, и сверить их
/// можно только в конце компиляции, когда известны оба.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ToolBarAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Кнопка зовёт команду, которой плагин не объявлял.</summary>
    public const string CommandId = "ARX0003";

    /// <summary>Свой контрол объявлен манифестом, а класса нет.</summary>
    public const string MissingId = "ARX0004";

    /// <summary>Класс помечен атрибутом, а манифест о нём молчит.</summary>
    public const string UndeclaredId = "ARX0005";

    private const string Attribute = "ToolBarItemAttribute";
    private const string Namespace = "ArxisStudio.Sdk";
    private const string Manifest = "plugin.json";

    private static readonly Regex Field =
        new(@"""(id|kind|command)""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly DiagnosticDescriptor Command = new(
        CommandId,
        "Кнопка полосы зовёт команду, которой плагин не объявлял",
        "{0}: команды {1} нет в contributions.commands — щелчок по кнопке ничего не сделает",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Кнопку студия рисует по манифесту и зовёт по ней команду, не спрашивая сборку. " +
                     "Команда, которую плагин не объявил, не попадёт ни в меню, ни в пробуждение по " +
                     "onCommand: — а щелчок по кнопке останется замечанием в журнале.");

    private static readonly DiagnosticDescriptor Missing = new(
        MissingId,
        "Свой контрол полосы объявлен, а класса для него нет",
        "{0}: в сборке нет класса с [ToolBarItem(\"{0}\")] — на месте элемента будет пусто",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Место в полосе студия отводит по манифесту, а класс ищет по атрибуту, когда плагин " +
                     "поднят. Не найдя его, она скажет об этом в журнал — но журнал увидит уже пользователь.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor Undeclared = new(
        UndeclaredId,
        "Класс помечен [ToolBarItem], а в манифесте его нет",
        "{0}: такого элемента нет в contributions.toolBar — студия не узнает о нём и не построит его",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Атрибут связывает объявленное с кодом, но не объявляет: полосу студия собирает по " +
                     "манифесту, не загружая сборку. Класс, которого в манифесте нет, не построит никто.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Command, Missing, Undeclared);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            return;
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Про команду видно по одному манифесту — разбираем его на нём самом.
        context.RegisterAdditionalFileAction(Commands);

        context.RegisterCompilationStartAction(Start);
    }

    private static void Commands(AdditionalFileAnalysisContext context)
    {
        if (!IsManifest(context.AdditionalFile.Path) ||
            context.AdditionalFile.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var source = text.ToString();
        var declared = Declared(source, "commands");

        foreach (var item in Items(source))
        {
            if (!item.IsButton || item.Command.Length == 0 || declared.Contains(item.Command))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Command,
                At(context.AdditionalFile.Path, text, item.Span),
                item.Id,
                item.Command));
        }
    }

    /// <remarks>
    /// Сверка манифеста со сборкой возможна только в конце компиляции: раньше
    /// известна лишь одна из двух записей. Среда покажет такую находку после
    /// полного разбора, а сборка — сразу.
    /// </remarks>
    private static void Start(CompilationStartAnalysisContext context)
    {
        var manifest = context.Options.AdditionalFiles.FirstOrDefault(file => IsManifest(file.Path));

        // Проекта без манифеста это правило не касается: так собирают частную
        // зависимость плагина, и объявлять ей нечего.
        if (manifest?.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var marked = new ConcurrentDictionary<string, Location>(StringComparer.Ordinal);

        context.RegisterSymbolAction(symbol => Mark(symbol, marked), SymbolKind.NamedType);

        context.RegisterCompilationEndAction(end =>
        {
            var source = text.ToString();
            var custom = Items(source).Where(item => item.IsCustom).ToList();

            foreach (var item in custom.Where(item => item.Id.Length > 0 && !marked.ContainsKey(item.Id)))
            {
                end.ReportDiagnostic(Diagnostic.Create(Missing, At(manifest.Path, text, item.Span), item.Id));
            }

            var known = new HashSet<string>(custom.Select(item => item.Id), StringComparer.Ordinal);

            foreach (var pair in marked.Where(pair => !known.Contains(pair.Key)))
            {
                end.ReportDiagnostic(Diagnostic.Create(Undeclared, pair.Value, pair.Key));
            }
        });
    }

    private static void Mark(SymbolAnalysisContext context, ConcurrentDictionary<string, Location> marked)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: Attribute } found ||
                found.ContainingNamespace?.ToDisplayString() != Namespace ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string id)
            {
                continue;
            }

            // Место находки — сам атрибут: править нужно там или в манифесте, а
            // не где-то в теле класса.
            var location = attribute.ApplicationSyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : context.Symbol.Locations.FirstOrDefault() ?? Location.None;

            marked[id] = location;
        }
    }

    /// <summary>Элемент полосы, как он записан в манифесте.</summary>
    private readonly struct Item(string id, string kind, string command, TextSpan span)
    {
        public string Id { get; } = id;

        public string Command { get; } = command;

        public TextSpan Span { get; } = span;

        /// <summary>Кнопка: вид по умолчанию, когда слово не написано.</summary>
        public bool IsButton => kind.Length == 0 || Is("button");

        public bool IsCustom => Is("custom");

        private bool Is(string what) => string.Equals(kind, what, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Разбирает секцию <c>toolBar</c>.
    /// </summary>
    /// <remarks>
    /// Разбирать JSON анализатору нечем: он живёт в netstandard2.0 и тащить в
    /// плагин чужую сборку ради одной секции не станет. Разбор поэтому свой и
    /// нарочно простой — он и может быть простым: у элемента полосы все поля
    /// строковые, вложенных объектов внутри не бывает, и кавычки в тексте
    /// считаются честно.
    /// </remarks>
    private static IEnumerable<Item> Items(string source)
    {
        var start = Section(source, "toolBar");

        if (start < 0)
        {
            yield break;
        }

        var quoted = false;
        var open = -1;

        for (var at = start; at < source.Length; at++)
        {
            var symbol = source[at];

            if (symbol == '"' && (at == 0 || source[at - 1] != '\\'))
            {
                quoted = !quoted;
            }

            if (quoted)
            {
                continue;
            }

            if (symbol == '{')
            {
                open = at;
            }
            else if (symbol == '}' && open >= 0)
            {
                yield return Read(source.Substring(open, at - open + 1), open);
                open = -1;
            }
            else if (symbol == ']')
            {
                yield break;
            }
        }
    }

    private static Item Read(string body, int offset)
    {
        var id = string.Empty;
        var kind = string.Empty;
        var command = string.Empty;
        var span = new TextSpan(offset, body.Length);

        foreach (Match match in Field.Matches(body))
        {
            var value = match.Groups[2].Value;

            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "id":
                    id = value;

                    // Место находки — имя элемента: по нему автор его и узнаёт.
                    span = new TextSpan(offset + match.Index, match.Length);
                    break;

                case "kind":
                    kind = value;
                    break;

                case "command":
                    command = value;
                    break;
            }
        }

        return new Item(id, kind, command, span);
    }

    /// <summary>Имена, объявленные в секции: <c>{ "id": "…" }</c> подряд.</summary>
    private static HashSet<string> Declared(string source, string section)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var start = Section(source, section);

        if (start < 0)
        {
            return names;
        }

        var end = source.IndexOf(']', start);
        var body = end < 0 ? source.Substring(start) : source.Substring(start, end - start);

        foreach (Match match in Field.Matches(body))
        {
            if (string.Equals(match.Groups[1].Value, "id", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(match.Groups[2].Value);
            }
        }

        return names;
    }

    /// <summary>Где начинается массив названной секции; -1 — её нет.</summary>
    private static int Section(string source, string name)
    {
        var match = Regex.Match(source, @"""" + name + @"""\s*:\s*\[", RegexOptions.IgnoreCase);

        return match.Success ? match.Index + match.Length : -1;
    }

    private static Location At(string path, SourceText text, TextSpan span) =>
        Location.Create(path, span, text.Lines.GetLinePositionSpan(span));

    private static readonly char[] Separators = { '/', '\\' };

    private static bool IsManifest(string path)
    {
        var separator = path.LastIndexOfAny(Separators);
        var name = separator < 0 ? path : path.Substring(separator + 1);

        return string.Equals(name, Manifest, StringComparison.OrdinalIgnoreCase);
    }
}
