using ArxisStudio.Surface;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Какие формы доски на виду: место формы — с заголовком окна над ней — против видимой области холста с
/// запасом.
/// </summary>
/// <remarks>
/// <para>
/// Холст дизайнера держит карточки всех форм — его панель не виртуализирует, — поэтому «у формы есть
/// карточка» ещё не значит «форму видно». Что дорого держать — живые формы и растры снимков, — держат
/// только формы на виду.
/// </para>
/// <para>
/// Запас — доля видимой области с каждой стороны (<see cref="Margin"/>): листают — следующая форма уже
/// готова. С виду форма уходит только за двойным запасом: взгляд, качнувшийся у края, не будит и не гасит
/// её на каждом кадре. Холст вне окна или спрятанный не видит ничего.
/// </para>
/// <para>
/// Пересчёт сливается в один на проход диспетчера: панорама меняет положение десятки раз за кадр.
/// </para>
/// </remarks>
internal sealed class BoardSight : IDisposable
{
    /// <summary>Запас вокруг видимой области — доля её ширины и высоты с каждой стороны.</summary>
    public const double Margin = 0.25;

    private readonly SurfaceView _sheet;
    private readonly Func<FormCard, Rect> _placeOf;
    private readonly Dictionary<FormCard, Control> _cards = [];
    private readonly HashSet<FormCard> _seen = [];
    private bool _queued;
    private bool _disposed;

    /// <summary>Начинает следить, какие формы на виду.</summary>
    /// <param name="sheet">Холст доски.</param>
    /// <param name="placeOf">Место формы на холсте — вместе с заголовком окна над ней.</param>
    public BoardSight(SurfaceView sheet, Func<FormCard, Rect> placeOf)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(placeOf);

        _sheet = sheet;
        _placeOf = placeOf;

        sheet.ContainerPrepared += OnPrepared;
        sheet.ContainerClearing += OnClearing;
        sheet.PropertyChanged += OnSheetChanged;
        sheet.AttachedToVisualTree += OnTreeChanged;
        sheet.DetachedFromVisualTree += OnTreeChanged;

        foreach (var container in sheet.GetRealizedContainers())
        {
            if (CardOf(container) is { } card)
                Watch(card, container);
        }

        Queue();
    }

    /// <summary>Форма показалась.</summary>
    public event EventHandler<FormCard>? Came;

    /// <summary>Форма ушла с виду — или с доски.</summary>
    public event EventHandler<FormCard>? Went;

    /// <summary>Формы на виду.</summary>
    public IReadOnlyCollection<FormCard> Seen => _seen;

    /// <summary>На виду ли форма.</summary>
    /// <param name="card">Форма.</param>
    public bool Sees(FormCard card) => _seen.Contains(card);

    /// <summary>
    /// Пересчитывает сразу, не дожидаясь прохода диспетчера: тестам и тем, кому ответ нужен сейчас.
    /// </summary>
    public void Update()
    {
        _queued = false;

        if (_disposed)
            return;

        var view = View();
        var came = new List<FormCard>();
        var went = new List<FormCard>();

        foreach (var card in _cards.Keys)
        {
            var seen = _seen.Contains(card);
            var area = view is { } shown ? Grown(shown, seen ? 2 * Margin : Margin) : default(Rect?);

            if (area is { } inside && _placeOf(card).Intersects(inside))
            {
                if (_seen.Add(card))
                    came.Add(card);
            }
            else if (_seen.Remove(card))
            {
                went.Add(card);
            }
        }

        foreach (var card in went)
            Went?.Invoke(this, card);

        foreach (var card in came)
            Came?.Invoke(this, card);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sheet.ContainerPrepared -= OnPrepared;
        _sheet.ContainerClearing -= OnClearing;
        _sheet.PropertyChanged -= OnSheetChanged;
        _sheet.AttachedToVisualTree -= OnTreeChanged;
        _sheet.DetachedFromVisualTree -= OnTreeChanged;

        foreach (var container in _cards.Values)
            container.PropertyChanged -= OnContainerChanged;

        _cards.Clear();
        _seen.Clear();
    }

    /// <summary>
    /// Видимая область холста в его мировых координатах; null — холст не виден вовсе.
    /// </summary>
    private Rect? View()
    {
        var zoom = _sheet.ViewportZoom;
        var size = _sheet.Bounds.Size;

        if (!_sheet.IsVisible || !_sheet.IsAttachedToVisualTree() || zoom <= 0 || size.Width <= 0 || size.Height <= 0)
            return null;

        return new Rect(_sheet.ViewportLocation, new Size(size.Width / zoom, size.Height / zoom));
    }

    private static Rect Grown(Rect area, double share) =>
        area.Inflate(new Thickness(area.Width * share, area.Height * share));

    private void Watch(FormCard card, Control container)
    {
        if (_cards.TryGetValue(card, out var old))
        {
            if (ReferenceEquals(old, container))
                return;

            old.PropertyChanged -= OnContainerChanged;
        }

        _cards[card] = container;
        container.PropertyChanged += OnContainerChanged;
    }

    private void OnPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (CardOf(e.Container) is not { } card)
            return;

        Watch(card, e.Container);
        Queue();
    }

    /// <summary>Форма ушла с доски: с виду она ушла сразу, а не на следующем проходе.</summary>
    private void OnClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (CardOf(e.Container) is not { } card
            || !_cards.TryGetValue(card, out var container)
            || !ReferenceEquals(container, e.Container))
        {
            return;
        }

        container.PropertyChanged -= OnContainerChanged;
        _cards.Remove(card);

        if (_seen.Remove(card))
            Went?.Invoke(this, card);
    }

    /// <summary>Карточка сдвинулась или сменила размер.</summary>
    private void OnContainerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
            Queue();
    }

    private void OnSheetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.ViewportLocationProperty
            || e.Property == SurfaceView.ViewportZoomProperty
            || e.Property == Visual.BoundsProperty
            || e.Property == Visual.IsVisibleProperty)
        {
            Queue();
        }
    }

    private void OnTreeChanged(object? sender, VisualTreeAttachmentEventArgs e) => Queue();

    private void Queue()
    {
        if (_queued || _disposed)
            return;

        _queued = true;
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Background);
    }

    private FormCard? CardOf(Control container) =>
        container.DataContext as FormCard ?? _sheet.ItemFromContainer(container) as FormCard;
}
