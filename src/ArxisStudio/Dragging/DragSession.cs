using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ArxisStudio.Dragging;

/// <summary>
/// Одна тяга: где курсор, чья цель под ним, что она ответила и как это видно.
/// </summary>
/// <remarks>
/// <para>
/// <b>Где.</b> Точка источника переводится в точку экрана и ищется в окнах студии сверху вниз: окно,
/// захватившее указатель, получает движения и тогда, когда курсор над оторванным окном поверх него.
/// </para>
/// <para>
/// <b>Чья цель.</b> Ближайший к курсору элемент с <see cref="StudioDragDrop.AllowDropProperty"/>, сам или
/// предок того, что под курсором. Сменилась цель — прежняя слышит <c>DragLeave</c>, новая —
/// <c>DragEnter</c>, и сразу <c>DragOver</c>: отвечает цель там.
/// </para>
/// <para>
/// <b>Как видно.</b> Подсказка у курсора (<see cref="DragGhost"/>) лежит в слое оверлеев того окна,
/// над которым курсор, и переезжает за ним из окна в окно. Курсор ставится источнику — он держит
/// захват, и его курсор выигрывает у любого под ним; прежний возвращается, когда тяга кончилась.
/// </para>
/// <para>
/// Упавшая цель выпадает из тяги: её не спрашивают до конца сеанса, а сбой уходит виновнику один раз,
/// а не на каждое движение мыши.
/// </para>
/// </remarks>
internal sealed class DragSession : IStudioDragSession
{
    private static readonly Cursor Moving = new(StandardCursorType.DragMove);
    private static readonly Cursor Copying = new(StandardCursorType.DragCopy);
    private static readonly Cursor Linking = new(StandardCursorType.DragLink);
    private static readonly Cursor Refusing = new(StandardCursorType.No);

    private readonly StudioDrags _owner;
    private readonly Control _source;
    private readonly Cursor? _cursor;
    private readonly HashSet<Interactive> _failed = [];
    private readonly HashSet<Interactive> _warned = [];

    private Interactive? _target;
    private TopLevel? _root;
    private Point _at;
    private KeyModifiers _keys;
    private Cursor? _shown;
    private bool _ended;

    /// <summary>Открывает сеанс; подсказка у курсора появится с первым движением.</summary>
    /// <param name="owner">Служба: окна, журнал, виновники.</param>
    /// <param name="source">Элемент, держащий указатель.</param>
    /// <param name="data">Что несут.</param>
    /// <param name="allowedEffects">Что разрешено чужим целям.</param>
    /// <param name="visual">Что видно у курсора.</param>
    public DragSession(StudioDrags owner, Control source, StudioDragData data, DragDropEffects allowedEffects, StudioDragVisual visual)
    {
        _owner = owner;
        _source = source;
        _cursor = source.Cursor;
        Data = data;
        AllowedEffects = allowedEffects;
        Ghost = new DragGhost(visual) { IsVisible = false };
    }

    /// <summary>Сеанс кончился — отпусканием, отказом источника или новой тягой.</summary>
    public event EventHandler? Closed;

    /// <inheritdoc/>
    public StudioDragData Data { get; }

    /// <inheritdoc/>
    public DragDropEffects AllowedEffects { get; }

    /// <inheritdoc/>
    public DragDropEffects Effect { get; private set; }

    /// <inheritdoc/>
    public string? Hint { get; private set; }

    /// <inheritdoc/>
    public bool IsActive => !_ended;

    /// <summary>Подсказка у курсора.</summary>
    public DragGhost Ghost { get; }

    /// <summary>Цель под курсором; её нет — пусто. Тестам.</summary>
    internal Interactive? Target => _target;

    /// <inheritdoc/>
    public Visual? ElementAt(Point position) =>
        !_ended && Locate(position) is ({ } root, var at) ? root.InputHitTest(at) as Visual : null;

    /// <inheritdoc/>
    public DragDropEffects Over(Point position, KeyModifiers keyModifiers)
    {
        if (_ended)
            return DragDropEffects.None;

        _keys = keyModifiers;

        var (root, at) = Locate(position);

        Follow(root, at);
        Retarget(root is null ? null : TargetAt(root, at), root, at);

        if (_target is { } target && Ask(target, StudioDragDrop.DragOverEvent, DragDropEffects.None, null) is { } answer)
            Show(answer.Effect, answer.Hint);
        else
            Show(DragDropEffects.None, null);

        return Effect;
    }

    /// <inheritdoc/>
    public void OverOwn(Point position, DragDropEffects effect, string? hint = null)
    {
        if (_ended)
            return;

        var (root, at) = Locate(position);

        Follow(root, at);
        Retarget(null, root, at);
        Show(effect, hint);
    }

    /// <inheritdoc/>
    public DragDropEffects Drop()
    {
        if (_ended)
            return DragDropEffects.None;

        var effect = DragDropEffects.None;

        if (_target is { } target)
        {
            effect = Ask(target, StudioDragDrop.DropEvent, Effect, Hint)?.Effect ?? DragDropEffects.None;
            Retarget(null, _root, _at);
        }

        End();

        return effect;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ended)
            return;

        Retarget(null, _root, _at);
        End();
    }

    /// <summary>
    /// Окно под курсором и точка в нём; курсор вне окон студии — пусто.
    /// </summary>
    private (TopLevel? Root, Point At) Locate(Point position)
    {
        if (TopLevel.GetTopLevel(_source) is not { IsVisible: true } own || _source.TranslatePoint(position, own) is not { } local)
            return (null, default);

        var screen = own.PointToScreen(local);

        // Окно, закрывшееся посреди тяги, у студии ещё числится, а экранных пикселей у него уже нет:
        // перевод в них кончился бы исключением.
        foreach (var window in _owner.Windows(own))
        {
            if (!window.IsVisible)
                continue;

            var client = window.PointToClient(screen);

            if (new Rect(window.ClientSize).Contains(client))
                return (window, client);
        }

        return (null, default);
    }

    /// <summary>Цель под точкой окна: сам элемент или его предок, объявивший себя целью.</summary>
    private Interactive? TargetAt(TopLevel root, Point at)
    {
        var target = (root.InputHitTest(at) as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<Interactive>()
            .FirstOrDefault(StudioDragDrop.GetAllowDrop);

        return target is not null && _failed.Contains(target) ? null : target;
    }

    /// <summary>Меняет цель: прежней — DragLeave, новой — DragEnter.</summary>
    private void Retarget(Interactive? target, TopLevel? root, Point at)
    {
        if (!ReferenceEquals(target, _target) && _target is { } previous)
        {
            // Прощаются с целью в её окне и в последней точке над ней: курсор мог уйти в другое окно.
            _target = null;
            Ask(previous, StudioDragDrop.DragLeaveEvent, DragDropEffects.None, null);
        }

        _root = root;
        _at = at;

        if (target is null || ReferenceEquals(target, _target))
            return;

        _target = target;
        Ask(target, StudioDragDrop.DragEnterEvent, DragDropEffects.None, null);
    }

    /// <summary>
    /// Поднимает событие на цели и читает её ответ; упала — пусто, и цель из тяги выпадает.
    /// </summary>
    private (DragDropEffects Effect, string? Hint)? Ask(Interactive target, RoutedEvent<StudioDragEventArgs> routed, DragDropEffects effect, string? hint)
    {
        if (_root is not { } root)
            return null;

        var args = new StudioDragEventArgs(routed, Data, AllowedEffects, root, _at, _keys)
        {
            Effect = effect,
            Hint = hint,
        };

        try
        {
            target.RaiseEvent(args);
        }
        catch (Exception error) when (Faults.Survivable(error))
        {
            if (_failed.Add(target))
                _owner.Fail(target, error);

            if (ReferenceEquals(_target, target))
            {
                _target = null;
                Farewell(target, root);
            }

            return null;
        }

        return (Checked(target, args.Effect), args.Hint);
    }

    /// <summary>
    /// Прощается с упавшей целью: она могла успеть поставить отметку, и убрать её может только она.
    /// </summary>
    /// <remarks>Падение и здесь — тот же сбой, уже сказанный: второй раз о нём не говорят.</remarks>
    private void Farewell(Interactive target, TopLevel root)
    {
        try
        {
            target.RaiseEvent(new StudioDragEventArgs(StudioDragDrop.DragLeaveEvent, Data, AllowedEffects, root, _at, _keys));
        }
        catch (Exception error) when (Faults.Survivable(error))
        {
            // Цель уже выпала из тяги, а виновник уже знает: второе падение — тот же сбой.
        }
    }

    /// <summary>
    /// Ответ цели по договору: один разрешённый эффект или отказ. Нарушение — отказ и строка в журнале,
    /// одна на цель за тягу.
    /// </summary>
    private DragDropEffects Checked(Interactive target, DragDropEffects effect)
    {
        var single = (effect & (effect - 1)) == 0;

        if (effect == DragDropEffects.None || (single && (AllowedEffects & effect) == effect))
            return effect;

        if (_warned.Add(target))
            _owner.Warn($"цель {target.GetType().Name} ответила {effect}, а источник разрешил {AllowedEffects}: это отказ");

        return DragDropEffects.None;
    }

    /// <summary>Ставит ответ: курсор источнику и подсказку у курсора.</summary>
    private void Show(DragDropEffects effect, string? hint)
    {
        Effect = effect;
        Hint = string.IsNullOrWhiteSpace(hint) ? null : hint;
        Ghost.Hint = Hint;

        var cursor = effect switch
        {
            DragDropEffects.Move => Moving,
            DragDropEffects.Copy => Copying,
            DragDropEffects.Link => Linking,
            _ => Refusing,
        };

        // Ответ спрашивается на каждом движении мыши, а меняется редко: курсор пишется, когда сменился.
        if (ReferenceEquals(cursor, _shown))
            return;

        _shown = cursor;
        _source.SetCurrentValue(InputElement.CursorProperty, cursor);
    }

    /// <summary>Ведёт подсказку к курсору — в слой оверлеев окна под ним; вне окон студии она прячется.</summary>
    private void Follow(TopLevel? root, Point at)
    {
        if (root is null || OverlayLayer.GetOverlayLayer(root) is not { } layer || root.TranslatePoint(at, layer) is not { } place)
        {
            Ghost.IsVisible = false;
            return;
        }

        if (!ReferenceEquals(Ghost.Parent, layer))
        {
            (Ghost.Parent as Panel)?.Children.Remove(Ghost);
            layer.Children.Add(Ghost);
        }

        var offset = root.TryFindResource("AxSpaceLoose", root.ActualThemeVariant, out var value) && value is double space ? space : 0;

        Canvas.SetLeft(Ghost, place.X + offset);
        Canvas.SetTop(Ghost, place.Y + offset);
        Ghost.IsVisible = true;
    }

    /// <summary>Кончает тягу: прячет подсказку, возвращает курсор и забывает цели.</summary>
    private void End()
    {
        _ended = true;
        _target = null;
        _root = null;
        _failed.Clear();
        _warned.Clear();

        (Ghost.Parent as Panel)?.Children.Remove(Ghost);
        _source.SetCurrentValue(InputElement.CursorProperty, _cursor);
        Effect = DragDropEffects.None;
        Hint = null;

        _owner.Ended(this);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
