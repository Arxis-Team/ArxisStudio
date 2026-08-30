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

                var tab = new AxTabItem { IsClosable = false };

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
