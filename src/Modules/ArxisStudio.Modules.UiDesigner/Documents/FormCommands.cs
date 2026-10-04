using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Форма, выбранное в которой правят команды: её выбор, документ и показ.</summary>
internal interface IFormTarget
{
    /// <summary>Выбор в форме — пути элементов, первый главный.</summary>
    IReadOnlyList<XamlElementPath> Selection { get; }

    /// <summary>Документ формы, когда он взят.</summary>
    IXamlDocumentHandle? Document { get; }

    /// <summary>Показ формы, когда он есть.</summary>
    IXamlDesignView? Shown { get; }
}

/// <summary>
/// Правки строения выбранного — вырезать, копировать, вставить, дублировать, удалить, обернуть, снять
/// обёртку: одной дорогой у клавиш холста и у пунктов его меню.
/// </summary>
/// <remarks>
/// <para>
/// <b>Что можно — спрашивается здесь.</b> Пункт меню выключен там же, где клавиша не берётся: нечего
/// делать — нажатие уходит дальше, а не пропадает. Так устроены и правки окна проекта.
/// </para>
/// <para>
/// <b>Буфер обмена — системы, текстом.</b> Скопированное — разметка, которую вставит и Rider, а вставка
/// берёт и то, что скопировано в нём: фрагмент сам называет свои пространства (<see cref="XamlFragment"/>).
/// Своего буфера у дизайнера нет — два буфера разошлись бы.
/// </para>
/// <para>
/// <b>Место вставки</b> — <see cref="FormLanding"/>: в выбранную панель или после выбранного. Несколько
/// элементов встают только туда, где мест сколько угодно, — в рамку встанет один.
/// </para>
/// </remarks>
internal sealed class FormCommands
{
    /// <summary>Пустой набор членов: месту в раскладке нечего читать.</summary>
    private static readonly IReadOnlySet<string> NoSlots = new HashSet<string>(StringComparer.Ordinal);

    private readonly IFormTarget _form;
    private readonly FormEdits _edits;
    private readonly IStudioStrings _strings;
    private readonly Action<string> _say;

    /// <summary>Команды формы.</summary>
    /// <param name="form">Форма на холсте: её выбор, документ и показ.</param>
    /// <param name="edits">Правки её документа.</param>
    /// <param name="strings">Словарь модуля.</param>
    /// <param name="say">Строка состояния.</param>
    public FormCommands(IFormTarget form, FormEdits edits, IStudioStrings strings, Action<string> say)
    {
        _form = form;
        _edits = edits;
        _strings = strings;
        _say = say;
    }

    /// <summary>
    /// Чем оборачивают — как «Group Into» у Blend: рамки и панели Avalonia. Тип пишется в пространстве
    /// Avalonia; приставку, если документ дал ему другую, найдёт правка.
    /// </summary>
    public static IReadOnlyList<string> Wrappers { get; } =
        ["Border", "Grid", "StackPanel", "DockPanel", "WrapPanel", "Canvas", "ScrollViewer", "Viewbox"];

    /// <summary>Держит ли обёртка одно содержимое: в такую встанет только один элемент.</summary>
    /// <param name="container">Тип обёртки.</param>
    public static bool HoldsOne(string container) => container is "Border" or "ScrollViewer" or "Viewbox";

    /// <summary>Есть ли что взять: выбрано что-то, кроме корня.</summary>
    public bool CanTake => Syntax is { } syntax && FormEdits.Taken(syntax, Selection).Count > 0;

    /// <summary>Есть ли куда вставить.</summary>
    /// <remarks>Что лежит в буфере, меню узнать не успевает: пустая вставка скажет об этом строкой состояния.</remarks>
    public bool CanPaste => Landing() is not null;

    /// <summary>Можно ли снять обёртку с выбранного: оно стоит в содержимом, и его дети поместятся на его место.</summary>
    public bool CanUnwrap => Unwrappable(out _) is { Count: > 0 };

    /// <summary>Можно ли обернуть выбранное этим контейнером.</summary>
    /// <param name="container">Тип обёртки.</param>
    public bool CanWrap(string container) =>
        Syntax is { } syntax
        && FormEdits.Wrappable(syntax, Selection) is { } taken
        && (taken.Count == 1 || !HoldsOne(container));

    private IReadOnlyList<XamlElementPath> Selection => _form.Selection;

    private XamlDocument? Syntax => _form.Document?.Syntax;

    /// <summary>Делает правку сочетания, если ей есть что делать.</summary>
    /// <param name="gesture">Сочетание из <see cref="FormKeys"/>.</param>
    /// <param name="owner">Где нажали.</param>
    /// <returns>Взята ли клавиша: делать нечего — нет.</returns>
    public bool Run(KeyGesture gesture, Visual owner)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(owner);

        if (gesture.Equals(FormKeys.Paste))
        {
            if (!CanPaste)
                return false;

            _ = PasteAsync(owner);

            return true;
        }

        if (!CanTake)
            return false;

        if (gesture.Equals(FormKeys.Copy))
            _ = CopyAsync(owner);
        else if (gesture.Equals(FormKeys.Cut))
            _ = CutAsync(owner);
        else if (gesture.Equals(FormKeys.Duplicate))
            _ = DuplicateAsync();
        else
            return false;

        return true;
    }

    /// <summary>Кладёт разметку выбранного в буфер обмена.</summary>
    /// <param name="owner">Чьё окно даёт буфер.</param>
    /// <returns>Легло ли в буфер.</returns>
    /// <remarks>Копирование глазу не видно — поэтому о нём говорит строка состояния.</remarks>
    public Task<bool> CopyAsync(Visual owner) => PutAsync(owner, say: true);

    /// <summary>Кладёт разметку выбранного в буфер и убирает выбранное одной правкой.</summary>
    /// <param name="owner">Чьё окно даёт буфер.</param>
    /// <returns>Вырезано ли.</returns>
    /// <remarks>Не легло в буфер — не убирается: вырезанное, которого нет нигде, — потерянное.</remarks>
    public async Task<bool> CutAsync(Visual owner)
    {
        var paths = Selection.ToList();

        if (!await PutAsync(owner, say: false))
            return false;

        await _edits.CutAsync(paths);

        return true;
    }

    /// <summary>Вставляет разметку из буфера в выбранную панель или после выбранного и выбирает вставленное.</summary>
    /// <param name="owner">Чьё окно даёт буфер.</param>
    /// <returns>Изменился ли текст формы.</returns>
    public async Task<bool> PasteAsync(Visual owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard)
        {
            _say(_strings["form.clipboard.none"]);
            return false;
        }

        var text = await clipboard.TryGetTextAsync();
        var fragments = text is null ? [] : FormEdits.Fragments(text);

        if (fragments.Count == 0)
        {
            _say(_strings["form.paste.empty"]);
            return false;
        }

        // Место — после чтения буфера: пока его читали, выбор мог смениться.
        if (Landing() is not { } landing
            || (fragments.Count > 1 && _form.Shown is { } shown && !FormLanding.HoldsMany(shown, landing.Parent)))
        {
            _say(_strings["form.paste.nowhere"]);
            return false;
        }

        return await _edits.PasteAsync(landing.Parent, landing.Index, fragments);
    }

    /// <summary>Ставит копию каждого выбранного рядом с ним.</summary>
    public Task<bool> DuplicateAsync() => _edits.DuplicateAsync([.. Selection]);

    /// <summary>Убирает выбранное.</summary>
    public Task DeleteAsync() => _edits.DeleteAsync([.. Selection]);

    /// <summary>Оборачивает выбранное контейнером, и место первого в раскладке переходит к обёртке.</summary>
    /// <param name="container">Тип обёртки.</param>
    /// <returns>Изменился ли текст.</returns>
    public Task<bool> WrapAsync(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        if (!CanWrap(container) || Syntax is not { } syntax || FormEdits.Wrappable(syntax, Selection) is not { } taken)
            return Task.FromResult(false);

        return _edits.WrapAsync([.. Selection], container, SlotsOf(taken[0]));
    }

    /// <summary>Снимает обёртку с выбранного: дети встают на её место и с её местом в раскладке.</summary>
    /// <returns>Изменился ли текст.</returns>
    public Task<bool> UnwrapAsync()
    {
        if (Unwrappable(out var refusal) is not { Count: > 0 } taken)
        {
            if (refusal is not null)
                _say(refusal);

            return Task.FromResult(false);
        }

        // Дети одного родителя ставятся одними членами: хватает спросить первого.
        var slots = taken.ToDictionary(
            XamlElementPath.Of,
            element => new UnwrapSlots(
                SlotsOf(element),
                element.ContentElements.FirstOrDefault() is { } child ? SlotsOf(child) : NoSlots));

        return _edits.UnwrapAsync([.. Selection], slots);
    }

    /// <summary>
    /// Что снимется: выбранное в содержимом своих родителей. Обёртка с несколькими детьми снимается только
    /// туда, где мест сколько угодно, — иначе все, кроме одного, стали бы ошибкой загрузки.
    /// </summary>
    private List<XamlElement>? Unwrappable(out string? refusal)
    {
        refusal = null;

        if (Syntax is not { } syntax || _form.Shown is not { } shown)
            return null;

        var taken = FormEdits.Unwrappable(syntax, Selection);

        foreach (var element in taken)
        {
            var children = element.ContentElements.Count();
            var parent = XamlElementPath.Of(element).Parent!;

            if (children > 1 && !FormLanding.HoldsMany(shown, parent))
            {
                refusal = string.Format(
                    CultureInfo.CurrentCulture,
                    _strings["form.unwrap.single"],
                    _edits.NameOf(XamlElementPath.Of(element)),
                    _edits.NameOf(parent),
                    children);

                return null;
            }
        }

        return taken;
    }

    /// <summary>Куда встанет вставленное; null — некуда.</summary>
    private (XamlElementPath Parent, int Index)? Landing() =>
        _form.Shown is { } shown && Syntax is { } syntax ? FormLanding.For(Selection, shown, syntax) : null;

    /// <summary>
    /// Члены, которыми родитель ставит элемент: присоединённые, чей владелец — тип родителя или его база, —
    /// <c>Grid.Row</c> у ребёнка сетки и у ребёнка её наследника, <c>Canvas.Left</c> у ребёнка <c>Canvas</c>.
    /// </summary>
    /// <remarks>
    /// Тип родителя знает показ — объектом, построенным из элемента; в ответ уходят только имена типов, и
    /// объект поколения дольше вызова не держится. Без показа — по виду имени: владелец — тот тип, которым
    /// документ написал родителя.
    /// </remarks>
    private IReadOnlySet<string> SlotsOf(XamlElement element)
    {
        if (element.Parent is not XamlElement parent)
            return NoSlots;

        var owners = _form.Shown?.ObjectAt(XamlElementPath.Of(parent)) is { } live
            ? TypeNames(live.GetType())
            : new HashSet<string>(StringComparer.Ordinal) { parent.Name.LocalName };

        return element.Attributes
            .Select(static attribute => attribute.Name.LocalName)
            .Where(name => name.IndexOf('.', StringComparison.Ordinal) is var dot and > 0 && owners.Contains(name[..dot]))
            .ToHashSet(StringComparer.Ordinal);

        static HashSet<string> TypeNames(Type? type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (; type is not null; type = type.BaseType)
                names.Add(type.Name);

            return names;
        }
    }

    /// <summary>Кладёт разметку выбранного в буфер обмена окна.</summary>
    /// <param name="owner">Чьё окно даёт буфер.</param>
    /// <param name="say">Сказать ли строкой состояния, что легло.</param>
    private async Task<bool> PutAsync(Visual owner, bool say)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (_edits.Copy([.. Selection]) is not { } copied)
            return false;

        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard)
        {
            _say(_strings["form.clipboard.none"]);
            return false;
        }

        await clipboard.SetTextAsync(copied.Text);

        if (say)
        {
            _say(copied.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, _strings["form.copied.one"], copied.Name)
                : string.Format(CultureInfo.CurrentCulture, _strings["form.copied.many"], copied.Count));
        }

        return true;
    }
}
