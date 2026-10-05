using ArxisStudio.Markup;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Panels;

// Чип состояния типов проекта, баннеры над холстом доски — те же, что у вкладки формы, — и имя формы над её XAML.
// Часть BoardPanel; общее описание типа — в BoardPanel.cs.
public sealed partial class BoardPanel
{
    private FormSession? _conflicted;
    private string? _dismissed;
    private TextSpan? _problemSpan;

    private void WireState(BoardView view)
    {
        view.TakeTheirs.Click += OnTakeTheirs;
        view.KeepMine.Click += OnKeepMine;
        view.Rebuild.Click += OnRebuild;
        view.ShowProblem.Click += OnShowProblem;
        view.Notice.Closed += OnNoticeClosed;
        view.Conflict.Closed += OnConflictClosed;

        if (_design is not null)
            _design.StateChanged += OnBoardStateChanged;

        if (_canvas is not null)
            _canvas.ActiveChanged += OnBoardStateChanged;

        if (_forms is not null)
            _forms.StateChanged += OnBoardStateChanged;
    }

    private void Unwire(BoardView view)
    {
        view.TakeTheirs.Click -= OnTakeTheirs;
        view.KeepMine.Click -= OnKeepMine;
        view.Rebuild.Click -= OnRebuild;
        view.ShowProblem.Click -= OnShowProblem;
        view.Notice.Closed -= OnNoticeClosed;
        view.Conflict.Closed -= OnConflictClosed;

        if (_design is not null)
            _design.StateChanged -= OnBoardStateChanged;

        if (_canvas is not null)
            _canvas.ActiveChanged -= OnBoardStateChanged;

        if (_forms is not null)
            _forms.StateChanged -= OnBoardStateChanged;
    }

    private void OnBoardStateChanged(object? sender, EventArgs e) => ShowState();

    /// <summary>
    /// Чип состояния типов проекта, вопрос о чужой записи и одно сообщение — о типах проекта и о форме, с
    /// которой работают.
    /// </summary>
    /// <remarks>
    /// Те же слова, что у вкладки (<see cref="FormNotices"/>); сообщение о форме на доске называет её файл:
    /// форм здесь много.
    /// </remarks>
    private void ShowState()
    {
        if (_view is not { } view)
            return;

        var (chip, busy) = FormNotices.Chip(_design);

        view.State.IsVisible = chip is not null;
        view.State.Content = chip is null ? null : Context.Strings[chip];
        ToolTip.SetTip(view.State, _design?.StateReason);
        view.Busy.IsVisible = busy;

        ShowNotice(view);
        ShowConflict(view);
        ShowCode(view);
    }

    /// <summary>
    /// Над XAML — имя формы, с которой работают; нет её — вместо текста сказано, как её выбрать.
    /// </summary>
    private void ShowCode(BoardView view)
    {
        var active = _canvas?.Active?.Session;

        // Пустой просмотр не стоит под подсказкой: у пустого текста он показал бы номер первой строки.
        view.CodeOf.Text = active?.Path.FileName;
        view.CodeOf.IsVisible = active is not null;
        view.Code.IsVisible = active is not null;
        view.CodeHint.IsVisible = active is null;
    }

    /// <summary>Баннер: одно сообщение, самое важное; закрытое человеком не возвращается, пока не сменится.</summary>
    private void ShowNotice(BoardView view)
    {
        var active = _canvas?.Active?.Session;
        var notice = FormNotices.For(_design, active, Context.Strings);
        var text = notice is { OfForm: true } && active is not null
            ? Say("board.notice.form", active.Path.FileName, notice.Text)
            : notice?.Text;

        _problemSpan = notice?.Span;

        if (notice is null || text == _dismissed)
        {
            Hide(view, view.Notice);
            return;
        }

        view.Notice.Severity = notice.Severity;
        view.NoticeText.Text = text;
        view.Rebuild.IsVisible = notice.Rebuild;
        view.ShowProblem.IsVisible = notice.Span is not null;
        view.Notice.IsVisible = true;
    }

    /// <summary>
    /// Ведёт к месту ошибки в XAML формы, с которой работают: в одном холсте его не видно — доска открывает
    /// разделение.
    /// </summary>
    private void OnShowProblem(object? sender, RoutedEventArgs e)
    {
        if (_problemSpan is not { } span || _view is not { } view || _modes is not { } modes)
            return;

        if (modes.Mode == FormViewMode.Design)
            modes.Apply(FormViewMode.Split);

        view.Code.CaretOffset = span.Start;
        view.Code.ScrollIntoView(span.Start);
        view.Code.Focus();
    }

    /// <summary>
    /// Вопрос о чужой записи: о форме, с которой работают, а нет у неё вопроса — о первой форме доски, у
    /// которой он есть.
    /// </summary>
    /// <remarks>
    /// Пока на вопрос не ответили, форма не пишется: без баннера её несохранённое так и висело бы, а человек
    /// не знал бы почему.
    /// </remarks>
    private void ShowConflict(BoardView view)
    {
        var active = _canvas?.Active?.Session;

        _conflicted = active is { Document: { HasConflict: true, IsClosed: false } }
            ? active
            : _forms?.Sessions.FirstOrDefault(session => session.Document is { HasConflict: true, IsClosed: false });

        if (_conflicted is not { } conflicted)
        {
            Hide(view, view.Conflict);
            return;
        }

        view.ConflictText.Text = Say("form.conflict", conflicted.Path.FileName);
        view.Conflict.IsVisible = true;
    }

    /// <summary>Прячет баннер, не теряя каретку: нажатая в нём кнопка уходит вместе с ним.</summary>
    private static void Hide(BoardView view, Control banner)
    {
        if (!banner.IsVisible)
            return;

        var caret = banner.IsKeyboardFocusWithin;

        banner.IsVisible = false;

        if (caret)
            view.Sheet.Focus();
    }

    /// <summary>Баннер закрыли крестиком: он спрятался сам, вместе с кареткой, — она возвращается холсту.</summary>
    private void KeepCaret()
    {
        if (_view is { } view && TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement() is null)
            view.Sheet.Focus();
    }

    private void OnTakeTheirs(object? sender, RoutedEventArgs e) => _ = ResolveAsync(XamlConflictChoice.TakeTheirs);

    private void OnKeepMine(object? sender, RoutedEventArgs e) => _ = ResolveAsync(XamlConflictChoice.KeepMine);

    /// <summary>Отвечает на вопрос о чужой записи формы, о которой он задан.</summary>
    private async Task ResolveAsync(XamlConflictChoice choice)
    {
        if (_conflicted is { } conflicted && await conflicted.ResolveAsync(choice))
            ShowState();
    }

    private void OnRebuild(object? sender, RoutedEventArgs e) => _ = RebuildAsync();

    /// <summary>Собирает дизайн заново; ход и итог показывает чип, а отказ — строка состояния.</summary>
    private async Task RebuildAsync()
    {
        if (_design is not { } design || _canvas is not { } canvas)
            return;

        var lifetime = canvas.Lifetime;

        try
        {
            await design.RebuildAsync(lifetime);
        }
        catch (InvalidOperationException e)
        {
            Context.GetService<Sdk.IStudioStatus>()?.Show(e.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnNoticeClosed(object? sender, RoutedEventArgs e)
    {
        _dismissed = _view?.NoticeText.Text;
        KeepCaret();
    }

    private void OnConflictClosed(object? sender, RoutedEventArgs e) => KeepCaret();
}
