using ArxisStudio.Controls;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Выбор и просмотр XAML: холст, текст и пути между ними.
// Часть LiveFormDocument; общее описание типа — в LiveFormDocument.cs.
internal sealed partial class LiveFormDocument
{
    private List<XamlElementPath> _selection = [];
    private XamlDocument? _code;
    private int _codeTurn;
    private int _syncing;
    private bool _reselecting;

    /// <summary>Выбор вкладки — пути элементов, первый главный; тестам.</summary>
    internal IReadOnlyList<XamlElementPath> Selection => _selection;

    /// <summary>Текст, который показывает просмотр XAML; тестам.</summary>
    internal XamlDocument? Code => _code;

    /// <summary>
    /// Выбирает элементы — после правки, сдвинувшей пути: выбрать надо то, что она оставила; и тестам.
    /// </summary>
    /// <param name="paths">Пути в нынешнем тексте, первый главный.</param>
    internal void Select(IReadOnlyList<XamlElementPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _selection = [.. paths.Distinct()];
        ShowSelectionInCode(reveal: true);
        Reselect();
    }

    /// <summary>
    /// Выбирает на холсте то, что названо путями, — когда холст разложен: контрол, построенный правкой
    /// только что, рамки ещё не имеет, и ядро его не выберет.
    /// </summary>
    /// <remarks>
    /// Просьбы сливаются в одну. Пока она ждёт, ядро может сообщить, что выбранный контрол ушёл из дерева,
    /// — это пересборка, а не выбор человека, и путей она не трогает.
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
    /// показывает. Взятая холстом, она уходила бы из кода после каждого щелчка.
    /// </remarks>
    private void SelectOnSheet()
    {
        if (_shown is not { Root: not null } shown || _document is not { } document || _frozen is not null)
            return;

        var paths = Resolved(document.Syntax);
        var targets = paths.Select(path => TargetOf(shown, path)).OfType<Control>().Distinct().ToList();

        _selection = paths;

        if (!SameAsSheet(targets))
        {
            using (Syncing())
            {
                if (targets.Count == 0)
                    _view.Sheet.SelectedItems?.Clear();

                for (var index = 0; index < targets.Count; index++)
                    _view.Sheet.SelectTarget(targets[index], additive: index > 0, takeFocus: false);
            }
        }

        ShowSelectionInCode(reveal: false);
    }

    /// <summary>Пути выбора в тексте: пропавший путь — к ближайшему уцелевшему предку.</summary>
    private List<XamlElementPath> Resolved(XamlDocument syntax)
    {
        var resolved = new List<XamlElementPath>(_selection.Count);

        foreach (var path in _selection)
        {
            var current = path;

            while (current is not null && current.Resolve(syntax) is null)
                current = current.Parent;

            if (current is not null && !resolved.Contains(current))
                resolved.Add(current);
        }

        return resolved;
    }

    /// <summary>
    /// Что на холсте стоит за элементом: корень — карточка, остальное — его контрол, а элемент, построивший
    /// не контрол (определение строки, ресурс), — ближайший контрол над ним.
    /// </summary>
    private Control? TargetOf(IXamlDesignView shown, XamlElementPath path)
    {
        for (XamlElementPath? current = path; current is not null; current = current.Parent)
        {
            if (current.Equals(XamlElementPath.Root))
                return _form;

            if (shown.ObjectAt(current) is Control control)
                return control;
        }

        return null;
    }

    /// <summary>Путь того, что выбрано на холсте: карточка — корень документа.</summary>
    private XamlElementPath? PathOfTarget(Control target) =>
        ReferenceEquals(target, _form) ? XamlElementPath.Root : _shown?.PathOf(target);

    private bool SameAsSheet(List<Control> targets)
    {
        var selected = _view.Sheet.SelectedTargets;

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
    private bool SelectParent()
    {
        if (_selection.FirstOrDefault() is not { } primary)
            return false;

        if (primary.Equals(XamlElementPath.Root))
        {
            _selection = [];

            using (Syncing())
                _view.Sheet.SelectedItems?.Clear();

            ShowSelectionInCode(reveal: false);

            return true;
        }

        _selection = [primary.Parent ?? XamlElementPath.Root];
        SelectOnSheet();
        ShowSelectionInCode(reveal: true);

        return true;
    }

    /// <summary>Человек выбрал на холсте: пути — из показа, отметка в XAML — на главном.</summary>
    private void OnSheetSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        if (_syncing > 0 || _reselecting || _frozen is not null)
            return;

        _selection = [.. e.NewTargets.Select(target => PathOfTarget(target.Target)).OfType<XamlElementPath>().Distinct()];
        ShowSelectionInCode(reveal: true);
    }

    /// <summary>Каретку поставили в XAML: выбирается самый вложенный элемент под ней.</summary>
    private void OnCaretMoved(object? sender, AxCodeCaretMovedEventArgs e)
    {
        if (_code is not { } syntax || XamlHighlighter.ElementAt(syntax, e.Offset) is not { } element)
            return;

        _selection = [XamlElementPath.Of(element)];
        _view.Code.Highlight = new AxCodeRange(element.Span.Start, element.Span.Length);
        SelectOnSheet();
    }

    /// <summary>Отмечает в XAML главный выбранный элемент — от открывающего тега до закрывающего.</summary>
    /// <param name="reveal">Прокрутить к нему, если его не видно.</param>
    private void ShowSelectionInCode(bool reveal)
    {
        if (_code is not { } syntax)
            return;

        AxCodeRange? range = _selection.FirstOrDefault()?.Resolve(syntax) is { } element
            ? new AxCodeRange(element.Span.Start, element.Span.Length)
            : null;

        _view.Code.Highlight = range;

        if (reveal && range is { } shown)
            _view.Code.ScrollIntoView(shown);
    }

    /// <summary>
    /// Перечитывает просмотр XAML: роли текста считаются вне потока интерфейса, а текст и роли встают
    /// вместе — роли прежнего текста на новом красили бы мимо.
    /// </summary>
    private async Task RefreshCodeAsync()
    {
        if (_document is not { } document)
            return;

        var syntax = document.Syntax;
        var turn = ++_codeTurn;
        IReadOnlyList<AxCodeSpan> spans;

        try
        {
            spans = await Task.Run(() => XamlHighlighter.Spans(syntax), _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _context.Log.Write(StudioLogLevel.Warning, BoardModel.LogSource, $"{_path.FileName}: XAML без подсветки — {e.Message}");
            spans = [];
        }

        if (_disposed || turn != _codeTurn)
            return;

        _code = syntax;
        _view.Code.Text = syntax.SourceText.ToString();
        _view.Code.Spans = spans;
        ShowSelectionInCode(reveal: false);
    }

    /// <summary>Выбор, который ставит сама вкладка: его события — не выбор человека.</summary>
    private SyncScope Syncing()
    {
        _syncing++;

        return new SyncScope(this);
    }

    private readonly struct SyncScope(LiveFormDocument owner) : IDisposable
    {
        public void Dispose() => owner._syncing--;
    }
}
