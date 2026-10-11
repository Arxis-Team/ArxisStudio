using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Inspector.Catalog;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>Что стоит в списке на месте: раздел, строка свойства, заголовок над строкой, раздел плагина.</summary>
/// <param name="Key">Ключ места: по нему список узнаёт ячейку, которая уже стоит.</param>
internal abstract record InspectorEntry(string Key);

/// <summary>Заголовок раздела.</summary>
/// <param name="Key">Ключ.</param>
/// <param name="Category">Раздел.</param>
/// <param name="Expanded">Раскрыт ли.</param>
/// <param name="Foldable">Можно ли свернуть: пока идёт поиск — нельзя, свёрнутое спрятало бы найденное.</param>
/// <param name="Written">Сколько в разделе задано здесь; пусто — цель этого не знает.</param>
internal sealed record GroupEntry(string Key, InspectorCategory Category, bool Expanded, bool Foldable, int? Written)
    : InspectorEntry(Key);

/// <summary>Строка свойства.</summary>
/// <param name="Key">Ключ.</param>
/// <param name="Row">Строка: живёт дольше ячейки.</param>
internal sealed record RowEntry(string Key, InspectorRow Row) : InspectorEntry(Key);

/// <summary>Заголовок над строкой — украшение метаданных, как <c>[Header]</c> у Unity.</summary>
/// <param name="Key">Ключ.</param>
/// <param name="Text">Текст.</param>
internal sealed record CaptionEntry(string Key, string Text) : InspectorEntry(Key);

/// <summary>Раздел плагина в списке.</summary>
/// <param name="Key">Ключ.</param>
/// <param name="Host">Место раздела: живёт дольше ячейки.</param>
internal sealed record SectionEntry(string Key, SectionHost Host) : InspectorEntry(Key);

/// <summary>Ячейка списка инспектора.</summary>
/// <remarks>
/// <para>
/// <b>Ячейка одноразовая.</b> Виртуализирующая панель Avalonia 12 помечает контейнер-элемент при первой
/// раскладке и, убранный из списка и вставленный снова — или переставленный, — больше в дерево его не ставит:
/// элемент получает место, но остаётся без родителя. Поэтому ушедшая ячейка не возвращается, а то, что должно
/// жить дольше, — строка с полем, раздел плагина — переезжает в новую ячейку. Переставлять ячейки список не
/// просит вовсе: переставленная строка — это старая ячейка снята, новая поставлена.
/// </para>
/// <para>
/// Ушедшую с экрана ячейку панель прячет, а не снимает: видимость ячейки и говорит строке, видно ли её.
/// </para>
/// </remarks>
internal interface IInspectorCell
{
    /// <summary>Ключ места.</summary>
    string Slot { get; }

    /// <summary>Годится ли ячейка для места с этим ключом: у строки — та же строка, а не построенная заново.</summary>
    /// <param name="entry">Место.</param>
    bool Fits(InspectorEntry entry);

    /// <summary>Обновляет ячейку на месте: заголовок, счёт, раскрытость.</summary>
    /// <param name="entry">Место.</param>
    void Update(InspectorEntry entry);

    /// <summary>Ячейка ушла из списка: отдаёт то, что живёт дольше неё, и отпускает подписки.</summary>
    void Detach();
}

/// <summary>
/// Ячейка строки свойства: стрелки ходят по ним, Enter и F2 открывают поле, Delete сбрасывает значение, а Esc из
/// поля, которому нечего отменять, возвращает каретку строке.
/// </summary>
/// <remarks>
/// Остановкой Tab ячейка не служит: Tab идёт от поля к полю, как в окне свойств Visual Studio, а по строкам ходят
/// стрелки. Каретка, вставшая в поле щелчком, выбирает и строку: выбранная строка — та, где работают.
/// </remarks>
internal sealed class PropertyCell : AxListBoxItem, IInspectorCell
{
    private readonly IDisposable[] _bindings;

    /// <summary>Ставит строку в ячейку.</summary>
    /// <param name="key">Ключ места.</param>
    /// <param name="row">Строка.</param>
    public PropertyCell(string key, InspectorRow row)
    {
        Slot = key;
        Row = row;

        // Новая ячейка видна, пока панель не спрятала её сама: строка, вернувшаяся в неё из спрятанной, иначе так и
        // считала бы себя невидной и не переспросила бы значения, сменившегося, пока её не было.
        row.Show(true);
        Content = row;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        KeyboardNavigation.SetIsTabStop(this, false);

        // Диктор слышит ячейку, на которой стоит каретка: её имя — имя свойства, пояснение — значение и откуда оно.
        _bindings =
        [
            this.Bind(AutomationProperties.NameProperty, row.GetObservable(AutomationProperties.NameProperty)),
            this.Bind(AutomationProperties.HelpTextProperty, row.GetObservable(AutomationProperties.HelpTextProperty)),
        ];
    }

    /// <inheritdoc/>
    public string Slot { get; }

    /// <summary>Строка.</summary>
    public InspectorRow Row { get; }

    /// <inheritdoc/>
    public bool Fits(InspectorEntry entry) => entry is RowEntry { Row: var row } && ReferenceEquals(row, Row);

    /// <inheritdoc/>
    public void Update(InspectorEntry entry)
    {
    }

    /// <inheritdoc/>
    public void Detach()
    {
        foreach (var binding in _bindings)
            binding.Dispose();

        Content = null;
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBoxItem);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
            Row.Show(IsVisible);
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        if (!ReferenceEquals(e.Source, this))
            IsSelected = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (ReferenceEquals(e.Source, this) && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key is Key.Enter or Key.F2 && Row.Enter())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && Row.CanReset)
            {
                Row.Reset();
                e.Handled = true;
                return;
            }
        }

        // Esc из поля, которому нечего отменять, — к строке: так из поля выходят обратно к списку.
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !ReferenceEquals(e.Source, this))
        {
            Focus(NavigationMethod.Directional);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>
/// Заголовок раздела — полосой компонента, как у Unity: стрелка, значок, имя, счётчик заданного и ⋮ справа. Его не
/// выбирают, стрелки его проходят, а щелчок, пробел и Enter на нём сворачивают раздел.
/// </summary>
/// <remarks>
/// Меню ⋮ — меню раздела; кнопка просит его так же, как правая кнопка и Shift+F10, — запросом контекстного меню, и
/// панель, узнав в источнике заголовок раздела, показывает пункты раздела.
/// </remarks>
internal sealed class GroupCell : AxListBoxItem, IInspectorCell
{
    private readonly Action<string, bool> _fold;
    private readonly AxButton _more;
    private bool _updating;

    /// <summary>Заводит заголовок.</summary>
    /// <param name="entry">Раздел.</param>
    /// <param name="fold">Раздел свернули или раскрыли: идентификатор и свёрнут ли.</param>
    /// <param name="words">Слова панели.</param>
    public GroupCell(GroupEntry entry, Action<string, bool> fold, InspectorWords words)
    {
        Slot = entry.Key;
        CategoryId = entry.Category.Id;
        Words = words;
        _fold = fold;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        // Полоса раздела — во всю ширину и во всю высоту строки, как у Unity: поля строки ей не нужны, а полоса ниже
        // шага оставляла бы просветы между разделом и его строками (стиль .group вида).
        Classes.Add("group");

        var menu = words["inspector.sectionMenu"];

        _more = new AxButton
        {
            Appearance = AxButtonAppearance.Toolbar,
            Size = AxControlSize.Compact,
            Content = new AxIcon { Data = AxIcons.MoreVertical },
            [AutomationProperties.NameProperty] = menu,
            [ToolTip.TipProperty] = menu,
        };
        _more.Click += OnMoreClick;

        Header = new AxGroupHeader { Appearance = AxGroupHeaderAppearance.Band, Actions = _more };
        Header.PropertyChanged += OnHeaderChanged;
        Content = Header;
        Update(entry);
    }

    /// <summary>Кнопка ⋮ раздела.</summary>
    public AxButton More => _more;

    /// <inheritdoc/>
    public string Slot { get; }

    /// <summary>Раздел.</summary>
    public string CategoryId { get; private set; }

    /// <summary>Сам заголовок: на нём встаёт каретка, когда раздел свернули с клавиатуры.</summary>
    public AxGroupHeader Header { get; }

    private InspectorWords Words { get; }

    /// <inheritdoc/>
    public bool Fits(InspectorEntry entry) => entry is GroupEntry;

    /// <inheritdoc/>
    public void Update(InspectorEntry entry)
    {
        var group = (GroupEntry)entry;

        _updating = true;

        try
        {
            CategoryId = group.Category.Id;
            Header.Content = group.Category.Title;
            Header.Icon = group.Category.Icon;
            Header.IsCollapsible = group.Foldable;
            Header.IsExpanded = group.Expanded;
            Header.Counter = group.Written is > 0 ? group.Written : null;
            ToolTip.SetTip(Header, group.Written is > 0 ? Words.Format("inspector.writtenCount", group.Written) : null);
            AutomationProperties.SetName(this, group.Category.Title);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <inheritdoc/>
    public void Detach()
    {
        Header.PropertyChanged -= OnHeaderChanged;
        _more.Click -= OnMoreClick;
        Content = null;
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBoxItem);

    /// <summary>⋮ просит меню раздела тем же запросом, что правая кнопка: показывает его панель.</summary>
    private void OnMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _more.RaiseEvent(new ContextRequestedEventArgs());

    /// <inheritdoc/>
    /// <remarks>Заголовок не выбирается и мышью: нажатие сворачивает раздел, а не ставит на него выбор.</remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
    }

    private void OnHeaderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_updating && e.Property == AxGroupHeader.IsExpandedProperty)
            _fold(CategoryId, !Header.IsExpanded);
    }
}

/// <summary>Заголовок над строкой: его не выбирают и стрелки его проходят.</summary>
internal sealed class CaptionCell : AxListBoxItem, IInspectorCell
{
    private readonly TextBlock _text;

    /// <summary>Заводит заголовок.</summary>
    /// <param name="entry">Место.</param>
    public CaptionCell(CaptionEntry entry)
    {
        Slot = entry.Key;
        Focusable = false;
        _text = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            [AxText.RoleProperty] = AxTextRole.Section,
            [AxText.ToneProperty] = AxTextTone.Secondary,
            [!MarginProperty] = new DynamicResourceExtension("AxSpaceTightAboveThickness"),
        };
        Content = _text;
        Update(entry);
    }

    /// <inheritdoc/>
    public string Slot { get; }

    /// <inheritdoc/>
    public bool Fits(InspectorEntry entry) => entry is CaptionEntry;

    /// <inheritdoc/>
    public void Update(InspectorEntry entry)
    {
        _text.Text = ((CaptionEntry)entry).Text;
        AutomationProperties.SetName(this, _text.Text);
    }

    /// <inheritdoc/>
    public void Detach() => Content = null;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBoxItem);

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
    }
}

/// <summary>Ячейка раздела плагина: сама не выбирается, а в раздел идут Tab — к его полям.</summary>
internal sealed class SectionCell : AxListBoxItem, IInspectorCell
{
    /// <summary>Ставит раздел в ячейку.</summary>
    /// <param name="entry">Место.</param>
    public SectionCell(SectionEntry entry)
    {
        Slot = entry.Key;
        Host = entry.Host;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = Host;
        AutomationProperties.SetName(this, Host.Title);
    }

    /// <inheritdoc/>
    public string Slot { get; }

    /// <summary>Место раздела.</summary>
    public SectionHost Host { get; }

    /// <inheritdoc/>
    public bool Fits(InspectorEntry entry) => entry is SectionEntry { Host: var host } && ReferenceEquals(host, Host);

    /// <inheritdoc/>
    public void Update(InspectorEntry entry)
    {
    }

    /// <inheritdoc/>
    public void Detach() => Content = null;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBoxItem);

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
    }
}
