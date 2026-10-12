using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

// Выражения элемента: работают ли его привязки — по тому, что документ объявляет.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
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

            var member = attribute.Name.LocalName;
            var kind = XamlExpressions.Classify(extension);

            // Мультипривязка — несколько источников сразу: за ней проверка не следует.
            if (kind != XamlExpressionKind.Binding || extension.TypeName.LocalName.StartsWith("MultiBinding", StringComparison.Ordinal))
            {
                checks.Add(new XamlExpressionCheck(member, kind, XamlExpressionState.Unchecked, null, false, null, null));
                continue;
            }

            XamlBindingCheck check;

            try
            {
                check = await session.CheckBindingAsync(syntax, member, extension, byDefault, linked.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
            {
                // Показ отдал объекты или сменил сессию, пока загрузчик отвечал: отвечать не о чем.
                return [];
            }

            if (!ReferenceEquals(Shown(), session))
                return [];

            checks.Add(Translate(member, check));
        }

        return checks;
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
}
