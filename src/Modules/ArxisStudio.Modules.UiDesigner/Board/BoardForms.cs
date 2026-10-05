using System.ComponentModel;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Формы доски на холсте: карточка каждой формы с её именем, снимок, пока живая форма не встала, и живая
/// форма, когда встала.
/// </summary>
/// <remarks>
/// <para>
/// <b>Живы видимые — на любом масштабе.</b> Форма, показавшаяся на холсте (<see cref="BoardSight"/>), встаёт
/// живой — документ берётся у службы XAML и показывается на карточке, — а ушедшая с виду отдаёт показ через
/// паузу (<see cref="UiDesignerOptions.BoardHideDelay"/>): листают туда и обратно — форма не строится заново.
/// Форма, в которой выбрано, показ не отдаёт, пока выбор в ней: выбранное — то, с чем работают, и меню и
/// клавиши холста — о нём. Встают формы по одной, в простое между ними: первая форма решения поднимает
/// сессию дизайна, и это секунды, а остальные встают за десятки миллисекунд, не останавливая холст.
/// </para>
/// <para>
/// <b>Вкладка на экране старше доски</b>: её форма стоит на доске снимком. Вкладка, ушедшая с экрана, — младше:
/// доска, у которой её форма на виду, берёт её живой (<see cref="FormShows.Freed"/>), и правка из другого
/// редактора видна на доске сразу.
/// </para>
/// <para>
/// <b>Пока живая не встала</b> — и когда форму держит вкладка на экране, — на её месте снимок: тот же, что на
/// плитке окна проекта. Снимка нет — подложка и контур её размера.
/// </para>
/// <para>
/// <b>Документ живёт дольше показа.</b> Форма, ушедшая с виду, отдаёт показ, а документ — только если его
/// нечего беречь: правки, которые доска может отменить, и несохранённое держат его, — иначе Ctrl+Z на доске
/// отменял бы то, чего уже нет, а несохранённое пропало бы.
/// </para>
/// </remarks>
internal sealed class BoardForms : IDisposable
{
    private readonly IStudioContext _context;
    private readonly FormsSheet _sheet;
    private readonly BoardSight _sight;
    private readonly FormCanvas _canvas;
    private readonly BoardHistory _history;
    private readonly UiDesignerOptions _options;
    private readonly IStudioXamlDocuments? _documents;
    private readonly FormShows _shows;
    private readonly Dictionary<FormCard, UiDesignerFormItem> _realized = [];
    private readonly Dictionary<CanonicalPath, LiveForm> _live = [];
    private readonly List<FormCard> _wanted = [];
    private Task _pump = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Подключает формы доски к холсту.</summary>
    /// <param name="context">Контекст модуля.</param>
    /// <param name="sheet">Холст доски.</param>
    /// <param name="sight">Какие формы на виду.</param>
    /// <param name="canvas">Холст форм над ним: выбор, жесты и правки.</param>
    /// <param name="history">История доски: правки форм ложатся в неё.</param>
    /// <param name="options">Часы и паузы.</param>
    public BoardForms(IStudioContext context, FormsSheet sheet, BoardSight sight, FormCanvas canvas, BoardHistory history, UiDesignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _sheet = sheet;
        _sight = sight;
        _canvas = canvas;
        _history = history;
        _options = options;
        _documents = context.XamlDocuments();
        _shows = FormShows.Of(context);

        sheet.ContainerPrepared += OnContainerPrepared;
        sheet.ContainerClearing += OnContainerClearing;
        sheet.SurfaceSelectionChanged += OnSelectionChanged;
        sight.Came += OnCame;
        sight.Went += OnWent;
        _shows.Freed += OnFreed;

        foreach (var container in sheet.GetRealizedContainers())
        {
            if (container is UiDesignerFormItem item && CardOf(item) is { } card)
                Prepare(card, item);
        }
    }

    /// <summary>
    /// У форм доски сменилось то, о чём говорят над холстом: состояние документа, вопрос о чужой записи,
    /// сбой открытия, — или форма встала или ушла.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>Идущие показы форм — тестам: дождаться, а не спать.</summary>
    internal Task Pumping => _pump;

    /// <summary>Живые и отпускаемые формы — их сессии; тестам.</summary>
    internal IReadOnlyCollection<FormSession> Sessions => [.. _live.Values.Select(form => form.Session)];

    /// <summary>Сессия формы, если доска её держит.</summary>
    /// <param name="card">Форма.</param>
    public FormSession? SessionOf(FormCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return _live.TryGetValue(card.Path, out var form) ? form.Session : null;
    }

    /// <summary>Стоит ли форма на холсте живой — с корнем показа на своей карточке.</summary>
    /// <param name="card">Форма.</param>
    public bool IsLive(FormCard card) =>
        _realized.TryGetValue(card, out var item)
        && _live.TryGetValue(card.Path, out var form)
        && ReferenceEquals(form.Slot.Item, item)
        && form.Session.Shown?.Root is not null;

    /// <summary>Сохраняет все формы доски, которые можно: окно студии потеряло фокус.</summary>
    public void SaveAll()
    {
        foreach (var form in _live.Values)
        {
            if (form.Session.AutoSaves)
                _ = form.Session.AutoSaveAsync();
        }
    }

    /// <summary>Доска сменила решение: прежние формы отпускаются, их правки уже не отменить.</summary>
    public void Forget()
    {
        _wanted.Clear();

        foreach (var form in _live.Values.ToList())
            Drop(form, save: true);
    }

    /// <inheritdoc/>
    /// <remarks>Несохранённое формы сохраняют, если сохранять самим разрешено, и только потом отпускают документ.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sheet.ContainerPrepared -= OnContainerPrepared;
        _sheet.ContainerClearing -= OnContainerClearing;
        _sheet.SurfaceSelectionChanged -= OnSelectionChanged;
        _sight.Came -= OnCame;
        _sight.Went -= OnWent;
        _shows.Freed -= OnFreed;

        foreach (var (card, item) in _realized)
        {
            card.PropertyChanged -= OnCardChanged;
            Reset(item);
        }

        _realized.Clear();
        _wanted.Clear();

        foreach (var form in _live.Values.ToList())
            Drop(form, save: true);
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is UiDesignerFormItem item && CardOf(item) is { } card)
            Prepare(card, item);
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is not UiDesignerFormItem item || CardOf(item) is not { } card || !_realized.Remove(card))
            return;

        card.PropertyChanged -= OnCardChanged;
        _wanted.Remove(card);

        if (_live.TryGetValue(card.Path, out var form) && ReferenceEquals(form.Slot.Item, item))
        {
            _canvas.Place(form.Slot, null);
            Hide(form);
        }

        Reset(item);
    }

    /// <summary>
    /// Готовит карточку формы: размеченный режим, объявленный размер, имя над формой, снимок — и ставит живую
    /// форму, если она уже есть, или просит её.
    /// </summary>
    private void Prepare(FormCard card, UiDesignerFormItem item)
    {
        if (!_realized.TryAdd(card, item))
            return;

        // Размеченный режим: что на форме редактируется, говорит документ — так же, как во вкладке.
        item.ContentMode = SurfaceContentMode.Annotated;
        item.Caption = card.Name;
        Size(card, item);
        card.PropertyChanged += OnCardChanged;

        if (_live.TryGetValue(card.Path, out var form))
        {
            form.Hiding?.Dispose();
            form.Hiding = null;
            _canvas.Place(form.Slot, item);
        }

        Paint(card);
        Request(card);
    }

    /// <summary>Возвращает карточке, которую холст отдаёт другой форме, то, что ставила доска.</summary>
    private static void Reset(UiDesignerFormItem item)
    {
        item.ClearValue(UiDesignerFormItem.CaptionProperty);
        Unpaint(item);
    }

    /// <summary>Снимает с места формы снимок и подложку: живая форма рисует себя сама.</summary>
    private static void Unpaint(UiDesignerFormItem item)
    {
        item.ClearValue(TemplatedControl.BackgroundProperty);
        item.ClearValue(TemplatedControl.BorderBrushProperty);
        item.ClearValue(TemplatedControl.BorderThicknessProperty);
    }

    /// <summary>
    /// Рисует место формы: живая — без подложки, её фон — фон корня; не живая — снимок, а без снимка —
    /// подложка и контур её размера.
    /// </summary>
    private void Paint(FormCard card)
    {
        if (!_realized.TryGetValue(card, out var item))
            return;

        if (IsLive(card))
        {
            Unpaint(item);
            return;
        }

        if (card.Preview is { } preview)
        {
            item.Background = new ImageBrush(preview) { Stretch = Stretch.Fill };
            item.ClearValue(TemplatedControl.BorderBrushProperty);
            item.ClearValue(TemplatedControl.BorderThicknessProperty);
            return;
        }

        item.Background = Brush("AxSurfaceBaseBrush");
        item.BorderBrush = Brush("AxStrokeControlBrush");
        item.BorderThickness = item.TryFindResource("AxHairlineThickness", item.ActualThemeVariant, out var thickness) && thickness is Thickness hairline
            ? hairline
            : default;
    }

    private IBrush? Brush(string key) =>
        _sheet.TryFindResource(key, _sheet.ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    /// <summary>
    /// Размер места формы — объявленный разметкой, а без него — рамка темы. Живой форме размер даёт её корень.
    /// </summary>
    private void Size(FormCard card, UiDesignerFormItem item)
    {
        item.Width = card.Root.Width ?? SheetControls.LengthOf(_sheet, "AxFormFrameWidth");
        item.Height = card.Root.Height ?? SheetControls.LengthOf(_sheet, "AxFormFrameHeight");
    }

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FormCard card || !_realized.TryGetValue(card, out var item))
            return;

        // Перечитанный файл мог объявить другой размер; у живой формы он свой — несохранённый, может быть.
        if (e.PropertyName is null && !IsLive(card))
            Size(card, item);

        if (e.PropertyName is nameof(FormCard.Preview) or null)
            Paint(card);
    }

    /// <summary>Форма показалась: живая остаётся, прочая встаёт.</summary>
    private void OnCame(object? sender, FormCard card)
    {
        if (_live.TryGetValue(card.Path, out var form))
        {
            form.Hiding?.Dispose();
            form.Hiding = null;
        }

        Request(card);
    }

    /// <summary>Форма ушла с виду: встать её больше не просят, а живая отдаст показ, если не вернётся.</summary>
    private void OnWent(object? sender, FormCard card)
    {
        _wanted.Remove(card);

        if (_live.TryGetValue(card.Path, out var form) && form.Shows)
            Hide(form);
    }

    /// <summary>
    /// Выбор ушёл из формы, ушедшей с виду: теперь и она отдаст показ. Холст форм подписан раньше доски и
    /// свой выбор к этому времени уже переложил.
    /// </summary>
    private void OnSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        foreach (var form in _live.Values)
        {
            if (form.Hiding is null && form.Shows && !Seen(form.Session.Path) && !Holds(form))
                Hide(form);
        }
    }

    /// <summary>Выбрано ли в форме: такая показ не отдаёт.</summary>
    private bool Holds(LiveForm form) => _canvas.SelectionOf(form.Slot).Count > 0;

    /// <summary>На виду ли форма по этому пути.</summary>
    private bool Seen(CanonicalPath path) => _sight.Seen.Any(card => card.Path == path);

    /// <summary>
    /// Вкладка отпустила форму или ушла с экрана: стоящая на виду встаёт живой снова.
    /// </summary>
    private void OnFreed(object? sender, string formPath)
    {
        foreach (var card in _realized.Keys.Where(card => string.Equals(card.Path.Value, formPath, StringComparison.OrdinalIgnoreCase)).ToList())
            Request(card);
    }

    /// <summary>Нужна ли форма живой: на виду, служба есть, и форму не держит вкладка на экране.</summary>
    private bool Wants(FormCard card) =>
        !_disposed
        && _documents is not null
        && card.Kind is not FormKind.Unreadable
        && _realized.ContainsKey(card)
        && _sight.Sees(card)
        && !_shows.IsHeldAbove(card.Path.Value, FormShowRank.Board);

    /// <summary>Просит форму живой: встанет, когда до неё дойдёт очередь.</summary>
    private void Request(FormCard card)
    {
        if (!Wants(card) || IsLive(card) || _wanted.Contains(card))
            return;

        _wanted.Add(card);

        if (_pump.IsCompleted)
            _pump = PumpAsync();
    }

    /// <summary>Ставит живыми формы по одной, в простое между ними.</summary>
    private async Task PumpAsync()
    {
        while (!_disposed && _wanted.Count > 0)
        {
            var card = _wanted[0];

            _wanted.RemoveAt(0);

            if (!Wants(card) || IsLive(card))
                continue;

            await LiveAsync(card);

            // Ввод, раскладка и кадр — раньше следующей формы: холст не стоит, пока формы встают.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }

    /// <summary>Ставит форму живой на её карточке.</summary>
    private async Task LiveAsync(FormCard card)
    {
        if (_documents is not { } documents || !_realized.TryGetValue(card, out var item))
            return;

        if (!_live.TryGetValue(card.Path, out var form))
        {
            var session = new FormSession(_context, documents, card.Path, _options, _canvas, FormShowRank.Board);

            form = new LiveForm(session, _canvas.Add(session, null));
            _live[card.Path] = form;
            session.Changed += OnSessionChanged;
            session.Edited += OnSessionEdited;
        }

        form.Hiding?.Dispose();
        form.Hiding = null;
        form.Shows = true;
        _canvas.Place(form.Slot, item);

        await form.Session.ShowAsync();
    }

    /// <summary>Корень формы встал или ушёл — место рисуется заново; о прочем говорят над холстом.</summary>
    private void OnSessionChanged(object? sender, FormChanges changes)
    {
        if (_disposed || sender is not FormSession session)
            return;

        if ((changes & (FormChanges.Root | FormChanges.Problem)) != 0)
        {
            foreach (var card in _realized.Keys.Where(card => card.Path == session.Path).ToList())
                Paint(card);
        }

        const FormChanges told = FormChanges.Opened | FormChanges.Root | FormChanges.State | FormChanges.Deleted
                                 | FormChanges.Closed | FormChanges.Conflict | FormChanges.Problem;

        if ((changes & told) != 0)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Правка формы — шаг её документа в истории доски.</summary>
    private void OnSessionEdited(object? sender, EventArgs e)
    {
        if (!_disposed && sender is FormSession session)
            _history.Push(new DocumentStep(session));
    }

    /// <summary>Форма ушла с виду: показ отпускается через паузу, если она не вернётся.</summary>
    private void Hide(LiveForm form)
    {
        form.Hiding?.Dispose();
        form.Hiding = _options.BoardHideDelay == Timeout.InfiniteTimeSpan
            ? null
            : _options.TimeProvider.CreateTimer(
                _ => Dispatcher.UIThread.Post(() => Hidden(form)),
                null,
                _options.BoardHideDelay,
                Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Пауза кончилась, а форма не вернулась: показ уходит, а документ — если его нечего беречь. Форма, в
    /// которой выбрано, ждёт, пока выбор из неё уйдёт.
    /// </summary>
    /// <remarks>
    /// Карточка остаётся за формой: корень с неё уходит вместе с показом, и на месте формы снова снимок, —
    /// а вернувшаяся на вид форма встаёт на ту же карточку.
    /// </remarks>
    private void Hidden(LiveForm form)
    {
        if (_disposed || !_live.TryGetValue(form.Session.Path, out var held) || !ReferenceEquals(held, form))
            return;

        form.Hiding?.Dispose();
        form.Hiding = null;

        if ((form.Slot.Item is not null && Seen(form.Session.Path)) || Holds(form))
            return;

        form.Shows = false;
        form.Session.Hide();

        if (!Keeps(form.Session))
            Drop(form, save: false);
    }

    /// <summary>
    /// Беречь ли документ формы, ушедшей с виду: правки, которые доска может отменить, несохранённое и
    /// вопрос о чужой записи.
    /// </summary>
    private bool Keeps(FormSession session) =>
        _history.Holds(session) || session.Document is { IsModified: true } or { HasConflict: true };

    /// <summary>Отпускает форму: показ и документ; несохранённое сперва сохраняется, если можно.</summary>
    private void Drop(LiveForm form, bool save)
    {
        if (!_live.Remove(form.Session.Path))
            return;

        form.Hiding?.Dispose();
        form.Hiding = null;
        form.Session.Changed -= OnSessionChanged;
        form.Session.Edited -= OnSessionEdited;

        _canvas.Remove(form.Slot);
        _ = CloseAsync(form.Session, save);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task CloseAsync(FormSession session, bool save)
    {
        if (save && session.AutoSaves)
            await session.AutoSaveAsync();

        await session.DisposeAsync();
    }

    private FormCard? CardOf(UiDesignerFormItem item) =>
        item.DataContext as FormCard ?? _sheet.ItemFromContainer(item) as FormCard;

    /// <summary>Живая форма доски: её сессия, место на холсте и пауза перед отпуском показа.</summary>
    private sealed class LiveForm(FormSession session, FormSlot slot)
    {
        public FormSession Session { get; } = session;

        public FormSlot Slot { get; } = slot;

        /// <summary>Доска просила показ и не отпускала: форма живая или встаёт.</summary>
        public bool Shows { get; set; }

        public ITimer? Hiding { get; set; }
    }
}
