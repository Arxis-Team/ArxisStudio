using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ключ темы, названный в коде строкой, в теме есть и подходит свойству, к которому привязан.
/// </summary>
/// <remarks>
/// В коде значение темы берут привязкой к ключу, и ошибка в его имени не валит ни сборку, ни
/// показ: привязка молча не даёт ничего, и зазор становится нулём. То же с ключом не того вида —
/// число там, где свойство ждёт толщину. В разметке опечатку в ключе хотя бы видно глазами на
/// экране, а в коде, где ключ — строка, её не заметит никто, пока не присмотрится к пикселям.
/// </remarks>
public class CodeResourceKeysTests
{
    /// <summary>Привязка свойства к ключу темы, одной строкой.</summary>
    private static readonly Regex Bindings = new(
        """Bind\(\s*(?:(\w+)\.)?(\w+)Property\s*,\s*[\w.]+\.GetResourceObservable\("(\w+)"\)\s*\)""",
        RegexOptions.Compiled);

    /// <summary>Ключ темы строкой: кавычка, приставка <c>Ax</c> и имя.</summary>
    private static readonly Regex Named = new(@"""(Ax[A-Z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    /// <summary>Где искать свойство, названное без хозяина.</summary>
    private static readonly Type[] Owners =
        [typeof(StackPanel), typeof(Layoutable), typeof(TemplatedControl), typeof(TextBlock), typeof(Shape)];

    [AvaloniaFact]
    public void Every_theme_key_bound_in_studio_code_exists_and_fits_its_property()
    {
        var bound = CodeSources.All()
            .SelectMany(source => Bindings.Matches(source.Text).Select(match => (source.Name, Match: match)))
            .ToList();

        Assert.Contains(bound, binding => binding.Match.Groups[3].Value == "AxDialogButtonMinWidth");

        var application = Application.Current!;
        var wrong = new List<string>();

        foreach (var (name, match) in bound)
        {
            var owner = match.Groups[1].Success ? match.Groups[1].Value : null;
            var property = Property(owner, match.Groups[2].Value);
            var key = match.Groups[3].Value;

            if (property is null)
                wrong.Add($"{name}: свойства {owner}.{match.Groups[2].Value} не нашлось");
            else if (!application.TryFindResource(key, application.ActualThemeVariant, out var value))
                wrong.Add($"{name}: ключа {key} в теме нет");
            else if (value is null || !property.PropertyType.IsInstanceOfType(value))
                wrong.Add($"{name}: {key} — это {value?.GetType().Name}, а {property.Name} ждёт {property.PropertyType.Name}");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// Ключ темы, названный строкой в любом коде — студии, модуля, плагина, шаблона, контролов,
    /// значков, — тема объявляет в обоих вариантах.
    /// </summary>
    /// <remarks>
    /// Проверка выше видит только привязку одной строкой, а ключ строкой берут и иначе: цвет рамки
    /// окна у <c>AxWindow</c>, палитра терминала, значок заставки — через свои помощники с
    /// <c>TryFindResource</c>. Промах там не пустой отступ, а запасной цвет, нарочно похожий на
    /// тему, и глазами он не виден вовсе, пока тема не сменит значение.
    /// <para>
    /// Строка с приставкой <c>Ax</c> в коде — всегда ключ темы: имена типов берут через
    /// <c>nameof</c> и <c>typeof</c>, а не строкой. Комментарии не читаются — в них имена
    /// контролов стоят в <c>cref</c>.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Every_theme_key_named_anywhere_in_code_is_declared_in_both_variants()
    {
        var named = CodeSources.Everywhere()
            .SelectMany(source => source.Text.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .SelectMany(line => Named.Matches(line))
                .Select(match => (source.Name, Key: match.Groups[1].Value)))
            .Distinct()
            .ToList();

        Assert.Contains(named, entry => entry.Key == "AxSurfacePanelColor");
        Assert.Contains(named, entry => entry.Key == "AxScrollThumbColor");
        Assert.Contains(named, entry => entry.Key == "AxGapFormRow");

        var application = Application.Current!;
        var missing = named
            .Where(entry => !application.TryFindResource(entry.Key, ThemeVariant.Dark, out _) ||
                            !application.TryFindResource(entry.Key, ThemeVariant.Light, out _))
            .Select(entry => $"{entry.Name}: {entry.Key}")
            .ToList();

        Assert.True(missing.Count == 0, "в теме нет ключей: " + string.Join("; ", missing));
    }

    private static AvaloniaProperty? Property(string? owner, string name) =>
        Owners
            .Where(type => owner is null || type.Name == owner)
            .Select(type => type.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null))
            .OfType<AvaloniaProperty>()
            .FirstOrDefault();
}
