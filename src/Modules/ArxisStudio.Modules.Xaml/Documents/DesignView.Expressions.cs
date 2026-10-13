using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

// Выражения элемента: работают ли его привязки и что они могут назвать — по тому, что документ объявляет.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    /// <summary>Привязка, которую сборка не компилирует: ею спрашивают данные показа, когда типа данных нет.</summary>
    private static readonly XamlQualifiedName ReflectionBinding = XamlQualifiedName.Unprefixed("ReflectionBinding");

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Привязку проверяет загрузчик (<see cref="XamlLoadSession.CheckBindingAsync"/>, ADR 0033 Markup): источник — в
    /// документе, путь — по типу, а компилируется ли она, служба говорит ему сама: живой показ грузит привязки
    /// отражением, прощая, а сборка дизайна идёт настройками проекта.
    /// </para>
    /// <para>
    /// Типы ответа загрузчика переводятся в имена в синхронном отрезке (<see cref="Translate"/>) и отпускаются. Замена
    /// поколения посреди вопроса кончает его пустым ответом, а не держит поколение через себя.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<XamlExpressionCheck>> CheckExpressionsAsync(
        XamlElementPath element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Shown() is not { } session || element.Resolve(session.Document) is not { } syntax)
            return [];

        var byDefault = _entry.Session.CompilesBindingsByDefault(_entry.Path);
        var checks = new List<XamlExpressionCheck>();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _generation.Token);

        foreach (var attribute in syntax.Attributes)
        {
            if (attribute is XamlNamespaceDeclaration || attribute.IsDirective || attribute.IsDesignTime || attribute.IsMarkupCompatibility ||
                attribute.GetValue() is not XamlMarkupExtensionValue extension)
            {
                continue;
            }

            XamlExpressionCheck? check;

            try
            {
                check = await CheckAsync(session, syntax, attribute.Name.LocalName, extension, byDefault, linked.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
            {
                // Показ отдал объекты или сменил сессию, пока загрузчик отвечал: отвечать не о чем.
                return [];
            }

            if (check is null)
                return [];

            checks.Add(check);
        }

        return checks;
    }

    /// <inheritdoc/>
    /// <remarks>Черновик проверяется тем же правилом, что написанное: элемент — из текста показа, выражение — нет.</remarks>
    public async ValueTask<XamlExpressionCheck?> CheckExpressionAsync(
        XamlElementPath element, string member, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentNullException.ThrowIfNull(text);

        if (Shown() is not { } session || element.Resolve(session.Document) is not { } syntax ||
            XamlValue.Parse(text) is not XamlMarkupExtensionValue extension)
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _generation.Token);

        try
        {
            return await CheckAsync(session, syntax, member, extension, _entry.Session.CompilesBindingsByDefault(_entry.Path), linked.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Тип, к которому привёл путь, — ответ загрузчика о самой привязке; без пути — тип источника. Где привязка
    /// компилируется, а типа данных в области нет, загрузчик называет её сломанной, и тогда данные спрашиваются, как их
    /// прочла бы привязка отражением, — у показа.
    /// </para>
    /// <para>
    /// Члены — что загрузчик отдаёт привязке (<see cref="XamlMemberResolver.EnumerateBindable"/>), а их типы читаются
    /// здесь же, в синхронном отрезке, и уходят именами (<see cref="Members"/>).
    /// </para>
    /// </remarks>
    public async ValueTask<XamlBindableMembers?> GetBindableMembersAsync(
        XamlElementPath element, string member, string binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentNullException.ThrowIfNull(binding);

        if (Shown() is not { } session || element.Resolve(session.Document) is not { } syntax ||
            XamlValue.Parse(binding) is not XamlMarkupExtensionValue extension ||
            XamlExpressions.Classify(extension) != XamlExpressionKind.Binding)
        {
            return null;
        }

        var byDefault = _entry.Session.CompilesBindingsByDefault(_entry.Path);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _generation.Token);

        try
        {
            var check = await session.CheckBindingAsync(syntax, member, extension, byDefault, linked.Token);

            if (check is { Status: XamlBindingPathStatus.Broken, Source: XamlBindingSource.DataContext, Step: null, Compiles: true })
            {
                var reflected = extension with { TypeName = ReflectionBinding, RawText = null };

                check = await session.CheckBindingAsync(syntax, member, reflected, compilesByDefault: false, linked.Token);
            }

            return ReferenceEquals(Shown(), session) && check is { Status: XamlBindingPathStatus.Resolved, ResultType: { } type }
                ? Members(session, syntax, member, type)
                : null;
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Проверка одного выражения; null — показ сменил сессию, пока загрузчик отвечал.</summary>
    private async Task<XamlExpressionCheck?> CheckAsync(
        XamlLoadSession session, XamlElement syntax, string member, XamlMarkupExtensionValue extension, bool byDefault, CancellationToken cancellationToken)
    {
        var kind = XamlExpressions.Classify(extension);

        // Мультипривязка — несколько источников сразу: за ней проверка не следует.
        if (kind != XamlExpressionKind.Binding || extension.TypeName.LocalName.StartsWith("MultiBinding", StringComparison.Ordinal))
            return new XamlExpressionCheck(member, kind, XamlExpressionState.Unchecked, null, false, null, null);

        var check = await session.CheckBindingAsync(syntax, member, extension, byDefault, cancellationToken);

        return ReferenceEquals(Shown(), session) ? Translate(member, check) : null;
    }

    /// <summary>Ответ загрузчика о привязке — именами и словами модуля; типы поколения здесь и отпускаются.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private XamlExpressionCheck Translate(string member, XamlBindingCheck check)
    {
        var words = _entry.Owner.Words;
        var source = check.SourceType is { } sourceType ? XamlMemberMetadata.TypeOf(sourceType) : null;
        var result = check.ResultType is { } resultType ? XamlMemberMetadata.TypeOf(resultType) : null;

        var (state, message) = check.Status switch
        {
            XamlBindingPathStatus.Resolved => (XamlExpressionState.Resolved, (string?)null),
            XamlBindingPathStatus.Broken => (XamlExpressionState.Broken, words.BindingBroken(check.Source, check.Step, source?.Name)),
            _ => (XamlExpressionState.Unchecked, words.BindingUnchecked),
        };

        if (check.BreaksBuild && message is not null)
            message = words.BreaksBuild(message);

        return new XamlExpressionCheck(member, XamlExpressionKind.Binding, state, message, check.BreaksBuild, source, result);
    }

    /// <summary>
    /// Члены типа, к которому привёл путь, — именами: тип каждого читается здесь, и подходит ли он члену, к которому
    /// привязывают, решает правило <see cref="Fits"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static XamlBindableMembers Members(XamlLoadSession session, XamlElement syntax, string member, Type type)
    {
        var resolver = session.Environment.MemberResolver;
        var target = session.Objects.GetObject(syntax) is { } owner && Target(session, owner, member) is { IsResolved: true } descriptor
            ? descriptor.ValueType
            : null;

        return new XamlBindableMembers(
            XamlMemberMetadata.TypeOf(type),
            [
                .. resolver.EnumerateBindable(type).Select(bindable =>
                {
                    var held = resolver.ResolveBindingPath(type, bindable.Name).ResultType;

                    return new XamlBindableMemberInfo(
                        bindable.Name,
                        bindable.TypeName,
                        bindable.CanWrite,
                        bindable.IsCollection,
                        bindable.IsCommand,
                        held is not null && HasMembers(held),
                        Fits(target, held));
                }),
            ]);
    }

    private static XamlMemberDescriptor? Target(XamlLoadSession session, object owner, string member)
    {
        try
        {
            return session.GetMember(owner, member);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Значение члена — объект со своими членами, в который путь может шагнуть: не список — и не текст, он тоже список
    /// символов, — не делегат и не команда.
    /// </summary>
    private static bool HasMembers(Type type) =>
        type.IsClass
        && !typeof(IEnumerable).IsAssignableFrom(type)
        && !typeof(Delegate).IsAssignableFrom(type)
        && !typeof(ICommand).IsAssignableFrom(type);

    /// <summary>
    /// Подходит ли значение члену, к которому привязывают: тексту и объекту — всё, — привязка сделает текст из чего
    /// угодно, — иначе тот же или приводимый тип либо число к числу. Член, чей тип не узнать, принимает всё.
    /// </summary>
    /// <remarks>Правило демо дизайнера Surface (<c>DesignerViewModel.Data</c>).</remarks>
    private static bool Fits(Type? target, Type? held)
    {
        if (target is null || target == typeof(string) || target == typeof(object))
            return true;

        if (held is null)
            return false;

        var wanted = Nullable.GetUnderlyingType(target) ?? target;
        var given = Nullable.GetUnderlyingType(held) ?? held;

        return wanted.IsAssignableFrom(given) || (Numeric(wanted) && Numeric(given));

        static bool Numeric(Type type) =>
            Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal && !type.IsEnum;
    }
}
