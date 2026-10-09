using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Выбор и просмотр XAML: холст, текст и пути между ними.
// Часть FormCanvas; общее описание типа — в FormCanvas.cs.
internal sealed partial class FormCanvas
{
    private readonly AxCodeView? _code;
    private List<Picked> _selection = [];
    private List<Control> _waiting = [];
    private FormSlot? _lastActive;
    private FormSlot? _announced;
    private IReadOnlyList<XamlElementPath> _announcedSelection = [];

    // За чьим текстом XAML пошёл последним — и чей текст стоит: текст встаёт после подсветки, и между ними он в пути.
    private FormSlot? _codeFor;
    private FormSlot? _codeSlot;
    private XamlDocument? _shownCode;
    private int _codeTurn;
    private int _syncing;
    private bool _reselecting;

    /// <summary>
    /// Форма, с которой работают: форма главного выбранного, без выбора — та, где выбирали последней, а у
    /// холста с одной формой — она.
    /// </summary>
    /// <remarks>
    /// Её правки строения делает меню, её XAML показывает просмотр. Считаются только формы, стоящие на
    /// холсте: форма, ушедшая с доски, ещё держит сессию — на случай, если её вернут, — но работать с ней
    /// уже нельзя. Выбрана карточка формы, которая ещё не встала живой, — формы, с которой работают, пока
    /// нет: прежняя ею уже не будет, а эта станет, когда встанет.
    /// </remarks>
    public FormSlot? Active =>
        _selection.Count > 0 ? _selection[0].Slot
        : _waiting.Count > 0 ? null
        : _lastActive is { Item: not null } last && _slots.Contains(last) ? last
        : Placed() is [var only] ? only
        : null;

    /// <summary>Сменилась форма, с которой работают (<see cref="Active"/>).</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>
    /// Сменился выбор формы, с которой работают (<see cref="Selection"/>): человек выбрал, правка сдвинула пути
    /// или форма сменилась.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// У формы, с которой работают, сменился текст: правка, отмена, текст с диска, — или её документ открылся,
    /// закрылся или удалён.
    /// </summary>
    public event EventHandler? ContentChanged;

    /// <summary>Выбор формы, с которой работают, — пути элементов, первый главный.</summary>
    public IReadOnlyList<XamlElementPath> Selection => Active is { } active ? SelectionOf(active) : [];

    /// <summary>Текст, который показывает просмотр XAML.</summary>
    public XamlDocument? Code => _shownCode;

    /// <summary>Пути, выбранные в форме, первый главный.</summary>
    /// <param name="slot">Форма.</param>
    public IReadOnlyList<XamlElementPath> SelectionOf(FormSlot slot) =>
        [.. _selection.Where(picked => ReferenceEquals(picked.Slot, slot)).Select(picked => picked.Path)];

    /// <summary>
    /// Выбирает элементы формы — после правки, сдвинувшей пути: выбрать надо то, что она оставила.
    /// </summary>
    /// <param name="slot">Форма.</param>
    /// <param name="paths">Пути в нынешнем тексте, первый главный.</param>
    public void Select(FormSlot slot, IReadOnlyList<XamlElementPath> paths)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(paths);

        _selection = [.. paths.Distinct().Select(path => new Picked(slot, path))];
        _waiting = [];
        Follow(reveal: true);
        Reselect();
    }

    /// <summary>
    /// Форма встала на холст: её карточку, выбранную, пока форма ещё не стояла живой, холст берёт в свой
    /// выбор — как корень, — и без этого выбор пропал бы при первом же переносе путей на холст.
    /// </summary>
    private void Adopt(FormSlot slot)
    {
        if (slot.Item is not { } item || _waiting.Count == 0)
            return;

        var adopted = _waiting.FindAll(target => ReferenceEquals(target, item) || item.IsVisualAncestorOf(target));

        if (adopted.Count == 0)
            return;

        _waiting.RemoveAll(adopted.Contains);

        foreach (var target in adopted)
        {
            if (slot.PathOf(target) is { } path && !_selection.Contains(new Picked(slot, path)))
                _selection.Add(new Picked(slot, path));
        }

        Follow(reveal: false);
    }

    /// <summary>Выбор, который ставит сам холст: его события — не выбор человека.</summary>
    internal SyncScope Syncing()
    {
        _syncing++;

        return new SyncScope(this);
    }

    /// <summary>
    /// Выбирает на холсте то, что названо путями, — когда холст разложен: контрол, построенный правкой
    /// только что, рамки ещё не имеет, и ядро его не выберет.
    /// </summary>
    /// <remarks>
    /// Просьбы сливаются в одну. Пока она ждёт, ядро может сообщить, что выбранный контрол ушёл из дерева, —
    /// это пересборка, а не выбор человека, и путей она не трогает.
    /// </remarks>
    private void Reselect()
    {
        if (_reselecting || _disposed)
            return;

        _reselecting = true;

        Dispatcher.UIThread.Post(() =>
        {
            _reselecting = false;

            if (!_disposed)
                SelectOnSheet();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Ставит выбор холста по путям; путь, которого в тексте больше нет, уступает родителю.</summary>
    /// <remarks>
    /// Клавиатура остаётся там, где работает человек: выбор, сделанный в XAML или правкой, холст только
    /// показывает. Взятая холстом, она уходила бы из кода после каждого щелчка. Выбор формы, которая не стоит
    /// на холсте живой, остаётся путями: встанет — выберется.
    /// </remarks>
    private void SelectOnSheet()
    {
        if (_frozen is not null || !_slots.Any(IsReady))
            return;

        var resolved = new List<Picked>(_selection.Count);
        var targets = new List<Control>();

        foreach (var picked in _selection)
        {
            if (!IsReady(picked.Slot))
            {
                if (!resolved.Contains(picked))
                    resolved.Add(picked);

                continue;
            }

            var syntax = picked.Slot.Document!.Syntax;
            var current = picked.Path;

            while (current is not null && current.Resolve(syntax) is null)
                current = current.Parent;

            if (current is null || resolved.Contains(new Picked(picked.Slot, current)))
                continue;

            resolved.Add(new Picked(picked.Slot, current));

            if (picked.Slot.TargetOf(current) is { } target && !targets.Contains(target))
                targets.Add(target);
        }

        // Выбранные карточки форм, которые ещё не встали, остаются выбранными: их выбор холст подхватит, когда
        // форма встанет (Adopt), а перенос путей не должен его стереть.
        foreach (var waiting in _waiting)
        {
            if (waiting.IsAttachedToVisualTree() && !targets.Contains(waiting))
                targets.Add(waiting);
        }

        _selection = resolved;
        Reconsider();

        if (!SameAsSheet(targets))
        {
            using (Syncing())
            {
                if (targets.Count == 0)
                    Sheet.SelectedItems?.Clear();

                for (var index = 0; index < targets.Count; index++)
                    Sheet.SelectTarget(targets[index], additive: index > 0, takeFocus: false);
            }
        }
    }

    /// <summary>Стоит ли форма на холсте живой: карточка есть, корень и документ — тоже.</summary>
    private static bool IsReady(FormSlot slot) =>
        slot.Item is not null && slot.Shown is { Root: not null } && slot.Document is not null;

    private bool SameAsSheet(List<Control> targets)
    {
        var selected = Sheet.SelectedTargets;

        if (selected.Count != targets.Count)
            return false;

        for (var index = 0; index < targets.Count; index++)
        {
            if (!ReferenceEquals(selected[index].Target, targets[index]) || !targets[index].IsAttachedToVisualTree())
                return false;
        }

        return true;
    }

    /// <summary>Esc — к тому, в чём стоит выбранное; на корне выбор снимается.</summary>
    /// <returns>Было ли что выбрано: нечего — Esc уходит дальше.</returns>
    internal bool SelectParent()
    {
        if (_selection.Count == 0 && _waiting.Count > 0)
        {
            // Выбрана карточка формы, которая ещё не встала: она и есть корень — Esc снимает выбор.
            _waiting = [];

            using (Syncing())
                Sheet.SelectedItems?.Clear();

            Reconsider();

            return true;
        }

        if (_selection.Count == 0)
            return false;

        var primary = _selection[0];

        if (primary.Path.Equals(XamlElementPath.Root))
        {
            _selection = [];

            using (Syncing())
                Sheet.SelectedItems?.Clear();

            Reconsider();

            return true;
        }

        _selection = [new Picked(primary.Slot, primary.Path.Parent ?? XamlElementPath.Root)];
        SelectOnSheet();
        ShowSelectionInCode(reveal: true);

        return true;
    }

    /// <summary>Забывает выбор формы, которую сняли с холста.</summary>
    private void Forget(FormSlot slot)
    {
        _selection.RemoveAll(picked => ReferenceEquals(picked.Slot, slot));

        if (ReferenceEquals(_lastActive, slot))
            _lastActive = null;

        Reconsider();
    }

    /// <summary>Формы, стоящие на холсте, — с карточкой.</summary>
    private List<FormSlot> Placed() => _slots.FindAll(slot => slot.Item is not null);

    /// <summary>
    /// Форма, с которой работают, могла смениться: XAML идёт за ней — сменилась, и текст перечитывается, нет — на
    /// стоящем ставится отметка главного, — а хозяин и иерархия узнают, если сменились она или её выбор.
    /// </summary>
    /// <param name="reveal">Прокрутить XAML к главному выбранному.</param>
    /// <remarks>
    /// <para>
    /// Дорога одна у всего, после чего она могла смениться: у выбора, каретки в XAML, Esc и форм, вставших на холст
    /// и ушедших с него, — единственная форма холста становится ею сама, а вставшая рядом вторая это снимает.
    /// Дорога, где XAML не сверили бы, оставила бы под формой чужой текст или пустой просмотр.
    /// </para>
    /// <para>
    /// Сверяют её с формой, за чьим текстом XAML пошёл последним, а не с той, чей текст стоит: текст встаёт после
    /// подсветки, вне потока интерфейса, и перемена за это время, сверенная со стоящим, прошла бы незамеченной. Пока
    /// формы доски встают по одной, текст первой, на миг единственной, встал бы под доской, когда рядом уже стоит
    /// вторая и работать не с чем. Текст ещё в пути, а показать надо выбранное — он перечитывается заново, с
    /// прокруткой.
    /// </para>
    /// </remarks>
    private void Reconsider(bool reveal = false)
    {
        var active = Active;

        if (_code is not null && (!ReferenceEquals(active, _codeFor) || (reveal && !ReferenceEquals(active, _codeSlot))))
            _ = RefreshCodeAsync(reveal);
        else
            ShowSelectionInCode(reveal);

        Announce(active);
    }

    /// <summary>Человек выбрал на холсте: пути — из показа, отметка в XAML — на главном.</summary>
    private void OnSheetSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        if (_syncing > 0 || _reselecting || _frozen is not null)
            return;

        var picked = new List<Picked>();
        var waiting = new List<Control>();

        foreach (var target in e.NewTargets)
        {
            if (SlotOf(target.Target) is { } slot && slot.PathOf(target.Target) is { } path)
            {
                if (!picked.Contains(new Picked(slot, path)))
                    picked.Add(new Picked(slot, path));
            }
            else
            {
                // Карточка формы, которая ещё не встала: путей у неё пока нет, а выбор — есть.
                waiting.Add(target.Target);
            }
        }

        _selection = picked;
        _waiting = waiting;
        Follow(reveal: true);
    }

    /// <summary>Каретку поставили в XAML: выбирается самый вложенный элемент под ней.</summary>
    private void OnCaretMoved(object? sender, AxCodeCaretMovedEventArgs e)
    {
        if (_code is null || _shownCode is not { } syntax || _codeSlot is not { } slot || XamlHighlighter.ElementAt(syntax, e.Offset) is not { } element)
            return;

        _selection = [new Picked(slot, XamlElementPath.Of(element))];
        _waiting = [];
        _lastActive = slot;
        _code.Highlight = new AxCodeRange(element.Span.Start, element.Span.Length);
        SelectOnSheet();
        Reconsider();
    }

    /// <summary>
    /// Выбор сменился: форма, с которой работают, запоминается, а XAML показывает её — со отметкой главного.
    /// </summary>
    private void Follow(bool reveal)
    {
        if (_selection.Count > 0)
            _lastActive = _selection[0].Slot;

        Reconsider(reveal);
    }

    /// <summary>
    /// Говорит, что форма, с которой работают, или её выбор сменились, — если сменились. Зовёт его только пересмотр
    /// (<see cref="Reconsider"/>), которым кончается каждая дорога выбора.
    /// </summary>
    /// <param name="active">Форма, с которой работают, — какой её счёл пересмотр.</param>
    private void Announce(FormSlot? active)
    {
        var selection = Selection;
        var formChanged = !ReferenceEquals(active, _announced);
        var selectionChanged = formChanged || !selection.SequenceEqual(_announcedSelection);

        _announced = active;
        _announcedSelection = selection;

        if (formChanged)
            ActiveChanged?.Invoke(this, EventArgs.Empty);

        if (selectionChanged)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Отмечает в XAML главный выбранный элемент — от открывающего тега до закрывающего.</summary>
    /// <param name="reveal">Прокрутить к нему, если его не видно.</param>
    private void ShowSelectionInCode(bool reveal)
    {
        if (_code is null || _shownCode is not { } syntax)
            return;

        AxCodeRange? range = _selection.FirstOrDefault(picked => ReferenceEquals(picked.Slot, _codeSlot)) is { Slot: not null } primary
                             && primary.Path.Resolve(syntax) is { } element
            ? new AxCodeRange(element.Span.Start, element.Span.Length)
            : null;

        _code.Highlight = range;

        if (reveal && range is { } shown)
            _code.ScrollIntoView(shown);
    }

    /// <summary>
    /// Перечитывает просмотр XAML: роли текста считаются вне потока интерфейса, а текст и роли встают
    /// вместе — роли прежнего текста на новом красили бы мимо.
    /// </summary>
    /// <param name="reveal">Прокрутить к главному выбранному, когда текст встанет.</param>
    /// <remarks>
    /// За чьим текстом пошли, помнится сразу, а чей текст стоит — когда он встал; встаёт только тот, за которым
    /// пошли последним. Без формы и у формы, чей документ ещё не открыт, показывать нечего, и стоящий текст уходит
    /// сразу: под её именем он был бы чужим. Её текст встанет, когда документ откроется.
    /// </remarks>
    private async Task RefreshCodeAsync(bool reveal = false)
    {
        if (_code is null || _disposed)
            return;

        var slot = _codeFor = Active;
        var turn = ++_codeTurn;

        if (slot?.Document is not { } document)
        {
            _codeSlot = null;
            _shownCode = null;
            _code.Text = string.Empty;
            _code.Spans = [];
            _code.Highlight = null;

            return;
        }

        var syntax = document.Syntax;
        var lifetime = _lifetime.Token;
        IReadOnlyList<AxCodeSpan> spans;

        try
        {
            spans = await Task.Run(() => XamlHighlighter.Spans(syntax), lifetime);

            if (_options.CodeHighlighted is { } highlighted)
                await highlighted(lifetime);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Context.Log.Write(StudioLogLevel.Warning, BoardModel.LogSource, $"{slot.Session.Path.FileName}: XAML без подсветки — {e.Message}");
            spans = [];
        }

        if (_disposed || turn != _codeTurn)
            return;

        _codeSlot = slot;
        _shownCode = syntax;
        _code.Text = syntax.SourceText.ToString();
        _code.Spans = spans;
        ShowSelectionInCode(reveal);
    }

    /// <summary>Выбранный элемент: форма и путь в ней.</summary>
    private readonly record struct Picked(FormSlot Slot, XamlElementPath Path);

    /// <summary>Выбор, который ставит сам холст.</summary>
    internal readonly struct SyncScope(FormCanvas owner) : IDisposable
    {
        public void Dispose() => owner._syncing--;
    }
}
