using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;

namespace ArxisStudio.Modules.Xaml.Documents;

// Члены элемента показа: что документ может написать, чем это править и к чему пришёл объект.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    /// <summary>Длиннее значение подсказкой не показывается: это подсказка, а не дамп объекта.</summary>
    private const int ValueTextLimit = 120;

    /// <inheritdoc/>
    public XamlElementInfo? DescribeElement(XamlElementPath element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Live(element) is not { } shown)
            return null;

        var type = shown.Object.GetType();
        var parent = OwnerOf(shown.Element) is { } owner ? shown.Session.Objects.GetObject(owner) : null;

        return new XamlElementInfo(type.Name, type.Namespace ?? string.Empty, parent?.GetType().Name, shown.Object is Panel);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Свои члены — всё, что загрузчик знает о типе объекта (<see cref="XamlLoadSession.GetMembers"/>), кроме
    /// событий, коллекций и того, что не пишется. Присоединённые он знает все, какие могут встать на
    /// объект, — от <c>Canvas.Left</c> до <c>ScrollViewer.HorizontalScrollBarVisibility</c>, — и из них
    /// здесь остаются те, что читает родитель: их владелец — его тип или его база. И те, что документ
    /// уже написал: они в файле, и прятать их — значит прятать то, что файл говорит.
    /// </para>
    /// <para>
    /// Родитель — тот, что в документе, а не в дереве показа: у корня родитель в дереве — рамка
    /// дизайнера, и её присоединённых членов форма не знает.
    /// </para>
    /// </remarks>
    public IReadOnlyList<XamlMemberRow> GetMembers(XamlElementPath element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Live(element) is not { } shown)
            return [];

        var parentType = OwnerOf(shown.Element) is { } owner ? shown.Session.Objects.GetObject(owner)?.GetType() : null;
        var written = shown.Element.Attributes
            .Where(attribute => attribute.Name.IsDotted)
            .Select(attribute => attribute.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);

        var rows = new List<XamlMemberRow>();

        foreach (var member in shown.Session.GetMembers(shown.Object))
        {
            if (!Writable(member))
                continue;

            if (Dotted(member.Name)
                && !written.Contains(member.Name)
                && (parentType is null || !member.DeclaringType.IsAssignableFrom(parentType)))
            {
                continue;
            }

            rows.Add(Row(shown, member));
        }

        return rows;
    }

    /// <inheritdoc/>
    public XamlMemberRow? GetMember(XamlElementPath element, string member)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);

        if (Live(element) is not { } shown)
            return null;

        var descriptor = Resolve(shown, member);

        return descriptor is not null && Writable(descriptor) ? Row(shown, descriptor, member) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Выражение — <c>{Binding …}</c>, <c>{DynamicResource …}</c> — не значение, а его разбор: его читает
    /// синтаксис документа, и здесь оно проходит. Отказ сказан словами модуля: загрузчик объясняет
    /// по-английски.
    /// </remarks>
    public XamlValueCheck CheckValue(XamlElementPath element, string member, string text)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || IsExpression(text) || Live(element) is not { } shown)
            return default;

        if (Resolve(shown, member) is not { IsResolved: true } descriptor)
            return default;

        return descriptor.ConvertFromText(text).Succeeded
            ? default
            : new XamlValueCheck(_entry.Owner.Words.ValueRejected(text, ValueType(descriptor).Name));
    }

    /// <summary>Сессия показа, элемент и построенный им объект; null — чего-то из этого нет.</summary>
    private LiveElement? Live(XamlElementPath path) =>
        Shown() is { } session
            && path.Resolve(session.Document) is { } element
            && session.Objects.GetObject(element) is { } live
            ? new LiveElement(session, element, live)
            : null;

    /// <summary>Элемент-владелец: родитель в документе, минуя элемент свойства.</summary>
    private static XamlElement? OwnerOf(XamlElement element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement { IsPropertyElementSyntax: false } owner)
                return owner;
        }

        return null;
    }

    private static XamlMemberDescriptor? Resolve(LiveElement shown, string member)
    {
        try
        {
            return shown.Session.GetMember(shown.Object, member);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Документ может написать член атрибутом: он разрешился, пишется и держит значение, а не список.</summary>
    private static bool Writable(XamlMemberDescriptor member) =>
        member is { IsResolved: true, CanWrite: true, IsReadOnly: false }
        && member.Kind is not (XamlMemberKind.Event or XamlMemberKind.Collection);

    private static XamlMemberRow Row(LiveElement shown, XamlMemberDescriptor member, string? name = null)
    {
        var value = ValueType(member);
        var (origin, current) = Current(shown, member);

        return new XamlMemberRow(
            name ?? member.Name,
            member.DeclaringType.Name,
            value.Name,
            Dotted(name ?? member.Name),
            EditorOf(value),
            value.IsEnum ? [.. Enum.GetNames(value)] : ImmutableArray<string>.Empty,
            origin,
            current);
    }

    private static Type ValueType(XamlMemberDescriptor member) =>
        Nullable.GetUnderlyingType(member.ValueType) ?? member.ValueType;

    /// <summary>Чем править значение типа.</summary>
    /// <remarks>
    /// Текстом — то, что загрузка прочтёт из текста: числа, строки, перечисления, структуры с
    /// <c>Parse</c> — <c>Thickness</c>, <c>GridLength</c>, — и типы с конвертером. Шаблон, меню или стиль
    /// атрибутом не пишут: у них <see cref="XamlValueEditor.None"/>.
    /// </remarks>
    private static XamlValueEditor EditorOf(Type value)
    {
        if (value == typeof(bool))
            return XamlValueEditor.Flag;

        if (value.IsEnum)
            return XamlValueEditor.Choice;

        if (typeof(IBrush).IsAssignableFrom(value) || value == typeof(Color))
            return XamlValueEditor.Brush;

        if (value == typeof(double) || value == typeof(float) || value == typeof(decimal)
            || value == typeof(int) || value == typeof(long) || value == typeof(short) || value == typeof(byte))
        {
            return XamlValueEditor.Number;
        }

        return Textual(value) ? XamlValueEditor.Text : XamlValueEditor.None;
    }

    private static bool Textual(Type value) =>
        value == typeof(string)
        || value == typeof(object)
        || value.IsPrimitive
        || value.IsValueType
        || value.GetCustomAttribute<TypeConverterAttribute>() is not null
        || value.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]) is not null;

    /// <summary>Нынешнее значение объекта и откуда оно; значение — текстом, для подсказки.</summary>
    private static (XamlValueOrigin Origin, string? Text) Current(LiveElement shown, XamlMemberDescriptor member)
    {
        try
        {
            if (shown.Object is AvaloniaObject target && member.AvaloniaProperty is { } property)
            {
                var info = shown.Session.GetValueInfo(target, property);

                return (OriginOf(info.Source, target.GetDiagnostic(property).Priority), TextOf(info.EffectiveValue));
            }

            // Свойство CLR: значение отвечает геттер, а откуда оно — только документ.
            var origin = shown.Element.GetAttribute(member.Name) is null ? XamlValueOrigin.Default : XamlValueOrigin.Document;

            return (origin, member.ClrProperty is { CanRead: true } clr ? TextOf(clr.GetValue(shown.Object)) : null);
        }
        catch (Exception e) when (e is InvalidOperationException or TargetInvocationException or NotSupportedException)
        {
            // Геттер чужого контрола упал или объект уже не в потоке: подсказки нет, строка остаётся.
            return (XamlValueOrigin.Default, null);
        }
    }

    /// <summary>Откуда значение: выражение стиля или шаблона — их, а не привязка документа.</summary>
    /// <remarks>
    /// Загрузчик называет привязкой всякое значение с выражением, а тема ставит размеры и кисти контролов
    /// <c>{DynamicResource}</c> — выражением стиля. Сказать о минимальной высоте флажка «привязка» значило бы
    /// послать человека искать привязку, которой в документе нет.
    /// </remarks>
    private static XamlValueOrigin OriginOf(XamlValueSource source, BindingPriority priority) => source switch
    {
        XamlValueSource.Local => XamlValueOrigin.Document,
        XamlValueSource.Binding => priority switch
        {
            BindingPriority.Style or BindingPriority.StyleTrigger => XamlValueOrigin.Style,
            BindingPriority.Template => XamlValueOrigin.Template,
            BindingPriority.Inherited => XamlValueOrigin.Inherited,
            _ => XamlValueOrigin.Binding,
        },
        XamlValueSource.Style or XamlValueSource.StyleTrigger => XamlValueOrigin.Style,
        XamlValueSource.Template => XamlValueOrigin.Template,
        XamlValueSource.Inherited => XamlValueOrigin.Inherited,
        XamlValueSource.Animation => XamlValueOrigin.Animation,
        _ => XamlValueOrigin.Default,
    };

    /// <summary>Значение текстом, как его написал бы документ; объект поколения текстом не показывается.</summary>
    private static string? TextOf(object? value)
    {
        var text = value switch
        {
            null => null,
            AvaloniaObject => null,
            double number => number.ToString("0.##", CultureInfo.InvariantCulture),
            float number => number.ToString("0.##", CultureInfo.InvariantCulture),
            bool flag => flag ? "True" : "False",
            ISolidColorBrush brush => brush.Color.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        // Тип без своего ToString отвечает именем типа — это не значение.
        if (text is null || value is not null && text == value.GetType().FullName)
            return null;

        return text.Length <= ValueTextLimit ? text : text[..ValueTextLimit] + "…";
    }

    /// <summary>
    /// Присоединённый — член, которого у типа нет под простым именем: <c>Canvas.Left</c>. Кегль у
    /// <c>TextBlock</c> — присоединённое свойство <c>TextElement</c>, взятое типом себе (<c>AddOwner</c>), и
    /// пишут его просто <c>FontSize</c>: это член типа, а не родителя.
    /// </summary>
    private static bool Dotted(string name) => name.Contains('.', StringComparison.Ordinal);

    /// <summary>Текст — выражение разметки, а не значение: <c>{Binding …}</c>, но не экранированное <c>{}…</c>.</summary>
    private static bool IsExpression(string text) =>
        text.StartsWith('{') && !text.StartsWith("{}", StringComparison.Ordinal);

    /// <summary>Элемент показа: сессия, элемент её текста и построенный им объект — на время одного вопроса.</summary>
    private readonly record struct LiveElement(XamlLoadSession Session, XamlElement Element, object Object);
}
