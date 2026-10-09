using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Modules.Inspector.Editors;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Строка свойства: подпись, поле редактора, откуда значение, заметка — одна и та же в списке, в разделе плагина и
/// в шапке.
/// </summary>
/// <remarks>
/// <para>
/// Поле строится, когда строка впервые встала на экран, а значение спрашивается, только пока её видно: список держит
/// сотни строк, а на экране десятки. Ушедшая с экрана строка помнит поле и, вернувшись, переспрашивает значение,
/// если оно успело смениться.
/// </para>
/// <para>
/// Поле строит редактор — чужой код, — поэтому оно стоит за барьером (<see cref="IStudioGuarded{T}.Host"/>):
/// упавшая раскладка поля станет строкой о сбое и не уронит панель. Поле пишет через контекст строки, а не через
/// цель: строка переживает смену цели того же типа, и запись уходит той, что показана сейчас.
/// </para>
/// <para>
/// Выражение — привязку, ресурс — строка показывает жетоном с самим выражением, а не полем редактора: число не
/// прочтёт <c>{Binding Width}</c>, а затереть привязку случайной протяжкой легко. Править выражение можно текстом
/// (Enter, F2, карандаш у жетона) или заменить значением, которое оно дало, — из меню строки.
/// </para>
/// </remarks>
internal sealed class InspectorRow : AxPropertyRow
{
    private readonly InspectorList _list;
    private readonly FieldContext _context;
    private IStudioGuarded<InspectorField>? _field;
    private Control? _fieldView;
    private TextField? _text;
    private AxChip? _token;
    private TextBlock? _tokenText;
    private AxButton? _edit;
    private bool _built;
    private bool _stale = true;
    private bool _shown = true;
    private bool _asText;
    private InspectorCheck _check = InspectorCheck.Valid;

    /// <summary>Заводит строку; поле она построит, когда встанет на экран.</summary>
    /// <param name="list">Список — дорога к цели и словам.</param>
    /// <param name="property">Свойство с метаданными.</param>
    /// <param name="editor">Выбранный редактор; пусто — не нашлось никакого, и строка скажет об этом.</param>
    public InspectorRow(InspectorList list, InspectorProperty property, ChosenEditor? editor)
    {
        _list = list;
        _context = new FieldContext(this);
        Editor = editor;
        Property = property;
        Describe();
    }

    /// <summary>Свойство строки.</summary>
    public InspectorProperty Property { get; private set; }

    /// <summary>Редактор строки: другой редактор — другая строка.</summary>
    public ChosenEditor? Editor { get; }

    /// <summary>Последнее значение, которое строка спросила; пусто — ещё не спрашивала.</summary>
    public InspectorValue? Value { get; private set; }

    /// <summary>Значение — выражение: строка показывает его жетоном.</summary>
    public bool IsExpression => Value is { IsMixed: false, Source: InspectorValueSource.Expression };

    /// <summary>Выражение правится текстом.</summary>
    public bool EditsAsText => _asText;

    /// <summary>Куда встаёт каретка, когда строку открывают: поле, текст выражения, карандаш у жетона.</summary>
    public Control? FocusTarget
    {
        get
        {
            if (_asText)
                return _text?.FocusTarget;

            if (IsExpression)
                return _edit;

            return _field is { } guarded && guarded.TryGet("gives its focus target", it => it.FocusTarget ?? it.View, out var target)
                ? target
                : null;
        }
    }

    /// <summary>В поле набрано то, что ещё не стало значением.</summary>
    public bool HasDraft => _asText
        ? _text?.HasDraft == true
        : _field is { } guarded && guarded.TryGet("tells about its draft", it => it.HasDraft, out var draft) && draft;

    /// <summary>Значение можно сбросить: оно задано здесь, и свойство пишется.</summary>
    public bool CanReset => !Property.IsReadOnly && Value?.IsWritten == true;

    /// <summary>Сбрасывает значение — Delete на строке и «Сбросить» в её меню.</summary>
    public void Reset()
    {
        if (CanReset)
            _ = _context.WriteAsync(null);
    }

    /// <summary>Свойство то же, а цель или её состав сменились: строка помнит поле и переспросит значение.</summary>
    /// <param name="property">Свойство с новыми метаданными.</param>
    public void Renew(InspectorProperty property)
    {
        Property = property;

        // Заметка о непрочтённом относится к набранному: нет набранного — нет и заметки.
        if (!HasDraft)
            _check = InspectorCheck.Valid;

        Describe();
        Invalidate();
    }

    /// <summary>Значение могло смениться: видная строка переспросит сразу, остальные — когда встанут на экран.</summary>
    public void Invalidate()
    {
        _stale = true;

        if (_shown && this.IsAttachedToVisualTree())
            Refresh();
    }

    /// <summary>Ячейка списка говорит, видно ли строку: ушедшую с экрана ячейку список прячет, а не снимает.</summary>
    /// <param name="shown">Видно ли.</param>
    public void Show(bool shown)
    {
        _shown = shown;

        if (shown && _stale && this.IsAttachedToVisualTree())
            Refresh();
    }

    /// <summary>Значение, даже если строку не видно, — нужно отбору «Только заданные» у цели, которая не знает заданного.</summary>
    public InspectorValue Known()
    {
        if (_stale || Value is null)
            Value = _list.ValueOf(Property.Id);

        return Value;
    }

    /// <summary>Ставит каретку в поле — Enter и F2 на строке; у выражения — открывает его текстом.</summary>
    /// <returns>Встала ли.</returns>
    public bool Enter()
    {
        if (IsExpression && !_asText)
            return EditAsText();

        return FocusTarget?.Focus(NavigationMethod.Tab) == true;
    }

    /// <summary>Открывает выражение текстом: «Изменить как текст».</summary>
    /// <returns>Открылось ли.</returns>
    public bool EditAsText()
    {
        if (!IsExpression || Property.IsReadOnly)
            return false;

        _asText = true;
        ShowContent();
        ShowValue();
        Dispatcher.UIThread.Post(() => _text?.FocusTarget?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

        return true;
    }

    /// <summary>Заменяет выражение значением, которое оно дало: «Заменить значением».</summary>
    public void Replace()
    {
        if (IsExpression && !Property.IsReadOnly && Value?.Text is { } text)
            _ = _context.WriteAsync(text);
    }

    /// <summary>Строка уходит: поле отпускается прощальной дорогой шва.</summary>
    public void Release()
    {
        if (_field is { } field)
        {
            field.Released -= OnFieldReleased;
            field.Farewell("releases a field", it => it.Release());
            _field = null;
        }

        _text?.Release();
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxPropertyRow);

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Build();

        if (_stale && _shown)
            Refresh();
    }

    /// <summary>Строит поле: редактор зовётся через шов, а его поле встаёт за барьер.</summary>
    private void Build()
    {
        if (_built)
            return;

        _built = true;

        if (Editor is { } editor &&
            editor.Editor.TryGet("creates a field", it => it.CreateField(_context), out var field) && field is not null)
        {
            _field = editor.Editor.Guard(field);
            _field.Released += OnFieldReleased;
            Layout = editor.Layout switch
            {
                InspectorFieldLayout.Below => AxPropertyRowLayout.Below,
                InspectorFieldLayout.Full => AxPropertyRowLayout.Full,
                _ => AxPropertyRowLayout.Inline,
            };
            _fieldView = _field.TryGet("gives its view", it => it.View, out var view) && view is not null
                ? _field.Host(view)
                : Failed();
            ScrubTarget = _field.TryGet("gives its scrub target", it => it.ScrubTarget, out var scrub) ? scrub : null;
        }
        else
        {
            _fieldView = Failed();
        }

        ShowContent();
    }

    /// <summary>Хозяина редактора выгружают: поле уходит с экрана раньше, чем его сборка.</summary>
    private void OnFieldReleased(object? sender, EventArgs e)
    {
        if (_field is { } field)
            field.Released -= OnFieldReleased;

        _field = null;
        ScrubTarget = null;
        _fieldView = Failed();
        ShowContent();
    }

    /// <summary>Переспрашивает значение и показывает его.</summary>
    private void Refresh()
    {
        _stale = false;
        Value = _list.ValueOf(Property.Id);
        IsOverridden = Value.IsWritten;

        // Выражение заменили значением — текстом его больше не правят.
        if (!IsExpression)
            _asText = false;

        ShowContent();
        ShowValue();
        ShowNote();
        Describe();
    }

    /// <summary>Ставит в строку то, чем значение правится сейчас: поле, жетон выражения или его текст.</summary>
    private void ShowContent()
    {
        if (!_built)
            return;

        Control? content = _asText ? Text().View : IsExpression ? Token() : _fieldView;

        if (!ReferenceEquals(Content, content))
        {
            var keep = IsKeyboardFocusWithin;

            Content = content;

            // Каретка была в строке, а то, в чём она стояла, сменилось: она переходит к тому, что встало.
            if (keep)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsKeyboardFocusWithin)
                        FocusTarget?.Focus(NavigationMethod.Tab);
                }, DispatcherPriority.Loaded);
            }
        }

        Actions = IsExpression && !_asText && !Property.IsReadOnly ? Edit() : null;
    }

    private void ShowValue()
    {
        if (Value is not { } value)
            return;

        if (_asText)
        {
            _text?.Show(value);
        }
        else if (IsExpression)
        {
            Token();
            _tokenText!.Text = value.Written ?? value.Text;
            ToolTip.SetTip(_token!, value.Written ?? value.Text);
        }
        else
        {
            _field?.Run("shows a value", it => it.Show(value));
        }
    }

    /// <summary>Заметка под полем: непрочтённое набранное, ошибка источника или пояснение метаданных.</summary>
    private void ShowNote()
    {
        var error = !_check.IsValid ? _check.Message ?? _list.Words["inspector.invalid"] : Value?.Error;
        var help = Property.Decorations.FirstOrDefault(static item => item.Kind == InspectorDecorationKind.Help)?.Text;

        Note = error ?? help;
        AxValidation.SetState(this, error is null ? AxValidationState.None : AxValidationState.Error);
    }

    /// <summary>Подпись, вид «только чтение», подсказка, слова для диктора и воздух над строкой — по метаданным.</summary>
    private void Describe()
    {
        Label = Property.Label;
        IsReadOnly = Property.IsReadOnly;
        AutomationProperties.SetName(this, Property.Label);

        var source = Value is { } value ? _list.Words.Source(value) : null;
        var tip = string.Join(Environment.NewLine, new[] { Property.Description, source }.Where(static line => !string.IsNullOrEmpty(line)));

        ToolTip.SetTip(this, tip.Length > 0 ? tip : null);
        AutomationProperties.SetHelpText(this, Spoken(source));

        if (Property.Decorations.Any(static item => item.Kind == InspectorDecorationKind.Space))
            this[!MarginProperty] = new DynamicResourceExtension("AxSpaceAboveThickness");
        else
            ClearValue(MarginProperty);
    }

    /// <summary>Что строка говорит диктору после имени: значение и откуда оно.</summary>
    private string? Spoken(string? source)
    {
        if (Value is not { } value)
            return Property.Description;

        var shown = value.IsMixed ? null : value.Written ?? value.Text;

        return string.Join(", ", new[] { shown, source }.Where(static part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>Поле выражения текстом — строится, когда его попросили.</summary>
    private TextField Text()
    {
        if (_text is not null)
            return _text;

        _text = new TextField(_context, multiline: false);

        // Каретка ушла из текста выражения — строка возвращается к жетону: набранное к этому времени уже записано.
        _text.View.LostFocus += (_, _) =>
        {
            if (!_asText)
                return;

            _asText = false;
            ShowContent();
            ShowValue();
        };

        return _text;
    }

    /// <summary>Жетон выражения; двойной щелчок открывает его текстом.</summary>
    private AxChip Token()
    {
        if (_token is not null)
            return _token;

        _tokenText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        _token = new AxChip
        {
            Kind = AxChipKind.Accent,
            Content = _tokenText,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _token.DoubleTapped += (_, e) =>
        {
            e.Handled = EditAsText();
        };

        return _token;
    }

    /// <summary>Карандаш у жетона — «Изменить как текст» мышью.</summary>
    private AxButton Edit()
    {
        if (_edit is not null)
            return _edit;

        var title = _list.Words["inspector.expression.edit"];

        _edit = new AxButton
        {
            Appearance = AxButtonAppearance.Toolbar,
            Content = new AxIcon { Data = AxIcons.Edit },
            [AutomationProperties.NameProperty] = title,
            [ToolTip.TipProperty] = title,
        };
        _edit.Click += (_, _) => EditAsText();

        return _edit;
    }

    /// <summary>Заглушка вместо поля, которое не построилось: строка остаётся и говорит о сбое.</summary>
    private TextBlock Failed() => new()
    {
        Text = _list.Words["inspector.fieldFailed"],
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        [AxText.ToneProperty] = AxTextTone.Secondary,
    };

    /// <summary>Что сказала проверка набранного.</summary>
    private void Report(InspectorCheck check)
    {
        _check = check;
        ShowNote();
    }

    /// <summary>Контекст поля: свойство строки и дорога к цели, показанной сейчас.</summary>
    private sealed class FieldContext(InspectorRow row) : InspectorFieldContext
    {
        public override InspectorProperty Property => row.Property;

        public override int Count => row._list.Count;

        public override async Task<bool> WriteAsync(string? text)
        {
            var written = await row._list.WriteAsync([new InspectorWrite(row.Property.Id, text)], null).ConfigureAwait(true);

            if (!written)
                row.Report(InspectorCheck.Invalid(row._list.Words["inspector.notWritten"]));

            return written;
        }

        public override InspectorCheck Check(string text) => row._list.Check(row.Property.Id, text);

        public override void Report(InspectorCheck check) => row.Report(check);

        public override IInspectorPreview? BeginPreview() => row._list.BeginPreview(row.Property.Id);

        public override IDisposable? Hold() => row._list.Hold(row.Property.Id);

        public override IReadOnlyList<InspectorChoice> Suggest(string typed) => row._list.Suggest(row.Property.Id, typed);
    }
}
