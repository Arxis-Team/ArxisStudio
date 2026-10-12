using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

// Выражения: какие они и работают ли — жетоном строки.
// Часть XamlInspectorSnapshot; общее описание типа — в XamlInspectorSnapshot.cs.
internal sealed partial class XamlInspectorSnapshot
{
    /// <summary>Проверки выражений выбранных — по номеру выбранного; null у выбранного — служба ещё не ответила.</summary>
    public IReadOnlyList<IReadOnlyList<XamlExpressionCheck>?> Checks => _checks;

    /// <summary>Свойства, чьи выражения проверены: их строки переспрашивают, когда ответ пришёл.</summary>
    public IReadOnlyCollection<string> CheckedIds =>
    [
        .. _checks
            .SelectMany(static checks => checks ?? [])
            .Select(static check => check.Member)
            .Where(_specs.ContainsKey)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Кладёт проверки выражений выбранных — по номеру выбранного.</summary>
    /// <param name="checks">Проверки; null у выбранного — службе нечего сказать.</param>
    /// <remarks>
    /// Служба отвечает о каждом выражении элемента, и пустой ответ у элемента с выражениями — вопрос, прерванный
    /// заменой поколения: прежний ответ лучше, чем ни одного.
    /// </remarks>
    public void Check(IReadOnlyList<IReadOnlyList<XamlExpressionCheck>?> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        for (var at = 0; at < _checks.Length && at < checks.Count; at++)
        {
            if (checks[at] is not { } answer || answer.Count == 0 && Expressions(_picked[at].Element).Any())
                continue;

            _checks[at] = answer;
        }
    }

    /// <summary>
    /// Берёт у прежнего снимка ответы службы, пока не пришли новые: данные — как были, проверки — тех выражений, чей
    /// текст не сменился. Иначе каждая правка мигала бы в строках данных «Нет», а новая запись стояла бы со старым
    /// ответом.
    /// </summary>
    /// <param name="previous">Прежний живой снимок тех же выбранных.</param>
    public void Seed(XamlInspectorSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        Describe(previous.Data);

        for (var at = 0; at < _checks.Length && at < previous._checks.Length && at < previous._picked.Count; at++)
        {
            if (previous._checks[at] is not { } checks)
                continue;

            var before = previous._picked[at].Element;
            var now = _picked[at].Element;

            _checks[at] = [.. checks.Where(check => string.Equals(TextOf(before, check.Member), TextOf(now, check.Member), StringComparison.Ordinal))];
        }
    }

    /// <summary>
    /// Выражение, которым написан член у выбранного: вид и короткая запись — по синтаксису, а работает ли оно — по
    /// проверке службы. Без показа проверки нет, и сломанным выражение делает ошибка загрузки, вставшая на его атрибут:
    /// форма, чьё выражение называет тип или член, которого нет, не загрузится вовсе.
    /// </summary>
    /// <param name="index">Номер выбранного.</param>
    /// <param name="member">Член, как его называет документ.</param>
    /// <param name="written">Написанное.</param>
    /// <returns>Выражение и почему оно сломано; null — написанное не выражение.</returns>
    private (InspectorExpression Expression, string? Error)? ExpressionOf(int index, string member, string? written)
    {
        if (XamlExpressions.Classify(written) is not { } kind)
            return null;

        var summary = XamlExpressions.Summarize(written)!;
        var check = _checks[index]?.FirstOrDefault(check => string.Equals(check.Member, member, StringComparison.Ordinal));

        if (check is null)
        {
            var failure = LoadErrorAt(_picked[index].Element, member);

            return (new InspectorExpression(KindOf(kind), summary)
            {
                State = failure is null ? InspectorExpressionState.Unchecked : InspectorExpressionState.Broken,
            }, failure);
        }

        var state = check.State switch
        {
            XamlExpressionState.Resolved => InspectorExpressionState.Resolved,
            XamlExpressionState.Broken => InspectorExpressionState.Broken,
            _ => InspectorExpressionState.Unchecked,
        };

        // Найденное — откуда и куда пришёл путь; непроверенное — почему: сломанное говорит заметкой под строкой.
        var detail = check switch
        {
            { State: XamlExpressionState.Resolved, SourceType: { } source, ResultType: { } result } => $"{source.Name} → {result.Name}",
            { State: XamlExpressionState.Unchecked } => check.Message,
            _ => null,
        };

        return (new InspectorExpression(KindOf(kind), summary) { State = state, Detail = detail },
            state == InspectorExpressionState.Broken ? check.Message : null);
    }

    /// <summary>Ошибка загрузки, вставшая на атрибут члена; null — такой нет.</summary>
    /// <remarks>
    /// Член назван так же, как его называет строка: без префикса — местным именем, с префиксом — полным. Место ошибки
    /// лежит внутри атрибута — компилятор ставит его в начало атрибута пустым, — а ошибка на весь элемент не говорит
    /// ни об одном его выражении.
    /// </remarks>
    private string? LoadErrorAt(XamlElement element, string member)
    {
        if (_errors.IsDefaultOrEmpty)
            return null;

        var attribute = element.Attributes.FirstOrDefault(attribute =>
            attribute is not XamlNamespaceDeclaration && !attribute.IsDirective && !attribute.IsDesignTime &&
            (string.Equals(attribute.Name.LocalName, member, StringComparison.Ordinal) ||
             string.Equals(attribute.Name.ToString(), member, StringComparison.Ordinal)));

        return attribute is null
            ? null
            : _errors.FirstOrDefault(error => error.Span is { } span && attribute.Span.Contains(span))?.Message;
    }

    /// <summary>Атрибуты элемента, написанные выражением, — те, о которых отвечает служба.</summary>
    private static IEnumerable<XamlAttribute> Expressions(XamlElement element) =>
        element.Attributes.Where(static attribute =>
            attribute is not XamlNamespaceDeclaration && !attribute.IsDirective && !attribute.IsDesignTime && !attribute.IsMarkupCompatibility &&
            attribute.GetValue() is XamlMarkupExtensionValue);

    /// <summary>Текст выражения, которым элемент пишет член, — как его называет служба; null — не написано.</summary>
    private static string? TextOf(XamlElement element, string member) =>
        Expressions(element).FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, member, StringComparison.Ordinal))?.GetValueText();

    private static InspectorExpressionKind KindOf(XamlExpressionKind kind) => kind switch
    {
        XamlExpressionKind.Binding => InspectorExpressionKind.Binding,
        XamlExpressionKind.Resource => InspectorExpressionKind.Resource,
        XamlExpressionKind.Static => InspectorExpressionKind.Static,
        XamlExpressionKind.Template => InspectorExpressionKind.Template,
        _ => InspectorExpressionKind.Other,
    };
}
