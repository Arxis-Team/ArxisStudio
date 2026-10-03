using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Dragging;

/// <summary>
/// Указатель тяги, которую ведёт студия: захват у источника, движения, отпускание, Esc и клавиши.
/// </summary>
/// <remarks>
/// <para>
/// Движения и отпускание слушаются на источнике — захват у него, и всё приходит к нему — первыми, на
/// спуске, и отмечаются обработанными: пока несут, источник не выбирает строки и не тянет рамку по
/// тем же движениям.
/// </para>
/// <para>
/// Клавиши слушаются у окна источника: каретка остаётся там, где стояла, а Esc и Ctrl должны работать,
/// где бы она ни была. Сменившийся модификатор спрашивает цель заново на том же месте — Ctrl меняет
/// ответ, не дожидаясь движения мыши.
/// </para>
/// <para>
/// Сперва — куда легло, потом — отпустить захват: отпускание синхронно поднимает «захват потерян», а
/// тот тягу бросает, и спрашивать «куда отпустили» было бы уже не у кого.
/// </para>
/// </remarks>
internal sealed class DragLoop
{
    private readonly DragSession _session;
    private readonly Control _source;
    private readonly IPointer _pointer;
    private readonly TopLevel? _top;
    private readonly TaskCompletionSource<DragDropEffects> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Point _at;
    private KeyModifiers _keys;
    private bool _finished;

    /// <summary>Захватывает указатель и спрашивает цель под ним.</summary>
    /// <param name="session">Сеанс тяги.</param>
    /// <param name="source">Элемент, за который взялись.</param>
    /// <param name="trigger">Движение, на котором жест стал тягой.</param>
    public DragLoop(DragSession session, Control source, PointerEventArgs trigger)
    {
        _session = session;
        _source = source;
        _pointer = trigger.Pointer;
        _top = TopLevel.GetTopLevel(source);
        _at = trigger.GetPosition(source);
        _keys = trigger.KeyModifiers;

        source.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        source.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        source.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, handledEventsToo: true);
        _top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top?.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        session.Closed += OnClosed;

        _pointer.Capture(source);
        session.Over(_at, _keys);
    }

    /// <summary>Ответ цели на отпускание; бросили — отказ.</summary>
    public Task<DragDropEffects> Done => _done.Task;

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
            return;

        _at = e.GetPosition(_source);
        _keys = e.KeyModifiers;
        _session.Over(_at, _keys);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
            return;

        _at = e.GetPosition(_source);
        _keys = e.KeyModifiers;
        _session.Over(_at, _keys);
        e.Handled = true;

        // Отпускание закрывает сеанс само, и «закрыт» отсюда — не новая тяга, а этот же ответ.
        _session.Closed -= OnClosed;
        Finish(_session.Drop());
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _pointer))
            Finish(DragDropEffects.None);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Finish(DragDropEffects.None);
            return;
        }

        Rekey(e.Key, down: true);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => Rekey(e.Key, down: false);

    /// <summary>Модификатор нажат или отпущен: цель отвечает заново на том же месте.</summary>
    private void Rekey(Key key, bool down)
    {
        var modifier = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            _ => KeyModifiers.None,
        };

        if (modifier == KeyModifiers.None)
            return;

        _keys = down ? _keys | modifier : _keys & ~modifier;
        _session.Over(_at, _keys);
    }

    /// <summary>Новая тяга закрыла этот сеанс: указатель отпускается, ответ — отказ.</summary>
    private void OnClosed(object? sender, EventArgs e) => Finish(DragDropEffects.None);

    /// <summary>Кончает тягу: закрывает сеанс, отписывается и отпускает захват.</summary>
    private void Finish(DragDropEffects effect)
    {
        if (_finished)
            return;

        // Тяга кончается раньше, чем отпускается захват: «захват потерян», поднятый отпусканием, должен
        // застать её законченной.
        _finished = true;
        _session.Closed -= OnClosed;
        _session.Dispose();

        _source.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _source.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _source.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _top?.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);

        if (ReferenceEquals(_pointer.Captured, _source))
            _pointer.Capture(null);

        _done.TrySetResult(effect);
    }
}
