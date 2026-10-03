using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Инспектор выбранного на форме впереди: тип, имя и члены по разделам, каждый — правкой документа.
/// </summary>
/// <remarks>
/// <para>
/// Строки — модель (<see cref="InspectorModel"/>), а здесь — клавиатура и фиксация: Enter и уход фокуса
/// пишут набранное, Esc возвращает написанное, стрелки шагают число, с Shift — на десять. Поле, которое
/// не прочтётся, остаётся с ошибкой и в документ не пишется.
/// </para>
/// <para>
/// Правка перестраивает строки — значения объекта читаются заново, — и каретка, стоявшая в поле члена,
/// возвращается в поле того же члена: человек, нажавший Enter, продолжает с того же места.
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.InspectorId)]
public sealed class InspectorPanel : ToolWindow
{
    private DesignerWorkbench? _bench;
    private InspectorView? _view;
    private InspectorModel? _model;

    /// <summary>Разметка — тестам.</summary>
    internal InspectorView? View => _view;

    /// <summary>Модель — тестам.</summary>
    internal InspectorModel? Model => _model;

    /// <inheritdoc/>
    /// <remarks>Каретка — у поиска: инспектор открывают, чтобы найти член, а у строк каретка своя.</remarks>
    public override Control? FocusTarget => _view?.Search;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var model = new InspectorModel(Context.Strings);
        var view = new InspectorView { DataContext = model };
        var bench = DesignerWorkbench.Of(Context);

        _model = model;
        _view = view;
        _bench = bench;

        bench.FormChanged += OnChanged;
        bench.SelectionChanged += OnChanged;
        bench.ContentChanged += OnChanged;

        view.Search.TextChanged += OnSearchChanged;
        view.Rows.AddHandler(InputElement.KeyDownEvent, OnRowKeyDown, RoutingStrategies.Tunnel);
        view.Rows.AddHandler(InputElement.LostFocusEvent, OnRowLostFocus, RoutingStrategies.Bubble);
        view.Rows.AddHandler(Button.ClickEvent, OnRowClick, RoutingStrategies.Bubble);
        view.NameBox.AddHandler(InputElement.KeyDownEvent, OnNameKeyDown, RoutingStrategies.Tunnel);
        view.NameBox.LostFocus += OnNameLostFocus;
        view.NameBox.TextChanged += OnNameChanged;

        Refresh();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_bench is { } bench)
        {
            bench.FormChanged -= OnChanged;
            bench.SelectionChanged -= OnChanged;
            bench.ContentChanged -= OnChanged;
        }

        if (_view is { } view)
        {
            view.Search.TextChanged -= OnSearchChanged;
            view.Rows.RemoveHandler(InputElement.KeyDownEvent, OnRowKeyDown);
            view.Rows.RemoveHandler(InputElement.LostFocusEvent, OnRowLostFocus);
            view.Rows.RemoveHandler(Button.ClickEvent, OnRowClick);
            view.NameBox.RemoveHandler(InputElement.KeyDownEvent, OnNameKeyDown);
            view.NameBox.LostFocus -= OnNameLostFocus;
            view.NameBox.TextChanged -= OnNameChanged;
        }

        _bench = null;
        _view = null;
        _model = null;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Refresh();

    /// <summary>Перестраивает строки; каретку из поля члена возвращает в поле того же члена.</summary>
    private void Refresh()
    {
        if (_view is not { } view || _model is not { } model)
            return;

        var focused = Focused(view) is StyledElement { DataContext: InspectorRow row } ? row.Member : null;

        model.Show(_bench?.Form, view.Search.Text ?? string.Empty);
        view.NameError.IsVisible = false;

        if (focused is not null)
            Dispatcher.UIThread.Post(() => Refocus(focused), DispatcherPriority.Loaded);
    }

    private void Refocus(string member)
    {
        if (_view is not { } view || _model is not { } model)
            return;

        if (model.Items.OfType<InspectorRow>().FirstOrDefault(row => row.Member == member) is not { } row
            || view.Rows.ContainerFromItem(row) is not { } container)
        {
            return;
        }

        container.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible && element is not Button)
            ?.Focus(NavigationMethod.Tab);
    }

    private static IInputElement? Focused(Visual visual) =>
        TopLevel.GetTopLevel(visual)?.FocusManager?.GetFocusedElement();

    private static InspectorRow? RowOf(object? source) =>
        source is StyledElement { DataContext: InspectorRow row } ? row : null;

    /// <summary>
    /// Клавиши поля значения — до списка: его стрелки ходят по строкам. Ctrl+Z в поле с ненаписанным
    /// отменяет набранное, а без него — шаг истории формы, как из любой панели модуля.
    /// </summary>
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is not { } view)
            return;

        if (e.Source is not TextBox box || RowOf(box) is not { } row)
        {
            HistoryKeys.Step(e, view.Rows, _bench);
            return;
        }

        if (row.Draft == row.Written && HistoryKeys.Step(e, view.Rows, _bench))
            return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = row.CommitAsync();
                break;

            case Key.Escape:
                e.Handled = true;
                row.Revert();
                break;

            case Key.Up or Key.Down when row.IsNumber:
                e.Handled = true;

                var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;

                _ = row.StepAsync(e.Key == Key.Up ? step : -step);
                break;
        }
    }

    private void OnRowLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box && RowOf(box) is { } row)
            _ = row.CommitAsync();
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } button && classes.Contains("reset") && RowOf(button) is { } row)
        {
            e.Handled = true;
            _ = row.ResetAsync();
        }
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is { } named && _model is { } shown && (named.NameBox.Text ?? string.Empty) == shown.Name
            && HistoryKeys.Step(e, named.NameBox, _bench))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = RenameAsync();
                break;

            case Key.Escape when _view is { } view && _model is { } model:
                e.Handled = true;
                view.NameBox.Text = model.Name;
                view.NameError.IsVisible = false;
                break;
        }
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => _ = RenameAsync();

    /// <summary>Набираемое имя проверяется сразу: ошибка видна до фиксации.</summary>
    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (_view is not { } view || _model is not { } model || _bench?.Form is not { } form)
            return;

        var error = model.NameError(form, view.NameBox.Text ?? string.Empty);

        view.NameError.Text = error;
        view.NameError.IsVisible = error is not null;
    }

    /// <summary>Пишет <c>x:Name</c> выбранного; пустое снимает имя.</summary>
    private async Task RenameAsync()
    {
        if (_view is not { } view || _model is not { CanName: true } model
            || _bench?.Form is not { Edits: { } edits } form || form.Selection is not [var path])
        {
            return;
        }

        var typed = (view.NameBox.Text ?? string.Empty).Trim();

        if (typed == model.Name || model.NameError(form, typed) is not null)
            return;

        await edits.SetAsync([path], FormEdits.NameDirective, typed);
    }
}
