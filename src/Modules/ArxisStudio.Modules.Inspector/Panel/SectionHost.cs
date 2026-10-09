using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Место раздела плагина: его интерфейс за барьером, строки по умолчанию, которые он взял, и что он о себе сказал.
/// </summary>
/// <remarks>
/// <para>
/// Раздел строится один раз на тип цели: цель того же типа он переживает, как строки, и узнаёт о ней через
/// <see cref="InspectorSectionView.Refresh"/>. Что раздел сказал о себе — заголовок, место, взятые свойства, —
/// спрошено один раз, при постройке: список перестраивается часто, а код раздела чужой.
/// </para>
/// <para>
/// Строки, взятые разделом у контекста, живут вместе с ним: уходит раздел — уходят и они, прощальной дорогой.
/// Выгружают хозяина раздела — его интерфейс снимается с экрана раньше, чем его сборка.
/// </para>
/// </remarks>
internal sealed class SectionHost : Border
{
    private readonly InspectorList _list;
    private readonly List<InspectorRow> _rows = [];
    private IStudioGuarded<InspectorSectionView>? _view;

    /// <summary>Строит раздел.</summary>
    /// <param name="list">Список — дорога к цели.</param>
    /// <param name="section">Вклад-раздел.</param>
    public SectionHost(InspectorList list, IStudioContribution<InspectorSection> section)
    {
        _list = list;
        Section = section;
        Title = section.TryGet("gives its title", static it => it.Title, out var title) && !string.IsNullOrWhiteSpace(title)
            ? title
            : section.OwnerId;
        Placement = section.TryGet("gives its placement", static it => it.Placement, out var placement)
            ? placement
            : InspectorSectionPlacement.Body;
        Category = section.TryGet("gives its category", static it => it.Category, out var category) ? category : null;
        Order = section.TryGet("gives its order", static it => it.Order, out var order) ? order : 0;
        Claims = section.TryGet("gives its claims", static it => it.Claims, out var claims) && claims is not null
            ? claims.ToArray()
            : [];
        ReplaceAll = section.TryGet("replaces the rows", static it => it.ReplaceAll, out var replaceAll) && replaceAll;

        var context = new Context(this);

        if (section.TryGet("creates a view", it => it.CreateView(context), out var view) && view is not null)
        {
            _view = section.Guard(view);
            _view.Released += OnReleased;
            Child = _view.TryGet("gives its view", static it => it.View, out var control) && control is not null
                ? _view.Host(control)
                : Failed();
        }
        else
        {
            Child = Failed();
        }
    }

    /// <summary>Вклад-раздел.</summary>
    public IStudioContribution<InspectorSection> Section { get; }

    /// <summary>Заголовок.</summary>
    public string Title { get; }

    /// <summary>Где стоит.</summary>
    public InspectorSectionPlacement Placement { get; }

    /// <summary>Раздел метаданных, внутрь которого встать; пусто — свой заголовок.</summary>
    public string? Category { get; }

    /// <summary>Порядок среди разделов.</summary>
    public int Order { get; }

    /// <summary>Свойства, которые раздел показывает сам.</summary>
    public IReadOnlyCollection<string> Claims { get; }

    /// <summary>Раздел показывает цель целиком.</summary>
    public bool ReplaceAll { get; }

    /// <summary>У цели что-то сменилось — или показана другая цель того же типа.</summary>
    /// <param name="change">Что сменилось.</param>
    public void Refresh(InspectorChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        foreach (var row in _rows)
        {
            if (change.Changes.HasFlag(InspectorChanges.Properties) && _list.PropertyOf(row.Property.Id) is { } property)
                row.Renew(property);
            else if (change.Touches(row.Property.Id))
                row.Invalidate();
        }

        _view?.Run("refreshes its view", it => it.Refresh(change));
    }

    /// <summary>Раздел уходит: показан другой тип или раздел выгружен.</summary>
    public void Release()
    {
        foreach (var row in _rows)
            row.Release();

        _rows.Clear();

        if (_view is { } view)
        {
            view.Released -= OnReleased;
            view.Farewell("releases its view", static it => it.Release());
            _view = null;
        }

        Child = null;
    }

    /// <summary>Строка по умолчанию для раздела.</summary>
    private InspectorRow? CreateRow(string id)
    {
        if (_list.CreateRow(id) is not { } row)
            return null;

        _rows.Add(row);

        return row;
    }

    /// <summary>Хозяина раздела выгружают: его интерфейс уходит с экрана раньше, чем его сборка.</summary>
    private void OnReleased(object? sender, EventArgs e)
    {
        if (_view is { } view)
            view.Released -= OnReleased;

        _view = null;
        Child = Failed();
    }

    private TextBlock Failed() => new()
    {
        Text = _list.Words["inspector.sectionFailed"],
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        [AxText.ToneProperty] = AxTextTone.Secondary,
    };

    /// <summary>Дорога раздела к цели, показанной сейчас.</summary>
    private sealed class Context(SectionHost host) : InspectorSectionContext
    {
        public override InspectorTypeInfo Type => host._list.Type ?? new InspectorTypeInfo(string.Empty);

        public override int Count => host._list.Count;

        public override IReadOnlyList<InspectorProperty> Properties => host._list.Properties;

        public override InspectorValue ValueOf(string id) => host._list.ValueOf(id);

        public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null) =>
            host._list.WriteAsync(writes, label);

        public override Control? CreateRow(string id) => host.CreateRow(id);
    }
}
