using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Панель привязки — содержимое панели выражения под строкой инспектора, как окно Add Binding у Unity: откуда читать,
/// путь с подсказками членов, режим и дополнительное, а снизу — запись и работает ли она.
/// </summary>
/// <remarks>
/// <para>
/// <b>Подсказки и проверка — у службы, по именам.</b> Члены, которые путь может назвать дальше, отвечает служба XAML
/// (<see cref="IXamlDesignView.GetBindableMembersAsync"/>) по тому, что документ объявляет; черновик она проверяет тем же
/// правилом, что написанное (<see cref="IXamlDesignView.CheckExpressionAsync"/>). Сломанное не пишется: панель
/// выключает «Применить» и говорит причину. Проект компилирует привязки, а типа данных нет — панель предлагает задать
/// его той же правкой или писать привязку без проверки типа (<c>{ReflectionBinding}</c>).
/// </para>
/// <para>
/// <b>Запись — одной правкой</b>, и меняются только тронутые аргументы (<see cref="BindingDraft"/>). Элемент без имени
/// получает имя той же правкой: свободное в документе и среди членов класса корня — поле класса с именем члена базы
/// роняло бы сборку. Пока имени нет, его члены и проверка читаются от самого элемента.
/// </para>
/// </remarks>
internal sealed class BindingEditor : InspectorExpressionEditor
{
    /// <summary>Конвертеры Avalonia, которые пишут без ресурса: <c>{x:Static BoolConverters.Not}</c>.</summary>
    private static readonly string[] Converters =
    [
        "BoolConverters.Not",
        "ObjectConverters.IsNull",
        "ObjectConverters.IsNotNull",
        "StringConverters.IsNullOrEmpty",
        "StringConverters.IsNotNullOrEmpty",
    ];

    private static readonly string[] Modes = ["OneWay", "TwoWay", "OneTime", "OneWayToSource"];

    private static readonly string[] Triggers = ["PropertyChanged", "LostFocus", "Explicit"];

    private static readonly string[] Priorities = ["LocalValue", "StyleTrigger", "Style", "Template", "Animation"];

    private readonly Setup _setup;
    private readonly InspectorExpressionContext _context;
    private readonly IStudioStrings _strings;
    private readonly XamlMarkupExtensionValue? _original;
    private readonly Control _view;
    private readonly AxSegmentedControl _source;
    private readonly AxComboBox _element;
    private readonly AxComboBox _ancestor;
    private readonly AxNumberField _level;
    private readonly Control _elementRow;
    private readonly Control _ancestorRow;
    private readonly AxTextBox _path;
    private readonly AxListBox _members;
    private readonly TextBlock _noMembers;
    private readonly AxCheckBox _fitsOnly;
    private readonly AxComboBox _mode;
    private readonly AxToggleButton _more;
    private readonly AxIcon _moreIcon;
    private readonly Control _advanced;
    private readonly AxComboBox _converter;
    private readonly AxTextBox _parameter;
    private readonly AxTextBox _format;
    private readonly Panel _formats;
    private readonly AxTextBox _fallback;
    private readonly AxTextBox _nullValue;
    private readonly AxComboBox _trigger;
    private readonly AxNumberField _delay;
    private readonly AxComboBox _priority;
    private readonly TextBlock _written;
    private readonly AxIcon _stateIcon;
    private readonly TextBlock _state;
    private readonly Control _stateRow;
    private readonly AxButton _setType;
    private readonly AxButton _reflection;
    private readonly Control _typeActions;
    private BindingDraft _draft;
    private XamlBindableMembers? _listed;
    private string? _listedFor;
    private XamlElementPath? _listedAt;
    private XamlExpressionCheck? _check;
    private IReadOnlyList<string> _resourceConverters = [];
    private InspectorCheck _reported = InspectorCheck.Valid;
    private Task _checked = Task.CompletedTask;
    private bool _filling;
    private bool _setTypeChosen;
    private bool _reflectionChosen;

    // Ответы службы приходят по порядку вопросов; ответ на прежний вопрос, пришедший позже, не нужен.
    private int _listAsked;
    private int _checkAsked;
    private bool _released;

    /// <summary>Заводит панель привязки.</summary>
    /// <param name="setup">Что панели знать о форме, элементе и члене.</param>
    /// <param name="context">Дорога к панели выражения.</param>
    /// <param name="strings">Словари модуля.</param>
    public BindingEditor(Setup setup, InspectorExpressionContext context, IStudioStrings strings)
    {
        _setup = setup;
        _context = context;
        _strings = strings;
        _original = setup.Original;
        _draft = BindingDraft.Read(_original);

        _source = new AxSegmentedControl
        {
            ItemsSource = new[]
            {
                Segment(strings["inspector.binding.source.data"], BindingSourceKind.Data),
                Segment(strings["inspector.binding.source.element"], BindingSourceKind.Element),
                Segment(strings["inspector.binding.source.ancestor"], BindingSourceKind.Ancestor),
                Segment(strings["inspector.binding.source.self"], BindingSourceKind.Self),
            },
            [AutomationProperties.NameProperty] = strings["inspector.binding.source"],
        };
        _source.SelectionChanged += (_, _) => OnSourceChanged();

        // Наименьшая ширина сегментов — та, что им нужна: по ней строка и уводит их под подпись, когда в ряд тесно.
        _source.AttachedToVisualTree += (_, _) =>
        {
            _source.ClearValue(Layoutable.MinWidthProperty);
            _source.Measure(Size.Infinity);
            _source.MinWidth = Math.Ceiling(_source.DesiredSize.Width);
        };

        _element = Combo(strings["inspector.binding.element"]);
        _element.ItemsSource = setup.Elements;
        _element.ItemTemplate = new FuncDataTemplate<ElementChoice?>((choice, _) => new TextBlock { Text = choice is null ? null : Titled(choice) });
        _element.SelectionChanged += (_, _) => OnElementChanged();

        _ancestor = Combo(strings["inspector.binding.ancestor"]);
        _ancestor.ItemsSource = new[] { strings["inspector.binding.anyAncestor"] }.Concat(setup.Ancestors).ToArray();
        _ancestor.SelectionChanged += (_, _) => OnAncestorChanged();

        _level = new AxNumberField
        {
            Size = AxControlSize.Compact,
            Minimum = 1,
            Step = 1,
            Decimals = 0,
            Value = 1,
            [AutomationProperties.NameProperty] = strings["inspector.binding.level"],
        };
        _level.ValueCommitted += (_, _) => OnAncestorChanged();

        _path = new AxTextBox
        {
            Size = AxControlSize.Compact,
            PlaceholderText = strings["inspector.binding.path.placeholder"],
            [AutomationProperties.NameProperty] = strings["inspector.binding.path"],
        };
        _path.TextChanged += (_, _) => OnPathChanged();
        _path.AddHandler(InputElement.KeyDownEvent, OnPathKey, RoutingStrategies.Tunnel);

        _members = new AxListBox
        {
            ItemTemplate = new FuncDataTemplate<XamlBindableMemberInfo?>((member, _) => member is null ? null : MemberRow(member)),
            [!Layoutable.MaxHeightProperty] = new DynamicResourceExtension("AxExpressionMembersMaxHeight"),
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
            [AutomationProperties.NameProperty] = strings["inspector.binding.members"],
        };
        _members.AddHandler(InputElement.KeyDownEvent, OnMembersKey, RoutingStrategies.Tunnel);
        _members.DoubleTapped += (_, e) => e.Handled = Choose(_members.SelectedItem as XamlBindableMemberInfo);

        _noMembers = new TextBlock { Text = strings["inspector.binding.noMembers"], IsVisible = false, TextWrapping = TextWrapping.Wrap };
        AxText.SetTone(_noMembers, AxTextTone.Secondary);
        AxText.SetRole(_noMembers, AxTextRole.Small);

        _fitsOnly = new AxCheckBox { Content = strings["inspector.binding.fitsOnly"], IsChecked = true };
        _fitsOnly.IsCheckedChanged += (_, _) => Fill();

        _mode = Combo(strings["inspector.binding.mode"]);
        _mode.ItemsSource = new[] { Format("inspector.binding.mode.default", setup.DefaultMode ?? "OneWay") }.Concat(Modes).ToArray();
        _mode.SelectionChanged += (_, _) => OnOptionChanged("Mode", _mode.SelectedIndex <= 0 ? null : Modes[_mode.SelectedIndex - 1]);

        _converter = Combo(strings["inspector.binding.converter"]);
        _converter.ItemsSource = ConverterChoices(_draft.Value("Converter"));
        _converter.SelectionChanged += (_, _) =>
            OnOptionChanged("Converter", _converter.SelectedIndex <= 0 ? null : (string)_converter.SelectedItem!);

        _parameter = Field(strings["inspector.binding.parameter"]);
        _parameter.TextChanged += (_, _) => OnOptionChanged("ConverterParameter", _parameter.Text);

        _format = Field(strings["inspector.binding.format"]);
        _format.TextChanged += (_, _) => OnOptionChanged("StringFormat", _format.Text);
        _formats = new WrapPanel { [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceHairAboveThickness") };

        _fallback = Field(strings["inspector.binding.fallback"]);
        _fallback.TextChanged += (_, _) => OnOptionChanged("FallbackValue", _fallback.Text);

        _nullValue = Field(strings["inspector.binding.nullValue"]);
        _nullValue.TextChanged += (_, _) => OnOptionChanged("TargetNullValue", _nullValue.Text);

        _trigger = Combo(strings["inspector.binding.trigger"]);
        _trigger.ItemsSource = new[] { strings["inspector.binding.default"] }.Concat(Triggers).ToArray();
        _trigger.SelectionChanged += (_, _) => OnOptionChanged("UpdateSourceTrigger", _trigger.SelectedIndex <= 0 ? null : Triggers[_trigger.SelectedIndex - 1]);

        _delay = new AxNumberField
        {
            Size = AxControlSize.Compact,
            Minimum = 0,
            Step = 50,
            Decimals = 0,
            AllowsAuto = true,
            AutoText = strings["inspector.binding.noDelay"],
            Value = double.NaN,
            [AutomationProperties.NameProperty] = strings["inspector.binding.delay"],
        };
        _delay.ValueCommitted += (_, e) =>
            OnOptionChanged("Delay", double.IsNaN(e.Value) || e.Value <= 0 ? null : ((int)e.Value).ToString(CultureInfo.InvariantCulture));

        _priority = Combo(strings["inspector.binding.priority"]);
        _priority.ItemsSource = new[] { strings["inspector.binding.default"] }.Concat(Priorities).ToArray();
        _priority.SelectionChanged += (_, _) => OnOptionChanged("Priority", _priority.SelectedIndex <= 0 ? null : Priorities[_priority.SelectedIndex - 1]);

        _advanced = new StackPanel
        {
            Children =
            {
                Row(strings["inspector.binding.converter"], _converter),
                Row(strings["inspector.binding.parameter"], _parameter),
                Row(strings["inspector.binding.format"], new StackPanel { Children = { _format, _formats } }),
                Row(strings["inspector.binding.fallback"], _fallback),
                Row(strings["inspector.binding.nullValue"], _nullValue),
                Row(strings["inspector.binding.trigger"], _trigger),
                Row(strings["inspector.binding.delay"], _delay),
                Row(strings["inspector.binding.priority"], _priority),
            },
        };

        // Раскрытие — шевроном, как узел дерева: вправо у свёрнутого, вниз у раскрытого.
        _moreIcon = new AxIcon { Data = AxIcons.ChevronRight, Size = AxIconSize.Small, VerticalAlignment = VerticalAlignment.Center };
        _moreIcon[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");
        _more = new AxToggleButton
        {
            Appearance = AxButtonAppearance.Subtle,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _moreIcon, new TextBlock { Text = strings["inspector.binding.more"], VerticalAlignment = VerticalAlignment.Center } },
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            [AutomationProperties.NameProperty] = strings["inspector.binding.more"],
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        _more.IsCheckedChanged += (_, _) => ShowAdvanced();

        _written = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AxText.SetRole(_written, AxTextRole.Small);

        _stateIcon = new AxIcon { Size = AxIconSize.Small, VerticalAlignment = VerticalAlignment.Top };
        _stateIcon[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");
        DockPanel.SetDock(_stateIcon, Dock.Left);
        _state = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AxText.SetTone(_state, AxTextTone.Secondary);
        AxText.SetRole(_state, AxTextRole.Small);
        _stateRow = new DockPanel { Children = { _stateIcon, _state } };

        _setType = new AxButton { Appearance = AxButtonAppearance.Subtle };
        _setType.Click += (_, _) => ChooseTypeFix(dataType: true);
        _reflection = new AxButton { Appearance = AxButtonAppearance.Subtle, Content = strings["inspector.binding.reflection"] };
        _reflection.Click += (_, _) => ChooseTypeFix(dataType: false);
        _typeActions = new WrapPanel { Children = { _setType, _reflection }, IsVisible = false };

        _elementRow = Row(strings["inspector.binding.element"], _element);
        _ancestorRow = Row(strings["inspector.binding.ancestor"], new DockPanel { Children = { Docked(_level, Dock.Right), _ancestor } });

        _view = new StackPanel
        {
            Children =
            {
                Row(strings["inspector.binding.source"], _source),
                _elementRow,
                _ancestorRow,
                Row(strings["inspector.binding.path"], _path),
                _members,
                _noMembers,
                _fitsOnly,
                Row(strings["inspector.binding.mode"], _mode),
                _more,
                _advanced,
                Spaced(_written),
                Spaced(_stateRow),
                _typeActions,
            },
        };

        Show(_draft);
        _more.IsChecked = BindingDraft.Options.Skip(1).Any(option => _draft.Value(option) is not null);
        ShowAdvanced();
        Refresh();
        _ = ConvertersAsync();
    }

    /// <inheritdoc/>
    public override string Title => Format("inspector.binding.title", _setup.Member);

    /// <inheritdoc/>
    public override Control View => _view;

    /// <inheritdoc/>
    public override Control? FocusTarget => _path;

    /// <summary>Черновик сейчас — тестам.</summary>
    internal BindingDraft Draft => _draft;

    /// <summary>Список членов пути — тестам.</summary>
    internal AxListBox Members => _members;

    /// <summary>Поле пути — тестам.</summary>
    internal AxTextBox PathField => _path;

    /// <summary>Источник — тестам.</summary>
    internal AxSegmentedControl SourceField => _source;

    /// <summary>Выбор элемента — тестам.</summary>
    internal AxComboBox ElementField => _element;

    /// <summary>Режим — тестам.</summary>
    internal AxComboBox ModeField => _mode;

    /// <summary>Конвертер — тестам.</summary>
    internal AxComboBox ConverterField => _converter;

    /// <summary>Что запишется сейчас — тестам.</summary>
    internal string Written => _written.Text ?? string.Empty;

    /// <summary>Что сказано о работе привязки — тестам.</summary>
    internal string State => _stateRow.IsVisible ? _state.Text ?? string.Empty : string.Empty;

    /// <summary>Кнопки, чинящие компиляцию без типа, — тестам.</summary>
    internal AxButton SetTypeButton => _setType;

    /// <summary>Кнопка «Без проверки типа» — тестам.</summary>
    internal AxButton ReflectionButton => _reflection;

    /// <summary>Тип данных встанет той же правкой — у привязки к данным: другому источнику он не нужен.</summary>
    private bool SetsType => _setTypeChosen && _draft.Source == BindingSourceKind.Data;

    /// <summary>Привязка пишется без проверки типа — у привязки к данным.</summary>
    private bool Reflects => _reflectionChosen && _draft.Source == BindingSourceKind.Data;

    /// <inheritdoc/>
    /// <remarks>Проверка набранного могла ещё не ответить — запись ждёт последнюю: сломанное не пишется и тогда.</remarks>
    public override async Task<bool> CommitAsync()
    {
        for (var pending = _checked; ; pending = _checked)
        {
            await pending.ConfigureAwait(true);

            if (ReferenceEquals(pending, _checked))
                break;
        }

        if (_released || !_reported.IsValid || !Writable(out var text))
            return false;

        var name = _draft.Source == BindingSourceKind.Element && SelectedElement(_draft) is { Name: null } unnamed
            ? (unnamed.Path, _draft.ElementName!)
            : ((XamlElementPath, string)?)null;
        var dataType = SetsType ? _setup.DataType?.Value : null;

        return await _setup.Write(text, name, dataType).ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public override void Release() => _released = true;

    /// <summary>Ставит черновик в поля, не поднимая их перемен.</summary>
    private void Show(BindingDraft draft)
    {
        _filling = true;

        try
        {
            _source.SelectedIndex = (int)draft.Source;
            _element.SelectedItem = SelectedElement(draft);
            _ancestor.SelectedIndex = draft.AncestorType is { Length: > 0 } type
                ? Math.Max(0, Array.IndexOf(_ancestor.ItemsSource!.Cast<string>().ToArray(), type))
                : 0;

            if (draft.AncestorType is { Length: > 0 } written && _ancestor.SelectedIndex == 0)
            {
                _ancestor.ItemsSource = _ancestor.ItemsSource!.Cast<string>().Append(written).ToArray();
                _ancestor.SelectedIndex = _ancestor.ItemCount - 1;
            }

            _level.Value = draft.AncestorLevel;
            _path.Text = draft.Path;
            _mode.SelectedIndex = draft.Value("Mode") is { } mode ? Array.IndexOf(Modes, mode) + 1 : 0;
            _converter.SelectedIndex = ConverterIndex(draft);
            _parameter.Text = draft.Value("ConverterParameter");
            _format.Text = draft.Value("StringFormat");
            _fallback.Text = draft.Value("FallbackValue");
            _nullValue.Text = draft.Value("TargetNullValue");
            _trigger.SelectedIndex = draft.Value("UpdateSourceTrigger") is { } trigger ? Array.IndexOf(Triggers, trigger) + 1 : 0;
            _delay.Value = int.TryParse(draft.Value("Delay"), CultureInfo.InvariantCulture, out var delay) ? delay : double.NaN;
            _priority.SelectedIndex = draft.Value("Priority") is { } priority ? Array.IndexOf(Priorities, priority) + 1 : 0;
            _elementRow.IsVisible = draft.Source == BindingSourceKind.Element;
            _ancestorRow.IsVisible = draft.Source == BindingSourceKind.Ancestor;
        }
        finally
        {
            _filling = false;
        }
    }

    private void OnSourceChanged()
    {
        if (_filling || _source.SelectedItem is not SourceSegment { Kind: var kind })
            return;

        _draft = _draft with { Source = kind, Path = string.Empty };

        if (kind == BindingSourceKind.Element && _draft.ElementName is null && _setup.Elements.FirstOrDefault() is { } first)
            _draft = _draft with { ElementName = first.Name ?? first.NewName };

        Show(_draft);
        Refresh();
    }

    private void OnElementChanged()
    {
        if (_filling || _element.SelectedItem is not ElementChoice choice)
            return;

        _draft = _draft with { ElementName = choice.Name ?? choice.NewName, Path = string.Empty };
        Show(_draft);
        Refresh();
    }

    private void OnAncestorChanged()
    {
        if (_filling)
            return;

        var type = _ancestor.SelectedIndex <= 0 ? null : _ancestor.SelectedItem as string;
        var level = double.IsNaN(_level.Value) ? 1 : Math.Max(1, (int)_level.Value);

        _draft = _draft with { AncestorType = type, AncestorLevel = level };
        Refresh();
    }

    private void OnPathChanged()
    {
        if (_filling)
            return;

        _draft = _draft with { Path = (_path.Text ?? string.Empty).Trim() };
        Refresh();
    }

    private void OnOptionChanged(string option, string? value)
    {
        if (_filling)
            return;

        _draft = _draft.With(option, value);
        Refresh();
    }

    /// <summary>Пересчитывает подсказки, запись и проверку — после каждой перемены черновика.</summary>
    private void Refresh()
    {
        _ = ListAsync();
        _checked = CheckAsync();
    }

    /// <summary>Члены типа, к которому привёл путь до последней точки.</summary>
    private async Task ListAsync()
    {
        var (prefix, _) = Split(_draft.Path);
        var (path, member, probe) = Probe(_draft with { Path = prefix });

        if (string.Equals(probe, _listedFor, StringComparison.Ordinal) && path == _listedAt)
        {
            Fill();
            return;
        }

        var asked = ++_listAsked;
        XamlBindableMembers? members;

        try
        {
            members = await _setup.View.GetBindableMembersAsync(path, member, probe).ConfigureAwait(true);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            members = null;
        }

        if (_released || asked != _listAsked)
            return;

        _listed = members;
        _listedFor = probe;
        _listedAt = path;
        Fill();
    }

    /// <summary>Список — члены, отобранные набранным после последней точки и правилом «Только подходящие».</summary>
    private void Fill()
    {
        var (_, typed) = Split(_draft.Path);
        var fitsOnly = _fitsOnly.IsChecked == true;
        var shown = (_listed?.Members ?? [])
            .Where(member => member.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Where(member => !fitsOnly || member.Fits || member.HasMembers)
            .ToList();

        _members.ItemsSource = shown;
        _members.IsVisible = shown.Count > 0;
        _noMembers.IsVisible = shown.Count == 0;
    }

    /// <summary>Проверяет черновик тем же правилом, что написанное, и говорит, можно ли его записать.</summary>
    private async Task CheckAsync()
    {
        if (!Writable(out var text))
            return;

        _written.Text = text;

        var (path, member, _) = Probe(_draft);
        var probe = ProbeText();
        var asked = ++_checkAsked;
        XamlExpressionCheck? check;

        try
        {
            check = await _setup.View.CheckExpressionAsync(path, member, probe).ConfigureAwait(true);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            check = null;
        }

        if (_released || asked != _checkAsked)
            return;

        _check = check;
        ShowCheck();
    }

    /// <summary>Что о черновике сказала проверка: найдено, не проверено — или сломано, и тогда записать нельзя.</summary>
    private void ShowCheck()
    {
        var check = _check;

        // Проект компилирует привязки, а типа данных нет: задать его той же правкой — или писать без проверки типа.
        var noType = !SetsType && !Reflects && _draft.Source == BindingSourceKind.Data &&
                     check is { State: XamlExpressionState.Broken, BreaksBuild: true, SourceType: null };

        _typeActions.IsVisible = noType;
        _setType.IsVisible = _setup.DataType is not null;
        _setType.Content = _setup.DataType is { } type ? Format("inspector.binding.setType", type.Title ?? type.Value) : null;

        // Тип, который встанет той же правкой, — тип данных, которые элемент получает: путь проверен по ним.
        var typeLine = SetsType ? Environment.NewLine + Format("inspector.binding.typeWillBeSet", _setup.DataType?.Title ?? string.Empty) : null;

        switch (check)
        {
            case { State: XamlExpressionState.Broken, Message: var message }:
                ShowState(null, null);
                Report(InspectorCheck.Invalid(message ?? _strings["inspector.binding.broken"]));
                break;
            case { State: XamlExpressionState.Resolved, SourceType: { } source, ResultType: { } result }:
                ShowState(AxIcons.Success, $"{source.Name} → {result.Name}{typeLine}");
                ShowFormats(result);
                Report(InspectorCheck.Valid);
                break;
            case { State: XamlExpressionState.Unchecked, Message: var message }:
                ShowState(AxIcons.Question, (message ?? _strings["inspector.binding.unchecked"]) + typeLine);
                Report(InspectorCheck.Valid);
                break;
            default:
                ShowState(null, null);
                Report(InspectorCheck.Valid);
                break;
        }
    }

    /// <summary>Говорит панели, можно ли записать набранное, и помнит сказанное — для записи.</summary>
    private void Report(InspectorCheck check)
    {
        _reported = check;
        _context.Report(check);
    }

    /// <summary>«Дополнительно» раскрыто — его строки видны, а шеврон смотрит вниз.</summary>
    private void ShowAdvanced()
    {
        var open = _more.IsChecked == true;

        _advanced.IsVisible = open;
        _moreIcon.Data = open ? AxIcons.ChevronDown : AxIcons.ChevronRight;
    }

    private void ShowState(Geometry? icon, string? text)
    {
        _stateRow.IsVisible = text is not null;
        _state.Text = text;
        _stateIcon.Data = icon;
        _stateIcon.IsVisible = icon is not null;
    }

    /// <summary>Заготовки формата — по типу, к которому пришёл путь: числу, дате, иному.</summary>
    private void ShowFormats(XamlTypeInfo result)
    {
        string[] presets = result.FullName switch
        {
            "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" => ["{0:d}", "{0:dd.MM.yyyy}", "{0:t}"],
            "System.Byte" or "System.Int16" or "System.Int32" or "System.Int64" or "System.UInt16" or "System.UInt32" or "System.UInt64" =>
                ["{0:N0}", "{0:D3}", "{0:P0}"],
            "System.Single" or "System.Double" or "System.Decimal" => ["{0:N2}", "{0:F1}", "{0:P0}", "{0:C}"],
            _ => [],
        };

        _formats.Children.Clear();

        foreach (var preset in presets)
        {
            var button = new AxButton { Appearance = AxButtonAppearance.Subtle, Size = AxControlSize.Compact, Content = preset };

            button.Click += (_, _) => _format.Text = preset;
            _formats.Children.Add(button);
        }

        _formats.IsVisible = presets.Length > 0;
    }

    /// <summary>
    /// Проект компилирует привязки, а типа данных нет: задать его той же правкой — или писать без проверки типа.
    /// </summary>
    private void ChooseTypeFix(bool dataType)
    {
        _setTypeChosen = dataType;
        _reflectionChosen = !dataType;
        Refresh();
    }

    /// <summary>Текст, который запишется; нельзя — панель говорит почему.</summary>
    private bool Writable(out string text)
    {
        try
        {
            text = _draft.Write(_original, Reflects ? "ReflectionBinding" : null);
            return true;
        }
        catch (ArgumentException)
        {
            // Значение держит обе кавычки: в аргумент привязки такое не записать.
            text = string.Empty;
            _written.Text = string.Empty;
            Report(InspectorCheck.Invalid(_strings["inspector.binding.quotes"]));

            return false;
        }
    }

    /// <summary>
    /// Где и чем спрашивать службу: от элемента, к которому привязывают, — а к элементу без имени, пока его нет, — от
    /// него самого, через <c>$self</c>.
    /// </summary>
    private (XamlElementPath Path, string Member, string Probe) Probe(BindingDraft draft)
    {
        if (draft.Source == BindingSourceKind.Element && SelectedElement(draft) is { Name: null } unnamed)
            return (unnamed.Path, _setup.Member, (draft with { Source = BindingSourceKind.Self }).Probe(draft.Path));

        return (_setup.Element, _setup.Member, draft.Probe(draft.Path));
    }

    /// <summary>
    /// Текст для проверки черновика. У элемента без имени — тот же, но от него самого. Тип данных, который встанет той же
    /// правкой, документ ещё не объявляет, и проверка читает привязку отражением — по данным, чей тип и встанет.
    /// </summary>
    private string ProbeText()
    {
        var draft = _draft.Source == BindingSourceKind.Element && SelectedElement(_draft) is { Name: null }
            ? _draft with { Source = BindingSourceKind.Self }
            : _draft;

        return draft.Write(_original, Reflects || SetsType ? "ReflectionBinding" : null);
    }

    /// <summary>Элемент, к которому ведёт черновик: безымянный — по имени, которое он получит той же правкой.</summary>
    private ElementChoice? SelectedElement(BindingDraft draft) =>
        _setup.Elements.FirstOrDefault(choice => string.Equals(choice.Name ?? choice.NewName, draft.ElementName, StringComparison.Ordinal));

    /// <summary>Выбирает член: путь кончается им, и каретка остаётся в поле пути.</summary>
    private bool Choose(XamlBindableMemberInfo? member)
    {
        if (member is null)
            return false;

        var (prefix, _) = Split(_draft.Path);

        _path.Text = prefix.Length == 0 ? member.Name : prefix + "." + member.Name;
        _path.CaretIndex = _path.Text.Length;
        _path.Focus(NavigationMethod.Directional);

        return true;
    }

    /// <summary>Шагает в член: путь получает его и точку, а список — его члены.</summary>
    private bool Enter(XamlBindableMemberInfo? member)
    {
        if (member is not { HasMembers: true })
            return false;

        var (prefix, _) = Split(_draft.Path);

        _path.Text = (prefix.Length == 0 ? member.Name : prefix + "." + member.Name) + ".";
        _path.CaretIndex = _path.Text.Length;

        return true;
    }

    /// <summary>Шагает на ступень вверх: путь теряет последний член.</summary>
    private bool Leave()
    {
        var (prefix, _) = Split(_draft.Path);

        if (prefix.Length == 0)
            return false;

        var (above, _) = Split(prefix);

        _path.Text = above.Length == 0 ? string.Empty : above + ".";
        _path.CaretIndex = _path.Text.Length;

        return true;
    }

    private void OnPathKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key != Key.Down || _members.ItemCount == 0)
            return;

        _members.SelectedIndex = Math.Max(0, _members.SelectedIndex);
        (_members.ContainerFromIndex(_members.SelectedIndex) as Control ?? _members).Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private void OnMembersKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return;

        var member = _members.SelectedItem as XamlBindableMemberInfo;

        e.Handled = e.Key switch
        {
            Key.Enter => Choose(member),
            Key.Right => Enter(member),
            Key.Left => Leave(),
            _ => false,
        };
    }

    /// <summary>Строка члена: имя, тип и отметки — запись, команда, список, — а у объекта стрелка вглубь.</summary>
    private Control MemberRow(XamlBindableMemberInfo member)
    {
        var marks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        void Mark(bool on, Geometry icon, string key)
        {
            if (!on)
                return;

            var mark = new AxIcon { Data = icon, Size = AxIconSize.Small, [ToolTip.TipProperty] = _strings[key] };

            mark[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");
            marks.Children.Add(mark);
        }

        Mark(member.CanWrite, AxIcons.Edit, "inspector.binding.writable");
        Mark(member.IsCommand, AxIcons.Play, "inspector.binding.command");
        Mark(member.IsCollection, AxIcons.List, "inspector.binding.collection");

        var type = new TextBlock { Text = member.TypeName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        AxText.SetTone(type, AxTextTone.Secondary);
        AxText.SetRole(type, AxTextRole.Small);
        type[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");

        var deeper = new AxIcon { Data = AxIcons.ChevronRight, Size = AxIconSize.Small, IsVisible = member.HasMembers, VerticalAlignment = VerticalAlignment.Center };

        var name = new TextBlock { Text = member.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        if (!member.Fits)
            AxText.SetTone(name, AxTextTone.Secondary);

        var row = new DockPanel { Children = { Docked(deeper, Dock.Right), Docked(marks, Dock.Right), Docked(type, Dock.Right), name } };

        AutomationProperties.SetName(row, $"{member.Name}, {member.TypeName}");

        return row;
    }

    private string[] ConverterChoices(string? written)
    {
        var choices = new List<string> { _strings["inspector.binding.converter.none"] };

        choices.AddRange(Converters.Select(static name => "{x:Static " + name + "}"));
        choices.AddRange(_resourceConverters);

        if (written is not null && !choices.Contains(written, StringComparer.Ordinal))
            choices.Add(written);

        return [.. choices];
    }

    /// <summary>Место написанного конвертера в списке; без конвертера — «Нет».</summary>
    private int ConverterIndex(BindingDraft draft) =>
        draft.Value("Converter") is { } converter
            ? Math.Max(0, Array.IndexOf(_converter.ItemsSource!.Cast<string>().ToArray(), converter))
            : 0;

    /// <summary>
    /// Конвертеры из ресурсов элемента — в список конвертеров, статической ссылкой: <c>Converter</c> — член привязки, а не
    /// свойство Avalonia, и динамическая ссылка на нём не встанет.
    /// </summary>
    private async Task ConvertersAsync()
    {
        IReadOnlyList<XamlResourceInfo> resources;

        try
        {
            resources = await _setup.View.GetResourcesAsync(_setup.Element, _setup.Member).ConfigureAwait(true);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (_released)
            return;

        _resourceConverters =
        [
            .. resources
                .Where(static resource => resource is { IsConverter: true, StaticReachable: true })
                .Select(static resource => ResourceEditor.Reference(resource.Key, dynamic: false))
                .OfType<string>(),
        ];

        if (_resourceConverters.Count == 0)
            return;

        _filling = true;

        try
        {
            _converter.ItemsSource = ConverterChoices(_draft.Value("Converter"));
            _converter.SelectedIndex = ConverterIndex(_draft);
        }
        finally
        {
            _filling = false;
        }
    }

    private string Titled(ElementChoice choice) =>
        choice.Name is { } name ? $"{choice.Type} «{name}»" : Format("inspector.binding.unnamed", choice.Type, choice.NewName ?? string.Empty);

    private string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], arguments);

    /// <summary>Путь до последней точки и то, что набрано после неё.</summary>
    private static (string Prefix, string Typed) Split(string path)
    {
        var dot = path.LastIndexOf('.');

        return dot < 0 ? (string.Empty, path) : (path[..dot], path[(dot + 1)..]);
    }

    private static AxComboBox Combo(string name) => new()
    {
        Size = AxControlSize.Compact,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        [AutomationProperties.NameProperty] = name,
    };

    private static AxTextBox Field(string name) => new()
    {
        Size = AxControlSize.Compact,
        [AutomationProperties.NameProperty] = name,
    };

    private static SourceSegment Segment(string title, BindingSourceKind kind) => new(title, kind);

    /// <summary>
    /// Строка панели — строка свойства, как в инспекторе: подпись колонкой, поле справа, а тесно — поле уходит под подпись.
    /// Сегментам источника в ряд с подписью места в панели не хватает, и обрезались бы они, а не переносились.
    /// </summary>
    private static Control Row(string label, Control field) => new AxPropertyRow
    {
        Label = label,
        Content = field,
        [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
    };

    private static Control Spaced(Control control)
    {
        control[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness");

        return control;
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);

        return control;
    }

    /// <summary>Что панели знать о форме, элементе и члене.</summary>
    /// <param name="View">Показ формы.</param>
    /// <param name="Element">Путь элемента, к члену которого привязывают; у нескольких — первого.</param>
    /// <param name="Member">Член.</param>
    /// <param name="Original">Привязка, которую член уже держит; пусто — новая.</param>
    /// <param name="DefaultMode">Режим по умолчанию у члена.</param>
    /// <param name="Elements">Элементы формы, к которым можно привязать: с именами первыми.</param>
    /// <param name="Ancestors">Типы предков элемента в документе, ближайший первым, и окно, в котором форма встанет.</param>
    /// <param name="DataType">Тип данных, который стоит задать, если привязки компилируются, а его нет; пусто — не знаем.</param>
    /// <param name="Write">Запись: текст привязки, имя другому элементу той же правкой, тип данных.</param>
    internal sealed record Setup(
        IXamlDesignView View,
        XamlElementPath Element,
        string Member,
        XamlMarkupExtensionValue? Original,
        string? DefaultMode,
        IReadOnlyList<ElementChoice> Elements,
        IReadOnlyList<string> Ancestors,
        InspectorChoice? DataType,
        Func<string, (XamlElementPath Path, string Name)?, string?, Task<bool>> Write);

    /// <summary>Элемент формы, к которому можно привязать.</summary>
    /// <param name="Path">Путь.</param>
    /// <param name="Name">Имя; пусто — нет, и он получит <paramref name="NewName"/>.</param>
    /// <param name="Type">Тип, как его пишет документ.</param>
    /// <param name="NewName">Свободное имя для элемента без имени.</param>
    internal sealed record ElementChoice(XamlElementPath Path, string? Name, string Type, string? NewName);

    /// <summary>Сегмент источника.</summary>
    private sealed record SourceSegment(string Title, BindingSourceKind Kind)
    {
        public override string ToString() => Title;
    }
}
