using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ключ темы, названный в коде студии, в теме есть и подходит свойству, к которому привязан.
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

    private static AvaloniaProperty? Property(string? owner, string name) =>
        Owners
            .Where(type => owner is null || type.Name == owner)
            .Select(type => type.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null))
            .OfType<AvaloniaProperty>()
            .FirstOrDefault();
}
