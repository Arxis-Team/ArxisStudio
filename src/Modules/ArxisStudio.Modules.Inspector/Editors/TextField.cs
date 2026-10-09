using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Поле текста: значение пишется на Enter и уходе каретки, Esc возвращает показанное, а пока в поле набрано своё,
/// цель держится — замена поколения ждёт.
/// </summary>
/// <remarks>
/// <para>
/// Непрочтённое (<see cref="InspectorFieldContext.Check"/>) не пишется: строка показывает почему заметкой, а черновик
/// остаётся в поле — человек правит, а не набирает заново. Выражение — привязка, ресурс — показывается тем, что
/// написано, а не тем, к чему оно пришло: правят выражение.
/// </para>
/// <para>
/// Варианты метаданных (<see cref="InspectorConstraints.Choices"/>) — подсказки, а не ограда: шрифты системы у
/// <c>FontFamily</c>. Кнопка в поле (и Alt+↓ или F4 с клавиатуры) открывает их меню — те, что содержат набранное,
/// а ничего не нашлось — все; выбранный вариант пишется сразу. Набрать своё по-прежнему можно.
/// </para>
/// </remarks>
internal sealed class TextField : InspectorField
{
    private readonly InspectorFieldContext _context;
    private readonly AxTextBox _box;
    private readonly AxButton? _choices;
    private string _shown = string.Empty;
    private IDisposable? _hold;
    private bool _writing;

    /// <summary>Заводит поле.</summary>
    /// <param name="context">Свойство строки и дорога к цели.</param>
    /// <param name="multiline">Многострочное: Enter — новая строка, фиксация — уходом каретки.</param>
    /// <param name="choicesName">
    /// Как назвать кнопку вариантов диктору и подсказке; пусто — кнопки нет, даже если варианты есть.
    /// </param>
    public TextField(InspectorFieldContext context, bool multiline, string? choicesName = null)
    {
        _context = context;
        _box = multiline ? new AxTextArea() : new AxTextBox();
        _box.Size = AxControlSize.Compact;
        _box.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _box.LostFocus += OnBlur;
        _box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Track();
        };

        if (!multiline && choicesName is not null && context.Property.Constraints.Choices.Count > 0 && !context.Property.IsReadOnly)
        {
            var menu = new AxMenuFlyout();

            menu.Opening += (_, _) => Fill(menu);
            _choices = new AxButton
            {
                Appearance = AxButtonAppearance.Toolbar,
                Content = new AxIcon { Data = AxIcons.ChevronDown },
                Flyout = menu,
                Focusable = false,
                [AutomationProperties.NameProperty] = choicesName,
                [ToolTip.TipProperty] = choicesName,
            };
            _box.InnerRightContent = _choices;
        }
    }

    /// <inheritdoc/>
    public override Control View => _box;

    /// <inheritdoc/>
    public override Control? FocusTarget => _box;

    /// <inheritdoc/>
    public override bool HasDraft => !_writing && !string.Equals(_box.Text ?? string.Empty, _shown, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override void Show(InspectorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var keep = HasDraft && _box.IsKeyboardFocusWithin;

        _shown = value.IsMixed ? string.Empty : Written(value) ?? string.Empty;
        _box.PlaceholderText = value.IsMixed ? "—" : null;
        _box.IsReadOnly = _context.Property.IsReadOnly;

        if (!keep)
            Put(_shown);
    }

    /// <inheritdoc/>
    public override void Release()
    {
        _hold?.Dispose();
        _hold = null;
    }

    /// <summary>Что показать: у выражения — само выражение, у остальных — значение.</summary>
    internal static string? Written(InspectorValue value) =>
        value.Source == InspectorValueSource.Expression ? value.Written ?? value.Text : value.Text;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_choices?.Flyout is { } menu &&
            (e is { Key: Key.Down, KeyModifiers: KeyModifiers.Alt } || e is { Key: Key.F4, KeyModifiers: KeyModifiers.None }))
        {
            menu.ShowAt(_choices);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Enter && !_box.AcceptsReturn)
        {
            _ = CommitAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && HasDraft)
        {
            Put(_shown);
            _context.Report(InspectorCheck.Valid);
            e.Handled = true;
        }
    }

    private void OnBlur(object? sender, FocusChangedEventArgs e) => _ = CommitAsync();

    /// <summary>Меню вариантов — те, что содержат набранное; ничего не нашлось — все.</summary>
    private void Fill(AxMenuFlyout menu)
    {
        var all = _context.Property.Constraints.Choices;
        var typed = HasDraft ? (_box.Text ?? string.Empty).Trim() : string.Empty;
        IReadOnlyList<InspectorChoice> found = typed.Length == 0
            ? all
            : [.. all.Where(choice => (choice.Title ?? choice.Value).Contains(typed, StringComparison.CurrentCultureIgnoreCase))];

        menu.Items.Clear();

        foreach (var choice in found.Count > 0 ? found : all)
        {
            var item = new AxMenuItem { Header = choice.Title ?? choice.Value };

            item.Click += (_, _) => _ = PickAsync(choice.Value);
            menu.Items.Add(item);
        }
    }

    /// <summary>Выбранный вариант — значение: в поле и одной правкой в цель.</summary>
    private async Task PickAsync(string value)
    {
        Put(value);
        await CommitAsync().ConfigureAwait(true);
    }

    /// <summary>Набранное — в значение: проверка, затем одна правка.</summary>
    private async Task CommitAsync()
    {
        if (!HasDraft || _context.Property.IsReadOnly)
            return;

        var text = _box.Text ?? string.Empty;
        var check = _context.Check(text);

        _context.Report(check);

        if (!check.IsValid)
            return;

        _writing = true;

        try
        {
            if (await _context.WriteAsync(text).ConfigureAwait(true))
                _shown = text;
        }
        finally
        {
            _writing = false;
            Track();
        }
    }

    /// <summary>Держит цель, пока набрано своё, и отпускает, когда набранного нет.</summary>
    private void Track()
    {
        if (HasDraft)
            _hold ??= _context.Hold();
        else
            Release();
    }

    private void Put(string text)
    {
        _writing = true;

        try
        {
            _box.Text = text;
        }
        finally
        {
            _writing = false;
        }

        Track();
    }
}
