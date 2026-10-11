using System.Collections.ObjectModel;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Inspector.Panel;

/// <summary>
/// Список инспектора: строки показанной цели по разделам или по алфавиту, отбор, свёрнутое — и дорога строк к цели.
/// </summary>
/// <remarks>
/// <para>
/// <b>Строки живут дольше списка.</b> Строка с полем помнится по идентификатору свойства, пока свойство есть у цели
/// и редактор для него тот же: поиск, отбор и свёрнутый раздел её только убирают из списка, а смена цели того же
/// типа отдаёт ей новое значение — вместе с кареткой и набранным, если они в ней были. Список сверяется с новым
/// планом по ключам и трогает только то, что сменилось: снятая и вставленная заново ячейка — это потерянная
/// каретка и поле, построенное ещё раз.
/// </para>
/// <para>
/// <b>Цель зовётся через шов её хозяина</b> (<see cref="StudioInspector.CurrentGuard"/>): упавший вопрос засчитан
/// источнику, а список отвечает пустым значением с заметкой. Предпросмотр и удержание, которые цель вернула, —
/// тоже её объекты, и зовутся они той же дорогой.
/// </para>
/// </remarks>
internal sealed class InspectorList
{
    /// <summary>Приставка ключа заголовка раздела.</summary>
    public const string GroupPrefix = "g:";

    /// <summary>Приставка ключа строки свойства.</summary>
    public const string RowPrefix = "p:";

    /// <summary>Приставка ключа заголовка над строкой.</summary>
    public const string CaptionPrefix = "h:";

    /// <summary>Приставка ключа раздела плагина.</summary>
    public const string SectionPrefix = "s:";

    /// <summary>Приставка раздела, который плагин поставил своим заголовком.</summary>
    private const string OwnSectionCategory = "@";

    private readonly AxListBox _box;
    private readonly StudioInspector _service;
    private readonly InspectorCatalog _catalog;
    private readonly ObservableCollection<Control> _items = [];
    private readonly Dictionary<string, InspectorRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InspectorRow> _headerRows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _headerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<IStudioContribution<InspectorSection>, SectionHost> _sections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, bool> _folded = new(StringComparer.Ordinal);
    private InspectorTarget? _target;
    private IStudioGuarded<InspectorTarget>? _guard;
    private InspectorTypeInfo? _type;
    private IReadOnlyList<InspectorProperty> _properties = [];
    private Dictionary<string, InspectorProperty> _byId = new(StringComparer.Ordinal);
    private string _query = string.Empty;
    private bool _alphabetical;
    private bool _writtenOnly;
    private bool _showAll;
    private bool _including;

    /// <summary>Заводит список.</summary>
    /// <param name="box">Список на экране.</param>
    /// <param name="service">Служба показа.</param>
    /// <param name="catalog">Каталог вкладов.</param>
    /// <param name="words">Слова панели.</param>
    public InspectorList(AxListBox box, StudioInspector service, InspectorCatalog catalog, InspectorWords words)
    {
        _box = box;
        _service = service;
        _catalog = catalog;
        Words = words;
        box.ItemsSource = _items;
    }

    /// <summary>Список перестроен: шапке и пустым состояниям пора посмотреть заново.</summary>
    public event EventHandler? Rebuilt;

    /// <summary>У цели сменилась шапка.</summary>
    public event EventHandler? HeaderChanged;

    /// <summary>Слова панели.</summary>
    public InspectorWords Words { get; }

    /// <summary>Ячейки списка.</summary>
    public IReadOnlyList<Control> Items => _items;

    /// <summary>Показанная цель.</summary>
    public InspectorTarget? Target => _target;

    /// <summary>Ручка к показанной цели.</summary>
    public IStudioGuarded<InspectorTarget>? Guard => _guard;

    /// <summary>Тип показанной цели.</summary>
    public InspectorTypeInfo? Type => _type;

    /// <summary>Свойства цели с метаданными.</summary>
    public IReadOnlyList<InspectorProperty> Properties => _properties;

    /// <summary>Разделы в шапке.</summary>
    public IReadOnlyList<SectionHost> HeaderSections { get; private set; } = [];

    /// <summary>Свойство флажка в шапке — <see cref="InspectorHeader.EnabledProperty"/>; пусто — флажка нет.</summary>
    public InspectorProperty? HeaderToggle { get; private set; }

    /// <summary>
    /// Строки шапки: имя и жетоны — <see cref="InspectorHeader.NameProperty"/> и <see cref="InspectorHeader.TagsProperty"/>.
    /// </summary>
    /// <remarks>
    /// Это свои строки шапки, а не строки списка: свойство шапки в списке не повторяется, и строка никогда не стоит в
    /// двух местах разом. Помнятся и обновляются они так же, как строки списка.
    /// </remarks>
    public IReadOnlyList<InspectorRow> HeaderRows { get; private set; } = [];

    /// <summary>Строки, которые список помнит, — с теми, что сейчас отобраны прочь.</summary>
    public IReadOnlyCollection<InspectorRow> Rows => _rows.Values;

    /// <summary>Сколько объектов показано.</summary>
    public int Count => _guard is { } guard && guard.TryGet("gives its count", static it => it.Count, out var count) ? Math.Max(count, 1) : 1;

    /// <summary>Поиск: строки, у которых подпись или идентификатор содержат набранное.</summary>
    public string Query
    {
        get => _query;
        set
        {
            value = value?.Trim() ?? string.Empty;

            if (string.Equals(_query, value, StringComparison.Ordinal))
                return;

            _query = value;
            Rebuild();
        }
    }

    /// <summary>По алфавиту, без разделов.</summary>
    public bool Alphabetical
    {
        get => _alphabetical;
        set
        {
            if (_alphabetical == value)
                return;

            _alphabetical = value;
            Rebuild();
        }
    }

    /// <summary>Только заданные здесь.</summary>
    public bool WrittenOnly
    {
        get => _writtenOnly;
        set
        {
            if (_writtenOnly == value)
                return;

            _writtenOnly = value;
            Rebuild();
        }
    }

    /// <summary>Все свойства: дополнительные, только для чтения и скрытые — последним разделом.</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (_showAll == value)
                return;

            _showAll = value;
            Rebuild();
        }
    }

    /// <summary>Идёт ли поиск.</summary>
    public bool IsSearching => _query.Length > 0;

    /// <summary>Свойство цели по идентификатору — с метаданными.</summary>
    /// <param name="id">Идентификатор.</param>
    public InspectorProperty? PropertyOf(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Раздел, в котором стоит свойство, — каким его понял каталог: «Layout» автора — тот же раздел макета.</summary>
    /// <param name="property">Свойство.</param>
    public string CategoryOf(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return _catalog.Category(property.IsHidden ? InspectorCatalog.HiddenCategory : property.Category).Id;
    }

    /// <summary>Идёт за службой: показана другая цель — строки того же типа остаются, остальные уходят.</summary>
    public void Follow()
    {
        var target = _service.Current;

        if (ReferenceEquals(target, _target))
            return;

        if (_target is { } previous)
            previous.Changed -= OnTargetChanged;

        _target = target;
        _guard = target is null ? null : _service.CurrentGuard;

        if (target is not null)
        {
            target.Changed += OnTargetChanged;
            Include();
        }

        Rebuild(renew: true);
    }

    /// <summary>Вклады сменились: строки и разделы строятся заново — у свойства мог смениться редактор.</summary>
    public void Reset()
    {
        ForgetRows();
        ForgetSections();
        Rebuild(renew: true);
    }

    /// <summary>Сворачивает или раскрывает раздел.</summary>
    /// <param name="category">Раздел.</param>
    /// <param name="folded">Свернуть ли.</param>
    public void Fold(string category, bool folded)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (IsFolded(category) == folded)
            return;

        _folded[category] = folded;
        Rebuild();
    }

    /// <summary>Сворачивает или раскрывает все разделы.</summary>
    /// <param name="folded">Свернуть ли.</param>
    public void FoldAll(bool folded)
    {
        foreach (var group in _items.OfType<GroupCell>())
            _folded[group.CategoryId] = folded;

        Rebuild();
    }

    /// <summary>Свёрнут ли раздел: выбор человека, а без него — как сказали метаданные.</summary>
    /// <param name="category">Раздел.</param>
    public bool IsFolded(string category) =>
        _folded.TryGetValue(category, out var folded) ? folded : _catalog.Category(category).Collapsed;

    /// <summary>Ячейка заголовка раздела, если раздел в списке.</summary>
    /// <param name="category">Раздел.</param>
    public GroupCell? GroupOf(string category) =>
        _items.OfType<GroupCell>().FirstOrDefault(cell => string.Equals(cell.CategoryId, category, StringComparison.Ordinal));

    /// <summary>Список уходит: строки и разделы прощаются, подписки снимаются.</summary>
    public void Release()
    {
        if (_target is { } target)
            target.Changed -= OnTargetChanged;

        _target = null;
        _guard = null;
        ForgetRows();
        ForgetSections();
        Clear();
    }

    /// <summary>Строка свойства для раздела плагина: тот же редактор, та же подпись, что в списке.</summary>
    /// <param name="id">Свойство.</param>
    /// <returns>Строка; пусто — такого свойства у цели нет.</returns>
    public InspectorRow? CreateRow(string id) =>
        PropertyOf(id) is { } property ? new InspectorRow(this, property, _catalog.EditorFor(property)) : null;

    /// <summary>Значение свойства у показанной цели.</summary>
    /// <param name="id">Свойство.</param>
    public InspectorValue ValueOf(string id) =>
        _guard is { } guard && guard.TryGet("gives a value", it => it.ValueOf(id), out var value) && value is not null
            ? value
            : InspectorValue.Empty with { Error = Words["inspector.valueFailed"] };

    /// <summary>Прочтётся ли текст значением.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="text">Текст.</param>
    public InspectorCheck Check(string id, string text) =>
        _guard is { } guard && guard.TryGet("checks a value", it => it.Check(id, text), out var check)
            ? check
            : InspectorCheck.Valid;

    /// <summary>Пишет значения — одной правкой.</summary>
    /// <param name="writes">Записи.</param>
    /// <param name="label">Как назвать шаг.</param>
    /// <returns>Записалось ли.</returns>
    public async Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label)
    {
        if (_guard is not { } guard)
            return false;

        var written = false;
        var ran = await guard.RunAsync("writes values", async it => written = await it.WriteAsync(writes, label).ConfigureAwait(true))
            .ConfigureAwait(true);

        return ran && written;
    }

    /// <summary>Предпросмотр значения — через шов хозяина цели.</summary>
    /// <param name="id">Свойство.</param>
    public IInspectorPreview? BeginPreview(string id) =>
        _guard is { } guard && guard.TryGet("begins a preview", it => it.BeginPreview(id), out var preview) && preview is not null
            ? new GuardedPreview(guard.Guard(preview))
            : null;

    /// <summary>Удержание цели, пока набрано своё, — через шов её хозяина.</summary>
    /// <param name="id">Свойство.</param>
    public IDisposable? Hold(string id) =>
        _guard is { } guard && guard.TryGet("holds itself", it => it.Hold(id), out var hold) && hold is not null
            ? new GuardedHold(guard.Guard(hold))
            : null;

    /// <summary>Подсказки к набору.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="typed">Набранное.</param>
    public IReadOnlyList<InspectorChoice> Suggest(string id, string typed) =>
        _guard is { } guard && guard.TryGet("suggests values", it => it.Suggest(id, typed), out var choices) && choices is not null
            ? choices
            : [];

    /// <summary>Шаг истории хозяина цели.</summary>
    /// <param name="back">Назад.</param>
    /// <returns>Сделан ли шаг.</returns>
    public async Task<bool> StepAsync(bool back)
    {
        if (_guard is not { } guard)
            return false;

        var stepped = false;
        var ran = await guard.RunAsync("steps its history", async it => stepped = await it.StepAsync(back).ConfigureAwait(true))
            .ConfigureAwait(true);

        return ran && stepped;
    }

    /// <summary>
    /// Свойства, заданные у цели, которые пишутся, — без имени из шапки: имя одно на элемент, его не сбрасывают и не
    /// переносят на другие.
    /// </summary>
    /// <remarks>Цель, которая не называет заданное сама, отвечает значениями — спрашивают их только здесь, по просьбе.</remarks>
    public IReadOnlyList<InspectorProperty> WrittenProperties()
    {
        if (_guard is not { } guard)
            return [];

        var written = Written(guard);
        var name = Header()?.NameProperty;

        return
        [
            .. _properties.Where(property =>
                !property.IsReadOnly &&
                !string.Equals(property.Id, name, StringComparison.Ordinal) &&
                (written?.Contains(property.Id) ?? ValueOf(property.Id).IsWritten)),
        ];
    }

    /// <summary>Шапка цели.</summary>
    public InspectorHeader? Header() =>
        _guard is { } guard && guard.TryGet("gives its header", static it => it.Header, out var header) ? header : null;

    /// <summary>Перестраивает список.</summary>
    /// <param name="renew">Цель или её состав сменились: строки получают свойства заново и переспрашивают значения.</param>
    public void Rebuild(bool renew = false)
    {
        var focused = FocusedIndex();

        if (_guard is not { } guard || !guard.TryGet("gives its type", static it => it.Type, out var type) || type is null)
        {
            _type = null;
            _properties = [];
            _byId = new Dictionary<string, InspectorProperty>(StringComparer.Ordinal);
            ForgetRows();
            ForgetSections();
            HeaderSections = [];
            Clear();
            Rebuilt?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Другой тип — другие разделы: их строили для прежнего.
        if (_type is null || !string.Equals(_type.FullName, type.FullName, StringComparison.Ordinal))
        {
            ForgetSections();
            renew = true;
        }

        _type = type;

        if (renew)
            Describe(guard, type);

        Head();

        var sections = Sections(type, renew);
        var claimed = sections.SelectMany(static section => section.Claims).ToHashSet(StringComparer.Ordinal);
        var replaceAll = sections.Any(static section => section.ReplaceAll);
        var written = Written(guard);
        var rows = new List<InspectorRow>();

        foreach (var property in _properties)
        {
            if (!Shows(property, claimed, replaceAll))
                continue;

            var row = RowFor(property);

            if (_writtenOnly && !(written?.Contains(property.Id) ?? row.Known().IsWritten))
                continue;

            rows.Add(row);
        }

        HeaderSections = sections.Where(static section => section.Placement == InspectorSectionPlacement.Header).ToArray();
        Reconcile(Plan(rows, sections.Where(section => section.Placement == InspectorSectionPlacement.Body && Matches(section)).ToArray(), written));
        Refocus(focused);
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Свойства цели — с метаданными; строки, чьё свойство ушло или чей редактор сменился, уходят.</summary>
    private void Describe(IStudioGuarded<InspectorTarget> guard, InspectorTypeInfo type)
    {
        var listed = guard.TryGet("gives its properties", static it => it.Properties, out var properties) && properties is not null
            ? properties
            : [];

        _properties = _catalog.Describe(type, listed);
        _byId = new Dictionary<string, InspectorProperty>(StringComparer.Ordinal);

        foreach (var property in _properties)
            _byId.TryAdd(property.Id, property);

        foreach (var rows in (Dictionary<string, InspectorRow>[])[_rows, _headerRows])
        {
            foreach (var (id, row) in rows.ToArray())
            {
                if (_byId.TryGetValue(id, out var property) && Equals(_catalog.EditorFor(property), row.Editor))
                {
                    row.Renew(property);
                    continue;
                }

                row.Release();
                rows.Remove(id);
            }
        }
    }

    /// <summary>
    /// Шапка цели: флажок «включён» и строки имени и жетонов. Их свойства в списке не повторяются; строка, чьё
    /// свойство ушло из шапки, отпускается.
    /// </summary>
    private void Head()
    {
        var header = Header();

        HeaderToggle = header?.EnabledProperty is { } enabled ? PropertyOf(enabled) : null;

        var rows = new List<InspectorRow>();

        foreach (var id in (string?[])[header?.NameProperty, header?.TagsProperty])
        {
            if (id is null || PropertyOf(id) is not { } property)
                continue;

            if (!_headerRows.TryGetValue(id, out var row))
            {
                row = new InspectorRow(this, property, _catalog.EditorFor(property));
                _headerRows[id] = row;
            }

            // Имя встаёт в строку заголовка, как имя объекта у Unity; жетоны — строкой под ним.
            row.IsHeadline = string.Equals(id, header?.NameProperty, StringComparison.Ordinal);
            rows.Add(row);
        }

        foreach (var (id, row) in _headerRows.ToArray())
        {
            if (rows.Contains(row))
                continue;

            row.Release();
            _headerRows.Remove(id);
        }

        HeaderRows = rows;
        _headerIds.Clear();
        _headerIds.UnionWith(rows.Select(static row => row.Property.Id));

        if (HeaderToggle is { } toggle)
            _headerIds.Add(toggle.Id);
    }

    /// <summary>Разделы для цели: прежние остаются, новых строят, ушедшие прощаются.</summary>
    private List<SectionHost> Sections(InspectorTypeInfo type, bool renew)
    {
        var wanted = _catalog.SectionsFor(type, _properties);
        var hosts = new List<SectionHost>(wanted.Count);

        foreach (var (section, host) in _sections.ToArray())
        {
            if (wanted.Contains(section))
                continue;

            host.Release();
            _sections.Remove(section);
        }

        foreach (var section in wanted)
        {
            if (_sections.TryGetValue(section, out var host))
            {
                // Тот же тип, другая цель или её состав: раздел узнаёт об этом сам.
                if (renew)
                    host.Refresh(new InspectorChangedEventArgs(InspectorChanges.Properties | InspectorChanges.Values));
            }
            else
            {
                host = new SectionHost(this, section);
                _sections[section] = host;
            }

            hosts.Add(host);
        }

        return hosts;
    }

    /// <summary>Видна ли строка свойства при нынешнем отборе.</summary>
    private bool Shows(InspectorProperty property, HashSet<string> claimed, bool replaceAll)
    {
        if (replaceAll || claimed.Contains(property.Id) || _headerIds.Contains(property.Id))
            return false;

        if (property.IsHidden && !_showAll)
            return false;

        // Только чтение — не правка, а инспектор — то, что правят: такой строки в списке нет, и поиск её не находит.
        // Прочесть её можно в «Все свойства» — на её месте в разделе, как отладочный вид Unity.
        if (property.IsReadOnly && !_showAll)
            return false;

        // Дополнительное находит поиск: его не показывают сразу, но и прятать от того, кто его ищет, незачем.
        if (property.IsAdvanced && !_showAll && !IsSearching)
            return false;

        return !IsSearching ||
               property.Label.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ||
               property.Id.Contains(_query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Отвечает ли раздел поиску — заголовком или свойством, которое он взял. Что ещё в нём искать, инспектор не
    /// знает: интерфейс раздела чужой. Не ответивший при поиске уходит, как ушла бы его строка.
    /// </summary>
    private bool Matches(SectionHost section) =>
        !IsSearching ||
        section.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ||
        section.Claims.Any(id =>
            id.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ||
            PropertyOf(id)?.Label.Contains(_query, StringComparison.CurrentCultureIgnoreCase) == true);

    private InspectorRow RowFor(InspectorProperty property)
    {
        if (_rows.TryGetValue(property.Id, out var row))
            return row;

        row = new InspectorRow(this, property, _catalog.EditorFor(property));
        _rows[property.Id] = row;

        return row;
    }

    private IReadOnlyCollection<string>? Written(IStudioGuarded<InspectorTarget> guard) =>
        guard.TryGet("gives its written properties", static it => it.WrittenIds, out var written) ? written : null;

    /// <summary>План списка: разделы плагинов своим заголовком, затем разделы свойств или все строки по алфавиту.</summary>
    private List<InspectorEntry> Plan(
        IReadOnlyList<InspectorRow> rows, IReadOnlyList<SectionHost> sections, IReadOnlyCollection<string>? written)
    {
        var plan = new List<InspectorEntry>();
        var culture = StringComparer.CurrentCultureIgnoreCase;

        // Раздел плагина без раздела метаданных стоит своим заголовком первым: это его инспектор для цели, как
        // Editor у Unity. Заголовок у него и при алфавите — без него раздел висел бы среди строк ничьим.
        foreach (var section in sections.Where(static section => section.Category is null)
                                        .OrderBy(static section => section.Order)
                                        .ThenBy(static section => section.Title, culture))
        {
            var category = new InspectorCategory(OwnSectionCategory + section.Title, section.Title, section.Order, false);
            var expanded = IsSearching || !IsFolded(category.Id);

            plan.Add(new GroupEntry(GroupPrefix + category.Id, category, expanded, !IsSearching, null));

            if (expanded)
                plan.Add(new SectionEntry(SectionPrefix + category.Id, section));
        }

        if (_alphabetical)
        {
            foreach (var row in rows.OrderBy(static row => row.Property.Label, culture))
                plan.Add(new RowEntry(RowPrefix + row.Property.Id, row));

            return plan;
        }

        var inside = sections.Where(static section => section.Category is not null).ToArray();
        var groups = rows.GroupBy(row => CategoryOf(row.Property), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);

        foreach (var section in inside)
            groups.TryAdd(_catalog.Category(section.Category).Id, []);

        foreach (var category in groups.Keys.Select(_catalog.Category)
                                            .OrderBy(static category => category.Order)
                                            .ThenBy(static category => category.Title, culture))
        {
            var expanded = IsSearching || !IsFolded(category.Id);
            var count = written is null
                ? (int?)null
                : _properties.Count(property => written.Contains(property.Id) && !_headerIds.Contains(property.Id) &&
                                                string.Equals(CategoryOf(property), category.Id, StringComparison.Ordinal));

            plan.Add(new GroupEntry(GroupPrefix + category.Id, category, expanded, !IsSearching, count));

            if (!expanded)
                continue;

            foreach (var section in inside.Where(section => string.Equals(_catalog.Category(section.Category).Id, category.Id, StringComparison.Ordinal))
                                          .OrderBy(static section => section.Order)
                                          .ThenBy(static section => section.Title, culture))
            {
                plan.Add(new SectionEntry(SectionPrefix + category.Id + "/" + section.Title, section));
            }

            foreach (var row in groups[category.Id].OrderBy(static row => row.Property.Order)
                                                   .ThenBy(static row => row.Property.Label, culture))
            {
                var caption = row.Property.Decorations
                    .FirstOrDefault(static item => item.Kind == InspectorDecorationKind.Header)?.Text;

                if (!string.IsNullOrWhiteSpace(caption))
                    plan.Add(new CaptionEntry(CaptionPrefix + row.Property.Id, caption));

                plan.Add(new RowEntry(RowPrefix + row.Property.Id, row));
            }
        }

        return plan;
    }

    /// <summary>
    /// Сверяет ячейки с планом: остаются те, что идут в его порядке, — самой длинной цепочкой, — уходят остальные, а
    /// недостающие встают новыми.
    /// </summary>
    private void Reconcile(IReadOnlyList<InspectorEntry> plan)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var at = 0; at < plan.Count; at++)
            index[plan[at].Key] = at;

        var positions = _items
            .Select(item => item is IInspectorCell cell && index.TryGetValue(cell.Slot, out var at) && cell.Fits(plan[at]) ? at : -1)
            .ToArray();
        var kept = Kept(positions);

        for (var at = _items.Count - 1; at >= 0; at--)
        {
            if (kept.Contains(at))
                continue;

            var cell = (IInspectorCell)_items[at];

            _items.RemoveAt(at);
            cell.Detach();
        }

        for (var at = 0; at < plan.Count; at++)
        {
            if (at < _items.Count && _items[at] is IInspectorCell cell && string.Equals(cell.Slot, plan[at].Key, StringComparison.Ordinal))
            {
                cell.Update(plan[at]);
                continue;
            }

            _items.Insert(at, CellFor(plan[at]));
        }
    }

    /// <summary>Номера ячеек, которые остаются: самая длинная цепочка, идущая в порядке плана.</summary>
    /// <param name="positions">Место каждой ячейки в плане; −1 — места нет.</param>
    internal static HashSet<int> Kept(IReadOnlyList<int> positions)
    {
        var tails = new List<int>();
        var previous = new int[positions.Count];

        for (var at = 0; at < positions.Count; at++)
        {
            previous[at] = -1;

            if (positions[at] < 0)
                continue;

            var low = 0;
            var high = tails.Count;

            while (low < high)
            {
                var middle = (low + high) / 2;

                if (positions[tails[middle]] < positions[at])
                    low = middle + 1;
                else
                    high = middle;
            }

            if (low > 0)
                previous[at] = tails[low - 1];

            if (low == tails.Count)
                tails.Add(at);
            else
                tails[low] = at;
        }

        var kept = new HashSet<int>();

        for (var at = tails.Count > 0 ? tails[^1] : -1; at >= 0; at = previous[at])
            kept.Add(at);

        return kept;
    }

    private Control CellFor(InspectorEntry entry) => entry switch
    {
        GroupEntry group => new GroupCell(group, Fold, Words),
        RowEntry row => new PropertyCell(row.Key, row.Row),
        CaptionEntry caption => new CaptionCell(caption),
        SectionEntry section => new SectionCell(section),
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    /// <summary>Номер ячейки, в которой стоит каретка; −1 — каретка не в списке.</summary>
    private int FocusedIndex()
    {
        if (TopLevel.GetTopLevel(_box)?.FocusManager?.GetFocusedElement() is not Visual focused || !_box.IsVisualAncestorOf(focused))
            return -1;

        for (var at = 0; at < _items.Count; at++)
        {
            if (ReferenceEquals(_items[at], focused) || _items[at].IsVisualAncestorOf(focused))
                return at;
        }

        return -1;
    }

    /// <summary>
    /// Каретка стояла в ячейке, которую перестройка убрала: она встаёт на строку там же — иначе она осталась бы
    /// нигде, и следующая стрелка не сделала бы ничего.
    /// </summary>
    private void Refocus(int at)
    {
        if (at < 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_box.IsKeyboardFocusWithin || _items.Count == 0)
                return;

            var from = Math.Min(at, _items.Count - 1);
            var cell = _items.Skip(from).OfType<PropertyCell>().FirstOrDefault()
                       ?? _items.Take(from).OfType<PropertyCell>().LastOrDefault();

            if (cell is not null)
            {
                _box.ScrollIntoView(cell);
                cell.Focus(NavigationMethod.Directional);
            }
        }, DispatcherPriority.Loaded);
    }

    private void Include()
    {
        if (_guard is not { } guard || !guard.TryGet("gives its type", static it => it.Type, out var type) || type is null)
            return;

        var always = _catalog.Always(type);

        if (always.Count == 0)
            return;

        // Цель может тут же объявить новый состав — список перестроится сразу после, один раз.
        _including = true;

        try
        {
            guard.Run("includes properties", it => it.Include(always));
        }
        finally
        {
            _including = false;
        }
    }

    private void OnTargetChanged(object? sender, InspectorChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _target) || _including || e.Changes.HasFlag(InspectorChanges.Closed))
            return;

        if (e.Changes.HasFlag(InspectorChanges.Properties))
        {
            Rebuild(renew: true);

            foreach (var host in _sections.Values)
                host.Refresh(e);
        }
        else if (e.Changes.HasFlag(InspectorChanges.Values))
        {
            foreach (var row in _rows.Values.Concat(_headerRows.Values))
            {
                if (e.Touches(row.Property.Id))
                    row.Invalidate();
            }

            foreach (var host in _sections.Values)
                host.Refresh(e);

            // Заданное сменилось: счёт в заголовках и отбор «Только заданные».
            Rebuild();
        }

        if (e.Changes.HasFlag(InspectorChanges.Header))
            HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ForgetRows()
    {
        foreach (var row in _rows.Values.Concat(_headerRows.Values))
            row.Release();

        _rows.Clear();
        _headerRows.Clear();
        _headerIds.Clear();
        HeaderRows = [];
        HeaderToggle = null;
    }

    private void ForgetSections()
    {
        foreach (var host in _sections.Values)
            host.Release();

        _sections.Clear();
    }

    private void Clear()
    {
        var cells = _items.OfType<IInspectorCell>().ToArray();

        _items.Clear();

        foreach (var cell in cells)
            cell.Detach();
    }

    /// <summary>Предпросмотр цели через шов её хозяина.</summary>
    private sealed class GuardedPreview(IStudioGuarded<IInspectorPreview> preview) : IInspectorPreview
    {
        private bool _ended;

        public InspectorCheck Show(string text) =>
            !_ended && preview.TryGet("shows a preview", it => it.Show(text), out var check) ? check : new InspectorCheck(false, null);

        public async Task<bool> CommitAsync(string text)
        {
            if (_ended)
                return false;

            var committed = false;
            var ran = await preview.RunAsync("commits a preview", async it => committed = await it.CommitAsync(text).ConfigureAwait(true))
                .ConfigureAwait(true);

            return ran && committed;
        }

        public void Dispose()
        {
            if (_ended)
                return;

            _ended = true;
            preview.Farewell("ends a preview", static it => it.Dispose());
        }
    }

    /// <summary>Удержание цели через шов её хозяина.</summary>
    private sealed class GuardedHold(IStudioGuarded<IDisposable> hold) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            hold.Farewell("releases a hold", static it => it.Dispose());
        }
    }
}
