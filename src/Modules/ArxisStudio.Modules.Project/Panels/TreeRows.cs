using System.ComponentModel;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Tree;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Плоское дерево окна проекта: список строк, который экранный диктор читает деревом.
/// </summary>
/// <remarks>
/// Для раскладки и клавиатуры дерево — список: строки идут подряд, отступ рисует шаблон, а
/// раскрытие вставляет и снимает строки диапазонами. Диктор видел в нём список и читал «элемент
/// списка» — без раскрытия, и шеврон для него был нем. Роль и раскрытие дают свои пиры: дерево,
/// узел дерева, «свёрнуто» и «развёрнуто», — а просьба диктора раскрыть узел идёт тем же путём,
/// что стрелка вправо.
/// <para>
/// Уровня вложенности и места среди соседей диктор не услышит: мост Windows в Avalonia 12 их не
/// передаёт. Где узел лежит, говорит подсказка строки — путь от решения.
/// </para>
/// <para>
/// Тему список и строки берут у <see cref="AxListBox"/> и <see cref="AxListBoxItem"/>: наследник
/// без этой оговорки искал бы тему по своему типу и остался бы без неё.
/// </para>
/// </remarks>
internal sealed class TreeRows : AxListBox
{
    /// <summary>Раскрыть (<c>true</c>) или свернуть строку — по просьбе диктора.</summary>
    public Action<Row, bool>? Expanding { get; set; }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBox);

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new TreePeer(this);

    /// <inheritdoc/>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new TreeRow(this);

    /// <inheritdoc/>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<TreeRow>(item, out recycleKey);

    /// <inheritdoc/>
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is TreeRow row)
            row.Show(item as Row);
    }

    /// <inheritdoc/>
    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is TreeRow row)
            row.Show(null);
    }

    /// <summary>Дерево для диктора: выбор, прокрутка и дети — от списка.</summary>
    private sealed class TreePeer(TreeRows owner) : ListBoxAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
    }
}

/// <summary>Строка плоского дерева: для диктора — узел, который раскрывается и сворачивается.</summary>
/// <param name="list">Дерево, которому строка принадлежит: через него идёт просьба раскрыть.</param>
internal sealed class TreeRow(TreeRows list) : AxListBoxItem
{
    private Row? _row;
    private ExpandCollapseState _told = ExpandCollapseState.LeafNode;

    /// <summary>Раскрыт узел, свёрнут или он лист — как его читает диктор.</summary>
    public ExpandCollapseState State =>
        _row is not { HasChildren: true } row ? ExpandCollapseState.LeafNode
        : row.IsExpanded ? ExpandCollapseState.Expanded
        : ExpandCollapseState.Collapsed;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxListBoxItem);

    /// <summary>Ставит строке узел, который она показывает; пусто — строка ушла из списка.</summary>
    public void Show(Row? row)
    {
        if (_row is not null)
            _row.PropertyChanged -= OnRowChanged;

        _row = row;

        if (_row is not null)
            _row.PropertyChanged += OnRowChanged;

        _told = State;
    }

    /// <summary>Просит дерево раскрыть или свернуть узел; лист и узел в нужном виде не трогает.</summary>
    public void Ask(bool expand)
    {
        if (_row is { HasChildren: true } row && row.IsExpanded != expand)
            list.Expanding?.Invoke(row, expand);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    /// <summary>
    /// Раскрытие сменилось — клавишей, мышью или диктором, — и диктор должен об этом услышать.
    /// </summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        var state = State;

        if (state == _told)
            return;

        var before = _told;

        _told = state;
        ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, before, state);
    }

    /// <summary>Узел дерева для диктора: выбор — от строки списка, раскрытие — своё.</summary>
    private sealed class RowPeer(TreeRow owner) : ListItemAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.State;

        public bool ShowsMenu => false;

        public void Expand() => owner.Ask(true);

        public void Collapse() => owner.Ask(false);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
    }
}
