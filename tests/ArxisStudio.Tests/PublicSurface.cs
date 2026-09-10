using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Публичная поверхность сборки — строками, по которым видно, что изменилось.
/// </summary>
/// <remarks>
/// Сборка, которую плагины видят общей, — обещание: плагин собран против её
/// типов, и снятый или изменённый член ломает его на первом обращении, уже у
/// человека. Поверхность сверяется с записанной, и сдвиг указателя подмодуля,
/// её поменявший, падает здесь, а не у автора плагина.
/// <para>
/// Одна строка на член, по алфавиту: заголовок типа первым, члены — следом
/// через <c>::</c>. Разница двух прогонов так читается глазами, а порядок
/// объявлений в исходнике на неё не влияет. Обнуляемость верхнего уровня в
/// строке есть, у аргументов обобщений — нет: её сдвиг двоичной совместимости не
/// ломает, а строк стало бы вдвое больше. Интерфейсы — только свои: унаследованные
/// от базового типа меняет рантайм, а не сборка, и новая версия .NET развела бы
/// поверхность без единой правки в ней.
/// </para>
/// </remarks>
internal static class PublicSurface
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Поверхность сборки: строка на тип и на каждый видимый снаружи член.</summary>
    /// <param name="assembly">Чью поверхность описать.</param>
    public static IReadOnlyList<string> Describe(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var context = new NullabilityInfoContext();
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            var name = TypeName(type);

            // Одинарное двоеточие сортируется раньше двойного: заголовок типа
            // встаёт над его членами.
            lines.Add($"{name} : {Header(type)}{Bases(type)}");

            if (type.IsEnum)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                    lines.Add($"{name} :: {field.Name} = {Literal(field.GetRawConstantValue())}");

                continue;
            }

            foreach (var member in type.GetMembers(Declared))
            {
                if (Member(member, context) is { } line)
                    lines.Add($"{name} :: {line}");
            }
        }

        lines.Sort(StringComparer.Ordinal);

        return lines;
    }

    /// <summary>
    /// Сверяет поверхность с записанной и называет разницу, если она есть.
    /// </summary>
    /// <param name="baseline">Файл записанной поверхности в репозитории.</param>
    /// <param name="actual">Поверхность, какая она сейчас.</param>
    /// <remarks>
    /// Новая поверхность кладётся во временную папку, а не в репозиторий:
    /// переписать обещание плагинам — решение человека, и тест его за человека не
    /// принимает.
    /// </remarks>
    public static void AssertRecorded(string baseline, IReadOnlyList<string> actual)
    {
        ArgumentNullException.ThrowIfNull(actual);

        var recorded = File.Exists(baseline) ? File.ReadAllLines(baseline) : [];

        var added = actual.Except(recorded, StringComparer.Ordinal).ToList();
        var removed = recorded.Except(actual, StringComparer.Ordinal).ToList();

        if (added.Count == 0 && removed.Count == 0)
            return;

        var fresh = Path.Combine(Path.GetTempPath(), $"arxis-surface-{Path.GetFileName(baseline)}");

        File.WriteAllLines(fresh, actual);

        Assert.Fail(
            $"поверхность {Path.GetFileName(baseline)} разошлась с записанной.\n" +
            $"Снято или изменено — {removed.Count.ToString(CultureInfo.InvariantCulture)}:\n  - " +
            string.Join("\n  - ", removed.Take(40)) + "\n" +
            $"Добавлено — {added.Count.ToString(CultureInfo.InvariantCulture)}:\n  + " +
            string.Join("\n  + ", added.Take(40)) + "\n" +
            "Снятое ломает уже собранные плагины — это новый мажор StudioSdk.Version; добавленное — минор. " +
            $"Решив, перенесите новую поверхность из {fresh}.");
    }

    private static string Header(Type type)
    {
        if (type.IsInterface)
            return "interface";

        if (type.IsEnum)
            return type.IsDefined(typeof(FlagsAttribute), inherit: false) ? "[Flags] enum" : "enum";

        var record = type.GetMethod(
            "PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(StringBuilder)]) is not null;

        if (type.IsValueType)
        {
            var shape = record ? "record struct" : "struct";

            return type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false) ? $"readonly {shape}" : shape;
        }

        var kind = record ? "record" : "class";

        if (type.IsAbstract && type.IsSealed)
            return $"static {kind}";

        if (type.IsAbstract)
            return $"abstract {kind}";

        return type.IsSealed ? $"sealed {kind}" : kind;
    }

    private static string Bases(Type type)
    {
        var bases = new List<string>();

        if (type.BaseType is { } baseType
            && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum))
        {
            bases.Add(TypeName(baseType));
        }

        var inherited = type.BaseType?.GetInterfaces() ?? [];

        bases.AddRange(type.GetInterfaces().Except(inherited).Select(TypeName).Order(StringComparer.Ordinal));

        return bases.Count == 0 ? string.Empty : " : " + string.Join(", ", bases);
    }

    private static string? Member(MemberInfo member, NullabilityInfoContext context) => member switch
    {
        ConstructorInfo constructor when Visible(constructor) =>
            $".ctor({Parameters(constructor.GetParameters(), context)})",

        MethodInfo method when Visible(method)
            && !method.Name.Contains('<', StringComparison.Ordinal)
            && (!method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)) =>
            $"{Modifiers(method)}{Annotated(method.ReturnType, context.Create(method.ReturnParameter).ReadState)} " +
            $"{method.Name}{GenericArguments(method)}({Parameters(method.GetParameters(), context)})",

        PropertyInfo property when Accessors(property) is { Length: > 0 } accessors =>
            $"{Required(property)}{Static(property)}{Annotated(property.PropertyType, context.Create(property).ReadState)} " +
            $"{Indexer(property, context)} {{ {accessors} }}",

        FieldInfo field when (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)
            && !field.Name.Contains('<', StringComparison.Ordinal) =>
            Field(field, context),

        EventInfo @event when @event.AddMethod is { } add && Visible(add) =>
            $"event {TypeName(@event.EventHandlerType!)} {@event.Name}",

        _ => null,
    };

    private static bool Visible(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string Modifiers(MethodInfo method)
    {
        if (method.IsStatic)
            return "static ";

        if (method.DeclaringType!.IsInterface)
            return method.IsAbstract ? string.Empty : "default ";

        if (method.IsAbstract)
            return "abstract ";

        return method.IsVirtual && !method.IsFinal ? "virtual " : string.Empty;
    }

    private static string GenericArguments(MethodInfo method) =>
        method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;

    private static string Parameters(ParameterInfo[] parameters, NullabilityInfoContext context) =>
        string.Join(", ", parameters.Select(parameter =>
        {
            var type = parameter.ParameterType;
            var passing = !type.IsByRef ? string.Empty : parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
            var element = type.IsByRef ? type.GetElementType()! : type;
            var text = $"{passing}{Annotated(element, context.Create(parameter).ReadState)} {parameter.Name}";

            return parameter.HasDefaultValue ? $"{text} = {Literal(parameter.DefaultValue)}" : text;
        }));

    private static string Accessors(PropertyInfo property)
    {
        var parts = new List<string>();

        if (property.GetMethod is { } get && Visible(get))
            parts.Add("get;");

        if (property.SetMethod is { } set && Visible(set))
        {
            var init = set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

            parts.Add(init ? "init;" : "set;");
        }

        return string.Join(" ", parts);
    }

    private static string Required(PropertyInfo property) =>
        property.IsDefined(typeof(RequiredMemberAttribute), inherit: false) ? "required " : string.Empty;

    private static string Static(PropertyInfo property) =>
        (property.GetMethod ?? property.SetMethod)?.IsStatic == true ? "static " : string.Empty;

    private static string Indexer(PropertyInfo property, NullabilityInfoContext context) =>
        property.GetIndexParameters() is { Length: > 0 } index
            ? $"this[{Parameters(index, context)}]"
            : property.Name;

    private static string Field(FieldInfo field, NullabilityInfoContext context)
    {
        var type = Annotated(field.FieldType, context.Create(field).ReadState);

        if (field.IsLiteral)
            return $"const {type} {field.Name} = {Literal(field.GetRawConstantValue())}";

        var modifiers = (field.IsStatic ? "static " : string.Empty) + (field.IsInitOnly ? "readonly " : string.Empty);

        return $"{modifiers}{type} {field.Name}";
    }

    private static string Annotated(Type type, NullabilityState state) =>
        !type.IsValueType && state == NullabilityState.Nullable ? TypeName(type) + "?" : TypeName(type);

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!);

        if (type.IsGenericParameter)
            return type.Name;

        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return TypeName(underlying) + "?";

        var name = type.IsNested ? $"{TypeName(type.DeclaringType!)}.{type.Name}" : $"{type.Namespace}.{type.Name}";
        var tick = name.IndexOf('`', StringComparison.Ordinal);

        if (tick >= 0)
            name = name[..tick];

        return type.IsGenericType
            ? $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>"
            : name;
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
