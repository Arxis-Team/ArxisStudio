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
    private FormSlot? _lastActive;
    private FormSlot? _announced;
    private FormSlot? _codeSlot;
    private XamlDocument? _shownCode;
    private int _codeTurn;
    private int _syncing;
    private bool _reselecting;

    /// <summary>
    /// Форма, с которой работают: форма главного выбранного, без выбора — та, где выбирали последней, а у
    /// холста с одной формой — она.
    /// </summary>
    /// <remarks>Её правки строения делает меню, её XAML показывает просмотр.</remarks>
    public FormSlot? Active =>
        _selection.Count > 0 ? _selection[0].Slot
        : _lastActive is { } last && _slots.Contains(last) ? last
        : _slots.Count == 1 ? _slots[0]
        : null;

    /// <summary>Сменилась форма, с которой работают (<see cref="Active"/>).</summary>
    public event EventHandler? ActiveChanged;

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
        Follow(reveal: true);
        Reselect();
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

        _selection = resolved;
        Announce();

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

        ShowSelectionInCode(reveal: false);
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
        if (_selection.Count == 0)
            return false;

        var primary = _selection[0];

        if (primary.Path.Equals(XamlElementPath.Root))
        {
            _selection = [];

            using (Syncing())
                Sheet.SelectedItems?.Clear();

            ShowSelectionInCode(reveal: false);
            Announce();

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

        if (ReferenceEquals(_codeSlot, slot))
            _ = RefreshCodeAsync(reveal: false);

        Announce();
    }

    /// <summary>Человек выбрал на холсте: пути — из показа, отметка в XAML — на главном.</summary>
    private void OnSheetSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        if (_syncing > 0 || _reselecting || _frozen is not null)
            return;

        var picked = new List<Picked>();

        foreach (var target in e.NewTargets)
        {
            if (SlotOf(target.Target) is { } slot && slot.PathOf(target.Target) is { } path && !picked.Contains(new Picked(slot, path)))
                picked.Add(new Picked(slot, path));
        }

        _selection = picked;
        Follow(reveal: true);
    }

    /// <summary>Каретку поставили в XAML: выбирается самый вложенный элемент под ней.</summary>
    private void OnCaretMoved(object? sender, AxCodeCaretMovedEventArgs e)
    {
        if (_code is null || _shownCode is not { } syntax || _codeSlot is not { } slot || XamlHighlighter.ElementAt(syntax, e.Offset) is not { } element)
            return;

        _selection = [new Picked(slot, XamlElementPath.Of(element))];
        _lastActive = slot;
        _code.Highlight = new AxCodeRange(element.Span.Start, element.Span.Length);
        SelectOnSheet();
        Announce();
    }

    /// <summary>
    /// Выбор сменился: форма, с которой работают, запоминается, а XAML показывает её — со отметкой главного.
    /// </summary>
    private void Follow(bool reveal)
    {
        if (_selection.Count > 0)
            _lastActive = _selection[0].Slot;

        if (_code is not null && !ReferenceEquals(Active, _codeSlot))
            _ = RefreshCodeAsync(reveal);
        else
            ShowSelectionInCode(reveal);

        Announce();
    }

    /// <summary>Говорит, что форма, с которой работают, сменилась, — если сменилась.</summary>
    private void Announce()
    {
        if (ReferenceEquals(Active, _announced))
            return;

        _announced = Active;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
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
    private async Task RefreshCodeAsync(bool reveal = false)
    {
        if (_code is null || _disposed)
            return;

        var slot = Active;
        var turn = ++_codeTurn;

        if (slot is null)
        {
            _codeSlot = null;
            _shownCode = null;
            _code.Text = string.Empty;
            _code.Spans = [];
            _code.Highlight = null;

            return;
        }

        if (slot.Document is not { } document)
            return;

        var syntax = document.Syntax;
        var lifetime = _lifetime.Token;
        IReadOnlyList<AxCodeSpan> spans;

        try
        {
            spans = await Task.Run(() => XamlHighlighter.Spans(syntax), lifetime);
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
