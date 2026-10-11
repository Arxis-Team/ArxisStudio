using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Панель инспектора: показанная цель строками свойств, полоса отбора, шапка и пустые состояния.
/// </summary>
/// <remarks>
/// <para>
/// Панель идёт за службой (<see cref="IStudioInspector.CurrentChanged"/>): показана другая цель — строки того же
/// типа остаются со своими полями, кареткой и набранным, остальные уходят. Каретки панель не берёт никогда: показ
/// — не переход, и выбор на холсте оставляет каретку на холсте.
/// </para>
/// <para>
/// <b>Клавиатура.</b> Стрелки, Home, End, PageUp и PageDown ходят по строкам; Enter и F2 открывают поле, Enter в
/// поле пишет и Esc возвращает показанное, а второй Esc — к строке; Delete сбрасывает; Ctrl со стрелкой влево и
/// вправо сворачивает и раскрывает раздел строки; Ctrl+F — к поиску. Ctrl+Z и Ctrl+Y отменяют набранное в поле, а
/// без набранного — шаг истории того, кто показал цель: правка из инспектора отменяется там же, где сделана.
/// </para>
/// </remarks>
[ToolWindow(InspectorModule.PanelId)]
public sealed class InspectorPanel : ToolWindow
{
    private StudioInspector? _service;
    private InspectorView? _view;
    private InspectorList? _list;
    private InspectorMenus? _menus;
    private Catalog.InspectorCatalog? _catalog;
    private bool _syncing;
    private bool _showingToggle;

    /// <summary>Разметка — тестам.</summary>
    internal InspectorView? View => _view;

    /// <summary>Список — тестам.</summary>
    internal InspectorList? List => _list;

    /// <summary>Меню — тестам: пункты собираются той же дорогой, какой их показывает панель.</summary>
    internal InspectorMenus? Menus => _menus;

    /// <inheritdoc/>
    /// <remarks>Каретка — у списка: выбранная строка получает её сама.</remarks>
    public override Control? FocusTarget => _view?.List;

    /// <inheritdoc/>
    protected override Control Build()
    {
        var view = new InspectorView();
        var words = new InspectorWords(Context.Strings);

        _view = view;
        _service = Context.Inspector() as StudioInspector;

        if (_service?.Catalog is not { } catalog)
        {
            // Тип службы занят чужой реализацией: показывать в этой панели нечего, и она говорит почему.
            ShowEmpty(words["inspector.unavailable"], null, unlock: false);
            view.Header.IsVisible = false;
            return view;
        }

        _catalog = catalog;
        _list = new InspectorList(view.List, _service, catalog, words);
        _menus = new InspectorMenus(_list, catalog, view);

        _service.CurrentChanged += OnCurrentChanged;
        catalog.Changed += OnCatalogChanged;
        _list.Rebuilt += OnRebuilt;
        _list.HeaderChanged += OnHeaderChanged;

        view.Query.PropertyChanged += OnQueryChanged;
        view.Alphabetical.IsCheckedChanged += OnFilterChanged;
        view.Written.IsCheckedChanged += OnFilterChanged;
        view.All.IsCheckedChanged += OnFilterChanged;
        view.Lock.IsCheckedChanged += OnLockChanged;
        view.Options.Click += OnOptionsClick;
        view.Unlock.Click += OnUnlockClick;
        view.Enabled.IsCheckedChanged += OnEnabledChanged;
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.List.ContextRequested += OnContextRequested;
        view.HeaderRows.ContextRequested += OnContextRequested;
        view.HeaderName.ContextRequested += OnContextRequested;

        _list.Follow();
        ShowState();

        return view;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        if (_service is { } service)
            service.CurrentChanged -= OnCurrentChanged;

        if (_catalog is { } catalog)
            catalog.Changed -= OnCatalogChanged;

        if (_list is { } list)
        {
            list.Rebuilt -= OnRebuilt;
            list.HeaderChanged -= OnHeaderChanged;
            list.Release();
        }

        if (_view is { } view)
        {
            view.Query.PropertyChanged -= OnQueryChanged;
            view.Alphabetical.IsCheckedChanged -= OnFilterChanged;
            view.Written.IsCheckedChanged -= OnFilterChanged;
            view.All.IsCheckedChanged -= OnFilterChanged;
            view.Lock.IsCheckedChanged -= OnLockChanged;
            view.Options.Click -= OnOptionsClick;
            view.Unlock.Click -= OnUnlockClick;
            view.Enabled.IsCheckedChanged -= OnEnabledChanged;
            view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            view.List.ContextRequested -= OnContextRequested;
            view.HeaderRows.ContextRequested -= OnContextRequested;
            view.HeaderName.ContextRequested -= OnContextRequested;
            view.HeaderRows.Children.Clear();
            view.HeaderName.Child = null;
        }

        _service = null;
        _catalog = null;
        _list = null;
        _menus = null;
        _view = null;
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        _list?.Follow();
        ShowState();
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => _list?.Reset();

    private void OnRebuilt(object? sender, EventArgs e) => ShowState();

    private void OnHeaderChanged(object? sender, EventArgs e) => ShowHeader();

    private void OnQueryChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && _list is { } list && _view is { } view)
            list.Query = view.Query.Text ?? string.Empty;
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (_list is not { } list || _view is not { } view)
            return;

        list.Alphabetical = view.Alphabetical.IsChecked == true;
        list.WrittenOnly = view.Written.IsChecked == true;
        list.ShowAll = view.All.IsChecked == true;
    }

    private void OnLockChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncing || _service is not { } service || _view is not { } view)
            return;

        service.Lock(view.Lock.IsChecked == true);
    }

    private void OnUnlockClick(object? sender, RoutedEventArgs e) => _service?.Lock(false);

    /// <summary>Флажок «включён» в шапке: щелчок пишет значение всем показанным — у разных тоже одно.</summary>
    private void OnEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_showingToggle || _list is not { } list || list.HeaderToggle is not { } toggle || _view?.Enabled.IsChecked is not { } on)
            return;

        _ = list.WriteAsync([new InspectorWrite(toggle.Id, on ? "True" : "False")], null);
    }

    private void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (_menus is { } menus && _view is { } view)
            InspectorMenus.Show(view.Options, menus.ForTarget(), atPointer: false);
    }

    /// <summary>Шапка, пустые состояния и полоса — по тому, что показано.</summary>
    private void ShowState()
    {
        if (_view is not { } view || _service is not { } service || _list is not { } list)
            return;

        _syncing = true;

        try
        {
            view.Lock.IsChecked = service.IsLocked;
            view.LockIcon.Data = service.IsLocked ? AxIcons.Lock : AxIcons.Unlock;
        }
        finally
        {
            _syncing = false;
        }

        ShowHeader();

        if (list.Target is null)
        {
            if (service.IsLocked)
                ShowEmpty(list.Words["inspector.closed"], null, unlock: true);
            else
                ShowEmpty(list.Words["inspector.empty"], list.Words["inspector.empty.hint"], unlock: false);

            return;
        }

        if (list.Items.Count == 0)
            ShowEmpty(list.Words["inspector.nothing"], null, unlock: false);
        else
            view.Empty.IsVisible = false;
    }

    private void ShowEmpty(string title, string? hint, bool unlock)
    {
        if (_view is not { } view)
            return;

        view.EmptyTitle.Text = title;
        view.EmptyHint.Text = hint;
        view.EmptyHint.IsVisible = hint is not null;
        view.Unlock.IsVisible = unlock;
        view.Empty.IsVisible = true;
    }

    /// <summary>
    /// Шапка: флажок «включён», значок, имя, тип, строки имени и жетонов и разделы плагинов, что встают в шапку; ниже —
    /// заметка источника.
    /// </summary>
    private void ShowHeader()
    {
        if (_view is not { } view || _list is not { } list)
            return;

        var header = list.Target is null ? null : list.Header();

        // Поле имени — на месте заголовка, как имя объекта у Unity; тогда что это за объект, говорит подзаголовок.
        var name = list.HeaderRows.FirstOrDefault(static row => row.IsHeadline);
        var subtitle = name is null ? header?.Subtitle : Joined(TypeName(list.Type), header?.Subtitle);

        view.Header.IsVisible = header is not null;
        view.HeaderDivider.IsVisible = header is not null;
        view.Title.Text = header?.Title;
        view.Title.IsVisible = name is null;
        view.Subtitle.Text = subtitle;
        view.Subtitle.IsVisible = !string.IsNullOrEmpty(subtitle);
        view.HeaderIcon.Data = header?.Icon;
        view.HeaderIcon.IsVisible = header?.Icon is not null;
        view.NoticeText.Text = header?.Notice;
        view.Notice.IsVisible = !string.IsNullOrEmpty(header?.Notice);

        ShowToggle(list);

        // Строка не стоит в двух местах разом: прежняя уходит из своего места раньше, чем новые встают.
        if (!ReferenceEquals(view.HeaderName.Child, name))
            view.HeaderName.Child = null;

        var rows = view.HeaderRows.Children;
        var below = list.HeaderRows.Where(static row => !row.IsHeadline).ToArray();

        if (!rows.SequenceEqual(below))
        {
            rows.Clear();
            rows.AddRange(below);
        }

        view.HeaderName.Child = name;
        view.HeaderName.IsVisible = name is not null;
        view.HeaderRows.IsVisible = rows.Count > 0;

        var sections = view.HeaderSections.Children;

        if (!sections.SequenceEqual(list.HeaderSections))
        {
            sections.Clear();
            sections.AddRange(list.HeaderSections);
        }

        view.HeaderSections.IsVisible = sections.Count > 0;
    }

    /// <summary>Короткое имя типа цели: <c>Button</c> у <c>Avalonia.Controls.Button</c>.</summary>
    private static string? TypeName(InspectorTypeInfo? type)
    {
        if (type?.FullName is not { Length: > 0 } full)
            return null;

        var name = full[(full.LastIndexOf('.') + 1)..];
        var generic = name.IndexOf('`', StringComparison.Ordinal);

        return generic < 0 ? name : name[..generic];
    }

    /// <summary>Части подзаголовка через точку; пустые — мимо.</summary>
    private static string Joined(params string?[] parts) =>
        string.Join(" · ", parts.Where(static part => !string.IsNullOrEmpty(part)));

    /// <summary>Флажок «включён»: значение свойства шапки, а у разных — третье состояние.</summary>
    private void ShowToggle(InspectorList list)
    {
        if (_view is not { } view)
            return;

        var toggle = list.HeaderToggle;

        view.Enabled.IsVisible = toggle is not null;

        if (toggle is null)
            return;

        var value = list.ValueOf(toggle.Id);

        _showingToggle = true;

        try
        {
            view.Enabled.IsThreeState = value.IsMixed;
            view.Enabled.IsChecked = value.IsMixed ? null : bool.TryParse(value.Text, out var on) && on;
            view.Enabled.IsEnabled = !toggle.IsReadOnly;
            AutomationProperties.SetName(view.Enabled, toggle.Label);
            ToolTip.SetTip(view.Enabled, toggle.Label);
        }
        finally
        {
            _showingToggle = false;
        }
    }

    /// <summary>
    /// Ctrl+F — к поиску; Ctrl+Z и Ctrl+Y — набранному в поле, а без него — истории хозяина цели; Ctrl со стрелкой —
    /// свернуть и раскрыть раздел строки.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view is not { } view || _list is not { } list)
            return;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            view.Query.Focus(NavigationMethod.Tab);
            view.Query.SelectAll();
            e.Handled = true;
            return;
        }

        // Остальное — клавиши списка и строк шапки: в поиске свои Ctrl+Z и стрелки.
        if (e.Source is not Visual source ||
            !view.List.IsVisualAncestorOf(source) && !ReferenceEquals(source, view.List) && !view.HeaderRows.IsVisualAncestorOf(source) &&
            !view.HeaderName.IsVisualAncestorOf(source))
        {
            return;
        }

        if (view.GetPlatformSettings()?.HotkeyConfiguration is { } keys)
        {
            var back = keys.Undo.Any(gesture => gesture.Matches(e));

            if (back || keys.Redo.Any(gesture => gesture.Matches(e)))
            {
                // Набранное в поле отменяет само поле: это ещё не правка, и истории о нём нечего сказать.
                if (RowOf(source) is { HasDraft: true })
                    return;

                e.Handled = true;
                _ = list.StepAsync(back);
                return;
            }
        }

        // На строке и заголовке, а не в поле: в поле Ctrl со стрелкой ходит по словам.
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.Control && !list.Alphabetical && !list.IsSearching &&
            source is PropertyCell or GroupCell or AxGroupHeader)
        {
            var category = RowOf(source) is { } row
                ? list.CategoryOf(row.Property)
                : source.FindAncestorOfType<GroupCell>(includeSelf: true)?.CategoryId;

            if (category is null)
                return;

            e.Handled = true;

            var fold = e.Key == Key.Left;

            list.Fold(category, fold);

            // Свёрнутый раздел унёс строку с кареткой: она встаёт на его заголовок — с него раздел и раскрывают.
            if (fold)
                list.GroupOf(category)?.Header.Focus(NavigationMethod.Directional);
        }
    }

    /// <summary>Меню строки — под указателем или у строки, если его просили клавишей; мимо строк — меню ⋮.</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_menus is not { } menus || _view is not { } view)
            return;

        var atPointer = e.TryGetPosition(view.List, out _);

        if (e.Source is Visual source && RowOf(source) is { } row)
        {
            var cell = source.FindAncestorOfType<PropertyCell>(includeSelf: true);

            if (cell is not null)
                cell.IsSelected = true;

            InspectorMenus.Show((Control?)cell ?? row, menus.ForRow(row), atPointer);
        }
        else
        {
            InspectorMenus.Show(view.List, menus.ForTarget(), atPointer);
        }

        e.Handled = true;
    }

    /// <summary>Строка свойства, которой принадлежит элемент.</summary>
    private static InspectorRow? RowOf(Visual source) =>
        source.FindAncestorOfType<InspectorRow>(includeSelf: true)
        ?? (source as PropertyCell)?.Row
        ?? source.FindAncestorOfType<PropertyCell>()?.Row;
}
