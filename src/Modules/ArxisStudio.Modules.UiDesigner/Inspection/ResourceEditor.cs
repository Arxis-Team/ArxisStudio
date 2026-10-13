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

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Панель ресурса — содержимое панели выражения под строкой инспектора: ресурсы, которые элемент может назвать, с
/// образцами и значениями, поиск, «только подходящие» и вид ссылки, а снизу — запись и где ресурс объявлен.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ресурсы — у службы, по именам</b> (<see cref="IXamlDesignView.GetResourcesAsync"/>): свои и предков в документе,
/// приложения формы и его темы, каждый ключ один раз, ближайший первым. Студийных среди них нет: форма в дизайнере стоит
/// внутри студии, и ключ, найденный там, в приложении не найдётся.
/// </para>
/// <para>
/// <b>Вид ссылки</b> по умолчанию — динамическая у кисти, цвета и ключа одной темы: они меняются с темой, — и
/// статическая у остального. Статическая не предлагается там, где ничего не найдёт: ключ одной темы вне окна и
/// <c>ThemeVariantScope</c> (проба Avalonia 12.1.1, запись 427); динамическая — у свойства CLR: она привязка, а привязку
/// ставят только на свойство Avalonia.
/// </para>
/// </remarks>
internal sealed class ResourceEditor : InspectorExpressionEditor
{
    private readonly Setup _setup;
    private readonly InspectorExpressionContext _context;
    private readonly IStudioStrings _strings;
    private readonly Control _view;
    private readonly AxSegmentedControl _scope;
    private readonly AxSearchField _search;
    private readonly AxListBox _list;
    private readonly TextBlock _empty;
    private readonly AxCheckBox _fitsOnly;
    private readonly AxSegmentedControl _kind;
    private readonly AxSegmentItem _dynamicItem;
    private readonly AxSegmentItem _staticItem;
    private readonly TextBlock _written;
    private readonly AxIcon _stateIcon;
    private readonly TextBlock _state;
    private readonly Control _stateRow;
    private IReadOnlyList<XamlResourceInfo> _resources = [];
    private bool _dynamic;
    private bool _kindChosen;
    private bool _filling;
    private bool _loaded;
    private bool _released;

    /// <summary>Заводит панель ресурса.</summary>
    /// <param name="setup">Что панели знать о форме, элементе и члене.</param>
    /// <param name="context">Дорога к панели выражения.</param>
    /// <param name="strings">Словари модуля.</param>
    public ResourceEditor(Setup setup, InspectorExpressionContext context, IStudioStrings strings)
    {
        _setup = setup;
        _context = context;
        _strings = strings;
        _dynamic = setup.WrittenDynamic ?? false;
        _kindChosen = setup.WrittenDynamic is not null;

        _scope = new AxSegmentedControl
        {
            Items =
            {
                Segment(strings["inspector.resource.scope.all"]),
                Segment(strings["inspector.resource.scope.form"]),
                Segment(strings["inspector.resource.scope.application"]),
                Segment(strings["inspector.resource.scope.theme"]),
            },
            SelectedIndex = 0,
            [AutomationProperties.NameProperty] = strings["inspector.resource.scope"],
        };
        _scope.SelectionChanged += (_, _) => Fill();
        _scope.AttachedToVisualTree += (_, _) => Fit(_scope);

        _search = new AxSearchField
        {
            Size = AxControlSize.Compact,
            PlaceholderText = strings["inspector.resource.search"],
            [AutomationProperties.NameProperty] = strings["inspector.resource.search"],
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        _search.TextChanged += (_, _) => Fill();
        _search.AddHandler(InputElement.KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);

        _list = new AxListBox
        {
            ItemTemplate = new FuncDataTemplate<XamlResourceInfo?>((resource, _) => resource is null ? null : Row(resource)),
            [!Layoutable.MaxHeightProperty] = new DynamicResourceExtension("AxExpressionMembersMaxHeight"),
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
            [AutomationProperties.NameProperty] = strings["inspector.resource.list"],
        };
        _list.SelectionChanged += (_, _) => Refresh();
        _list.DoubleTapped += (_, e) =>
        {
            if (Selected() is not null)
            {
                _context.Commit();
                e.Handled = true;
            }
        };

        _empty = new TextBlock { Text = strings["inspector.resource.none"], IsVisible = false, TextWrapping = TextWrapping.Wrap };
        AxText.SetTone(_empty, AxTextTone.Secondary);
        AxText.SetRole(_empty, AxTextRole.Small);

        _fitsOnly = new AxCheckBox { Content = strings["inspector.resource.fitsOnly"], IsChecked = true };
        _fitsOnly.IsCheckedChanged += (_, _) => Fill();

        _dynamicItem = Segment(strings["inspector.resource.dynamic"]);
        _staticItem = Segment(strings["inspector.resource.static"]);
        _kind = new AxSegmentedControl
        {
            Items = { _dynamicItem, _staticItem },
            [AutomationProperties.NameProperty] = strings["inspector.resource.kind"],
        };
        _kind.SelectionChanged += (_, _) => OnKindChanged();
        _kind.AttachedToVisualTree += (_, _) => Fit(_kind);

        _written = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AxText.SetRole(_written, AxTextRole.Small);

        _stateIcon = new AxIcon { Size = AxIconSize.Small, VerticalAlignment = VerticalAlignment.Top };
        _stateIcon[!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness");
        DockPanel.SetDock(_stateIcon, Dock.Left);
        _state = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AxText.SetTone(_state, AxTextTone.Secondary);
        AxText.SetRole(_state, AxTextRole.Small);
        _stateRow = new DockPanel { Children = { _stateIcon, _state }, IsVisible = false };

        _view = new StackPanel
        {
            Children =
            {
                PropertyRow(strings["inspector.resource.scope"], _scope),
                _search,
                _list,
                _empty,
                _fitsOnly,
                PropertyRow(strings["inspector.resource.kind"], _kind),
                Spaced(_written),
                Spaced(_stateRow),
            },
        };

        ShowKind();
        Unchosen();
        _ = LoadAsync();
    }

    /// <inheritdoc/>
    public override string Title => string.Format(CultureInfo.CurrentCulture, _strings["inspector.resource.title"], _setup.Member);

    /// <inheritdoc/>
    public override Control View => _view;

    /// <inheritdoc/>
    public override Control? FocusTarget => _search;

    /// <summary>Поиск — тестам.</summary>
    internal AxSearchField SearchField => _search;

    /// <summary>Откуда — тестам.</summary>
    internal AxSegmentedControl ScopeField => _scope;

    /// <summary>«Только подходящие» — тестам.</summary>
    internal AxCheckBox FitsOnlyField => _fitsOnly;

    /// <summary>Список ресурсов — тестам.</summary>
    internal AxListBox List => _list;

    /// <summary>Вид ссылки — тестам.</summary>
    internal AxSegmentedControl KindField => _kind;

    /// <summary>Что запишется сейчас — тестам.</summary>
    internal string Written => _written.Text ?? string.Empty;

    /// <summary>Что сказано о выбранном — тестам.</summary>
    internal string State => _stateRow.IsVisible ? _state.Text ?? string.Empty : string.Empty;

    /// <summary>Список ответила служба — тестам.</summary>
    internal bool Loaded => _loaded;

    /// <summary>Можно ли сейчас выбрать статическую ссылку — тестам.</summary>
    internal bool StaticAllowed => _staticItem.IsEnabled;

    /// <inheritdoc/>
    public override Task<bool> CommitAsync() =>
        !_released && Selected() is { } resource && Text(resource) is { } text ? _setup.Write(text) : Task.FromResult(false);

    /// <inheritdoc/>
    public override void Release() => _released = true;

    /// <summary>Спрашивает службу о ресурсах элемента.</summary>
    private async Task LoadAsync()
    {
        IReadOnlyList<XamlResourceInfo> resources;

        try
        {
            resources = await _setup.View.GetResourcesAsync(_setup.Element, _setup.Member).ConfigureAwait(true);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            resources = [];
        }

        if (_released)
            return;

        _resources = resources;
        _loaded = true;
        Fill();
    }

    /// <summary>Список — ресурсы, отобранные «откуда», поиском и правилом «только подходящие»; выбранный остаётся.</summary>
    private void Fill()
    {
        if (!_loaded)
            return;

        var query = (_search.Text ?? string.Empty).Trim();
        var fitsOnly = _fitsOnly.IsChecked == true;
        var scope = _scope.SelectedIndex;
        var shown = _resources
            .Where(resource => scope switch
            {
                1 => resource.Scope is XamlResourceScope.Element or XamlResourceScope.Ancestor,
                2 => resource.Scope == XamlResourceScope.Application,
                3 => resource.Scope == XamlResourceScope.Theme,
                _ => true,
            })
            .Where(resource => query.Length == 0 || resource.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(resource => !fitsOnly || resource.Fits || string.Equals(resource.Key, _setup.WrittenKey, StringComparison.Ordinal))
            .ToList();

        // Выбранное остаётся выбранным, а при первом показе выбран написанный ключ: панель открыли, чтобы его поменять.
        var keep = Selected()?.Key ?? _setup.WrittenKey;

        _filling = true;

        try
        {
            _list.ItemsSource = shown;
            _list.SelectedItem = shown.FirstOrDefault(resource => string.Equals(resource.Key, keep, StringComparison.Ordinal)) ??
                                 (query.Length > 0 ? shown.FirstOrDefault() : null);
            _list.IsVisible = shown.Count > 0;
            _empty.IsVisible = shown.Count == 0;
        }
        finally
        {
            _filling = false;
        }

        if (_list.SelectedItem is { } selected)
            _list.ScrollIntoView(selected);

        Refresh();
    }

    /// <summary>Выбранный ресурс сменился: вид ссылки по умолчанию, запись и что о нём сказать.</summary>
    private void Refresh()
    {
        if (_filling)
            return;

        var resource = Selected();

        if (resource is not null && !_kindChosen)
            _dynamic = resource.IsThemed || IsBrush(resource);

        // Где одна из ссылок ничего не найдёт или не встанет, выбора нет.
        if (!_setup.DynamicAllowed)
            _dynamic = false;
        else if (resource is { StaticReachable: false })
            _dynamic = true;

        ShowKind();

        if (resource is null)
        {
            Unchosen();
            return;
        }

        if (Text(resource) is not { } text)
        {
            ShowWritten(null);
            ShowState(null, null);
            _context.Report(InspectorCheck.Invalid(_strings["inspector.binding.quotes"]));
            return;
        }

        ShowWritten(text);

        var where = Describe(resource);

        if (resource.IsThemed && !_dynamic)
            ShowState(AxIcons.Warning, where + Environment.NewLine + _strings["inspector.resource.frozen"]);
        else
            ShowState(AxIcons.Success, where);

        _context.Report(InspectorCheck.Valid);
    }

    /// <summary>
    /// Ресурс не выбран: записать нечего, но это не ошибка — подсказка нейтральным тоном, а не причиной под панелью.
    /// </summary>
    private void Unchosen()
    {
        ShowWritten(null);
        ShowState(null, _list.IsVisible || !_loaded ? _strings["inspector.resource.choose"] : null);

        // Пустая причина — сказать нечего: «Применить» выключено, а заметки под панелью нет.
        _context.Report(InspectorCheck.Invalid(string.Empty));
    }

    /// <summary>
    /// Вид ссылки в сегментах: выбранный, и что из двух недоступно — с подсказкой почему. Пока ресурс не выбран и вид
    /// не назван — ни написанной ссылкой, ни человеком, — он не выбран: умолчание зависит от ресурса.
    /// </summary>
    private void ShowKind()
    {
        var resource = Selected();

        _dynamicItem.IsEnabled = _setup.DynamicAllowed;
        _staticItem.IsEnabled = resource is not { StaticReachable: false };
        ToolTip.SetTip(_dynamicItem, _setup.DynamicAllowed ? null : _strings["inspector.resource.dynamicUnavailable"]);
        ToolTip.SetTip(_staticItem, _staticItem.IsEnabled ? null : _strings["inspector.resource.staticUnreachable"]);

        _filling = true;

        try
        {
            _kind.SelectedItem = resource is null && !_kindChosen ? null : _dynamic ? _dynamicItem : _staticItem;
        }
        finally
        {
            _filling = false;
        }
    }

    private void OnKindChanged()
    {
        if (_filling)
            return;

        _dynamic = ReferenceEquals(_kind.SelectedItem, _dynamicItem);
        _kindChosen = true;
        Refresh();
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key != Key.Down || _list.ItemCount == 0)
            return;

        _list.SelectedIndex = Math.Max(0, _list.SelectedIndex);
        (_list.ContainerFromIndex(_list.SelectedIndex) as Control ?? _list).Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private XamlResourceInfo? Selected() => _list.SelectedItem as XamlResourceInfo;

    private string? Text(XamlResourceInfo resource) => Reference(resource.Key, _dynamic);

    /// <summary>Ссылка на ресурс текстом: ключ, который голый аргумент прочёл бы иначе, — в кавычках; null — не записать.</summary>
    /// <param name="key">Ключ.</param>
    /// <param name="dynamic">Динамическая ссылка; иначе статическая.</param>
    internal static string? Reference(string key, bool dynamic)
    {
        try
        {
            var reference = (XamlMarkupExtensionValue)XamlValue.Parse(dynamic ? "{DynamicResource}" : "{StaticResource}");

            return reference.WithArgument(null, new XamlLiteralValue(key)).ToXamlText();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Где объявлен ресурс, его тип и значение: «Ресурс приложения · SolidColorBrush · #FF2563EB».</summary>
    private string Describe(XamlResourceInfo resource)
    {
        var where = resource.Scope switch
        {
            XamlResourceScope.Element => _strings["inspector.resource.where.element"],
            XamlResourceScope.Ancestor => _strings["inspector.resource.where.ancestor"],
            XamlResourceScope.Application => _strings["inspector.resource.where.application"],
            _ => _strings["inspector.resource.where.theme"],
        };

        return string.Join(" · ", new[] { where, resource.TypeName, resource.ValueText }.Where(static part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>Строка ресурса: образец, если он есть, ключ, значение, а у ключа одной темы — её знак.</summary>
    private Control Row(XamlResourceInfo resource)
    {
        var swatch = new AxColorSwatch
        {
            VerticalAlignment = VerticalAlignment.Center,
            [!Layoutable.WidthProperty] = new DynamicResourceExtension("AxIconSize"),
            [!Layoutable.HeightProperty] = new DynamicResourceExtension("AxIconSize"),
        };

        switch (resource.Sample)
        {
            case Color color:
                swatch.Color = color;
                break;
            case IBrush brush:
                swatch.Brush = brush;
                break;
            default:
                swatch.Opacity = 0;
                break;
        }

        // Место образца есть у каждой строки: ключи стоят столбцом, а образцы — у тех, у кого они есть.
        var mark = new Border { Child = swatch, [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness") };

        DockPanel.SetDock(mark, Dock.Left);

        var themed = new AxIcon
        {
            Data = AxIcons.ThemeDark,
            Size = AxIconSize.Small,
            IsVisible = resource.IsThemed,
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = _strings["inspector.resource.themed"],
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
        };

        DockPanel.SetDock(themed, Dock.Right);

        var value = new TextBlock
        {
            Text = resource.ValueText ?? resource.TypeName,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
        };

        AxText.SetTone(value, AxTextTone.Secondary);
        AxText.SetRole(value, AxTextRole.Small);
        DockPanel.SetDock(value, Dock.Right);

        var key = new TextBlock { Text = resource.Key, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        if (!resource.Fits)
            AxText.SetTone(key, AxTextTone.Secondary);

        var row = new DockPanel { Children = { mark, themed, value, key } };

        ToolTip.SetTip(row, Describe(resource));
        AutomationProperties.SetName(row, $"{resource.Key}, {Describe(resource)}");

        return row;
    }

    /// <summary>Что запишется; пустое строки не занимает.</summary>
    private void ShowWritten(string? text)
    {
        _written.Text = text;
        _written.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void ShowState(Geometry? icon, string? text)
    {
        _stateRow.IsVisible = text is not null;
        _state.Text = text;
        _stateIcon.Data = icon;
        _stateIcon.IsVisible = icon is not null;
    }

    /// <summary>Кисть или цвет: такие ресурсы меняют с темой, и по умолчанию их называют динамически.</summary>
    private static bool IsBrush(XamlResourceInfo resource) =>
        resource.Sample is Color or IBrush || resource.TypeName is { } type && (type.EndsWith("Brush", StringComparison.Ordinal) || type == "Color");

    private static AxSegmentItem Segment(string title) => new()
    {
        Content = title,
        [AutomationProperties.NameProperty] = title,
    };

    /// <summary>Наименьшая ширина сегментов — та, что им нужна: по ней строка уводит их под подпись, когда в ряд тесно.</summary>
    private static void Fit(AxSegmentedControl segments)
    {
        segments.ClearValue(Layoutable.MinWidthProperty);
        segments.Measure(Size.Infinity);
        segments.MinWidth = Math.Ceiling(segments.DesiredSize.Width);
    }

    /// <summary>Строка панели — строка свойства, как в инспекторе: тесно — поле уходит под подпись.</summary>
    private static Control PropertyRow(string label, Control field) => new AxPropertyRow
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

    /// <summary>Что панели знать о форме, элементе и члене.</summary>
    /// <param name="View">Показ формы.</param>
    /// <param name="Element">Путь элемента; у нескольких выбранных — первого.</param>
    /// <param name="Member">Член.</param>
    /// <param name="WrittenKey">Ключ, который член уже называет; пусто — ресурса нет.</param>
    /// <param name="WrittenDynamic">Написанная ссылка динамическая; пусто — ресурса нет.</param>
    /// <param name="DynamicAllowed">Динамическая ссылка встанет: член — свойство Avalonia.</param>
    /// <param name="Write">Запись: текст ссылки.</param>
    internal sealed record Setup(
        IXamlDesignView View,
        XamlElementPath Element,
        string Member,
        string? WrittenKey,
        bool? WrittenDynamic,
        bool DynamicAllowed,
        Func<string, Task<bool>> Write);
}
