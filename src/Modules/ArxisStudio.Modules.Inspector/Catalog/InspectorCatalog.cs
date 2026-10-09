using System.Globalization;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Inspector.Catalog;

/// <summary>Раздел списка: заголовок и место среди разделов.</summary>
/// <param name="Id">Идентификатор — как его называет свойство.</param>
/// <param name="Title">Заголовок.</param>
/// <param name="Order">Место среди разделов; описанные метаданными — раньше остальных.</param>
/// <param name="Collapsed">Свёрнут, пока его не раскроют.</param>
internal sealed record InspectorCategory(string Id, string Title, int Order, bool Collapsed);

/// <summary>Редактор, выбранный для свойства, и как его поле стоит в строке.</summary>
/// <param name="Editor">Вклад-редактор.</param>
/// <param name="Layout">Как стоит поле.</param>
internal sealed record ChosenEditor(IStudioContribution<PropertyEditor> Editor, InspectorFieldLayout Layout);

/// <summary>
/// Каталог вкладов инспектора: метаданные слоями, выбор редактора по цепочке, разделы для цели, пункты меню.
/// </summary>
/// <remarks>
/// <para>
/// Вклады собирает студия (<see cref="IStudioContributions.Collect{T}"/>): модули раньше плагинов, поднятый позже
/// входит в набор, выгруженный уходит. Каталог помнит таблицы метаданных и сбрасывает их, когда сменился состав
/// набора или таблица попросила спросить её заново, — и говорит об этом событием <see cref="Changed"/>: панель
/// перестраивает строки.
/// </para>
/// <para>
/// <b>Слои.</b> Свойство приходит от источника со своими атрибутами — нижний слой. Над ним таблицы модулей, над
/// ними таблицы плагинов; слои сливаются по полю. Внутри слоя описание более производного типа сильнее: таблица,
/// описавшая <c>Button</c>, побеждает описавшую <c>Control</c>.
/// </para>
/// <para>
/// <b>Цепочка редактора</b> — как у Unity, от точного к общему: подсказка свойства, точный тип, обобщённое
/// определение, базы с <see cref="PropertyEditorAttribute.ForDerived"/> от ближней, интерфейсы, затем текст и
/// только чтение. На одной ступени не-запасной сильнее запасного, плагин — модуля, а при равенстве побеждает
/// собранный раньше.
/// </para>
/// </remarks>
internal sealed class InspectorCatalog : IDisposable
{
    /// <summary>Подсказка редактора текста — последней ступени для свойства, которое пишется текстом.</summary>
    public const string TextHint = "text";

    /// <summary>Подсказка редактора «только чтение» — для свойства, которое не пишется.</summary>
    public const string ReadOnlyHint = "readonly";

    /// <summary>Раздел для свойств, которых не назвал никто.</summary>
    public const string OtherCategory = "other";

    /// <summary>Раздел скрытых свойств — виден, когда просят все свойства.</summary>
    public const string HiddenCategory = "hidden";

    /// <summary>База всякого перечисления.</summary>
    private const string EnumType = "System.Enum";

    private readonly IStudioContributionSet<PropertyEditor>? _editors;
    private readonly IStudioContributionSet<InspectorSection>? _sections;
    private readonly IStudioContributionSet<InspectorMetadata>? _metadata;
    private readonly IStudioContributionSet<InspectorMenu>? _menus;
    private readonly Func<string, string> _ownTitle;
    private readonly Dictionary<IStudioContribution<PropertyEditor>, PropertyEditorAttribute[]> _editorMarks =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IStudioContribution<InspectorSection>, InspectorSectionAttribute[]> _sectionMarks =
        new(ReferenceEqualityComparer.Instance);
    private Layers? _layers;

    /// <summary>Собирает вклады.</summary>
    /// <param name="contributions">Сбор вкладов студии; пусто — каталог пуст.</param>
    /// <param name="ownTitle">Заголовок своего раздела модуля — «Прочее», «Скрытые» — по идентификатору, словами модуля.</param>
    public InspectorCatalog(IStudioContributions? contributions, Func<string, string> ownTitle)
    {
        _ownTitle = ownTitle;
        _editors = contributions?.Collect<PropertyEditor>();
        _sections = contributions?.Collect<InspectorSection>();
        _metadata = contributions?.Collect<InspectorMetadata>();
        _menus = contributions?.Collect<InspectorMenu>();

        foreach (var set in new object?[] { _editors, _sections, _menus })
        {
            if (set is IStudioContributionSet<StudioContribution> contributionSet)
                contributionSet.Changed += OnSetChanged;
        }

        if (_metadata is not null)
            _metadata.Changed += OnMetadataChanged;
    }

    /// <summary>Вклады сменились: строки надо перестроить.</summary>
    public event EventHandler? Changed;

    /// <summary>Пункты меню плагинов и модулей.</summary>
    public IReadOnlyList<IStudioContribution<InspectorMenu>> Menus => _menus?.Items ?? [];

    /// <summary>Раздел по идентификатору: описанный метаданными, с аргументом или по имени из атрибута автора.</summary>
    /// <param name="id">Идентификатор или имя раздела; пусто — «Прочее».</param>
    /// <remarks>
    /// <para>
    /// Раздел с аргументом пишется через косую черту — <c>parent/Grid</c>: описанный раздел <c>parent</c> с
    /// заголовком «В {0}» становится «В Grid» и встаёт на его место. Так источник называет раздел присоединённых
    /// членов, которые читает родитель, не зная ни заголовков, ни языка.
    /// </para>
    /// <para>
    /// Раздел автора, совпавший с описанным без учёта регистра, — он и есть: <c>[Category("Layout")]</c> контрола
    /// проекта встаёт в раздел макета, а не своим разделом рядом с ним.
    /// </para>
    /// </remarks>
    public InspectorCategory Category(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? OtherCategory : id;

        var layers = Describe();

        if (layers.Categories.TryGetValue(id, out var category))
            return category;

        var slash = id.IndexOf('/', StringComparison.Ordinal);

        if (slash > 0 && slash < id.Length - 1 &&
            layers.Categories.TryGetValue(id[..slash], out var based) &&
            based.Title.Contains("{0}", StringComparison.Ordinal))
        {
            return new InspectorCategory(
                id, string.Format(CultureInfo.CurrentCulture, based.Title, id[(slash + 1)..]), based.Order, based.Collapsed);
        }

        if (layers.Categories.Values.FirstOrDefault(known => string.Equals(known.Id, id, StringComparison.OrdinalIgnoreCase)) is { } alike)
            return alike;

        // Свои разделы модуля стоят последними, даже когда таблиц нет; раздел, который назвал автор атрибутом
        // [Category], а не таблица, — заголовком из его имени, после описанных.
        return id switch
        {
            OtherCategory => new InspectorCategory(id, _ownTitle(id), int.MaxValue - 1, false),
            HiddenCategory => new InspectorCategory(id, _ownTitle(id), int.MaxValue, true),
            _ => new InspectorCategory(id, id, int.MaxValue - 2, false),
        };
    }

    /// <summary>Свойства цели с метаданными всех слоёв.</summary>
    /// <param name="type">Тип цели.</param>
    /// <param name="properties">Свойства от источника.</param>
    public IReadOnlyList<InspectorProperty> Describe(InspectorTypeInfo type, IReadOnlyList<InspectorProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(properties);

        var layers = Describe();
        var chain = Chain(type);

        return properties.Select(property => Apply(Apply(property, layers.Modules, chain), layers.Plugins, chain)).ToArray();
    }

    /// <summary>Свойства, которые метаданные просят показывать всегда — цель добавит их, если умеет.</summary>
    /// <param name="type">Тип цели.</param>
    public IReadOnlyCollection<string> Always(InspectorTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var layers = Describe();
        var chain = Chain(type);

        return layers.Modules.Concat(layers.Plugins)
            .Where(hints => chain.Contains(hints.TypeName, StringComparer.Ordinal))
            .SelectMany(hints => hints.Properties.Where(pair => pair.Value.Always == true).Select(pair => pair.Key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Редактор для свойства — по цепочке.</summary>
    /// <param name="property">Свойство с метаданными.</param>
    /// <returns>Редактор; пусто — не нашлось даже текстового: строку покажут только чтением.</returns>
    public ChosenEditor? EditorFor(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var editors = _editors?.Items ?? [];

        foreach (var step in Steps(property))
        {
            var chosen = editors
                .Select((editor, index) => (Editor: editor, Index: index, Mark: MarksOf(editor).FirstOrDefault(step)))
                .Where(candidate => candidate.Mark is not null)
                .OrderBy(candidate => candidate.Mark!.Fallback)
                .ThenBy(candidate => candidate.Editor.IsBuiltIn)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault(candidate => Accepts(candidate.Editor, property));

            if (chosen.Editor is not null)
            {
                var layout = chosen.Editor.TryGet("layout of a property", editor => editor.LayoutOf(property), out var asked)
                    ? asked
                    : InspectorFieldLayout.Inline;

                return new ChosenEditor(chosen.Editor, layout);
            }
        }

        return null;
    }

    /// <summary>Разделы для цели: тип совпал и раздел согласился.</summary>
    /// <param name="type">Тип цели.</param>
    /// <param name="properties">Её свойства с метаданными.</param>
    public IReadOnlyList<IStudioContribution<InspectorSection>> SectionsFor(
        InspectorTypeInfo type, IReadOnlyList<InspectorProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(properties);

        return (_sections?.Items ?? [])
            .Where(section => MarksOf(section)
                .Any(mark => string.Equals(mark.TypeName, type.FullName, StringComparison.Ordinal) ||
                             (mark.ForDerived && type.Is(mark.TypeName))))
            .Where(section => section.TryGet("accepts a target", it => it.Accepts(type, properties), out var accepts) && accepts)
            .ToArray();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var set in new object?[] { _editors, _sections, _menus })
        {
            if (set is IStudioContributionSet<StudioContribution> contributionSet)
                contributionSet.Changed -= OnSetChanged;
        }

        if (_metadata is not null)
            _metadata.Changed -= OnMetadataChanged;

        Forget();
    }

    /// <summary>Ступени цепочки: каждая отвечает, подходит ли отметка редактора на этой ступени.</summary>
    private static IEnumerable<Func<PropertyEditorAttribute, bool>> Steps(InspectorProperty property)
    {
        var type = property.ValueType;

        if (property.Editor is { Length: > 0 } hint)
            yield return mark => string.Equals(mark.Hint, hint, StringComparison.Ordinal);

        // Только чтение — раньше типа: поле, в которое нельзя писать, не должно выглядеть полем.
        if (property.IsReadOnly || !property.AcceptsText)
        {
            yield return static mark => string.Equals(mark.Hint, ReadOnlyHint, StringComparison.Ordinal);
            yield break;
        }

        yield return mark => mark.Hint is null && string.Equals(mark.TypeName, type.FullName, StringComparison.Ordinal);

        if (type.GenericDefinition is { } definition)
            yield return mark => mark.Hint is null && string.Equals(mark.TypeName, definition, StringComparison.Ordinal);

        // Перечисление — наследник System.Enum, даже если источник, описывая базы, его не назвал: так описывает их
        // служба XAML, а редактор перечислений отмечен именно им.
        var bases = type.IsEnum && !type.BaseTypes.Contains(EnumType, StringComparer.Ordinal)
            ? type.BaseTypes.Append(EnumType)
            : type.BaseTypes;

        foreach (var name in bases.Concat(type.Interfaces))
            yield return mark => mark.Hint is null && mark.ForDerived && string.Equals(mark.TypeName, name, StringComparison.Ordinal);

        yield return static mark => string.Equals(mark.Hint, TextHint, StringComparison.Ordinal);
        yield return static mark => string.Equals(mark.Hint, ReadOnlyHint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отметки редактора — один раз на состав набора: атрибуты читаются отражением через шов, а цепочка спрашивает
    /// их у каждого редактора на каждой ступени для каждого свойства — на всякую смену цели.
    /// </summary>
    private PropertyEditorAttribute[] MarksOf(IStudioContribution<PropertyEditor> editor)
    {
        if (!_editorMarks.TryGetValue(editor, out var marks))
            _editorMarks[editor] = marks = [.. editor.Attributes<PropertyEditorAttribute>()];

        return marks;
    }

    /// <summary>Отметки раздела — тоже один раз на состав набора.</summary>
    private InspectorSectionAttribute[] MarksOf(IStudioContribution<InspectorSection> section)
    {
        if (!_sectionMarks.TryGetValue(section, out var marks))
            _sectionMarks[section] = marks = [.. section.Attributes<InspectorSectionAttribute>()];

        return marks;
    }

    private static bool Accepts(IStudioContribution<PropertyEditor> editor, InspectorProperty property) =>
        editor.TryGet("accepts a property", it => it.Accepts(property), out var accepts) && accepts;

    /// <summary>Типы цели от общего к частному: интерфейсы, базы от дальней, сам тип.</summary>
    private static string[] Chain(InspectorTypeInfo type) =>
        [.. type.Interfaces, .. type.BaseTypes.Reverse(), type.FullName];

    /// <summary>Накладывает слой на свойство: типы — от общего к частному, поле за полем.</summary>
    private static InspectorProperty Apply(InspectorProperty property, IReadOnlyList<InspectorTypeHints> layer, string[] chain)
    {
        foreach (var typeName in chain)
        {
            foreach (var hints in layer)
            {
                if (string.Equals(hints.TypeName, typeName, StringComparison.Ordinal) &&
                    hints.Properties.TryGetValue(property.Id, out var described))
                {
                    property = Merge(property, described);
                }
            }
        }

        return property;
    }

    private static InspectorProperty Merge(InspectorProperty property, InspectorPropertyHints hints)
    {
        var constraints = property.Constraints with
        {
            Minimum = hints.Minimum ?? property.Constraints.Minimum,
            Maximum = hints.Maximum ?? property.Constraints.Maximum,
            Step = hints.Step ?? property.Constraints.Step,
            Decimals = hints.Decimals ?? property.Constraints.Decimals,
            AllowsAuto = hints.AllowsAuto ?? property.Constraints.AllowsAuto,
            Multiline = hints.Multiline ?? property.Constraints.Multiline,
            Choices = hints.Choices ?? property.Constraints.Choices,
            ChoicesOnly = hints.ChoicesOnly ?? property.Constraints.ChoicesOnly,
        };

        return property with
        {
            Category = hints.Category ?? property.Category,
            Order = hints.Order ?? property.Order,
            DisplayName = hints.Title ?? property.DisplayName,
            Description = hints.Description ?? property.Description,
            IsHidden = hints.Hidden ?? property.IsHidden,
            IsAdvanced = hints.Advanced ?? property.IsAdvanced,

            // Таблица может закрыть свойство для записи, но не открыть: что источник не пишет, то не пишется.
            IsReadOnly = property.IsReadOnly || hints.ReadOnly == true,
            Editor = hints.Editor ?? property.Editor,
            Constraints = constraints,
            Decorations = hints.Decorations ?? property.Decorations,
        };
    }

    /// <summary>Таблицы метаданных — по разу на состав набора, двумя слоями.</summary>
    private Layers Describe()
    {
        if (_layers is { } layers)
            return layers;

        var modules = new List<InspectorTypeHints>();
        var plugins = new List<InspectorTypeHints>();
        var categories = new Dictionary<string, InspectorCategory>(StringComparer.Ordinal);
        var described = 0;
        var subscribed = new List<IStudioContribution<InspectorMetadata>>();

        // Модули раньше плагинов: категорию, которую описали оба, заголовком и местом называет плагин.
        foreach (var metadata in (_metadata?.Items ?? []).OrderBy(item => !item.IsBuiltIn))
        {
            var builder = new InspectorMetadataBuilder();

            if (!metadata.Run("describes the inspector", it => it.Describe(builder)))
                continue;

            (metadata.IsBuiltIn ? modules : plugins).AddRange(builder.Types);

            foreach (var hints in builder.Categories)
            {
                var known = categories.TryGetValue(hints.Id, out var before) ? before : null;

                categories[hints.Id] = new InspectorCategory(
                    hints.Id,
                    hints.Title ?? known?.Title ?? hints.Id,
                    hints.Order ?? known?.Order ?? 1000 + described,
                    hints.Collapsed ?? known?.Collapsed ?? false);
                described++;
            }

            if (metadata.Run("listens to its invalidation", it => it.Invalidated += OnInvalidated))
                subscribed.Add(metadata);
        }

        _layers = new Layers(modules, plugins, categories, subscribed);

        return _layers;
    }

    /// <summary>Забывает таблицы: следующий вопрос спросит их заново.</summary>
    private void Forget()
    {
        if (_layers is not { } layers)
            return;

        _layers = null;

        foreach (var metadata in layers.Subscribed)
            metadata.Farewell("stops listening to its invalidation", it => it.Invalidated -= OnInvalidated);
    }

    private void OnInvalidated(object? sender, EventArgs e)
    {
        Forget();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMetadataChanged(object? sender, EventArgs e)
    {
        Forget();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSetChanged(object? sender, EventArgs e)
    {
        _editorMarks.Clear();
        _sectionMarks.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Таблицы двумя слоями и разделы.</summary>
    private sealed record Layers(
        IReadOnlyList<InspectorTypeHints> Modules,
        IReadOnlyList<InspectorTypeHints> Plugins,
        IReadOnlyDictionary<string, InspectorCategory> Categories,
        IReadOnlyList<IStudioContribution<InspectorMetadata>> Subscribed);
}
