using System.Globalization;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Правки строения: буфер обмена, дубликат, обёртка и её снятие.
// Часть FormEdits; общее описание типа — в FormEdits.cs.
internal sealed partial class FormEdits
{
    /// <summary>Пространство Avalonia: в нём пишутся обёртки.</summary>
    public const string AvaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>Корень, внутри которого читаются несколько элементов подряд: документом они не читаются.</summary>
    private const string ClipRoot = "ArxisClipboard";

    /// <summary>
    /// Что из выбранного берут правки строения: без корня и без вложенного в другое выбранное — оно уже в
    /// разметке того, во что вложено, — по порядку документа.
    /// </summary>
    /// <param name="syntax">Текст формы.</param>
    /// <param name="paths">Пути выбранного.</param>
    /// <returns>Элементы; пусто — брать нечего.</returns>
    /// <remarks>Корень не берётся: без него нет документа, а вставленный в форму он стал бы окном в окне.</remarks>
    public static List<XamlElement> Taken(XamlDocument syntax, IEnumerable<XamlElementPath> paths)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(paths);

        var elements = paths
            .Where(path => !path.Steps.IsEmpty)
            .Select(path => path.Resolve(syntax))
            .OfType<XamlElement>()
            .Distinct()
            .ToList();

        return [.. elements.Where(element => !elements.Any(other => IsInside(element, other))).OrderBy(element => element.Span.Start)];
    }

    /// <summary>
    /// Что можно обернуть одной обёрткой: соседи одного родителя, стоящие в его содержимом.
    /// </summary>
    /// <param name="syntax">Текст формы.</param>
    /// <param name="paths">Пути выбранного.</param>
    /// <returns>Элементы по порядку документа; null — обернуть нечего или выбранное стоит в разных местах.</returns>
    /// <remarks>
    /// Элемент внутри элемента свойства — определение строки, стиль — не оборачивается: его соседи — значения
    /// члена, и обёртка между ними ничего бы не значила.
    /// </remarks>
    public static List<XamlElement>? Wrappable(XamlDocument syntax, IEnumerable<XamlElementPath> paths)
    {
        var taken = Taken(syntax, paths);

        if (taken.Count == 0 || taken[0].Parent is not XamlElement { IsPropertyElementSyntax: false } parent)
            return null;

        return taken.All(element => ReferenceEquals(element.Parent, parent)) ? taken : null;
    }

    /// <summary>Что можно снять: выбранное с детьми, стоящее в содержимом своего родителя.</summary>
    /// <param name="syntax">Текст формы.</param>
    /// <param name="paths">Пути выбранного.</param>
    /// <returns>Элементы по порядку документа; пусто — снимать нечего.</returns>
    /// <remarks>
    /// Элемент без детей — не обёртка: снять с подписи нечего, и снятие, убравшее бы её целиком, было бы
    /// удалением под чужим именем. Удаляют её Delete.
    /// </remarks>
    public static List<XamlElement> Unwrappable(XamlDocument syntax, IEnumerable<XamlElementPath> paths) =>
    [
        .. Taken(syntax, paths).Where(static element =>
            element.Parent is XamlElement { IsPropertyElementSyntax: false } && element.ContentElements.Any()),
    ];

    /// <summary>
    /// Разметка выбранного для буфера обмена: каждый элемент — фрагментом с объявлениями, которыми он
    /// пишется, по порядку документа и каждый со своей строки.
    /// </summary>
    /// <param name="paths">Пути выбранного.</param>
    /// <returns>Что скопировано; null — копировать нечего: выбран корень или ничего.</returns>
    /// <remarks>
    /// Текст — то, что вставит и Rider: фрагмент сам называет свои пространства, и <c>local:Badge</c>,
    /// вставленный в другой файл, останется тем же контролом.
    /// </remarks>
    public Copied? Copy(IReadOnlyList<XamlElementPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var syntax = _document.Syntax;
        var taken = Taken(syntax, paths);

        if (taken.Count == 0)
            return null;

        var text = string.Join(NewLineOf(syntax), taken.Select(element => XamlFragment.From(element).ToXamlText()));

        return new Copied(text, taken.Count, NameOf(XamlElementPath.Of(taken[0])));
    }

    /// <summary>Читает текст буфера обмена фрагментами разметки — одним элементом или несколькими подряд.</summary>
    /// <param name="text">Текст.</param>
    /// <returns>Фрагменты по порядку; пусто — разметки в тексте нет.</returns>
    /// <remarks>
    /// Несколько элементов подряд — то, что кладёт копирование выбора; документом такой текст не читается, у
    /// документа один корень. Поэтому он читается внутри корня-обёртки, и каждый элемент поднимается фрагментом
    /// сам — со своими объявлениями. Слова и комментарии вокруг элементов фрагментом не становятся — так
    /// читает текст и <see cref="XamlFragment.Parse"/>, и строка из переписки «вот кнопка: &lt;Button /&gt;»
    /// вставляет кнопку. Элемент свойства не ставится ничего и не вставляется.
    /// </remarks>
    public static IReadOnlyList<XamlFragment> Fragments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var single = XamlFragment.Parse(text);

        if (single is { Root: { } root, Document.IsWellFormed: true })
            return root.IsPropertyElementSyntax ? [] : [single];

        var gathered = XamlDocument.Parse($"<{ClipRoot}>{text}</{ClipRoot}>");

        return gathered is { IsWellFormed: true, Root: { } clip }
            ? [.. clip.ContentElements.Select(XamlFragment.From)]
            : [];
    }

    /// <summary>Вставляет фрагменты подряд в содержимое родителя одной правкой и выбирает вставленное.</summary>
    /// <param name="parent">Путь родителя.</param>
    /// <param name="index">Место среди элементов его содержимого; больше их числа — в конец.</param>
    /// <param name="fragments">Фрагменты по порядку.</param>
    /// <returns>Изменился ли текст.</returns>
    /// <remarks>
    /// Имена, которые документ уже носит, у вставленного снимаются — двух одинаковых полей у класса формы не
    /// бывает, — а свободные остаются: вырезанное и вставленное назад сохраняет имя, на которое ссылается код.
    /// Фрагмент, которому здесь не встать, пропускается, и почему — говорится.
    /// </remarks>
    public async Task<bool> PasteAsync(XamlElementPath parent, int index, IReadOnlyList<XamlFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(fragments);

        if (fragments.Count == 0)
            return false;

        var label = fragments.Count == 1
            ? Format("form.edit.paste", fragments[0].Root?.Name.LocalName ?? string.Empty)
            : string.Format(CultureInfo.CurrentCulture, _strings["form.edit.pasteMany"], fragments.Count);

        var pasted = new List<XamlElementPath>(fragments.Count);
        string? refusal = null;

        var changed = await EditAsync(label, editor =>
        {
            if (parent.Resolve(editor.Document) is not { } container)
                return;

            // Все — в одно место: вставки в одну точку встают в том порядке, в каком записаны.
            var at = Math.Clamp(index, 0, container.ContentElements.Count());

            foreach (var fragment in fragments)
            {
                var before = editor.Diagnostics.Length;

                editor.InsertFragment(container, at, fragment);

                if (editor.Diagnostics.Skip(before).FirstOrDefault(static diagnostic => diagnostic.IsError) is { } refused)
                {
                    refusal ??= refused.Message;
                    continue;
                }

                pasted.Add(Append(parent, new XamlPathStep(null, at + pasted.Count)));
            }
        }, pasted);

        if (refusal is not null)
            _refused(refusal);

        return changed;
    }

    /// <summary>Вырезает: убирает элементы одной правкой, названной вырезанием.</summary>
    /// <param name="paths">Пути элементов.</param>
    /// <remarks>Текст в буфер кладёт тот, кто вырезает, — раньше правки: не легло в буфер — не убирается.</remarks>
    public Task CutAsync(IReadOnlyList<XamlElementPath> paths) => RemoveAsync(paths, "form.edit.cut", "form.edit.cutMany");

    /// <summary>
    /// Ставит копию каждого выбранного сразу после него одной правкой и выбирает копии.
    /// </summary>
    /// <param name="paths">Пути выбранного.</param>
    /// <returns>Изменился ли текст.</returns>
    /// <remarks>
    /// Имён у копий нет: имя — поле класса формы, и второе такое же сломало бы сборку. Копия встаёт рядом с
    /// оригиналом, в той же панели и с теми же атрибутами раскладки: в <c>Canvas</c> — поверх него.
    /// </remarks>
    public Task<bool> DuplicateAsync(IReadOnlyList<XamlElementPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var taken = Taken(_document.Syntax, paths);

        if (taken.Count == 0)
            return Task.FromResult(false);

        var label = taken.Count == 1
            ? Format("form.edit.duplicate", NameOf(XamlElementPath.Of(taken[0])))
            : string.Format(CultureInfo.CurrentCulture, _strings["form.edit.duplicateMany"], taken.Count);

        var copies = new List<XamlElementPath>(taken.Count);

        return EditAsync(label, editor =>
        {
            var originals = Taken(editor.Document, paths);
            var shifts = new List<Shift>(originals.Count);

            foreach (var original in originals)
            {
                var path = XamlElementPath.Of(original);
                var last = path.Steps[^1];

                editor.DuplicateElement(original);
                shifts.Add(new Shift(path.Parent!, last.MemberName, last.Index, 1));
            }

            foreach (var original in originals)
            {
                var moved = Shifted(XamlElementPath.Of(original), shifts);

                copies.Add(Sibling(moved, moved.Steps[^1].Index + 1));
            }
        }, copies);
    }

    /// <summary>
    /// Оборачивает соседей одним контейнером одной правкой и выбирает обёртку.
    /// </summary>
    /// <param name="paths">Пути выбранного: соседи одного родителя.</param>
    /// <param name="container">Тип обёртки в пространстве Avalonia: <c>StackPanel</c>, <c>Border</c>.</param>
    /// <param name="slots">
    /// Члены, которыми родитель ставит детей, как их пишет документ: <c>Grid.Row</c>, <c>Canvas.Left</c>.
    /// </param>
    /// <returns>Изменился ли текст.</returns>
    /// <remarks>
    /// <para>
    /// Обёртка встаёт на место первого из них, и место в раскладке родителя переходит к ней: строка сетки,
    /// сторона <c>DockPanel</c>, точка <c>Canvas</c> первого пишутся на обёртке, а у обёрнутых снимаются — в
    /// обёртке они ничего не значат. Иначе рамка вокруг текста в третьей строке сетки уехала бы в первую, а
    /// текст внутри неё носил бы строку, которой там нет.
    /// </para>
    /// <para>
    /// Правка — одна замена на месте первого и уход остальных. Текст обёртки собирается в черновике:
    /// снять атрибуты и обернуть одним редактором нельзя — обе правки трогают тот же элемент.
    /// </para>
    /// </remarks>
    public Task<bool> WrapAsync(IReadOnlyList<XamlElementPath> paths, string container, IReadOnlySet<string> slots)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentNullException.ThrowIfNull(slots);

        if (Wrappable(_document.Syntax, paths) is not { } taken)
            return Task.FromResult(false);

        var label = taken.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, _strings["form.edit.wrap"], NameOf(XamlElementPath.Of(taken[0])), container)
            : string.Format(CultureInfo.CurrentCulture, _strings["form.edit.wrapMany"], taken.Count, container);

        var wrapper = new List<XamlElementPath>(1);

        return EditAsync(label, editor =>
        {
            if (Wrappable(editor.Document, paths) is not { } elements)
                return;

            var first = elements[0];
            var name = editor.Qualify((XamlElement)first.Parent!, AvaloniaNamespace, container);

            editor.ReplaceElement(first, Wrapped(editor.Document, elements, name.ToString(), slots));

            foreach (var other in elements.Skip(1))
                editor.RemoveElement(other);

            wrapper.Add(XamlElementPath.Of(first));
        }, wrapper);
    }

    /// <summary>
    /// Снимает обёртки одной правкой: дети встают на место обёртки, а выбранными — они.
    /// </summary>
    /// <param name="paths">Пути выбранного.</param>
    /// <param name="placements">Для каждой обёртки — чем ставит её родитель и чем ставит детей она сама.</param>
    /// <returns>Изменился ли текст.</returns>
    /// <remarks>
    /// Зеркало обёртки: место обёртки в раскладке её родителя переходит к детям, а то, чем их ставила
    /// обёртка, у них снимается. Сколько детей примет место обёртки, решает тот, кто зовёт: правка этого не
    /// знает.
    /// </remarks>
    public Task<bool> UnwrapAsync(IReadOnlyList<XamlElementPath> paths, IReadOnlyDictionary<XamlElementPath, UnwrapSlots> placements)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(placements);

        var taken = Unwrappable(_document.Syntax, paths);

        if (taken.Count == 0)
            return Task.FromResult(false);

        var label = taken.Count == 1
            ? Format("form.edit.unwrap", NameOf(XamlElementPath.Of(taken[0])))
            : string.Format(CultureInfo.CurrentCulture, _strings["form.edit.unwrapMany"], taken.Count);

        var lifted = new List<XamlElementPath>();

        return EditAsync(label, editor =>
        {
            var elements = Unwrappable(editor.Document, paths);
            var shifts = new List<Shift>(elements.Count);
            var counts = new List<(XamlElementPath Path, int Children)>(elements.Count);

            foreach (var element in elements)
            {
                var path = XamlElementPath.Of(element);
                var last = path.Steps[^1];
                var children = element.ContentElements.Count();

                editor.ReplaceElement(element, Unwrapped(editor.Document, path, placements.GetValueOrDefault(path) ?? UnwrapSlots.None));

                // Одно место стало местами всех детей: соседи после него сдвинулись на детей без одного.
                shifts.Add(new Shift(path.Parent!, last.MemberName, last.Index, children - 1));
                counts.Add((path, children));
            }

            foreach (var (path, children) in counts)
            {
                var moved = Shifted(path, shifts);

                for (var child = 0; child < children; child++)
                    lifted.Add(Sibling(moved, moved.Steps[^1].Index + child));
            }
        }, lifted);
    }

    /// <summary>
    /// Текст обёртки, какой она встанет на место первого: соседи внутри, место первого в раскладке — на ней.
    /// </summary>
    private static string Wrapped(XamlDocument document, List<XamlElement> elements, string wrapperName, IReadOnlySet<string> slots)
    {
        var paths = elements.Select(XamlElementPath.Of).ToList();
        var carried = SlotsOn(elements[0], slots);

        var strip = document.Edit();

        foreach (var element in elements)
        {
            foreach (var attribute in SlotsOn(element, slots))
                strip.RemoveAttribute(element, attribute.Name);
        }

        var stripped = strip.Apply();
        var markup = carried.Count == 0
            ? $"<{wrapperName}></{wrapperName}>"
            : $"<{wrapperName} {string.Join(' ', carried.Select(static attribute => attribute.GetText()))}></{wrapperName}>";

        var wrapped = stripped.WrapElements(paths.Select(path => path.Resolve(stripped)!), markup);

        return paths[0].Resolve(wrapped)!.GetText();
    }

    /// <summary>
    /// Текст, который встанет на место снятой обёртки: её дети — с её местом в раскладке и без того, чем
    /// их ставила она.
    /// </summary>
    private static string Unwrapped(XamlDocument document, XamlElementPath path, UnwrapSlots placement)
    {
        var element = path.Resolve(document)!;
        var carried = SlotsOn(element, placement.ByParent);
        var prepare = document.Edit();

        foreach (var child in element.ContentElements)
        {
            foreach (var attribute in SlotsOn(child, placement.OfChildren))
            {
                if (!carried.Any(written => written.Name.LocalName == attribute.Name.LocalName))
                    prepare.RemoveAttribute(child, attribute.Name);
            }

            foreach (var attribute in carried)
                prepare.SetAttribute(child, attribute.Name, attribute.GetValueText());
        }

        var prepared = prepare.Apply();
        var inPrepared = path.Resolve(prepared)!;
        var unwrapped = prepared.UnwrapElement(inPrepared);

        // Снятие — одна замена на месте обёртки: что встало вместо неё, столько же длиннее или короче текст.
        var length = inPrepared.Span.Length + (unwrapped.SourceText.Length - prepared.SourceText.Length);

        return unwrapped.SourceText.GetText(new TextSpan(inPrepared.Span.Start, length));
    }

    /// <summary>Атрибуты элемента, которые читает его место в раскладке, — по порядку, как написаны.</summary>
    private static List<XamlAttribute> SlotsOn(XamlElement element, IReadOnlySet<string> slots) =>
        slots.Count == 0
            ? []
            : [.. element.Attributes.Where(attribute => attribute is not XamlNamespaceDeclaration && slots.Contains(attribute.Name.LocalName))];

    /// <summary>
    /// Путь после правки, которая прибавила или убрала элементы у его предков: шаг сдвигается на всё, что
    /// прибавилось перед ним в том же месте того же родителя.
    /// </summary>
    private static XamlElementPath Shifted(XamlElementPath path, IReadOnlyList<Shift> shifts)
    {
        var steps = new List<XamlPathStep>(path.Steps.Length);
        var prefix = XamlElementPath.Root;

        foreach (var step in path.Steps)
        {
            var by = shifts
                .Where(shift => shift.Parent.Equals(prefix)
                    && string.Equals(shift.Member, step.MemberName, StringComparison.Ordinal)
                    && shift.At < step.Index)
                .Sum(static shift => shift.By);

            steps.Add(step with { Index = step.Index + by });
            prefix = Append(prefix, step);
        }

        return PathOf(steps);
    }

    /// <summary>Путь на шаг глубже.</summary>
    private static XamlElementPath Append(XamlElementPath parent, XamlPathStep step) =>
        PathOf([.. parent.Steps, step]);

    /// <summary>Путь из шагов — так, как его пишет и читает <see cref="XamlElementPath"/>.</summary>
    private static XamlElementPath PathOf(IReadOnlyList<XamlPathStep> steps) =>
        steps.Count == 0
            ? XamlElementPath.Root
            : XamlElementPath.Parse(string.Concat(steps.Select(static step => step.MemberName is null
                ? string.Create(CultureInfo.InvariantCulture, $"/{step.Index}")
                : string.Create(CultureInfo.InvariantCulture, $"/{step.MemberName}:{step.Index}"))));

    /// <summary>Перевод строки документа: им же разделены элементы в буфере.</summary>
    private static string NewLineOf(XamlDocument syntax) =>
        syntax.SourceText.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Сдвиг мест у родителя: после места <paramref name="At"/> стало на <paramref name="By"/> элементов больше.</summary>
    private readonly record struct Shift(XamlElementPath Parent, string? Member, int At, int By);
}

/// <summary>Что скопировано: текст для буфера, сколько элементов и как назвать первый.</summary>
/// <param name="Text">Разметка.</param>
/// <param name="Count">Сколько элементов.</param>
/// <param name="Name">Первый — тип и имя.</param>
internal sealed record Copied(string Text, int Count, string Name);

/// <summary>Чем снятую обёртку ставил её родитель и чем она сама ставила детей — именами, как их пишет документ.</summary>
/// <param name="ByParent">Члены, которые читает родитель обёртки: <c>Grid.Row</c> у ребёнка сетки.</param>
/// <param name="OfChildren">Члены, которые обёртка читает у детей: <c>Canvas.Left</c> у детей <c>Canvas</c>.</param>
internal sealed record UnwrapSlots(IReadOnlySet<string> ByParent, IReadOnlySet<string> OfChildren)
{
    /// <summary>Места в раскладке нет ни у кого: панели потока.</summary>
    public static UnwrapSlots None { get; } = new(new HashSet<string>(), new HashSet<string>());
}
