using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панель выражения под строкой инспектора (контракт 1.4, запись 425): открывается Enter и пунктом меню, держит цель,
/// пишет редактором и закрывается Esc, применением и перестройкой — каретка возвращается в строку.
/// </summary>
[Collection(StudioStateCollection.Name)]
public class ExpressionPopoverTests
{
    /// <summary>Enter на выражении, которое цель правит панелью, открывает панель под строкой с кареткой в редакторе.</summary>
    [AvaloniaFact]
    public void Enter_on_an_expression_the_target_edits_opens_the_panel_under_the_row()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var row = Row(panel, "Text");

        Cell(panel, "Text").Focus(NavigationMethod.Directional);
        studio.Frame();
        Press(studio, Key.Enter);

        var popover = panel.Expressions!;

        Assert.True(popover.IsOpen, "панель не открылась");
        Assert.Same(row, popover.Row);
        Assert.Same(row, popover.Popup.PlacementTarget);
        Assert.Equal(1, target.Holding);
        Assert.True(popover.CanClear, "выражение нечем снять");

        var editor = Assert.Single(target.Editors);

        Assert.Equal(InspectorExpressionKind.Binding, editor.Kind);
        Assert.True(editor.Field.IsFocused, $"каретка не встала в редактор: в дереве {editor.Field.IsAttachedToVisualTree()}, окно {TopLevel.GetTopLevel(editor.Field)?.GetType().Name}, каретка у {TopLevel.GetTopLevel(editor.Field)?.FocusManager?.GetFocusedElement()?.GetType().Name}");
        Assert.Empty(target.Writes);
    }

    /// <summary>
    /// Enter в поле редактора — «Применить»: пишет редактором, и записалось — панель закрывается, удержание отпущено,
    /// каретка в строке.
    /// </summary>
    [AvaloniaFact]
    public void Enter_applies_through_the_editor_and_returns_the_caret_to_the_row()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Assert.True(Row(panel, "Text").OpenExpression(), "выражение не открылось панелью");
        studio.Frame();

        var editor = Assert.Single(target.Editors);

        editor.Field.Text = "{Binding Title}";
        editor.Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = editor.Field });
        studio.Frame();

        Assert.Equal([new InspectorWrite("Text", "{Binding Title}")], Assert.Single(target.Writes));
        Assert.False(panel.Expressions!.IsOpen);
        Assert.True(editor.Released, "редактор не отпущен");
        Assert.Equal(0, target.Holding);
        Assert.True(Cell(panel, "Text").IsFocused, "каретка не вернулась в строку");
    }

    /// <summary>
    /// Esc и щелчок мимо закрывают панель, ничего не записав, а свой Esc редактор забирает сам и панель не закрывает.
    /// Карандаш у жетона открывает её снова.
    /// </summary>
    [AvaloniaFact]
    public void Escape_and_a_click_outside_close_the_panel_without_writing()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;
        var editor = Assert.Single(target.Editors);

        editor.Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = editor.Field, Handled = true });
        Assert.True(popover.IsOpen, "панель закрыл Esc, который забрал редактор");

        editor.Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = editor.Field });
        studio.Frame();

        Assert.False(popover.IsOpen);
        Assert.Empty(target.Writes);
        Assert.True(editor.Released, "редактор не отпущен");
        Assert.True(Cell(panel, "Text").IsFocused, "каретка не вернулась в строку");

        var pencil = Row(panel, "Text").GetVisualDescendants().OfType<AxButton>()
            .Single(button => Equals(AutomationProperties.GetName(button), studio.Strings["inspector.expression.change"]));

        pencil.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        studio.Frame();

        Assert.True(popover.IsOpen, "карандаш у жетона не открыл панель");

        popover.Popup.IsOpen = false;
        studio.Frame();

        Assert.False(popover.IsOpen, "щелчок мимо не закрыл панель");
        Assert.Equal(0, target.Holding);
        Assert.Empty(target.Writes);
    }

    /// <summary>Что записать нельзя, редактор говорит панели: «Применить» выключено, причина — под содержимым.</summary>
    [AvaloniaFact]
    public async Task A_reported_problem_turns_apply_off_and_says_why()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;
        var editor = Assert.Single(target.Editors);

        editor.Context.Report(InspectorCheck.Invalid("Пути нет"));

        Assert.False(popover.CanApply);
        Assert.Equal("Пути нет", popover.Note);
        Assert.False(await popover.CommitAsync());
        Assert.Empty(target.Writes);

        editor.Context.Report(InspectorCheck.Valid);

        Assert.True(popover.CanApply);
        Assert.Null(popover.Note);
    }

    /// <summary>
    /// Своя запись перестраивает список, но панель не закрывает: не записалось — она остаётся с причиной. Перестройка,
    /// снявшая строку, и показ другой цели — закрывают.
    /// </summary>
    [AvaloniaFact]
    public async Task Its_own_write_keeps_the_panel_and_a_rebuild_without_the_row_closes_it()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget { Accepts = false };

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;

        Assert.False(await popover.CommitAsync());
        studio.Frame();

        Assert.True(popover.IsOpen, "панель закрыла своя же перестройка");
        Assert.Equal(studio.Strings["inspector.notWritten"], popover.Note);

        target.Reshape(FakeTarget.Property("Width", typeof(double)));
        studio.Frame();

        Assert.False(popover.IsOpen, "строки нет, а панель открыта");

        Row(panel, "Width").OpenPanel(InspectorExpressionKind.Binding);
        studio.Frame();
        Assert.True(popover.IsOpen);

        using var other = studio.Service.Show(new PanelTarget());

        studio.Frame();

        Assert.False(popover.IsOpen, "показана другая цель, а панель открыта");
    }

    /// <summary>
    /// Пока запись идёт, панель пропускает щелчок мимо и показ другой цели, а когда запись кончилась, догоняет их:
    /// закрывается и отпускает строку.
    /// </summary>
    [AvaloniaFact]
    public async Task A_panel_left_while_writing_closes_when_the_write_ends()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget { Pending = new TaskCompletionSource<bool>() };

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;
        var writing = popover.CommitAsync();

        // Щелчок мимо закрывает всплывающее окно, а панель ждёт записи.
        popover.Popup.IsOpen = false;
        studio.Frame();
        Assert.True(popover.IsOpen, "панель бросила запись на полпути");

        target.Pending.SetResult(false);
        Assert.False(await writing);
        studio.Frame();

        Assert.False(popover.IsOpen, "окно закрыто щелчком мимо, а панель после записи открыта");
        Assert.Equal(0, target.Holding);

        target.Pending = new TaskCompletionSource<bool>();
        Row(panel, "Text").OpenExpression();
        studio.Frame();
        writing = popover.CommitAsync();

        using (studio.Service.Show(new PanelTarget()))
        {
            studio.Frame();
            Assert.True(popover.IsOpen, "панель бросила запись на полпути");

            target.Pending.SetResult(false);
            Assert.False(await writing);
            studio.Frame();

            Assert.False(popover.IsOpen, "показана другая цель, а панель после записи открыта");
        }

        Assert.Equal(0, target.Holding);
    }

    /// <summary>
    /// Выпадающий список редактора, закрываясь, оставляет каретку в панели — у себя, — а не отдаёт её туда, где окно
    /// держало её до панели (живая проверка, запись 425).
    /// </summary>
    [AvaloniaFact]
    public void A_list_closing_in_the_panel_keeps_the_caret_in_the_panel()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();

        // Каретка в окне до панели — в ячейке строки: туда закрывшийся список её и уводил.
        Cell(panel, "Width").Focus(NavigationMethod.Directional);
        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;
        var editor = Assert.Single(target.Editors);

        editor.Choices.Focus(NavigationMethod.Pointer);
        editor.Choices.IsDropDownOpen = true;
        studio.Frame();
        editor.Choices.IsDropDownOpen = false;
        studio.Frame();

        var focused = TopLevel.GetTopLevel(editor.Choices)?.FocusManager?.GetFocusedElement();

        Assert.True(popover.IsOpen, "закрывшийся список закрыл панель");
        Assert.True(ReferenceEquals(focused, editor.Choices), $"каретка ушла из панели: {focused?.GetType().Name}");
    }

    /// <summary>
    /// Редактор закрытой панели до панели, открытой следом, не дотянется: его запоздалая проверка, отмена и запись — уже
    /// не о ней.
    /// </summary>
    [AvaloniaFact]
    public void A_closed_editor_cannot_reach_the_panel_opened_after_it()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        using var shown = studio.Service.Show(target);

        studio.Frame();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        var popover = panel.Expressions!;
        var closed = Assert.Single(target.Editors);

        popover.Close();
        Row(panel, "Text").OpenExpression();
        studio.Frame();

        closed.Context.Report(InspectorCheck.Invalid("Пути нет"));
        closed.Context.Cancel();
        closed.Context.Commit();
        studio.Frame();

        Assert.True(popover.IsOpen, "отмена закрытого редактора закрыла следующую панель");
        Assert.True(popover.CanApply, "закрытый редактор выключил «Применить» следующей панели");
        Assert.Null(popover.Note);
        Assert.Empty(target.Writes);
    }

    /// <summary>
    /// Меню строки предлагает «Привязать…» и у свойства без выражения, а у выражения без панели Enter открывает текст.
    /// </summary>
    [AvaloniaFact]
    public void The_row_menu_offers_bind_and_an_expression_without_a_panel_opens_as_text()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var target = new PanelTarget();

        target.Put("Width", new InspectorValue("100") { Source = InspectorValueSource.Local });

        using var shown = studio.Service.Show(target);

        studio.Frame();

        var tag = Row(panel, "Width");
        var bind = Assert.Single(panel.Menus!.ForRow(tag).OfType<AxMenuItem>(), item => Equals(item.Header, studio.Strings["inspector.expression.bind"]));

        bind.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        studio.Frame();

        Assert.True(panel.Expressions!.IsOpen, "«Привязать…» не открыло панель");
        Assert.Same(tag, panel.Expressions.Row);
        Assert.False(panel.Expressions.CanClear, "«Снять» у свойства без выражения сбросило бы его значение");

        panel.Expressions.Close();
        target.Kinds = [];
        studio.Frame();

        var text = Row(panel, "Text");

        Assert.True(text.OpenExpression());
        Assert.True(text.EditsAsText, "выражение без панели не открылось текстом");
        Assert.False(panel.Expressions.IsOpen);
    }

    private static List<PropertyCell> Cells(InspectorPanel panel) => panel.List!.Items.OfType<PropertyCell>().ToList();

    private static PropertyCell Cell(InspectorPanel panel, string id) => Assert.Single(Cells(panel), cell => cell.Row.Property.Id == id);

    private static InspectorRow Row(InspectorPanel panel, string id) => Cell(panel, id).Row;

    private static void Press(InspectorStudio studio, Key key)
    {
        studio.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Frame();
    }

    /// <summary>Цель-проба с панелью выражения: свойство Text держит привязку, Width — значение.</summary>
    private sealed class PanelTarget : InspectorTarget
    {
        private readonly Dictionary<string, InspectorValue> _values = new(StringComparer.Ordinal)
        {
            ["Text"] = new InspectorValue("Анна")
            {
                Source = InspectorValueSource.Expression,
                Written = "{Binding Name}",
                Expression = new InspectorExpression(InspectorExpressionKind.Binding, "Name") { State = InspectorExpressionState.Resolved },
            },
        };

        private IReadOnlyList<InspectorProperty> _properties =
            [FakeTarget.Property("Text", typeof(string)), FakeTarget.Property("Width", typeof(double))];

        public override InspectorHeader Header { get; } = new("TextBlock");

        public override InspectorTypeInfo Type { get; } = InspectorTypeInfo.Of(typeof(TextBlock));

        public override IReadOnlyList<InspectorProperty> Properties => _properties;

        /// <summary>Какими выражениями свойства задают в панели.</summary>
        public IReadOnlyList<InspectorExpressionKind> Kinds { get; set; } = [InspectorExpressionKind.Binding];

        /// <summary>Принимает ли запись.</summary>
        public bool Accepts { get; set; } = true;

        /// <summary>Запись, которая кончится, когда скажет тест; пусто — кончается сразу.</summary>
        public TaskCompletionSource<bool>? Pending { get; set; }

        public List<IReadOnlyList<InspectorWrite>> Writes { get; } = [];

        public List<ProbeEditor> Editors { get; } = [];

        public int Holding { get; private set; }

        public void Put(string id, InspectorValue value) => _values[id] = value;

        public void Reshape(params InspectorProperty[] properties)
        {
            _properties = properties;
            Raise(InspectorChanges.Properties);
        }

        public override InspectorValue ValueOf(string id) => _values.TryGetValue(id, out var value) ? value : new InspectorValue((string?)null);

        public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null)
        {
            Writes.Add(writes);

            // Своя запись перестраивает список, как перестраивает его правка формы.
            Raise(InspectorChanges.Properties | InspectorChanges.Values);

            return Pending?.Task ?? Task.FromResult(Accepts);
        }

        public override IDisposable? Hold(string id)
        {
            Holding++;

            return new Released(() => Holding--);
        }

        public override IReadOnlyList<InspectorExpressionKind> ExpressionKinds(string id) => Kinds;

        public override InspectorExpressionEditor? CreateExpressionEditor(string id, InspectorExpressionKind kind, InspectorExpressionContext context)
        {
            var editor = new ProbeEditor(this, id, kind, context);

            Editors.Add(editor);

            return editor;
        }
    }

    /// <summary>Редактор-проба: поле текста выражения и выпадающий список, запись — тем, что набрано.</summary>
    private sealed class ProbeEditor : InspectorExpressionEditor
    {
        private readonly PanelTarget _target;
        private readonly string _id;

        public ProbeEditor(PanelTarget target, string id, InspectorExpressionKind kind, InspectorExpressionContext context)
        {
            _target = target;
            _id = id;
            Kind = kind;
            Context = context;
            View = new StackPanel { Children = { Field, Choices } };
        }

        public AxTextBox Field { get; } = new() { Text = "{Binding Name}" };

        public AxComboBox Choices { get; } = new() { ItemsSource = new[] { "OneWay", "TwoWay" }, SelectedIndex = 0 };

        public InspectorExpressionKind Kind { get; }

        public InspectorExpressionContext Context { get; }

        public bool Released { get; private set; }

        public override string Title => "Привязка — " + _id;

        public override Control View { get; }

        public override Control? FocusTarget => Field;

        public override Task<bool> CommitAsync() => _target.WriteAsync([new InspectorWrite(_id, Field.Text)]);

        public override void Release() => Released = true;
    }

    private sealed class Released(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
