using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace ArxisStudio.Docking;

/// <summary>
/// Группа вкладок на экране: полоса вкладок и содержимое выбранной.
/// </summary>
/// <remarks>
/// Вкладка есть и у одинокой панели. В прежней оболочке для одного понятия было
/// два устройства — боковая панель с заголовком и нижняя со вкладками, — и
/// перетаскивать одно в другое было нечем. Здесь устройство одно, и взяться
/// мышью есть за что в любом случае.
/// </remarks>
public class DockGroupView : TemplatedControl
{
    private readonly List<string> _shown = [];
    private readonly List<IDisposable> _bound = [];
    private AxTabStrip? _tabs;
    private ContentControl? _content;
    /// <summary>
    /// Есть ли в группе хоть одна вкладка.
    /// </summary>
    /// <remarks>
    /// Шапка пустой группы — это полоса в 38 пикселей, в которой нечего
    /// показать. Такой остаётся область документов, пока в ней ничего не
    /// открыто, и пустая рамка над заставкой выглядит недоделкой, а не местом.
    /// </remarks>
    public static readonly DirectProperty<DockGroupView, bool> HasTabsProperty =
        AvaloniaProperty.RegisterDirect<DockGroupView, bool>(nameof(HasTabs), view => view.HasTabs);

    private bool _hasTabs;
    private DockGroup? _group;
    private DockItems? _items;
    private object? _empty;
    private bool _filling;

    /// <summary>Человек попросил закрыть панель; в поле — её имя.</summary>
    /// <remarks>
    /// Именно попросил: закрывает хозяин. У документа могут быть несохранённые
    /// правки, и спрашивать о них — не дело вида.
    /// </remarks>
    public event EventHandler<string>? Closing;

    /// <summary>Человек выбрал вкладку; в поле — имя панели.</summary>
    /// <remarks>
    /// Выбор — часть раскладки, а не состояние контрола: он переживает
    /// перезапуск. Поэтому вид о нём только сообщает, а записывает его в дерево
    /// тот, кто деревом владеет.
    /// </remarks>
    public event EventHandler<string>? Chosen;

    /// <summary>Имя показанной группы; пусто — вид ничем не занят.</summary>
    public string Id => _group?.Id ?? string.Empty;

    /// <inheritdoc cref="HasTabsProperty"/>
    public bool HasTabs
    {
        get => _hasTabs;
        private set => SetAndRaise(HasTabsProperty, ref _hasTabs, value);
    }

    /// <summary>
    /// Докуда сверху идёт полоса вкладок.
    /// </summary>
    /// <remarks>
    /// Нужно перетаскиванию: бросок в полосу вкладок значит «встань соседней
    /// вкладкой», а тот же бросок парой пикселей ниже — «раздели область
    /// сверху». Спрашивать об этом попадание мыши нельзя: во время тяги
    /// указатель захвачен, и источник события — уже не то, над чем он летит.
    /// </remarks>
    public double HeaderHeight =>
        _tabs is { } tabs && tabs.TranslatePoint(default, this) is { } origin
            ? origin.Y + tabs.Bounds.Height
            : 0;

    /// <summary>
    /// Место в полосе, куда встанет вкладка, брошенная на этом расстоянии слева.
    /// </summary>
    /// <param name="x">Расстояние от левого края группы.</param>
    /// <param name="ignore">Какую вкладку не считать — её как раз и несут.</param>
    /// <returns>Номер в счёте дерева; −1 — полосы ещё нет.</returns>
    /// <remarks>
    /// Номер именно в счёте дерева, а не показанных вкладок: панель выключенного
    /// плагина остаётся в группе именем, вкладки у неё нет, и место, посчитанное
    /// по экрану, уехало бы мимо.
    /// <para>
    /// Несомая вкладка не считается: место человек выбирает среди остальных, и
    /// <see cref="DockTree.Attach"/> убирает её из группы ровно так же. Считай мы
    /// её — перестановка внутри полосы промахивалась бы на единицу.
    /// </para>
    /// </remarks>
    public int SlotAt(double x, string? ignore = null)
    {
        if (_tabs is null || _group is null)
            return -1;

        var rest = _group.Items
            .Where(id => !string.Equals(id, ignore, StringComparison.Ordinal))
            .ToList();

        for (var at = 0; at < rest.Count; at++)
        {
            var shown = _shown.IndexOf(rest[at]);

            // У панели выключенного плагина вкладки нет — мерить нечего, и место
            // она делит с ближайшей видимой соседкой слева.
            if (shown < 0 || shown >= _tabs.Items.Count)
                continue;

            if (_tabs.Items[shown] is Control tab
                && tab.TranslatePoint(new Point(tab.Bounds.Width / 2, 0), this) is { } middle
                && x < middle.X)
            {
                return at;
            }
        }

        return rest.Count;
    }

    /// <summary>Имя панели, которой принадлежит вкладка; null — вкладка не наша.</summary>
    /// <param name="tab">Вкладка из полосы этой группы.</param>
    public string? Item(Control tab)
    {
        var at = _tabs?.Items.IndexOf(tab) ?? -1;

        return at >= 0 && at < _shown.Count ? _shown[at] : null;
    }

    /// <summary>Показывает группу.</summary>
    /// <param name="group">Что показывать.</param>
    /// <param name="items">Где брать живые панели.</param>
    /// <param name="empty">Что показать, если показывать нечего; может быть null.</param>
    public void Update(DockGroup group, DockItems items, object? empty = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(items);

        _group = group;
        _items = items;
        _empty = empty;

        Fill();
    }

    /// <summary>
    /// Отпускает панели, ничего о них не забывая на экране.
    /// </summary>
    /// <remarks>
    /// Нужно перед перекладкой: у контрола Avalonia родитель ровно один, и
    /// панель, переезжающая в соседнюю группу, обязана сперва уйти отсюда.
    /// </remarks>
    public void Release()
    {
        _group = null;
        _items = null;
        _empty = null;

        Clear();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_tabs is not null)
            _tabs.SelectionChanged -= OnChosen;

        _tabs = e.NameScope.Find<AxTabStrip>("PART_Tabs");
        _content = e.NameScope.Find<ContentControl>("PART_Content");

        if (_tabs is not null)
            _tabs.SelectionChanged += OnChosen;

        Fill();
    }

    /// <summary>Раскладывает вкладки группы и показывает выбранную.</summary>
    private void Fill()
    {
        if (_tabs is null || _content is null)
            return;

        _filling = true;

        try
        {
            Clear();

            if (_group is null || _items is null)
                return;

            foreach (var id in _group.Items)
            {
                // Панели может не быть: плагин выключили, а имя в дереве
                // осталось — чтобы панель вернулась на своё место, когда его
                // включат обратно.
                if (_items.Find(id) is not { } item)
                    continue;

                var tab = new AxTabItem { IsClosable = item.CanClose };

                if (item.CanClose)
                {
                    var closing = id;

                    tab.CloseRequested += (_, _) => Closing?.Invoke(this, closing);
                }

                _bound.Add(tab.Bind(ContentControl.ContentProperty, item.GetObservable(DockItem.TitleProperty)));
                _tabs.Items.Add(tab);
                _shown.Add(id);
            }

            var chosen = _group.Selected is { } selected ? _shown.IndexOf(selected) : -1;

            if (chosen < 0 && _shown.Count > 0)
                chosen = 0;

            _tabs.SelectedIndex = chosen;
            _content.Content = chosen >= 0 ? _items.Find(_shown[chosen])?.Content : _empty;
            HasTabs = _shown.Count > 0;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>Снимает вкладки и отпускает показанную панель.</summary>
    private void Clear()
    {
        foreach (var binding in _bound)
            binding.Dispose();

        _bound.Clear();
        _shown.Clear();

        if (_tabs is not null)
        {
            _tabs.SelectedIndex = -1;
            _tabs.Items.Clear();
        }

        if (_content is not null)
            _content.Content = null;
    }

    private void OnChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_filling || _tabs is null || _content is null)
            return;

        var at = _tabs.SelectedIndex;

        if (at < 0 || at >= _shown.Count)
            return;

        // Содержимое меняем сразу, не дожидаясь круга через владельца дерева:
        // щелчок по вкладке обязан показать панель, даже если хозяин раскладки
        // ответит на это событие позже или не ответит вовсе.
        _content.Content = _items?.Find(_shown[at])?.Content;

        Chosen?.Invoke(this, _shown[at]);
    }
}
