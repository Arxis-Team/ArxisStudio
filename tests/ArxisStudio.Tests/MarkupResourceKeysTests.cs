using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ключ темы, названный разметкой студии, модуля, плагина или шаблона, тема объявляет, а зависящий
/// от варианта берётся динамически.
/// </summary>
/// <remarks>
/// Опечатка в ключе разметки не валит ни сборку, ни показ: DynamicResource молча отдаёт пустоту, и
/// заметить её можно только глазами, в той теме и в том состоянии экрана, где ключ стоит. У
/// расширений извне прежние имена называет ARX0009, а разметку самой студии и её модулей сверяет
/// этот тест — с живой темой, какой её собирает приложение.
/// <para>
/// Плотность здесь не перебирается намеренно: ступени плотности переопределяют только объявленные
/// темой ключи и новых не заводят — это держит <c>DensityTests</c> темы, — поэтому ключ, найденный
/// в обычной ступени, есть в любой.
/// </para>
/// <para>
/// StaticResource читает значение один раз, при загрузке, и смены темы не видит: кисть, взятая
/// так, осталась бы тёмной в светлой студии.
/// </para>
/// </remarks>
public class MarkupResourceKeysTests
{
    private static readonly Regex Reference = new(
        """\{(?:\w+:)?(Dynamic|Static)Resource\s+(?:ResourceKey\s*=\s*)?([A-Za-z_][\w.]*)\s*\}|<StaticResource\s[^>]*?ResourceKey="([A-Za-z_][\w.]*)(?=")""",
        RegexOptions.Compiled);

    private static readonly Regex Declared = new("""x:Key="([A-Za-z_][\w.]*)(?=")""", RegexOptions.Compiled);

    /// <summary>Каждый ключ разметки разрешается в обоих вариантах, если файл не объявил его сам.</summary>
    [AvaloniaFact]
    public void Every_key_named_in_markup_is_declared_in_both_variants()
    {
        var application = Application.Current!;
        var named = References().ToList();

        Assert.Contains(named, reference => reference.File == "TerminalPanelView.axaml");
        Assert.Contains(named, reference => reference.File == "DockingStyles.axaml");
        Assert.Contains(named, reference => reference.File == "PluginPanelView.axaml");

        var missing = named
            .Where(reference => !Found(application, reference.Key, ThemeVariant.Dark, out _) ||
                                !Found(application, reference.Key, ThemeVariant.Light, out _))
            .Select(reference => $"{reference.File}: {reference.Key}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "в теме нет ключей: " + string.Join("; ", missing));
    }

    /// <summary>Ключ, у которого в тёмном и светлом варианте разные значения, статически не берётся.</summary>
    [AvaloniaFact]
    public void A_key_that_follows_the_variant_is_never_taken_statically()
    {
        var application = Application.Current!;

        var frozen = References()
            .Where(reference => reference.Static)
            .Where(reference =>
                Found(application, reference.Key, ThemeVariant.Dark, out var dark) &&
                Found(application, reference.Key, ThemeVariant.Light, out var light) &&
                !Equals(dark, light))
            .Select(reference => $"{reference.File}: {reference.Key}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(frozen.Count == 0, "берутся статически, а зависят от варианта: " + string.Join("; ", frozen));
    }

    /// <summary>Ссылки на ключи, кроме объявленных в том же файле.</summary>
    private static IEnumerable<(string File, string Key, bool Static)> References() =>
        MarkupSources.All().SelectMany(source =>
        {
            var local = Declared.Matches(source.Text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            return Reference.Matches(source.Text)
                .Select(match => match.Groups[3].Success
                    ? (source.Name, match.Groups[3].Value, true)
                    : (source.Name, match.Groups[2].Value, match.Groups[1].Value == "Static"))
                .Where(reference => !local.Contains(reference.Item2));
        });

    private static bool Found(Application application, string key, ThemeVariant variant, out object? value) =>
        application.TryFindResource(key, variant, out value);
}
