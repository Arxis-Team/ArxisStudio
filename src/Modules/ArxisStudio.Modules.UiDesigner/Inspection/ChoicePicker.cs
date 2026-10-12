using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// Выбор под полем объекта, как ⊙ у Unity: поиск, список вариантов цели и строка о том, почему пусто; поле картинки и
/// поле данных открывают его одинаково.
/// </summary>
/// <remarks>
/// <para>
/// Каретка встаёт в поиск; ↓ уводит её в список, Enter выбирает, Esc закрывает, а щелчок выбирает строку — только
/// строку: щелчок мимо строк выбирал выделенное, а выделено нынешнее значение, и выбор закрывался, переписывая то же
/// значение (запись 396).
/// </para>
/// <para>
/// Варианты отбираются по набранному в подписи и в записи — у картинки это путь в сборке, у класса — его
/// пространство в записи. Пояснение (<see cref="Note"/>) стоит под списком: что значит выбор, а не что выбрано.
/// </para>
/// </remarks>
internal sealed class ChoicePicker
{
    private readonly Action<InspectorChoice> _chosen;
    private readonly AxSearchField _search;
    private readonly AxListBox _list;
    private readonly TextBlock _empty;
    private readonly TextBlock _note;
    private IReadOnlyList<InspectorChoice> _choices = [];
    private string? _current;
    private string _whenEmpty = string.Empty;
    private Func<string, string> _whenNothing = static typed => typed;

    /// <summary>Заводит выбор под полем.</summary>
    /// <param name="anchor">Поле, под которым он открывается и к которому возвращает каретку.</param>
    /// <param name="searchPlaceholder">Заглушка поиска.</param>
    /// <param name="row">Строка варианта.</param>
    /// <param name="chosen">Что делать с выбранным; выбор к этому времени закрыт.</param>
    public ChoicePicker(Control anchor, string searchPlaceholder, Func<InspectorChoice, Control> row, Action<InspectorChoice> chosen)
    {
        _chosen = chosen;

        _search = new AxSearchField { Size = AxControlSize.Compact, PlaceholderText = searchPlaceholder };
        _search.TextChanged += (_, _) => Fill();
        _search.AddHandler(InputElement.KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);

        _list = new AxListBox
        {
            // Ячейку, которую список перерабатывает, шаблон строит и без варианта — пустой.
            ItemTemplate = new FuncDataTemplate<InspectorChoice?>((choice, _) => choice is null ? null : row(choice)),
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        _list.AddHandler(InputElement.KeyDownEvent, OnListKey, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.TappedEvent, OnListTapped);

        _empty = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AxText.SetTone(_empty, AxTextTone.Secondary);

        _note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        AxText.SetTone(_note, AxTextTone.Secondary);
        AxText.SetRole(_note, AxTextRole.Small);

        var picker = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        DockPanel.SetDock(_note, Dock.Bottom);
        picker.Children.Add(_search);
        picker.Children.Add(_empty);
        picker.Children.Add(_note);
        picker.Children.Add(_list);

        Popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Child = picker,
                [!Layoutable.WidthProperty] = new DynamicResourceExtension("AxObjectPickerWidth"),
                [!Layoutable.MaxHeightProperty] = new DynamicResourceExtension("AxObjectPickerMaxHeight"),
                [!Decorator.PaddingProperty] = new DynamicResourceExtension("AxSpaceTightThickness"),
                [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
                [!Border.BackgroundProperty] = new DynamicResourceExtension("AxSurfaceOverlayBrush"),
                [!Border.BorderBrushProperty] = new DynamicResourceExtension("AxStrokeControlBrush"),
                [!Border.BorderThicknessProperty] = new DynamicResourceExtension("AxHairlineThickness"),
                [!Border.CornerRadiusProperty] = new DynamicResourceExtension("AxCornerRadiusLarge"),
                [!Border.BoxShadowProperty] = new DynamicResourceExtension("AxShadowPopup"),
            },
        };
        Popup.Closed += (_, _) => anchor.Focus();
    }

    /// <summary>Всплывающее окно выбора — его ставят рядом с полем.</summary>
    public Popup Popup { get; }

    /// <summary>Выбор открыт.</summary>
    public bool IsOpen => Popup.IsOpen;

    /// <summary>Пояснение под списком; пусто — его нет.</summary>
    public string? Note
    {
        get => _note.Text;
        set
        {
            _note.Text = value;
            _note.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Открывает выбор с кареткой в поиске.</summary>
    /// <param name="choices">Варианты.</param>
    /// <param name="current">Запись нынешнего значения — её строка выбрана; пусто — никакая.</param>
    /// <param name="whenEmpty">Что сказать, если вариантов нет вовсе.</param>
    /// <param name="whenNothing">Что сказать, если набранному не подошёл ни один, — по набранному.</param>
    public void Open(IReadOnlyList<InspectorChoice> choices, string? current, string whenEmpty, Func<string, string> whenNothing)
    {
        _choices = choices;
        _current = current;
        _whenEmpty = whenEmpty;
        _whenNothing = whenNothing;
        _search.Text = string.Empty;
        Fill();
        Popup.IsOpen = true;

        Dispatcher.UIThread.Post(() => _search.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Закрывает выбор.</summary>
    public void Close() => Popup.IsOpen = false;

    /// <summary>Список — варианты, у которых подпись или запись содержит набранное; нынешний выбран.</summary>
    private void Fill()
    {
        var typed = (_search.Text ?? string.Empty).Trim();
        var shown = typed.Length == 0
            ? _choices
            : [.. _choices.Where(choice =>
                (choice.Title ?? string.Empty).Contains(typed, StringComparison.CurrentCultureIgnoreCase) ||
                choice.Value.Contains(typed, StringComparison.CurrentCultureIgnoreCase))];

        _list.ItemsSource = shown;
        _list.SelectedItem = shown.FirstOrDefault(choice => string.Equals(choice.Value, _current, StringComparison.Ordinal));

        _empty.IsVisible = shown.Count == 0;
        _empty.Text = _choices.Count == 0 ? _whenEmpty : _whenNothing(typed);
    }

    /// <summary>Выбранное уходит хозяину, выбор закрывается, и каретка возвращается в поле.</summary>
    private void Choose(InspectorChoice? choice)
    {
        if (choice is null)
            return;

        Popup.IsOpen = false;
        _chosen(choice);
    }

    /// <summary>Щелчок по строке выбирает её; по пустому месту списка и по полосе прокрутки — ничего.</summary>
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: InspectorChoice choice })
            Choose(choice);
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Down && _list.ItemCount > 0)
        {
            _list.SelectedIndex = Math.Max(0, _list.SelectedIndex);
            (_list.ContainerFromIndex(_list.SelectedIndex) as Control ?? _list).Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Choose(_list.SelectedItem as InspectorChoice ?? _list.Items.OfType<InspectorChoice>().FirstOrDefault());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Enter)
        {
            Choose(_list.SelectedItem as InspectorChoice);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Popup.IsOpen = false;
            e.Handled = true;
        }
    }
}
