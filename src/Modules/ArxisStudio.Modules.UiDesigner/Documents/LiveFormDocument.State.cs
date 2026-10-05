using ArxisStudio.Controls;
using ArxisStudio.Markup;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Documents;

// Вид вкладки, чип состояния и баннеры.
// Часть LiveFormDocument; общее описание типа — в LiveFormDocument.cs.
internal sealed partial class LiveFormDocument
{
    private readonly FormViewModes _modes;
    private TextSpan? _problemSpan;

    /// <summary>Вид сейчас — тестам.</summary>
    internal FormViewMode Mode => _modes.Mode;

    /// <summary>
    /// Холст снова виден: форма, сменившаяся под видом «XAML», снимается теперь — спрятанную не снять.
    /// </summary>
    private void OnModeApplied(object? sender, EventArgs e)
    {
        if (_modes.Mode != FormViewMode.Xaml)
            _canvas.QueueSnapshots();
    }

    /// <summary>Чип состояния типов проекта и баннер того, что требует внимания.</summary>
    private void ShowState()
    {
        var (chip, busy) = FormNotices.Chip(_design);

        _view.State.IsVisible = chip is not null;
        _view.State.Content = chip is null ? null : _context.Strings[chip];
        ToolTip.SetTip(_view.State, _design?.StateReason);
        _view.Busy.IsVisible = busy;

        ShowNotice();
    }

    /// <summary>Баннер: одно сообщение, самое важное; закрытое человеком не возвращается, пока не сменится.</summary>
    private void ShowNotice()
    {
        var notice = FormNotices.For(_design, _session, _context.Strings);

        _problemSpan = notice?.Span;

        if (notice is null || notice.Text == _dismissed)
        {
            Hide(_view.Notice);
            return;
        }

        _view.Notice.Severity = notice.Severity;
        _view.NoticeText.Text = notice.Text;
        _view.Rebuild.IsVisible = notice.Rebuild;
        _view.ShowProblem.IsVisible = notice.Span is not null;
        _view.Notice.IsVisible = true;
    }

    /// <summary>Вопрос о чужой записи — свой баннер, отдельно от сообщений: на него отвечают.</summary>
    /// <remarks>
    /// Закрытый крестиком, он только откладывает ответ: несохранённое без ответа не пишется, и сохранение
    /// возвращает вопрос (<see cref="SaveAsync"/>).
    /// </remarks>
    private void ShowConflict()
    {
        if (_session.Document is not { HasConflict: true, IsClosed: false })
        {
            Hide(_view.Conflict);
            return;
        }

        _view.ConflictText.Text = Format("form.conflict", _path.FileName);
        _view.Conflict.IsVisible = true;
    }

    /// <summary>Прячет баннер, не теряя каретку: нажатая в нём кнопка уходит вместе с ним.</summary>
    private void Hide(Control banner)
    {
        if (!banner.IsVisible)
            return;

        var caret = banner.IsKeyboardFocusWithin;

        banner.IsVisible = false;

        if (caret)
            FocusTarget?.Focus();
    }

    /// <summary>Баннер закрыли крестиком: он спрятался сам, вместе с кареткой, — она возвращается вкладке.</summary>
    private void KeepCaret()
    {
        if (TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement() is null)
            FocusTarget?.Focus();
    }

    private void OnTakeTheirs(object? sender, RoutedEventArgs e) => _ = ResolveAsync(XamlConflictChoice.TakeTheirs);

    private void OnKeepMine(object? sender, RoutedEventArgs e) => _ = ResolveAsync(XamlConflictChoice.KeepMine);

    /// <summary>Отвечает на вопрос о чужой записи; оставленные правки сохраняются поверх файла как обычно.</summary>
    private async Task ResolveAsync(XamlConflictChoice choice)
    {
        if (await _session.ResolveAsync(choice) && !_disposed)
            ShowConflict();
    }

    private void OnRebuild(object? sender, RoutedEventArgs e) => _ = RebuildAsync();

    /// <summary>Собирает дизайн заново; ход и итог показывает чип, а отказ — строка состояния.</summary>
    private async Task RebuildAsync()
    {
        if (_design is null || _disposed)
            return;

        try
        {
            await _design.RebuildAsync(_canvas.Lifetime);
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Ведёт к месту ошибки в XAML: в одном холсте его не видно — вкладка открывает разделение.</summary>
    private void OnShowProblem(object? sender, RoutedEventArgs e)
    {
        if (_problemSpan is not { } span)
            return;

        if (_modes.Mode == FormViewMode.Design)
            _modes.Apply(FormViewMode.Split);

        _view.Code.CaretOffset = span.Start;
        _view.Code.ScrollIntoView(span.Start);
        _view.Code.Focus();
    }

    private void OnNoticeClosed(object? sender, RoutedEventArgs e)
    {
        _dismissed = _view.NoticeText.Text;
        KeepCaret();
    }

    private void OnConflictClosed(object? sender, RoutedEventArgs e) => KeepCaret();
}
