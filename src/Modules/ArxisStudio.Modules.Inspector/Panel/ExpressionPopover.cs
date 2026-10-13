using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Панель выражения под строкой — как окно привязки у Unity: заголовок, редактор цели, причина, если записать нельзя, и
/// «Снять», «Отмена», «Применить».
/// </summary>
/// <remarks>
/// <para>
/// <b>Хозяин — инспектор, содержимое — цель.</b> Редактор строит цель (<see cref="InspectorTarget.CreateExpressionEditor"/>)
/// — чужой код, — и всё, что панель у него спрашивает, идёт через шов хозяина цели: содержимое стоит за барьером, а
/// закрытая панель отпускает редактор прощальной дорогой. Панель одна на вид инспектора: ячейки списка одноразовые, а
/// поле строки строка подменяет, и всплывашка внутри них не пережила бы перестройки.
/// </para>
/// <para>
/// <b>Открыта — держит.</b> Пока панель открыта, цель держит замену поколения (<see cref="InspectorList.Hold"/>): набранный
/// путь понадобится тем же типам. Закрывают её Esc, щелчок мимо, «Отмена», показ другой цели, перестройка, которая
/// сняла строку, и выгрузка хозяина цели. Запись, которую панель сама ведёт, перестраивает список, — её перемены панель
/// не закрывают; записалось — панель закрывается, нет — остаётся с причиной.
/// </para>
/// <para>
/// <b>Каретка — в строку.</b> Закрылась панель — каретка сразу, а не постом, встаёт на строку, из которой её открыли:
/// клавиатура продолжает там, где была.
/// </para>
/// </remarks>
internal sealed class ExpressionPopover
{
    private readonly InspectorList _list;
    private readonly AxIcon _icon;
    private readonly TextBlock _title;
    private readonly Border _content;
    private readonly TextBlock _note;
    private readonly AxButton _clear;
    private readonly AxButton _apply;
    private Opened? _open;
    private IDisposable? _keeper;

    /// <summary>Заводит панель вида инспектора.</summary>
    /// <param name="list">Список вида — дорога к цели, строкам и словам.</param>
    public ExpressionPopover(InspectorList list)
    {
        _list = list;

        var words = list.Words;

        _icon = new AxIcon { Size = AxIconSize.Small, VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        AxText.SetRole(_title, AxTextRole.Section);

        var header = new DockPanel();

        _icon[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");
        DockPanel.SetDock(_icon, Dock.Left);
        header.Children.Add(_icon);
        header.Children.Add(_title);

        _content = new Border
        {
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };

        _note = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false,
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        AxText.SetTone(_note, AxTextTone.Error);
        AxText.SetRole(_note, AxTextRole.Small);

        _clear = new AxButton { Content = words["inspector.expression.clear"], Appearance = AxButtonAppearance.Subtle };
        _clear.Click += (_, _) => _ = ClearAsync();

        var cancel = new AxButton { Content = words["inspector.expression.cancel"] };
        cancel.Click += (_, _) => Close();

        _apply = new AxButton { Content = words["inspector.expression.apply"], Appearance = AxButtonAppearance.Primary };
        _apply.Click += (_, _) => _ = CommitAsync();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, _apply },
            [!StackPanel.SpacingProperty] = new DynamicResourceExtension("AxSpaceTight"),
        };

        var footer = new DockPanel
        {
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };

        DockPanel.SetDock(_clear, Dock.Left);
        footer.Children.Add(_clear);
        footer.Children.Add(actions);

        var body = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(_note, Dock.Bottom);
        body.Children.Add(header);
        body.Children.Add(footer);
        body.Children.Add(_note);
        body.Children.Add(_content);

        Frame = new Border
        {
            Child = body,
            [!Layoutable.WidthProperty] = new DynamicResourceExtension("AxExpressionPanelWidth"),
            [!Decorator.PaddingProperty] = new DynamicResourceExtension("AxSpaceThickness"),
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("AxSurfaceOverlayBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("AxStrokeControlBrush"),
            [!Border.BorderThicknessProperty] = new DynamicResourceExtension("AxHairlineThickness"),
            [!Border.CornerRadiusProperty] = new DynamicResourceExtension("AxCornerRadiusLarge"),
            [!Border.BoxShadowProperty] = new DynamicResourceExtension("AxShadowPopup"),
        };

        // Свои Esc и Enter редактор забирает сам — закрыть свой выпадающий список, шагнуть в член пути; до панели
        // доходят только те, что ему не понадобились.
        Frame.AddHandler(InputElement.KeyDownEvent, OnKeyDown);

        Popup = new Popup
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = Frame,
        };
        Popup.Closed += OnPopupClosed;

        list.Rebuilt += OnRebuilt;
    }

    /// <summary>Всплывающее окно панели.</summary>
    public Popup Popup { get; }

    /// <summary>Рамка панели — её содержимое целиком.</summary>
    public Border Frame { get; }

    /// <summary>Открыта ли панель.</summary>
    public bool IsOpen => _open is not null;

    /// <summary>Строка, под которой панель открыта; пусто — закрыта.</summary>
    public InspectorRow? Row => _open?.Row;

    /// <summary>Почему записать нельзя; пусто — можно.</summary>
    public string? Note => _note.IsVisible ? _note.Text : null;

    /// <summary>Редактор открытой панели — тестам.</summary>
    internal InspectorExpressionEditor? Editor =>
        _open is { } open && open.Editor.TryGet("gives itself", static it => it, out var editor) ? editor : null;

    /// <summary>Можно ли применить.</summary>
    public bool CanApply => _apply.IsEnabled;

    /// <summary>Можно ли снять выражение.</summary>
    public bool CanClear => _clear.IsEnabled;

    /// <summary>Открывает панель выражения под строкой.</summary>
    /// <param name="row">Строка.</param>
    /// <param name="kind">Какое выражение правят.</param>
    /// <returns>Открылась ли: цель могла не дать редактора.</returns>
    public bool Open(InspectorRow row, InspectorExpressionKind kind)
    {
        ArgumentNullException.ThrowIfNull(row);

        Close(returnCaret: false);

        if (row.Property.IsReadOnly || _list.Guard is not { } guard)
            return false;

        var context = new Context(this, row.Property, _list.Count);

        if (!guard.TryGet("creates an expression editor", it => it.CreateExpressionEditor(row.Property.Id, kind, context), out var created) ||
            created is null)
        {
            context.Closed = true;
            return false;
        }

        var editor = guard.Guard(created);

        if (!editor.TryGet("gives its view", static it => it.View, out var view) || view is null)
        {
            context.Closed = true;
            editor.Farewell("releases an expression editor", static it => it.Release());
            return false;
        }

        _title.Text = editor.TryGet("gives its title", static it => it.Title, out var title) && !string.IsNullOrEmpty(title) ? title : row.Property.Label;
        _icon.Data = InspectorRow.IconOf(kind);
        _content.Child = editor.Host(view);
        _open = new Opened(row, editor, guard, context, _list.Hold(row.Property.Id));
        editor.Released += OnEditorReleased;

        // Редактор мог сказать, что записать нельзя, ещё пока его строили.
        Report(context.Last);

        // «Снять» снимает выражение, а у свойства, которое только привязывают, снимать нечего.
        _clear.IsEnabled = row.Value?.Source == InspectorValueSource.Expression && row.CanReset;
        AutomationProperties.SetName(Frame, _title.Text);

        Popup.PlacementTarget = row;
        Popup.IsOpen = true;
        _keeper = InputElement.GettingFocusEvent.AddClassHandler<InputElement>(OnGettingFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        var target = editor.TryGet("gives its focus target", static it => it.FocusTarget, out var focus) ? focus : null;

        Dispatcher.UIThread.Post(() =>
        {
            if (_open?.Editor == editor)
                (target ?? FirstFocusable(view))?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);

        return true;
    }

    /// <summary>Пишет выражение: панель закрывается, если записалось.</summary>
    /// <returns>Записалось ли.</returns>
    public async Task<bool> CommitAsync()
    {
        if (_open is not { Committing: false } open || !_apply.IsEnabled)
            return false;

        open.Committing = true;
        _apply.IsEnabled = false;

        var written = false;
        var ran = await open.Editor.RunAsync("commits an expression", async it => written = await it.CommitAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (!Finish(open, ran && written))
        {
            _apply.IsEnabled = open.Check.IsValid;

            if (open.Check.IsValid)
                ShowNote(_list.Words["inspector.notWritten"]);
        }

        return ran && written;
    }

    /// <summary>Закрывает панель, ничего не записав, и возвращает каретку в строку.</summary>
    public void Close() => Close(returnCaret: true);

    /// <summary>Вид уходит: панель закрывается, а список перестаёт о ней говорить.</summary>
    public void Release()
    {
        _list.Rebuilt -= OnRebuilt;
        Close(returnCaret: false);
    }

    /// <summary>Редактор сказал, можно ли записать набранное.</summary>
    private void Report(InspectorCheck check)
    {
        if (_open is { } open)
            open.Check = check;

        _apply.IsEnabled = check.IsValid && _open is not { Committing: true };

        if (check.IsValid)
            ShowNote(null);
        else
            ShowNote(check.Message ?? _list.Words["inspector.invalid"]);
    }

    private void ShowNote(string? text)
    {
        _note.Text = text;
        _note.IsVisible = !string.IsNullOrEmpty(text);
    }

    /// <summary>«Снять» — снимает выражение у всех показанных одной правкой.</summary>
    private async Task ClearAsync()
    {
        if (_open is not { Committing: false } open)
            return;

        open.Committing = true;

        var written = await _list.WriteAsync([new InspectorWrite(open.Row.Property.Id, null)], null).ConfigureAwait(true);

        if (!Finish(open, written))
            ShowNote(_list.Words["inspector.notWritten"]);
    }

    /// <summary>
    /// Запись кончилась: записалось — панель закрывается. Пока запись шла, панель пропускала перемены списка и щелчок
    /// мимо — строки могло не стать, а окно уже закрылось, и тогда панель закрывается, не трогая каретки.
    /// </summary>
    /// <returns>Закрыта ли панель, которая писала.</returns>
    private bool Finish(Opened open, bool written)
    {
        open.Committing = false;

        if (!ReferenceEquals(_open, open))
            return true;

        if (!Popup.IsOpen || Gone(open))
            Close(returnCaret: false);
        else if (written)
            Close();

        return !ReferenceEquals(_open, open);
    }

    /// <summary>Строки, под которой панель открыта, больше нет: показана другая цель, или свойства не стало.</summary>
    private bool Gone(Opened open) =>
        !ReferenceEquals(_list.Guard, open.Guard) || _list.PropertyOf(open.Row.Property.Id) is null;

    private void Close(bool returnCaret)
    {
        if (_open is not { } open)
            return;

        Ended(open);
        open.Editor.Farewell("releases an expression editor", static it => it.Release());

        if (returnCaret)
            Caret(open.Row);
    }

    /// <summary>Панель закрыта: строка отпущена, окно спрятано, каретку панель больше не держит, а редактор говорит уже не с ней.</summary>
    private void Ended(Opened open)
    {
        _open = null;
        open.Context.Closed = true;
        open.Editor.Released -= OnEditorReleased;
        open.Hold?.Dispose();
        _keeper?.Dispose();
        _keeper = null;
        Popup.IsOpen = false;
        _content.Child = null;
    }

    /// <summary>
    /// Всплывашка внутри панели — выпадающий список редактора, — закрываясь, отдаёт каретку окну под панелью, туда, где
    /// окно держало её до панели: свою всплывашку Avalonia числит за окном, а не за панелью, из которой её открыли. Пока
    /// панель открыта, такая каретка остаётся в панели — у того, чья всплывашка закрылась. Каретку, которую уводит
    /// человек — щелчком, Tab, стрелками, — панель не держит.
    /// </summary>
    private void OnGettingFocus(InputElement element, FocusChangingEventArgs e)
    {
        if (_open is null || !ReferenceEquals(element, e.Source) || e.NavigationMethod != NavigationMethod.Unspecified ||
            e.NewFocusedElement is Visual next && Frame.IsVisualAncestorOf(next) ||
            e.OldFocusedElement is not StyledElement old || old is Visual visual && Frame.IsVisualAncestorOf(visual) ||
            Owner(old) is not { } owner)
        {
            return;
        }

        e.TrySetNewFocusedElement(owner);
    }

    /// <summary>Хозяин элемента всплывашки внутри панели — ближайший логический предок, который стоит в самой панели.</summary>
    private Control? Owner(StyledElement element)
    {
        for (var at = element.Parent; at is not null; at = at.Parent)
        {
            if (at is Control { Focusable: true, IsEffectivelyEnabled: true } control && Frame.IsVisualAncestorOf(control))
                return control;
        }

        return null;
    }

    /// <summary>Каретка — в ячейку строки: оттуда панель открыли, и оттуда же её открывают снова.</summary>
    private static void Caret(InspectorRow row)
    {
        if (row.FindAncestorOfType<ListBoxItem>() is { } cell)
            cell.Focus(NavigationMethod.Directional);
        else
            row.FocusTarget?.Focus(NavigationMethod.Directional);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            _ = CommitAsync();
            e.Handled = true;
        }
    }

    /// <summary>Щелчок мимо закрыл всплывающее окно: это отмена.</summary>
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (_open is { Committing: false })
            Close();
    }

    /// <summary>
    /// Список перестроился: другая цель или строки этого свойства в нём больше нет — панель закрывается; своя запись
    /// перестраивает список, и её перемены панель пропускает.
    /// </summary>
    private void OnRebuilt(object? sender, EventArgs e)
    {
        if (_open is { Committing: false } open && Gone(open))
            Close(returnCaret: false);
    }

    /// <summary>Хозяина цели выгружают: редактор уходит раньше, чем его сборка.</summary>
    private void OnEditorReleased(object? sender, EventArgs e)
    {
        if (_open is { } open)
            Ended(open);
    }

    private static Control? FirstFocusable(Control view) =>
        view.GetVisualDescendants().OfType<Control>().FirstOrDefault(static control => control is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true });

    /// <summary>
    /// Открытая панель: строка, редактор за швом, цель, при которой открыта, дорога редактора, удержание и последняя
    /// проверка.
    /// </summary>
    private sealed class Opened(
        InspectorRow row,
        IStudioGuarded<InspectorExpressionEditor> editor,
        IStudioGuarded<InspectorTarget> guard,
        Context context,
        IDisposable? hold)
    {
        public InspectorRow Row { get; } = row;

        public IStudioGuarded<InspectorExpressionEditor> Editor { get; } = editor;

        public IStudioGuarded<InspectorTarget> Guard { get; } = guard;

        public Context Context { get; } = context;

        public IDisposable? Hold { get; } = hold;

        public bool Committing { get; set; }

        public InspectorCheck Check { get; set; } = InspectorCheck.Valid;
    }

    /// <summary>
    /// Дорога редактора к панели — своей: закрылась панель, и что редактор скажет после, — запоздалый ответ, отмена,
    /// запись, — к панели, открытой следом, не относится.
    /// </summary>
    private sealed class Context(ExpressionPopover popover, InspectorProperty property, int count) : InspectorExpressionContext
    {
        public override InspectorProperty Property => property;

        public override int Count => count;

        /// <summary>Последнее, что редактор сказал о набранном.</summary>
        public InspectorCheck Last { get; private set; } = InspectorCheck.Valid;

        /// <summary>Панель этого редактора закрыта.</summary>
        public bool Closed { get; set; }

        public override void Report(InspectorCheck check)
        {
            if (Closed)
                return;

            Last = check;
            popover.Report(check);
        }

        public override void Commit() => Dispatcher.UIThread.Post(() =>
        {
            if (!Closed)
                _ = popover.CommitAsync();
        });

        public override void Cancel() => Dispatcher.UIThread.Post(() =>
        {
            if (!Closed)
                popover.Close();
        });
    }
}
