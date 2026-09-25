using System.Collections.Immutable;
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
/// <para>
/// Оба манифеста: <c>plugin.json</c> у внешнего плагина и <c>module.json</c> у
/// встроенного модуля. Полосу обоих студия строит по одной секции и одними
/// правилами, и код, переносимый между режимами, не должен менять смысл при
/// переносе. Прежде правила узнавали только <c>plugin.json</c>, и модуль с
/// кнопкой, зовущей необъявленную команду, собирался без единого замечания —
/// тот же пробел, что был когда-то у <c>ARX0002</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ToolBarAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Кнопка зовёт команду, которой расширение не объявляло.</summary>
    public const string CommandId = "ARX0003";

    /// <summary>Свой контрол объявлен манифестом, а класса нет.</summary>
    public const string MissingId = "ARX0004";

    /// <summary>Класс помечен атрибутом, а манифест о нём молчит.</summary>
    public const string UndeclaredId = "ARX0005";

    private const string Attribute = "ToolBarItemAttribute";

    /// <summary>Поле элемента полосы — только во вкладах и только своё, не вложенное.</summary>
    private static readonly Regex ItemField = new(
        @"^(?<item>contributions\.toolBar\[\d+\])\.(?<field>id|kind|command)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Имя объявленной команды.</summary>
    private static readonly Regex CommandField = new(
        @"^contributions\.commands\[\d+\]\.id$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Command = new(
        CommandId,
        "Кнопка полосы зовёт команду, которой расширение не объявляло",
        "{0}: команды {1} нет в contributions.commands — щелчок по кнопке ничего не сделает",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Кнопку студия рисует по манифесту и зовёт по ней команду, не спрашивая сборку. " +
                     "Команда, которую расширение не объявило, не попадёт ни в меню, ни в пробуждение по " +
                     "onCommand: — а щелчок по кнопке останется замечанием в журнале.");

    private static readonly DiagnosticDescriptor Missing = new(
        MissingId,
        "Свой контрол полосы объявлен, а класса для него нет",
        "{0}: в сборке нет класса с [ToolBarItem(\"{0}\")] — на месте элемента будет пусто",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Место в полосе студия отводит по манифесту, а класс ищет по атрибуту, когда расширение " +
                     "поднято. Не найдя его, она скажет об этом в журнал — но журнал увидит уже пользователь.",
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

        // Про свой контрол одного манифеста мало: класс живёт в сборке.
        context.RegisterCompilationStartAction(start => MarkedClasses.Reconcile(
            start,
            Attribute,
            fields => Items(fields).Where(item => item.IsCustom).Select(item => (item.Id, item.Span)),
            Missing,
            Undeclared));
    }

    private static void Commands(AdditionalFileAnalysisContext context)
    {
        if (!ManifestFiles.IsManifest(context.AdditionalFile.Path) ||
            context.AdditionalFile.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var fields = ManifestJson.Strings(text.ToString());
        var declared = Declared(fields);

        foreach (var item in Items(fields))
        {
            if (!item.IsButton || item.Command.Length == 0 || declared.Contains(item.Command))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Command,
                ManifestFiles.At(context.AdditionalFile.Path, text, item.Span),
                item.Id,
                item.Command));
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
    /// Элементы полосы — из <c>contributions.toolBar</c>, в порядке записи.
    /// </summary>
    /// <remarks>
    /// Элемент узнаётся по месту в структуре манифеста, а не по первому
    /// вхождению имени секции в тексте: разбор общий с <c>ARX0011</c> —
    /// <see cref="ManifestJson"/>. Прежний разбор искал секцию регулярным
    /// выражением и комментариев не видел, хотя студия их читает:
    /// закомментированная старая полоса над настоящей сверялась вместо неё,
    /// элемент в комментарии считался объявленным, а одноимённая секция вне
    /// <c>contributions</c> — полосой студии.
    /// </remarks>
    private static List<Item> Items(List<ManifestJson.Field> fields)
    {
        var order = new List<string>();
        var drafts = new Dictionary<string, Draft>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var match = ItemField.Match(field.Path);

            if (!match.Success)
            {
                continue;
            }

            // Элемент — по полной дороге к нему, а не по номеру: номер 0 есть у
            // первого элемента любого массива, и поля двух разных элементов
            // слились бы в один.
            var item = match.Groups["item"].Value;

            if (!drafts.TryGetValue(item, out var draft))
            {
                draft = new Draft();
                drafts[item] = draft;
                order.Add(item);
            }

            switch (match.Groups["field"].Value.ToLowerInvariant())
            {
                case "id":
                    draft.Id = field.Value;
                    draft.Named = field.Span;
                    break;

                case "kind":
                    draft.Kind = field.Value;
                    break;

                case "command":
                    draft.Command = field.Value;
                    draft.Called = field.Span;
                    break;
            }
        }

        // Место находки — имя элемента: по нему автор его и узнаёт. Безымянной
        // кнопке остаётся её команда — о ней находка и говорит.
        return order
            .Select(item => drafts[item])
            .Select(draft => new Item(draft.Id, draft.Kind, draft.Command, draft.Named ?? draft.Called ?? default))
            .ToList();
    }

    /// <summary>Элемент полосы, пока его поля собираются по одному.</summary>
    private sealed class Draft
    {
        public string Id { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public TextSpan? Named { get; set; }

        public TextSpan? Called { get; set; }
    }

    /// <summary>Команды, объявленные в <c>contributions.commands</c>.</summary>
    private static HashSet<string> Declared(List<ManifestJson.Field> fields) =>
        new(fields.Where(field => CommandField.IsMatch(field.Path)).Select(field => field.Value), StringComparer.Ordinal);
}
