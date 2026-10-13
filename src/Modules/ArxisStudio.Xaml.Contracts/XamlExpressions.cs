using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Xaml;

/// <summary>Что выражение разметки делает со значением.</summary>
/// <remarks>Появилось в контракте 1.3.</remarks>
public enum XamlExpressionKind
{
    /// <summary>Привязка: <c>{Binding}</c>, <c>{CompiledBinding}</c>, <c>{ReflectionBinding}</c>, <c>{MultiBinding}</c>.</summary>
    Binding,

    /// <summary>Ресурс по ключу: <c>{StaticResource}</c>, <c>{DynamicResource}</c>.</summary>
    Resource,

    /// <summary>Статический член типа: <c>{x:Static}</c>.</summary>
    Static,

    /// <summary>Привязка шаблона к контролу, который он одевает: <c>{TemplateBinding}</c>.</summary>
    Template,

    /// <summary>Иное: <c>{x:Null}</c>, <c>{x:Type}</c>, расширение проекта.</summary>
    Other,
}

/// <summary>Работает ли выражение — по тому, что документ объявляет.</summary>
/// <remarks>Появилось в контракте 1.3.</remarks>
public enum XamlExpressionState
{
    /// <summary>Не проверено: проверка не следует за таким выражением или не знает, что оно читает.</summary>
    Unchecked,

    /// <summary>Найдено: путь ведёт к значению.</summary>
    Resolved,

    /// <summary>Сломано: путь или источник не ведут никуда.</summary>
    Broken,
}

/// <summary>Что проверка сказала о выражении, которым элемент пишет член, — именами.</summary>
/// <param name="Member">Член, как его называет документ: <c>Text</c>, <c>Grid.Row</c>.</param>
/// <param name="Kind">Вид выражения.</param>
/// <param name="State">Работает ли оно.</param>
/// <param name="Message">Почему не работает или почему не проверено — словами для человека; null — сказать нечего.</param>
/// <param name="BreaksBuild">Сборка проекта, которая компилирует привязку, на ней остановится.</param>
/// <param name="SourceType">Тип, по которому читался путь: тип данных, элемента или предка; null — не известен.</param>
/// <param name="ResultType">Тип, к которому путь пришёл; null — не пришёл.</param>
/// <remarks>Имена, а не типы: тип — объект поколения. Появилось в контракте 1.3.</remarks>
public sealed record XamlExpressionCheck(
    string Member,
    XamlExpressionKind Kind,
    XamlExpressionState State,
    string? Message,
    bool BreaksBuild,
    XamlTypeInfo? SourceType,
    XamlTypeInfo? ResultType);

/// <summary>Выражения разметки по одному синтаксису: вид и короткая запись — без показа и без поколения.</summary>
/// <remarks>Появилось в контракте 1.3.</remarks>
public static class XamlExpressions
{
    /// <summary>Вид выражения, которым написано значение; null — значение не выражение.</summary>
    /// <param name="written">Значение, как его пишет документ.</param>
    /// <remarks><c>{}</c> в начале — экранированный текст, а не выражение.</remarks>
    public static XamlExpressionKind? Classify(string? written) =>
        Parse(written) is { } extension ? Classify(extension) : null;

    /// <summary>Вид выражения.</summary>
    /// <param name="extension">Выражение.</param>
    public static XamlExpressionKind Classify(XamlMarkupExtensionValue extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        return Bare(extension.TypeName.LocalName) switch
        {
            "Binding" or "CompiledBinding" or "ReflectionBinding" or "MultiBinding" => XamlExpressionKind.Binding,
            "StaticResource" or "DynamicResource" => XamlExpressionKind.Resource,
            "Static" => XamlExpressionKind.Static,
            "TemplateBinding" => XamlExpressionKind.Template,
            _ => XamlExpressionKind.Other,
        };
    }

    /// <summary>
    /// Короткая запись выражения для жетона: путь привязки с её источником — <c>Customer.Name</c>, <c>#slider.Value</c>,
    /// <c>$parent[Window].Title</c>, — ключ ресурса, член статического, свойство шаблона; без них — выражение как написано.
    /// </summary>
    /// <param name="written">Значение, как его пишет документ.</param>
    /// <returns>Запись; null — значение не выражение.</returns>
    public static string? Summarize(string? written) =>
        Parse(written) is { } extension ? Summarize(extension) : null;

    /// <summary>Короткая запись выражения для жетона.</summary>
    /// <param name="extension">Выражение.</param>
    public static string Summarize(XamlMarkupExtensionValue extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var main = Text(extension.PositionalArguments.FirstOrDefault()?.Value);

        var summary = Classify(extension) switch
        {
            XamlExpressionKind.Binding => BindingSummary(extension, main ?? Text(extension.GetArgument("Path")?.Value)),
            XamlExpressionKind.Resource => main ?? Text(extension.GetArgument("ResourceKey")?.Value),
            XamlExpressionKind.Static => main ?? Text(extension.GetArgument("Member")?.Value),
            XamlExpressionKind.Template => main ?? Text(extension.GetArgument("Property")?.Value),
            _ => null,
        };

        return summary is { Length: > 0 } ? summary : extension.ToXamlText();
    }

    /// <summary>Путь привязки вместе с источником, который она называет аргументом.</summary>
    private static string? BindingSummary(XamlMarkupExtensionValue binding, string? path)
    {
        if (Text(binding.GetArgument("ElementName")?.Value) is { Length: > 0 } name)
            return Joined("#" + name, path);

        if (binding.GetArgument("RelativeSource")?.Value is XamlMarkupExtensionValue relative)
        {
            var mode = Text((relative.PositionalArguments.FirstOrDefault() ?? relative.GetArgument("Mode"))?.Value);
            var type = relative.GetArgument("AncestorType")?.Value switch
            {
                XamlLiteralValue literal => literal.Text.Trim(),
                XamlMarkupExtensionValue named => Text((named.PositionalArguments.FirstOrDefault() ?? named.GetArgument("TypeName"))?.Value),
                _ => null,
            };

            // AncestorLevel считает с единицы, а $parent[T;n] — с нуля, как их читает Avalonia.
            var level = int.TryParse(Text(relative.GetArgument("AncestorLevel")?.Value), System.Globalization.CultureInfo.InvariantCulture, out var written)
                ? written
                : 1;

            if (type is not null)
                return Joined(level > 1 ? $"$parent[{type};{level - 1}]" : $"$parent[{type}]", path);

            if (string.Equals(mode, "Self", StringComparison.Ordinal))
                return Joined("$self", path);
        }

        return path;
    }

    private static string Joined(string source, string? path) =>
        string.IsNullOrEmpty(path) || path == "." ? source : source + "." + path;

    private static string? Text(XamlValue? value) =>
        value is XamlLiteralValue { Text: { } text } ? text.Trim() : null;

    private static string Bare(string name) =>
        name.EndsWith("Extension", StringComparison.Ordinal) ? name[..^"Extension".Length] : name;

    // Экранированный текст и простое значение разбор читает литералом сам — так же, как читает атрибут документа.
    private static XamlMarkupExtensionValue? Parse(string? written) =>
        string.IsNullOrEmpty(written) ? null : XamlValue.Parse(written) as XamlMarkupExtensionValue;
}
