using System.Globalization;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>Откуда привязка читает — так, как его выбирают в панели.</summary>
internal enum BindingSourceKind
{
    /// <summary>Данные элемента.</summary>
    Data,

    /// <summary>Элемент формы по имени: <c>#slider</c>.</summary>
    Element,

    /// <summary>Предок по типу: <c>$parent[Window]</c>.</summary>
    Ancestor,

    /// <summary>Сам элемент: <c>$self</c>.</summary>
    Self,
}

/// <summary>
/// Привязка, разобранная на то, что правит панель: источник, путь, режим и дополнительное, — и записанная обратно так,
/// что меняются только тронутые аргументы.
/// </summary>
/// <remarks>
/// <para>
/// Источник читается из обеих записей Avalonia: короткой — <c>#name</c>, <c>$self</c>, <c>$parent[T;n]</c> в начале пути,
/// — и длинной — <c>ElementName</c>, <c>RelativeSource</c>. Пишется короткой, когда источник сменили; не сменили —
/// остаётся, как был написан. Уровень предка — с единицы, как в <c>AncestorLevel</c>; в <c>$parent[T;n]</c> Avalonia
/// считает с нуля, и запись переводит.
/// </para>
/// <para>
/// Остальное — <c>Mode</c>, <c>Converter</c>, <c>StringFormat</c> и прочие — пишется, только если его тронули: аргумент,
/// которого панель не знает (<c>Source</c>, своё расширение), и тот, что не меняли, остаются текстом документа
/// (<see cref="XamlMarkupExtensionValue.WithArgument"/>).
/// </para>
/// </remarks>
internal sealed record BindingDraft
{
    /// <summary>Аргументы, которые панель правит сама, кроме пути и источника.</summary>
    public static readonly string[] Options =
        ["Mode", "Converter", "ConverterParameter", "StringFormat", "FallbackValue", "TargetNullValue", "UpdateSourceTrigger", "Delay", "Priority"];

    /// <summary>Откуда читает.</summary>
    public BindingSourceKind Source { get; init; }

    /// <summary>Имя элемента-источника; пусто — не элемент.</summary>
    public string? ElementName { get; init; }

    /// <summary>Тип предка, как написан; пусто — ближайший предок любого типа.</summary>
    public string? AncestorType { get; init; }

    /// <summary>Какой предок этого типа, считая ближайшего первым.</summary>
    public int AncestorLevel { get; init; } = 1;

    /// <summary>Путь от источника: <c>Customer.Name</c>; пусто — сам источник.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Значения дополнительного — текстом, как их пишет документ; нет ключа — не написано.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Источник записан длинной записью — <c>ElementName</c> или <c>RelativeSource</c>.</summary>
    public bool LongSource { get; init; }

    /// <summary>Путь записан аргументом <c>Path=</c>, а не первым по порядку.</summary>
    public bool NamedPath { get; init; }

    /// <summary>Значение дополнительного; пусто — не написано.</summary>
    /// <param name="name">Аргумент.</param>
    public string? Value(string name) => Values.GetValueOrDefault(name);

    /// <summary>Тот же черновик с другим значением дополнительного.</summary>
    /// <param name="name">Аргумент.</param>
    /// <param name="value">Значение; пусто — снять.</param>
    public BindingDraft With(string name, string? value)
    {
        var values = new Dictionary<string, string>(Values, StringComparer.Ordinal);

        if (string.IsNullOrEmpty(value))
            values.Remove(name);
        else
            values[name] = value;

        return this with { Values = values };
    }

    /// <summary>Путь вместе с источником, как его пишет короткая запись: <c>#slider.Value</c>, <c>$parent[Window;1].Title</c>.</summary>
    public string SourcedPath => Joined(SourcePrefix, WrittenPath);

    /// <summary>
    /// Путь, как его пишут: точка в конце — шаг, которого ещё не сделали. Список членов уже показывает, куда шагнуть, а в
    /// запись она не идёт: такой путь Avalonia не прочтёт.
    /// </summary>
    private string WrittenPath => Path.TrimEnd('.');

    /// <summary>Привязка до шага, которого ещё нет: члены чего показывать при этом пути.</summary>
    /// <param name="prefix">Путь до последней точки.</param>
    public string Probe(string prefix)
    {
        var path = Joined(SourcePrefix, prefix);

        return path.Length == 0 ? "{Binding}" : "{Binding " + path + "}";
    }

    /// <summary>Разбирает написанную привязку; пусто — новая, к данным.</summary>
    /// <param name="binding">Привязка, как её написал документ.</param>
    public static BindingDraft Read(XamlMarkupExtensionValue? binding)
    {
        if (binding is null)
            return new BindingDraft();

        var positional = binding.PositionalArguments.FirstOrDefault();
        var named = binding.GetArgument("Path");
        var path = Text((positional ?? named)?.Value)?.Trim() ?? string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var option in Options)
        {
            if (Text(binding.GetArgument(option)?.Value) is { } value)
                values[option] = value;
        }

        var draft = new BindingDraft { Path = path, Values = values, NamedPath = positional is null && named is not null };

        if (Text(binding.GetArgument("ElementName")?.Value) is { Length: > 0 } element)
            return draft with { Source = BindingSourceKind.Element, ElementName = element.Trim(), LongSource = true };

        if (binding.GetArgument("RelativeSource")?.Value is XamlMarkupExtensionValue relative && Relative(draft, relative) is { } read)
            return read;

        return Compact(draft);
    }

    /// <summary>
    /// Пишет черновик поверх написанного: тронутое — заново, остальное — как было.
    /// </summary>
    /// <param name="original">Привязка, какой её написал документ; пусто — пишется новая.</param>
    /// <param name="typeName">Имя расширения, если его надо сменить: <c>ReflectionBinding</c>; пусто — как было.</param>
    /// <returns>Текст привязки.</returns>
    /// <exception cref="ArgumentException">Значение держит обе кавычки: такое в аргумент не записать.</exception>
    public string Write(XamlMarkupExtensionValue? original, string? typeName = null)
    {
        var start = Read(original);
        var binding = original ?? (XamlMarkupExtensionValue)XamlValue.Parse("{Binding}");

        if (!SameSource(start) || !string.Equals(WrittenPath, start.Path, StringComparison.Ordinal))
        {
            // Источник тот же и записан длинно — путь встаёт на своё место, без приставки; иначе источник уходит в путь.
            var keepsLong = start.LongSource && SameSource(start);
            var path = keepsLong ? WrittenPath : SourcedPath;

            if (!keepsLong)
            {
                binding = binding.WithArgument("ElementName", null).WithArgument("RelativeSource", null);
            }

            var value = path.Length == 0 ? null : new XamlLiteralValue(path);

            binding = start.NamedPath ? binding.WithArgument("Path", value) : binding.WithArgument(null, value);
        }

        foreach (var option in Options)
        {
            var now = Value(option);

            if (string.Equals(now, start.Value(option), StringComparison.Ordinal))
                continue;

            binding = binding.WithArgument(option, ArgumentOf(option, now));
        }

        var text = binding.ToXamlText();

        return typeName is null ? text : Renamed(text, binding.TypeName.ToString(), typeName);
    }

    /// <summary>Тот же ли источник, что у другого черновика.</summary>
    private bool SameSource(BindingDraft other) =>
        Source == other.Source && Source switch
        {
            BindingSourceKind.Element => string.Equals(ElementName, other.ElementName, StringComparison.Ordinal),
            BindingSourceKind.Ancestor => string.Equals(AncestorType, other.AncestorType, StringComparison.Ordinal) && AncestorLevel == other.AncestorLevel,
            _ => true,
        };

    /// <summary>Приставка источника в короткой записи пути.</summary>
    private string SourcePrefix => Source switch
    {
        BindingSourceKind.Element => "#" + ElementName,
        BindingSourceKind.Self => "$self",
        BindingSourceKind.Ancestor => (AncestorType, AncestorLevel) switch
        {
            (null or "", <= 1) => "$parent",
            ({ Length: > 0 } type, <= 1) => $"$parent[{type}]",
            (var type, var level) => $"$parent[{type};{(level - 1).ToString(CultureInfo.InvariantCulture)}]",
        },
        _ => string.Empty,
    };

    /// <summary>Источник короткой записью в начале пути: <c>#name</c>, <c>$self</c>, <c>$parent[T;n]</c>.</summary>
    private static BindingDraft Compact(BindingDraft draft)
    {
        var path = draft.Path;

        if (path.StartsWith('#'))
        {
            var end = path.IndexOfAny(['.', '['], 1);

            return draft with
            {
                Source = BindingSourceKind.Element,
                ElementName = end < 0 ? path[1..] : path[1..end],
                Path = After(path, end),
            };
        }

        if (path.StartsWith("$self", StringComparison.Ordinal))
            return draft with { Source = BindingSourceKind.Self, Path = After(path, "$self".Length) };

        if (!path.StartsWith("$parent", StringComparison.Ordinal))
            return draft;

        var at = "$parent".Length;
        string? type = null;
        var level = 1;

        if (at < path.Length && path[at] == '[' && path.IndexOf(']', at) is var close and > 0)
        {
            var parts = path[(at + 1)..close].Split(';');

            type = parts[0].Trim() is { Length: > 0 } named ? named : null;

            // $parent[T;n] считает с нуля, как Avalonia: ;1 — второй предок этого типа.
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var written))
                level = written + 1;

            at = close + 1;
        }

        return draft with { Source = BindingSourceKind.Ancestor, AncestorType = type, AncestorLevel = Math.Max(1, level), Path = After(path, at) };
    }

    /// <summary><c>RelativeSource</c>: сам, предок — или то, чего панель не правит, и тогда источник остаётся данными.</summary>
    private static BindingDraft? Relative(BindingDraft draft, XamlMarkupExtensionValue relative)
    {
        var mode = Text((relative.PositionalArguments.FirstOrDefault() ?? relative.GetArgument("Mode"))?.Value)?.Trim();
        var type = relative.GetArgument("AncestorType")?.Value switch
        {
            XamlLiteralValue literal => literal.Text.Trim(),
            XamlMarkupExtensionValue named => Text((named.PositionalArguments.FirstOrDefault() ?? named.GetArgument("TypeName"))?.Value)?.Trim(),
            _ => null,
        };
        var level = int.TryParse(Text(relative.GetArgument("AncestorLevel")?.Value), CultureInfo.InvariantCulture, out var written) ? written : 1;

        if (type is not null || string.Equals(mode, "FindAncestor", StringComparison.Ordinal))
            return draft with { Source = BindingSourceKind.Ancestor, AncestorType = type, AncestorLevel = Math.Max(1, level), LongSource = true };

        return string.Equals(mode, "Self", StringComparison.Ordinal)
            ? draft with { Source = BindingSourceKind.Self, LongSource = true }
            : null;
    }

    /// <summary>
    /// Значение дополнительного аргумента: конвертер, написанный выражением, — выражением, остальное — литералом, которому
    /// запись аргумента сама поставит кавычки и экранирование; пусто — снять.
    /// </summary>
    private static XamlValue? ArgumentOf(string option, string? value) => value switch
    {
        null => null,
        _ when string.Equals(option, "Converter", StringComparison.Ordinal) && value.TrimStart().StartsWith('{') => XamlValue.Parse(value.Trim()),
        _ => new XamlLiteralValue(value),
    };

    /// <summary>Значение аргумента текстом: литерал — как есть, выражение — его записью.</summary>
    private static string? Text(XamlValue? value) => value switch
    {
        XamlLiteralValue literal => literal.Text,
        XamlMarkupExtensionValue extension => extension.ToXamlText(),
        _ => null,
    };

    private static string After(string path, int end)
    {
        if (end < 0 || end >= path.Length)
            return string.Empty;

        return path[end] == '.' ? path[(end + 1)..] : path[end..];
    }

    private static string Joined(string source, string path) =>
        source.Length == 0 ? path : path.Length == 0 ? source : path.StartsWith('[') ? source + path : source + "." + path;

    /// <summary>Привязка под другим именем расширения: <c>{ReflectionBinding …}</c> вместо <c>{Binding …}</c>.</summary>
    private static string Renamed(string text, string from, string to)
    {
        var at = text.IndexOf(from, StringComparison.Ordinal);

        return at < 0 ? text : text[..at] + to + text[(at + from.Length)..];
    }
}
