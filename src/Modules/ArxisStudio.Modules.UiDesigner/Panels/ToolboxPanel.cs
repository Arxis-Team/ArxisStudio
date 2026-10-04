using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Panels;

/// <summary>
/// Палитра контролов: контролы проекта формы впереди и контролы Avalonia, которые несут на форму или
/// ставят клавиатурой.
/// </summary>
/// <remarks>
/// <para>
/// <b>Тяга</b> — служба студии (<see cref="IStudioDragDrop"/>): строку берут мышью, и дальше несомое —
/// разметка типа (<see cref="XamlDataFormats.Snippet"/>) — над холстом формы, а холст сам говорит, куда
/// оно встанет. Порог — тот же, что у вкладок докинга и окна проекта.
/// </para>
/// <para>
/// <b>Без мыши</b> — Enter и двойной щелчок: контрол встаёт в выбранную панель, в конец, а рядом с
/// выбранным не-панелью — сразу после него. Ничего не выбрано — в панель, которая стоит в корне формы.
/// Правило общее со вставкой из буфера (<see cref="FormLanding"/>). Тяга без клавиатурной дороги была бы
/// жестом, который не всем доступен (WCAG 2.5.7).
/// </para>
/// </remarks>
[ToolWindow(UiDesignerModule.ToolboxId)]
public sealed class ToolboxPanel : ToolWindow
{
    private DesignerWorkbench? _bench;
    private IStudioXamlDesign? _design;
    private ToolboxView? _view;
    private ToolboxModel? _model;
    private IReadOnlyList<XamlPlaceable> _placeables = [];
    private ToolboxEntry? _pressed;
    private Point _pressedAt;
    private int _listing;

    /// <summary>Разметка — тестам.</summary>
    internal ToolboxView? View => _view;

    /// <summary>Модель — тестам.</summary>
    internal ToolboxModel? Model => _model;

    /// <inheritdoc/>
    /// <remarks>Каретка — у поиска: палитру открывают, чтобы найти контрол.</remarks>
    public override Control? FocusTarget => _view?.Search;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var model = new ToolboxModel(Context.Strings);
        var view = new ToolboxView { DataContext = model };
        var bench = DesignerWorkbench.Of(Context);

        _model = model;
        _view = view;
        _bench = bench;
        _design = Context.XamlDesign();

        bench.FormChanged += OnFormChanged;

        if (_design is not null)
            _design.StateChanged += OnDesignStateChanged;

        view.Search.TextChanged += OnSearchChanged;
        view.Entries.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.Entries.DoubleTapped += OnDoubleTapped;
        view.Entries.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        view.Entries.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        view.Entries.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        Show();
        _ = ListAsync();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_bench is { } bench)
            bench.FormChanged -= OnFormChanged;

        if (_design is not null)
            _design.StateChanged -= OnDesignStateChanged;

        if (_view is { } view)
        {
            view.Search.TextChanged -= OnSearchChanged;
            view.Entries.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            view.Entries.DoubleTapped -= OnDoubleTapped;
            view.Entries.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            view.Entries.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            view.Entries.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }

        _bench = null;
        _design = null;
        _view = null;
        _model = null;
        _pressed = null;
    }

    /// <summary>Ставит контрол клавиатурой — в выбранную панель или после выбранного.</summary>
    /// <param name="entry">Строка палитры.</param>
    /// <returns>Изменился ли текст формы.</returns>
    internal async Task<bool> InsertAsync(ToolboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_bench?.Form is not { Shown: { } shown, Document: { } document } form)
        {
            Say(Context.Strings["toolbox.noForm"]);
            return false;
        }

        if (FormLanding.For(form.Selection, shown, document.Syntax) is not { } target)
        {
            Say(string.Format(CultureInfo.CurrentCulture, Context.Strings["toolbox.full"], entry.Name));
            return false;
        }

        var (parent, index) = target;

        XamlFragment fragment;

        try
        {
            fragment = TypeMarkup.Fragment(entry.Type, placement: null);
        }
        catch (ArgumentException e)
        {
            Say(e.Message);
            return false;
        }

        var name = form.Edits?.NameOf(parent) ?? parent.ToString();

        return await form.Drops.WriteAsync(entry.Type, new DropIntent(parent, index, fragment, name));
    }

    private void OnFormChanged(object? sender, EventArgs e) => _ = ListAsync();

    private void OnDesignStateChanged(object? sender, EventArgs e)
    {
        if (_design?.State == XamlDesignState.Live)
            _ = ListAsync();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Show();

    /// <summary>Перечисляет контролы проекта формы впереди заново; ответ, пришедший к устаревшему вопросу, отбрасывается.</summary>
    private async Task ListAsync()
    {
        var listing = ++_listing;
        IReadOnlyList<XamlPlaceable> placeables = [];

        if (_bench?.Form is { } form && Context.XamlTypes() is { } types)
        {
            try
            {
                placeables = await types.GetPlaceableAsync(form.Path);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
            {
                // Сессия кончилась, пока спрашивали: перечислять нечего.
            }
        }

        if (listing != _listing)
            return;

        _placeables = placeables;
        Show();
    }

    private void Show() => _model?.Show(_placeables, _view?.Search.Text ?? string.Empty);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is { } view && HistoryKeys.Step(e, view.Entries, _bench))
            return;

        if (e.Key == Key.Enter && _view?.Entries.SelectedItem is ToolboxEntry entry)
        {
            e.Handled = true;
            _ = InsertAsync(entry);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (EntryOf(e.Source) is { } entry)
        {
            e.Handled = true;
            _ = InsertAsync(entry);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_view is not { } view || !e.GetCurrentPoint(view.Entries).Properties.IsLeftButtonPressed)
            return;

        _pressed = EntryOf(e.Source);
        _pressedAt = e.GetPosition(view.Entries);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => _pressed = null;

    /// <summary>Мышь с нажатой кнопкой ушла от точки нажатия дальше порога — строку несут.</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } entry || _view is not { } view || !e.GetCurrentPoint(view.Entries).Properties.IsLeftButtonPressed)
            return;

        var at = e.GetPosition(view.Entries);

        if (Math.Abs(at.X - _pressedAt.X) < StudioDragDrop.Threshold && Math.Abs(at.Y - _pressedAt.Y) < StudioDragDrop.Threshold)
            return;

        _pressed = null;

        if (Context.GetService<IStudioDragDrop>() is not { } drags)
            return;

        var data = new StudioDragData().With(XamlDataFormats.Snippet, entry.Type);

        // Копия: палитра контрол не отдаёт, а ставит новый.
        _ = drags.DragAsync(view.Entries, e, data, DragDropEffects.Copy, new StudioDragVisual(entry.Name));
    }

    private static ToolboxEntry? EntryOf(object? source) =>
        source is StyledElement { DataContext: ToolboxEntry entry } ? entry : null;

    private void Say(string message) => Context.GetService<IStudioStatus>()?.Show(message);
}
