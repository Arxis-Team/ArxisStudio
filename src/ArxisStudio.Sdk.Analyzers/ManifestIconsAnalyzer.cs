using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// ARX0011: запись значка в манифесте студия не разберёт.
/// </summary>
/// <remarks>
/// Значок кнопки полосы, панели, команды и пункта создания — имя из набора
/// студии (<c>arxis:Play</c>) или контур в сетке 16. Запись, которая не разобралась,
/// ничего не отменяет: элемент встаёт без значка, а студия пишет о записи в
/// журнал — но журнал читает уже пользователь, а не автор. Опечатку в имени
/// дешевле поймать здесь, и назвать заодно то имя, которое имелось в виду.
/// <para>
/// Имена берутся из самой компиляции — из типа <c>AxIcons</c>, против которого
/// расширение собирается, и ровно так, как их берёт студия: открытые статические
/// свойства-геометрии самого типа, без вложенных наборов. Копия списка здесь
/// разошлась бы с набором на первом новом глифе. Набор в компиляцию не
/// подключён — имя сверять не с чем, и проверяется только сама запись.
/// </para>
/// <para>
/// Контур здесь не разбирается: разборщик Avalonia в анализатор не перенести, а
/// свой разошёлся бы с ним на первом же непривычном написании. Ловится то, что
/// контуром быть не может, — слово без приставки и путь к картинке: у контура
/// есть числа, у слова их нет.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManifestIconsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Код диагностики.</summary>
    public const string DiagnosticId = "ARX0011";

    /// <summary>Чем начинается ссылка на глиф набора.</summary>
    private const string Prefix = "arxis:";

    private const string Set = "ArxisStudio.Icons.AxIcons";

    private static readonly string[] Manifests = { "plugin.json", "module.json" };

    private static readonly string[] Images = { ".png", ".svg", ".ico", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>
    /// Значок в одной из четырёх секций вкладов или у варианта пункта создания — и больше нигде.
    /// </summary>
    private static readonly Regex Icon = new(
        @"^contributions\.(?:(?<section>toolBar|toolWindows|commands|newItems)\[(?<index>\d+)\]|(?<section>newItems)\[(?<index>\d+)\]\.variants\[(?<variant>\d+)\])\.icon$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Слово: буквы без чисел. Контуром оно не бывает — у контура есть координаты.</summary>
    private static readonly Regex Word = new(@"^[A-Za-z_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Запись значка в манифесте студия не разберёт",
        "{0}: «{1}» — {2}",
        "ArxisStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Значок кнопки полосы, панели, команды и пункта создания — имя из набора студии (arxis:Play) или контур в сетке 16. " +
                     "Запись, которая не разобралась, ничего не отменяет: элемент встаёт без значка, а студия пишет о ней " +
                     "в журнал — уже у пользователя, а не у автора.");

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

        // Имена набора — у компиляции, запись — в манифесте. Манифест
        // разбирается на нём самом, как у ARX0002: среда покажет находку сразу,
        // а не после разбора всего кода.
        context.RegisterCompilationStartAction(start =>
        {
            var names = Names(start.Compilation);

            start.RegisterAdditionalFileAction(file => Check(file, names));
        });
    }

    private static void Check(AdditionalFileAnalysisContext context, HashSet<string>? names)
    {
        var manifest = context.AdditionalFile;

        if (!IsManifest(manifest.Path) || manifest.GetText(context.CancellationToken) is not { } text)
        {
            return;
        }

        var fields = ManifestJson.Strings(text.ToString());

        foreach (var field in fields)
        {
            var icon = Icon.Match(field.Path);

            if (!icon.Success || Complaint(field.Value, names) is not { } complaint)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                Location.Create(manifest.Path, field.Span, text.Lines.GetLinePositionSpan(field.Span)),
                Owner(fields, icon),
                field.Value,
                complaint));
        }
    }

    /// <summary>
    /// Что не так с записью; null — студия её разберёт или проверять тут нечего.
    /// </summary>
    /// <remarks>
    /// Порядок проверок — порядок чтения в студии: сперва приставка, потом всё
    /// остальное считается контуром. Пробелы по краям студия снимает и сама.
    /// </remarks>
    private static string? Complaint(string value, HashSet<string>? names)
    {
        var record = value.Trim();

        if (record.Length == 0)
        {
            return null;
        }

        if (record.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = record.Substring(Prefix.Length).Trim();

            if (name.Length == 0)
            {
                return "после arxis: не названо имя значка";
            }

            if (names is null || names.Contains(name))
            {
                return null;
            }

            return Closest(name, names) switch
            {
                null => "в наборе студии такого значка нет",
                { } same when string.Equals(same, name, StringComparison.OrdinalIgnoreCase) =>
                    "имя сверяется с набором строго, как записано в коде: arxis:" + same,
                { } near => "в наборе студии такого значка нет; ближе всего — arxis:" + near,
            };
        }

        if (IsImage(record))
        {
            return "картинка файлом не принимается: значок — имя из набора (arxis:Play) или контур в сетке 16";
        }

        if (Word.IsMatch(record))
        {
            var meant = names is null ? null : names.Contains(record) ? record : Closest(record, names);

            return meant is null
                ? "это не контур: имя из набора пишется с приставкой arxis:, контур — командами пути в сетке 16"
                : "это не контур: имя из набора пишется с приставкой — arxis:" + meant;
        }

        return null;
    }

    /// <summary>
    /// Кому принадлежит значок — словами и по имени, как автор его узнает.
    /// </summary>
    /// <remarks>
    /// Имя берётся у того же элемента, а не у ближайшего <c>id</c> в тексте: у
    /// панели внутри лежит объект места, и поле с тем же именем в нём — не её.
    /// </remarks>
    private static string Owner(List<ManifestJson.Field> fields, Match icon)
    {
        var section = icon.Groups["section"].Value;
        var item = "contributions." + section + "[" + icon.Groups["index"].Value + "].id";
        var id = fields.FirstOrDefault(field => string.Equals(field.Path, item, StringComparison.OrdinalIgnoreCase)).Value;

        var what = section.ToLowerInvariant() switch
        {
            "toolbar" => "элемент полосы",
            "toolwindows" => "панель",
            "newitems" => "пункт создания",
            _ => "команда",
        };

        var owner = id is { Length: > 0 } ? what + " " + id : what + " без id";

        if (!icon.Groups["variant"].Success)
        {
            return owner;
        }

        var path = item.Substring(0, item.Length - ".id".Length) + ".variants[" + icon.Groups["variant"].Value + "].id";
        var variant = fields.FirstOrDefault(field => string.Equals(field.Path, path, StringComparison.OrdinalIgnoreCase)).Value;

        return owner + ", вариант " + (variant is { Length: > 0 } ? variant : "без id");
    }

    /// <summary>
    /// Имена набора в этой компиляции; null — набор не подключён.
    /// </summary>
    /// <remarks>
    /// Те же, что берёт студия отражением: открытые статические свойства самого
    /// типа, чей тип — геометрия Avalonia или её наследник. Вложенные наборы —
    /// значки палитры дизайнера — в манифест не отдаются, и здесь их нет.
    /// </remarks>
    private static HashSet<string>? Names(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(Set) is not { } set)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in set.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } property &&
                IsGeometry(property.Type))
            {
                names.Add(property.Name);
            }
        }

        return names;
    }

    private static bool IsGeometry(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "Geometry" && current.ContainingNamespace?.ToDisplayString() == "Avalonia.Media")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Имя набора, ближе всего похожее на записанное; null — похожего нет.
    /// </summary>
    /// <remarks>
    /// Похожим считается отличие в регистре и опечатка в одну правку — у имени
    /// от восьми букв в две; переставленные соседние буквы — одна правка.
    /// Допуск узкий нарочно: у шестибуквенного имени две правки превращают
    /// Wrench в Branch, а подсказка, которая называет чужой значок, хуже молчания.
    /// </remarks>
    private static string? Closest(string name, HashSet<string> names)
    {
        var allowed = name.Length >= 8 ? 2 : 1;
        var asked = name.ToLowerInvariant();
        string? best = null;
        var shortest = int.MaxValue;

        foreach (var candidate in names.OrderBy(candidate => candidate, StringComparer.Ordinal))
        {
            var distance = Distance(asked, candidate.ToLowerInvariant());

            if (distance <= allowed && distance < shortest)
            {
                best = candidate;
                shortest = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Сколько правок отделяют одно имя от другого: заменить, вставить, убрать
    /// букву или переставить две соседние.
    /// </summary>
    private static int Distance(string from, string to)
    {
        var table = new int[from.Length + 1, to.Length + 1];

        for (var row = 0; row <= from.Length; row++)
        {
            table[row, 0] = row;
        }

        for (var column = 0; column <= to.Length; column++)
        {
            table[0, column] = column;
        }

        for (var row = 1; row <= from.Length; row++)
        {
            for (var column = 1; column <= to.Length; column++)
            {
                var cost = from[row - 1] == to[column - 1] ? 0 : 1;
                var best = Math.Min(Math.Min(table[row - 1, column] + 1, table[row, column - 1] + 1), table[row - 1, column - 1] + cost);

                if (row > 1 && column > 1 && from[row - 1] == to[column - 2] && from[row - 2] == to[column - 1])
                {
                    best = Math.Min(best, table[row - 2, column - 2] + 1);
                }

                table[row, column] = best;
            }
        }

        return table[from.Length, to.Length];
    }

    private static bool IsImage(string record)
    {
        if (record.IndexOf('/') >= 0 || record.IndexOf('\\') >= 0)
        {
            return true;
        }

        foreach (var extension in Images)
        {
            if (record.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Манифест ли это — по имени файла.</summary>
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
