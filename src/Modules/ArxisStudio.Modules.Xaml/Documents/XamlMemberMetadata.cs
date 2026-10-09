using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>
/// Что говорят о членах и типах их метаданные: базы, интерфейсы, атрибуты .NET, пространство владельца.
/// </summary>
/// <remarks>
/// <para>
/// Ответы — имена и строки: тип — объект поколения, а сведения о нём переживают замену. Кэши держатся на
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>, где ключ — сам тип или сборка: ссылка на ключ слабая, и
/// кэш не держит поколение, которое пора отпустить, а значения в нём — только имена.
/// </para>
/// <para>
/// Атрибуты читаются со свойства CLR, а у присоединённого — с его геттера <c>GetИмя</c>: туда их ставит
/// автор, и так же их читает окно свойств Visual Studio. Атрибуты Avalonia (<c>[PrivateApi]</c>,
/// <c>[Unstable]</c>) узнаются по имени: от их сборки служба не зависит.
/// </para>
/// </remarks>
internal static class XamlMemberMetadata
{
    private static readonly ConditionalWeakTable<Type, XamlTypeInfo> Types = new();
    private static readonly ConditionalWeakTable<MemberInfo, XamlMemberAttributes> Attributes = new();
    private static readonly ConditionalWeakTable<Assembly, XamlTypeCatalog> Catalogs = new();

    /// <summary>Полное имя типа, как его пишут сведения: у вложенного — через точку.</summary>
    public static string FullName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    /// <summary>Базовые классы — от ближайшего, без <c>System.Object</c> и <c>System.ValueType</c>.</summary>
    public static ImmutableArray<string> Bases(Type type)
    {
        var bases = ImmutableArray.CreateBuilder<string>();

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current == typeof(object) || current == typeof(ValueType) || current == typeof(Enum))
                break;

            bases.Add(FullName(current));
        }

        return bases.ToImmutable();
    }

    /// <summary>Тип значения — именами; у <c>Nullable</c> — то, что внутри, с отметкой.</summary>
    public static XamlTypeInfo TypeOf(Type type) => Types.GetValue(type, Describe);

    /// <summary>Атрибуты .NET члена — с его свойства CLR или с геттера присоединённого.</summary>
    public static XamlMemberAttributes AttributesOf(XamlMemberDescriptor member)
    {
        MemberInfo? source = member.ClrProperty
                             ?? (MemberInfo?)member.AttachedAccessors.Getter
                             ?? member.DeclaringType.GetProperty(member.Name.Split('.')[^1],
                                 BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        return source is null ? XamlMemberAttributes.None : Attributes.GetValue(source, Read);
    }

    /// <summary>
    /// Пространство разметки, в котором документ пишет тип, и префикс, который предлагает его сборка.
    /// </summary>
    /// <remarks>
    /// Тем же чтением, каким загрузчик разрешает имена (<see cref="XamlTypeCatalog"/>): так присоединённый член
    /// владельца из проекта пишется с префиксом, который загрузка поймёт. Каталог сборки читается один раз — и
    /// держит только имена.
    /// </remarks>
    public static (string? Namespace, string? Prefix) NamespaceOf(Type owner)
    {
        try
        {
            var catalog = Catalogs.GetValue(owner.Assembly, assembly => XamlTypeCatalog.Create([assembly]));

            return catalog.Find(owner.FullName ?? owner.Name) is { } entry
                ? (entry.XmlNamespace, entry.SuggestedPrefix)
                : (owner.Namespace is { Length: > 0 } clr ? "using:" + clr : null, null);
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or FileLoadException or ReflectionTypeLoadException)
        {
            return (owner.Namespace is { Length: > 0 } clr ? "using:" + clr : null, null);
        }
    }

    /// <summary>
    /// Пишется ли значение типа атрибутом — текстом: числа, строки, перечисления, структуры с <c>Parse</c> —
    /// <c>Thickness</c>, <c>GridLength</c>, — и типы с конвертером. Шаблон, меню или стиль атрибутом не пишут.
    /// </summary>
    public static bool AcceptsText(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;

        return value == typeof(string)
               || value == typeof(object)
               || value.IsPrimitive
               || value.IsEnum
               || value.IsValueType
               || value.GetCustomAttribute<TypeConverterAttribute>(inherit: true) is not null
               || value.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]) is not null;
    }

    private static XamlTypeInfo Describe(Type declared)
    {
        var underlying = Nullable.GetUnderlyingType(declared);
        var type = underlying ?? declared;

        return new XamlTypeInfo(
            FullName(type),
            Bases(type),
            [.. type.GetInterfaces().Select(FullName).Order(StringComparer.Ordinal)],
            type.IsEnum ? [.. Enum.GetNames(type)] : [],
            type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false),
            underlying is not null || !declared.IsValueType);
    }

    private static XamlMemberAttributes Read(MemberInfo member)
    {
        var attributes = member.GetCustomAttributes(inherit: true).OfType<Attribute>().ToList();

        var range = attributes.OfType<RangeAttribute>().FirstOrDefault();
        var obsolete = attributes.OfType<ObsoleteAttribute>().FirstOrDefault();

        return new XamlMemberAttributes(
            attributes.OfType<DisplayNameAttribute>().FirstOrDefault()?.DisplayName is { Length: > 0 } shown ? shown : null,
            attributes.OfType<CategoryAttribute>().FirstOrDefault()?.Category,
            attributes.OfType<DescriptionAttribute>().FirstOrDefault()?.Description is { Length: > 0 } said ? said : null,
            attributes.OfType<DefaultValueAttribute>().FirstOrDefault() is { } fallback ? Text(fallback.Value) : null,
            attributes.OfType<BrowsableAttribute>().FirstOrDefault()?.Browsable ?? true,
            attributes.OfType<EditorBrowsableAttribute>().FirstOrDefault()?.State switch
            {
                EditorBrowsableState.Never => XamlEditorBrowsable.Never,
                EditorBrowsableState.Advanced => XamlEditorBrowsable.Advanced,
                _ => XamlEditorBrowsable.Always,
            },
            attributes.OfType<ReadOnlyAttribute>().FirstOrDefault()?.IsReadOnly ?? false,
            Number(range?.Minimum),
            Number(range?.Maximum),
            obsolete is null ? null : obsolete.Message ?? string.Empty,
            attributes.Any(attribute => Named(attribute, "Avalonia.Metadata.PrivateApiAttribute")),
            attributes.Any(attribute => Named(attribute, "Avalonia.Metadata.UnstableAttribute")));
    }

    private static bool Named(Attribute attribute, string fullName) =>
        string.Equals(attribute.GetType().FullName, fullName, StringComparison.Ordinal);

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Граница <c>[Range]</c> числом; бесконечность и не число — без границы.</summary>
    private static double? Number(object? value)
    {
        if (value is null)
            return null;

        try
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            return double.IsFinite(number) && Math.Abs(number) < double.MaxValue ? number : null;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }
}
