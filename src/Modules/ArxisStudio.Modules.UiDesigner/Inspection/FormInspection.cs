using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Дизайнер — источник инспектора: выбранное на холсте впереди показано в нём, а без выбора — корень формы, с которой
/// работают.
/// </summary>
/// <remarks>
/// <para>
/// <b>Идёт за холстом впереди</b> (<see cref="FormFront"/>) — тем же, за которым идёт иерархия: его формой, с которой
/// работают, и её выбором. Холста впереди нет — дизайнеру показывать нечего, и его цель уходит.
/// </para>
/// <para>
/// <b>Возвращает своё наверх, когда с холстом снова работают</b> (<see cref="FormFront.Working"/>): плагин мог
/// показать в инспекторе свой объект, а вернувшийся к холсту человек ждёт там снова выбранное.
/// </para>
/// <para>
/// <b>Каретки не берёт.</b> Показ — не переход: выбор на холсте оставляет каретку на холсте.
/// </para>
/// <para>
/// <b>Замок уважает.</b> Новый выбор — новая цель поверх прежней, а прежнюю, которую держит замок инспектора,
/// дизайнер не отпускает, пока замок не снимут: закреплённое остаётся показанным, пока на холсте выбирают другое.
/// </para>
/// </remarks>
internal sealed class FormInspection : IDisposable
{
    private readonly IStudioContext _context;
    private readonly IStudioInspector _inspector;
    private readonly FormFront _front;
    private readonly List<(XamlInspectorTarget Target, IDisposable Shown)> _kept = [];
    private FormCanvas? _canvas;
    private XamlInspectorTarget? _target;
    private IDisposable? _shown;
    private bool _disposed;

    /// <summary>Начинает показывать выбранное в инспекторе.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="inspector">Инспектор студии.</param>
    public FormInspection(IStudioContext context, IStudioInspector inspector)
    {
        _context = context;
        _inspector = inspector;
        _front = FormFront.Of(context);
        _front.CanvasChanged += OnCanvasChanged;
        _front.Working += OnWorking;
        _inspector.CurrentChanged += OnCurrentChanged;
        Follow(_front.Canvas);
    }

    /// <summary>Цель, которую дизайнер показывает сейчас, — тестам.</summary>
    internal XamlInspectorTarget? Target => _target;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _front.CanvasChanged -= OnCanvasChanged;
        _front.Working -= OnWorking;
        _inspector.CurrentChanged -= OnCurrentChanged;
        Unfollow();
        Let();
        Forget();
    }

    private void OnCanvasChanged(object? sender, EventArgs e) => Follow(_front.Canvas);

    /// <summary>Идёт за холстом впереди — или ни за каким.</summary>
    private void Follow(FormCanvas? canvas)
    {
        if (!ReferenceEquals(canvas, _canvas))
        {
            Unfollow();

            _canvas = canvas;

            if (canvas is not null)
            {
                canvas.ActiveChanged += OnSelection;
                canvas.SelectionChanged += OnSelection;
                canvas.ContentChanged += OnSelection;
            }
        }

        Pick();
    }

    private void Unfollow()
    {
        if (_canvas is not { } canvas)
            return;

        canvas.ActiveChanged -= OnSelection;
        canvas.SelectionChanged -= OnSelection;
        canvas.ContentChanged -= OnSelection;
        _canvas = null;
    }

    private void OnSelection(object? sender, EventArgs e) => Pick();

    /// <summary>Показывает выбранное в форме, с которой работают; без выбора — её корень.</summary>
    private void Pick()
    {
        if (_disposed)
            return;

        if (_canvas is not { IsDisposed: false } canvas || canvas.Active is not { Session.Document: not null } slot)
        {
            Let();
            return;
        }

        var paths = canvas.SelectionOf(slot);

        if (paths.Count == 0)
            paths = [XamlElementPath.Root];

        if (_target is { IsClosed: false } current && ReferenceEquals(current.Slot, slot) && current.Paths.SequenceEqual(paths))
            return;

        Show(new XamlInspectorTarget(_context, canvas, slot, paths));
    }

    /// <summary>Ставит новую цель поверх прежней; прежнюю отпускает, если её не держит замок.</summary>
    private void Show(XamlInspectorTarget target)
    {
        var previous = _target;
        var previousShown = _shown;

        _target = target;

        // Новая встаёт раньше, чем уходит прежняя: между ними инспектор показал бы то, что лежит ниже.
        _shown = _inspector.Show(target);

        if (previous is null || previousShown is null)
            return;

        if (_inspector.IsLocked && ReferenceEquals(_inspector.Current, previous) && !previous.IsClosed)
        {
            _kept.Add((previous, previousShown));
            return;
        }

        previousShown.Dispose();
        previous.Shut();
    }

    /// <summary>Отпускает цель: показывать нечего.</summary>
    private void Let()
    {
        if (_target is not { } target)
            return;

        if (_inspector.IsLocked && ReferenceEquals(_inspector.Current, target) && !target.IsClosed && _shown is { } shown)
            _kept.Add((target, shown));
        else
        {
            _shown?.Dispose();
            target.Shut();
        }

        _target = null;
        _shown = null;
    }

    /// <summary>
    /// С холстом снова работают: выбранное — наверх, если поверх него показано чужое.
    /// </summary>
    private void OnWorking(object? sender, FormCanvas canvas)
    {
        if (_disposed || !ReferenceEquals(canvas, _canvas) || _target is not { IsClosed: false } target ||
            ReferenceEquals(_inspector.Current, target))
        {
            return;
        }

        var again = _inspector.Show(target);

        _shown?.Dispose();
        _shown = again;
    }

    /// <summary>Замок сняли: цели, которые он держал, больше не нужны.</summary>
    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        if (!_inspector.IsLocked)
            Forget();
    }

    /// <summary>Отпускает цели, которые держал замок.</summary>
    private void Forget()
    {
        var kept = _kept.ToArray();

        _kept.Clear();

        foreach (var (target, shown) in kept)
        {
            shown.Dispose();
            target.Shut();
        }
    }
}
