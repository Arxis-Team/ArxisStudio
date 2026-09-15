using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Вид контрола — свойством, а не классом темы: в разметке и в коде студии, модулей, плагинов и
/// шаблона плагина.
/// </summary>
/// <remarks>
/// С SDK 6.0 тема классов вида не знает: кнопка — <c>Appearance</c> и <c>Size</c>, текст —
/// <c>AxText.Role</c> и <c>AxText.Tone</c>, поле — <c>AxValidation.State</c>. Класс, оставшийся
/// где-то по старой памяти, ничего не сломает на сборке и молча даст контрол по умолчанию, —
/// ровно та беда, ради которой классы и сняты. Классы, у которых свои стили в студии или в
/// витрине, — <c>nav</c>, <c>row</c>, <c>settings-row</c>, — сюда не относятся: их стили рядом.
/// </remarks>
public class ThemeClassesTests
{
    /// <summary>Классы вида, которые тема знала до 6.0.</summary>
    private static readonly HashSet<string> Retired = new(StringComparer.Ordinal)
    {
        "accent", "ghost", "icon", "compact", "danger",
        "small", "caption", "title", "section", "mono",
        "dim", "dimmer", "ok", "warn", "bad",
        "kbd", "round", "orange", "green", "purple", "red",
        "large", "error", "warning", "framed", "current",
    };

    private static readonly Regex MarkupList = new("""(?<![\w.])Classes="([^"]*)(?=")""", RegexOptions.Compiled);
    private static readonly Regex MarkupSwitch = new("""(?<![\w])(?:\w+:)?Classes\.([\w-]+)=""", RegexOptions.Compiled);
    private static readonly Regex CodeList = new("""Classes\s*=\s*\{([^}]*)\}""", RegexOptions.Compiled);
    private static readonly Regex CodeCall = new("""Classes\.(?:Add|Set|Remove|Contains)\(\s*"([^"]+)(?=")""", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"""([^""]+)(?="")", RegexOptions.Compiled);

    [Fact]
    public void No_retired_theme_class_is_left_in_markup_or_code()
    {
        var markup = MarkupSources.All().SelectMany(source =>
            MarkupList.Matches(source.Text).SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Concat(MarkupSwitch.Matches(source.Text).Select(match => match.Groups[1].Value))
                .Select(name => (source.Name, Class: name)));

        var code = CodeSources.Everywhere().SelectMany(source =>
            CodeList.Matches(source.Text).SelectMany(match => Quoted.Matches(match.Groups[1].Value).Select(found => found.Groups[1].Value))
                .Concat(CodeCall.Matches(source.Text).Select(match => match.Groups[1].Value))
                .Select(name => (source.Name, Class: name)));

        var found = markup.Concat(code)
            .Where(entry => Retired.Contains(entry.Class))
            .Select(entry => $"{entry.Name}: {entry.Class}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(found.Count == 0, "классы вида, которых тема больше не знает: " + string.Join("; ", found));
    }
}
