using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Xaml.Documents;

// Члены элемента показа: что документ может написать, как это описал автор типа и к чему пришёл объект.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    /// <inheritdoc/>
    public XamlElementInfo? DescribeElement(XamlElementPath element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Live(element) is not { } shown)
            return null;

        var type = shown.Object.GetType();
        var parent = OwnerOf(shown.Element) is { } owner ? shown.Session.Objects.GetObject(owner)?.GetType() : null;

        return new XamlElementInfo(
            XamlMemberMetadata.FullName(type),
            type.Assembly.GetName().Name ?? string.Empty,
            XamlMemberMetadata.Bases(type),
            parent is null ? null : XamlMemberMetadata.FullName(parent),
            parent is null ? [] : XamlMemberMetadata.Bases(parent),
            shown.Object is Panel,
            shown.Session.GetMembers(shown.Object).FirstOrDefault(member => member.Kind == XamlMemberKind.Content)?.Name);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Свои члены — всё, что загрузчик знает о типе объекта (<see cref="XamlLoadSession.GetMembers"/>), кроме
    /// событий и списков. Присоединённые он знает все, какие могут встать на объект, — от <c>Canvas.Left</c>
    /// до <c>ScrollViewer.HorizontalScrollBarVisibility</c>, — и из них здесь остаются те, что читает
    /// родитель: их владелец — его тип или его база, так что ребёнок наследника сетки получает
    /// <c>Grid.Row</c>. И те, что документ уже написал: они в файле, и прятать их — значит прятать то, что
    /// файл говорит.
    /// </para>
    /// <para>
    /// Родитель — тот, что в документе, а не в дереве показа: у корня родитель в дереве — рамка
    /// дизайнера, и её присоединённых членов форма не знает.
    /// </para>
    /// </remarks>
    public IReadOnlyList<XamlMemberInfo> GetMembers(XamlElementPath element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Live(element) is not { } shown)
            return [];

        var parent = Parent(shown);
        var written = shown.Element.Attributes
            .Where(attribute => attribute.Name.IsDotted)
            .Select(attribute => attribute.Name.ToString())
            .ToHashSet(StringComparer.Ordinal);

        var members = new List<XamlMemberInfo>();

        foreach (var member in shown.Session.GetMembers(shown.Object))
        {
            if (!Listed(member))
                continue;

            var reads = Reads(member, member.Name, parent);

            if (Dotted(member.Name) && !reads && !written.Contains(member.Name))
                continue;

            members.Add(Info(shown, member, member.Name, reads));
        }

        return members;
    }

    /// <inheritdoc/>
    public XamlMemberInfo? GetMember(XamlElementPath element, string member)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);

        if (Live(element) is not { } shown || Resolve(shown, member) is not { } descriptor || !Listed(descriptor))
            return null;

        return Info(shown, descriptor, member, Reads(descriptor, member, Parent(shown)));
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

        return Resolve(shown, member) is { IsResolved: true } descriptor ? Check(descriptor, text) : default;
    }

    /// <summary>
    /// Прочтётся ли текст значением члена — тем же разбором, каким его прочтёт загрузка. Картинка — имя ресурса, и что
    /// оно называет, читает загрузка через среду (ADR 0031 Markup): не нашлось — правка не встанет, и скажет почему.
    /// </summary>
    private XamlValueCheck Check(XamlMemberDescriptor descriptor, string text) =>
        descriptor.ReadsResource || descriptor.ConvertFromText(text).Succeeded
            ? default
            : new XamlValueCheck(_entry.Owner.Words.ValueRejected(text, XamlMemberMetadata.TypeOf(descriptor.ValueType).Name));

    /// <summary>Сессия показа, элемент и построенный им объект; null — чего-то из этого нет.</summary>
    private LiveElement? Live(XamlElementPath path) =>
        Shown() is { } session
            && path.Resolve(session.Document) is { } element
            && session.Objects.GetObject(element) is { } live
            ? new LiveElement(session, element, live)
            : null;

    /// <summary>Тип родителя в документе; null — у корня.</summary>
    private static Type? Parent(LiveElement shown) =>
        OwnerOf(shown.Element) is { } owner ? shown.Session.Objects.GetObject(owner)?.GetType() : null;

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

    /// <summary>Член строкой бывает: он разрешился и держит значение — не событие и не список.</summary>
    private static bool Listed(XamlMemberDescriptor member) =>
        member.IsResolved && member.Kind is not (XamlMemberKind.Event or XamlMemberKind.Collection);

    /// <summary>
    /// Присоединённый член, который читает родитель: владелец — тип родителя или его база, но не база всякого
    /// контрола.
    /// </summary>
    /// <remarks>
    /// Владелец, от которого происходит сам <see cref="Control"/>, — <c>InputElement</c>, <c>Visual</c> и ниже, —
    /// база любого родителя, и его присоединённое о родителе ничего не говорит: это жест элемента
    /// (<c>InputElement.IsHoldingEnabled</c>) или чужая проводка, зарегистрированная на всех контролах процесса. Такие
    /// стояли у холста в панели разделами «В Control» и «В InputElement» (запись 372).
    /// </remarks>
    private static bool Reads(XamlMemberDescriptor member, string name, Type? parent) =>
        Dotted(name) && parent is not null && member.DeclaringType.IsAssignableFrom(parent) &&
        !member.DeclaringType.IsAssignableFrom(typeof(Control));

    private static XamlMemberInfo Info(LiveElement shown, XamlMemberDescriptor member, string name, bool reads)
    {
        var (ownerNamespace, ownerPrefix) = Dotted(name)
            ? XamlMemberMetadata.NamespaceOf(member.DeclaringType)
            : (null, null);

        return new XamlMemberInfo(
            name,
            KindOf(member, name),
            XamlMemberMetadata.FullName(member.DeclaringType),
            ownerNamespace,
            ownerPrefix,
            XamlMemberMetadata.TypeOf(member.ValueType),
            member is { CanWrite: true, IsReadOnly: false },
            member.ReadsResource || XamlMemberMetadata.AcceptsText(member.ValueType),
            member.Kind == XamlMemberKind.Content,
            reads,
            XamlMemberMetadata.AttributesOf(member),
            XamlValueOrigins.Read(shown.Session, shown.Element, shown.Object, member))
        {
            DefaultBindingMode = BindingModeOf(member, shown.Object.GetType()),
        };
    }

    /// <summary>
    /// Режим привязки к члену, когда он не написан: у свойства Avalonia — его метаданные для этого типа, <c>Default</c> —
    /// односторонний; у свойства CLR — null: привязку на него не ставят.
    /// </summary>
    private static string? BindingModeOf(XamlMemberDescriptor member, Type type) =>
        member.AvaloniaProperty?.GetMetadata(type).DefaultBindingMode switch
        {
            null => null,
            Avalonia.Data.BindingMode.Default => nameof(Avalonia.Data.BindingMode.OneWay),
            { } mode => mode.ToString(),
        };

    /// <summary>
    /// Каким свойством член держит значение — по устройству, а не по роли содержимого; присоединённый — тот,
    /// что документ пишет с именем владельца.
    /// </summary>
    private static XamlPropertyKind KindOf(XamlMemberDescriptor member, string name) => member switch
    {
        _ when Dotted(name) => XamlPropertyKind.Attached,
        { AvaloniaProperty.IsDirect: true } => XamlPropertyKind.Direct,
        { AvaloniaProperty: not null } => XamlPropertyKind.Styled,
        _ => XamlPropertyKind.Clr,
    };

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
