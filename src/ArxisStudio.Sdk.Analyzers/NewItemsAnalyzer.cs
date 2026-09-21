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
/// Пункты создания («Добавить ▸»): запись в манифесте и её согласие с кодом.
/// </summary>
/// <remarks>
/// Пункт студия читает без сборки, и всё, что в записи не так, она молча обходит: незнакомый вид
/// снимает пункт, путь наружу не создаёт, переменную, которой нет, оставляет в имени файла как
/// написано, а код пункта у спящего расширения, не ждущего своего события, не разбудит никто. Об
/// этом студия говорит в журнал — уже у пользователя. Здесь то же самое слышит автор.
/// <para>
/// Правил три, и разделены они так же, как у полосы (<c>ARX0003</c>–<c>ARX0005</c>): про запись
/// видно по одному манифесту — <c>ARX0015</c> разбирает его на нём самом, и среда покажет находку
/// сразу; про код одного манифеста мало — пункт без класса (<c>ARX0016</c>) и класс без пункта
/// (<c>ARX0017</c>) сверяются в конце компиляции, когда известны оба.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NewItemsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Запись пункта создания студия не прочтёт так, как задумано.</summary>
    public const string RecordId = "ARX0015";

    /// <summary>Пункт с кодом объявлен, а класса нет.</summary>
    public const string MissingId = "ARX0016";

    /// <summary>Класс помечен атрибутом, а манифест о нём молчит.</summary>
    public const string UndeclaredId = "ARX0017";

    private const string Attribute = "NewItemAttribute";
    private const string Namespace = "ArxisStudio.Sdk";

    private static readonly string[] Manifests = { "plugin.json", "module.json" };

    private static readonly string[] Kinds = { "file", "directory", "code" };

    private static readonly string[] Rules = { "file", "identifier" };

    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Поле внутри пункта: дорога к пункту и остаток.</summary>
    private static readonly Regex ItemPath = new(@"^(?<item>contributions\.newItems\[\d+\])(?<rest>\..+)$", Options);

    private static readonly Regex OwnField = new(@"^\.(?<field>id|kind|title|name|nameRule)$", Options);

    private static readonly Regex FileField = new(@"^\.files\[(?<file>\d+)\]\.(?<field>path|template)$", Options);

    private static readonly Regex VariantPath = new(@"^\.variants\[(?<variant>\d+)\](?<rest>\..+)$", Options);

    private static readonly Regex VariantField = new(@"^\.(?<field>id|title)$", Options);

    private static readonly Regex ActivationField = new(@"^activation\[\d+\]$", Options);

    private static readonly Regex ToolBarKind = new(@"^contributions\.toolBar\[\d+\]\.kind$", Options);

    /// <summary>Переменная записи: буквы между знаками доллара.</summary>
    private static readonly Regex Variable = new(@"\$(?<name>[A-Za-z]+)\$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Record = new(
        RecordId,
        "Запись пункта создания студия прочтёт не так, как задумано",
        "{0}: {1}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Пункт меню «Добавить ▸» студия читает по манифесту, не загружая сборку, и неверную запись " +
                     "обходит молча: незнакомый вид снимает пункт, путь наружу не создаёт, лишнюю переменную " +
                     "оставляет в имени как написано, а пункт с кодом у спящего расширения, не ждущего onNewItem:, " +
                     "никого не разбудит. Студия скажет об этом в журнал — но журнал увидит уже пользователь.");

    private static readonly DiagnosticDescriptor Missing = new(
        MissingId,
        "Пункт создания с кодом объявлен, а класса для него нет",
        "{0}: в сборке нет класса с [NewItem(\"{0}\")] — выбор пункта кончится отказом",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Пункт вида code студия ставит в меню по манифесту, а класс, который собирает файлы, ищет по " +
                     "атрибуту, когда расширение поднято. Не найдя его, она откажет человеку и скажет об этом в журнал.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor Undeclared = new(
        UndeclaredId,
        "Класс помечен [NewItem], а пункта с кодом в манифесте нет",
        "{0}: такого пункта вида code нет в contributions.newItems — студия класс не позовёт",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Атрибут связывает объявленное с кодом, но не объявляет: меню «Добавить ▸» студия собирает по " +
                     "манифесту. Класс, чьего пункта в манифесте нет или чей пункт не вида code, не позовёт никто.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Record, Missing, Undeclared);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            return;
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterAdditionalFileAction(Records);
        context.RegisterCompilationStartAction(Start);
    }

    private static void Records(AdditionalFileAnalysisContext context)
    {
        if (!IsManifest(context.AdditionalFile.Path) ||
            context.AdditionalFile.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var fields = ManifestJson.Strings(text.ToString());
        var lazy = IsLazy(context.AdditionalFile.Path, fields);
        var activation = new HashSet<string>(
            fields.Where(field => ActivationField.IsMatch(field.Path)).Select(field => field.Value.Trim()),
            StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in Items(fields))
        {
            foreach (var (span, complaint) in Complaints(item, lazy, activation, ids))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Record,
                    Location.Create(context.AdditionalFile.Path, span, text.Lines.GetLinePositionSpan(span)),
                    item.Named,
                    complaint));
            }
        }
    }

    /// <summary>Что в записи пункта студия прочтёт не так, как задумано, — с местом.</summary>
    private static IEnumerable<(TextSpan Span, string Complaint)> Complaints(
        Item item, bool lazy, HashSet<string> activation, HashSet<string> ids)
    {
        var at = item.IdSpan ?? item.First;
        var kind = item.Kind.Trim();

        if (item.Id.Length == 0)
        {
            yield return (item.First, "у пункта нет id — ни класс, ни событие подъёма с ним не свяжутся, и студия его не покажет");
        }
        else if (!ids.Add(item.Id))
        {
            yield return (at, "пункт с таким id уже объявлен выше — второй студия не покажет");
        }

        if (kind.Length > 0 && !Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            yield return (item.KindSpan ?? at, "вид «" + item.Kind + "» студии не знаком — directory, file или code; пункт не покажется");
        }

        if (item.Title.Trim().Length == 0)
        {
            yield return (at, "у пункта нет строки меню (title) — студия его не покажет");
        }

        if (item.NameRule.Trim().Length > 0 && !Rules.Contains(item.NameRule.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            yield return (item.NameRuleSpan ?? at, "правило имени «" + item.NameRule + "» студии не знакомо — file или identifier; прочтётся как file");
        }

        foreach (var name in Variables(item.Name).Where(name => name != "n"))
        {
            yield return (item.NameSpan ?? at, "в имени по умолчанию подставляется только $n$ — «$" + name + "$» останется как написано");
        }

        var isDirectory = string.Equals(kind, "directory", StringComparison.OrdinalIgnoreCase);
        var isCode = string.Equals(kind, "code", StringComparison.OrdinalIgnoreCase);
        var files = item.Files.Concat(item.Variants.SelectMany(variant => variant.Files)).ToList();

        if ((isDirectory || isCode) && files.Count > 0)
        {
            yield return (files[0].Where, "у пункта вида " + kind.ToLowerInvariant() + " файлов нет — студия их не читает");
        }
        else if (item.Files.Count > 0 && item.Variants.Count > 0)
        {
            yield return (item.Files[0].Where, "у пункта с вариантами файлы объявляет каждый вариант — эти студия не читает");
        }

        if (isDirectory && item.Variants.Count > 0)
        {
            yield return (item.Variants[0].Where, "у каталога вариантов нет — студия их не покажет");
        }

        foreach (var variant in item.Variants.Where(variant => !isDirectory))
        {
            if (variant.Id.Length == 0)
            {
                yield return (variant.Where, "у варианта нет id — студия его не покажет");
            }
            else if (variant.Title.Trim().Length == 0)
            {
                yield return (variant.Where, "у варианта " + variant.Id + " нет строки (title) — студия его не покажет");
            }
        }

        foreach (var file in isDirectory || isCode ? [] : files)
        {
            if (file.Path.Trim().Length == 0)
            {
                yield return (file.Where, "у файла не назван путь (path) — студия пункт не создаст");
            }

            foreach (var name in Variables(file.Path).Where(name => name != "name"))
            {
                yield return (file.PathSpan ?? file.Where,
                    "в пути файла подставляется только $name$ — «$" + name + "$» останется в имени файла как написано; прочие переменные — для содержимого шаблона");
            }

            if (file.Path.Trim().Length > 0 && Leaves(file.Path))
            {
                yield return (file.PathSpan ?? file.Where, "путь уводит из целевого каталога — студия пункт не создаст");
            }

            if (file.Template.Trim().Length > 0 && Leaves(file.Template))
            {
                yield return (file.TemplateSpan ?? file.Where, "шаблон назван вне папки расширения — студия его не прочтёт");
            }
        }

        if (isCode && lazy && item.Id.Length > 0 && !activation.Contains("onNewItem:" + item.Id))
        {
            yield return (at, "расширение спит до своего события, а onNewItem:" + item.Id + " в activation нет — выбор пункта его не разбудит");
        }
    }

    /// <remarks>
    /// Сверка манифеста со сборкой возможна только в конце компиляции: раньше известна лишь одна из
    /// двух записей.
    /// </remarks>
    private static void Start(CompilationStartAnalysisContext context)
    {
        var manifest = context.Options.AdditionalFiles.FirstOrDefault(file => IsManifest(file.Path));

        // Проекта без манифеста правило не касается: так собирают частную зависимость расширения.
        if (manifest?.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var marked = new ConcurrentDictionary<string, Location>(StringComparer.Ordinal);

        context.RegisterSymbolAction(symbol => Mark(symbol, marked), SymbolKind.NamedType);

        context.RegisterCompilationEndAction(end =>
        {
            var code = Items(ManifestJson.Strings(text.ToString()))
                .Where(item => item.Id.Length > 0 && string.Equals(item.Kind.Trim(), "code", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var item in code.Where(item => !marked.ContainsKey(item.Id)))
            {
                var span = item.IdSpan ?? item.First;

                end.ReportDiagnostic(Diagnostic.Create(
                    Missing,
                    Location.Create(manifest.Path, span, text.Lines.GetLinePositionSpan(span)),
                    item.Id));
            }

            var known = new HashSet<string>(code.Select(item => item.Id), StringComparer.Ordinal);

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

            // Место находки — сам атрибут: править нужно там или в манифесте.
            var location = attribute.ApplicationSyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : context.Symbol.Locations.FirstOrDefault() ?? Location.None;

            marked[id] = location;
        }
    }

    /// <summary>
    /// Спит ли расширение до события — так, как это решает студия.
    /// </summary>
    /// <remarks>
    /// Правило то же, что у <c>PluginActivation.IsEager</c>: без событий — поднимается сразу, с
    /// <c>onStartup</c> или <c>onToolWindow:</c> — тоже, и со своим контролом в полосе — тоже. Модуль
    /// не спит никогда: студия поднимает встроенные модули при запуске, мимо событий, и жалоба о
    /// забытом событии была бы у него ложной. Уедет модуль в плагин — манифест станет
    /// <c>plugin.json</c>, и жалоба придёт тогда, когда станет правдой.
    /// </remarks>
    private static bool IsLazy(string manifest, List<ManifestJson.Field> fields)
    {
        if (manifest.EndsWith("module.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var events = fields.Where(field => ActivationField.IsMatch(field.Path)).Select(field => field.Value.Trim()).ToList();

        if (events.Count == 0 ||
            events.Any(value => string.Equals(value, "onStartup", StringComparison.OrdinalIgnoreCase) ||
                                value.StartsWith("onToolWindow:", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !fields.Any(field => ToolBarKind.IsMatch(field.Path) &&
                                    string.Equals(field.Value.Trim(), "custom", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Уводит ли путь из своего каталога: корень, диск или <c>..</c> среди сегментов.</summary>
    private static bool Leaves(string path)
    {
        var trimmed = path.Trim();

        if (trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.StartsWith("\\", StringComparison.Ordinal) ||
            trimmed.IndexOf(':') >= 0)
        {
            return true;
        }

        return trimmed.Split('/', '\\').Any(segment => segment.Trim() == "..");
    }

    private static IEnumerable<string> Variables(string value) =>
        Variable.Matches(value).Cast<Match>().Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal);

    /// <summary>Пункты создания — из <c>contributions.newItems</c>, в порядке записи.</summary>
    /// <remarks>
    /// Пункт — по полной дороге к нему, а не по номеру: номер 0 есть у первого элемента любого
    /// массива, и поля двух разных пунктов слились бы в один. Узнаётся пункт по любому строковому
    /// полю — и по тому, которое правило не проверяет.
    /// </remarks>
    private static List<Item> Items(List<ManifestJson.Field> fields)
    {
        var order = new List<string>();
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            var match = ItemPath.Match(field.Path);

            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["item"].Value;

            if (!items.TryGetValue(key, out var item))
            {
                item = new Item(field.Span);
                items[key] = item;
                order.Add(key);
            }

            item.Take(match.Groups["rest"].Value, field);
        }

        return order.Select(key => items[key]).ToList();
    }

    /// <summary>Пункт создания, как он записан в манифесте.</summary>
    private sealed class Item(TextSpan first)
    {
        public TextSpan First { get; } = first;

        public string Id { get; private set; } = string.Empty;

        public string Kind { get; private set; } = string.Empty;

        public string Title { get; private set; } = string.Empty;

        public string Name { get; private set; } = string.Empty;

        public string NameRule { get; private set; } = string.Empty;

        public TextSpan? IdSpan { get; private set; }

        public TextSpan? KindSpan { get; private set; }

        public TextSpan? NameSpan { get; private set; }

        public TextSpan? NameRuleSpan { get; private set; }

        public List<File> Files { get; } = new();

        public List<Variant> Variants { get; } = new();

        /// <summary>Как пункт назвать в находке.</summary>
        public string Named => Id.Length > 0 ? "пункт создания " + Id : "пункт создания без id";

        public void Take(string rest, ManifestJson.Field field)
        {
            var own = OwnField.Match(rest);

            if (own.Success)
            {
                switch (own.Groups["field"].Value.ToLowerInvariant())
                {
                    case "id":
                        Id = field.Value.Trim();
                        IdSpan = field.Span;
                        break;

                    case "kind":
                        Kind = field.Value;
                        KindSpan = field.Span;
                        break;

                    case "title":
                        Title = field.Value;
                        break;

                    case "name":
                        Name = field.Value;
                        NameSpan = field.Span;
                        break;

                    case "namerule":
                        NameRule = field.Value;
                        NameRuleSpan = field.Span;
                        break;
                }

                return;
            }

            if (FileField.Match(rest) is { Success: true } file)
            {
                File.Take(Files, file, field);
                return;
            }

            if (VariantPath.Match(rest) is not { Success: true } path)
            {
                return;
            }

            var index = path.Groups["variant"].Value;
            var variant = Variants.FirstOrDefault(each => each.Index == index);

            if (variant is null)
            {
                variant = new Variant(index, field.Span);
                Variants.Add(variant);
            }

            variant.Take(path.Groups["rest"].Value, field);
        }
    }

    /// <summary>Вариант пункта.</summary>
    private sealed class Variant(string index, TextSpan where)
    {
        public string Index { get; } = index;

        public TextSpan Where { get; } = where;

        public string Id { get; private set; } = string.Empty;

        public string Title { get; private set; } = string.Empty;

        public List<File> Files { get; } = new();

        public void Take(string rest, ManifestJson.Field field)
        {
            if (VariantField.Match(rest) is { Success: true } own)
            {
                if (string.Equals(own.Groups["field"].Value, "id", StringComparison.OrdinalIgnoreCase))
                {
                    Id = field.Value.Trim();
                }
                else
                {
                    Title = field.Value;
                }

                return;
            }

            if (FileField.Match(rest) is { Success: true } file)
            {
                File.Take(Files, file, field);
            }
        }
    }

    /// <summary>Файл пункта или варианта.</summary>
    private sealed class File(string index, TextSpan where)
    {
        public string Index { get; } = index;

        public TextSpan Where { get; } = where;

        public string Path { get; private set; } = string.Empty;

        public string Template { get; private set; } = string.Empty;

        public TextSpan? PathSpan { get; private set; }

        public TextSpan? TemplateSpan { get; private set; }

        /// <summary>Кладёт поле файла в его запись, заводя её при первом поле.</summary>
        public static void Take(List<File> files, Match match, ManifestJson.Field field)
        {
            var index = match.Groups["file"].Value;
            var file = files.FirstOrDefault(each => each.Index == index);

            if (file is null)
            {
                file = new File(index, field.Span);
                files.Add(file);
            }

            if (string.Equals(match.Groups["field"].Value, "path", StringComparison.OrdinalIgnoreCase))
            {
                file.Path = field.Value;
                file.PathSpan = field.Span;
            }
            else
            {
                file.Template = field.Value;
                file.TemplateSpan = field.Span;
            }
        }
    }

    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Манифест ли это — по имени файла, как у <c>ARX0003</c>.</summary>
    private static bool IsManifest(string path)
    {
        var separator = path.LastIndexOfAny(Separators);
        var name = separator < 0 ? path : path.Substring(separator + 1);

        foreach (var manifest in Manifests)
        {
            if (string.Equals(name, manifest, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
