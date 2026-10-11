using System.Reflection;
using ArxisStudio.Sdk;
using Avalonia;

namespace ArxisStudio.Inspector;

/// <summary>
/// Метаданные инспектора: разделы, порядок, подписи, скрытость и ограничения свойств — для типов, которые сами о
/// себе этого не говорят.
/// </summary>
/// <remarks>
/// <para>
/// У Unity это атрибуты на полях. У Avalonia своих разделов нет, а у чужих типов атрибуты не напишешь —
/// поэтому то же самое объявляют таблицей: модуль инспектора — для Avalonia, плагин — для своей библиотеки
/// контролов. Проект пользователя описывает свои контролы стандартными атрибутами .NET, их цель приносит сама.
/// </para>
/// <para>
/// <b>Слои.</b> Атрибуты источника — низший слой, над ними таблицы модулей, над ними таблицы плагинов. Слои
/// сливаются по полю: плагин, назвавший свойству только диапазон, раздела модуля не теряет. Внутри слоя
/// побеждает описание более производного типа.
/// </para>
/// <para>
/// Таблицу студия спрашивает один раз и помнит; переспросить её — <see cref="Invalidate"/>: сменился язык, и
/// подписи разделов нужны другие.
/// </para>
/// </remarks>
public abstract class InspectorMetadata : StudioContribution
{
    /// <summary>Таблица устарела: студия спросит её заново.</summary>
    public event EventHandler? Invalidated;

    /// <summary>Описывает разделы и свойства.</summary>
    /// <param name="metadata">Куда описывать.</param>
    public abstract void Describe(InspectorMetadataBuilder metadata);

    /// <summary>Просит студию спросить таблицу заново.</summary>
    protected void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
}

/// <summary>Куда описывают метаданные: разделы и типы.</summary>
public sealed class InspectorMetadataBuilder
{
    private readonly List<InspectorCategoryHints> _categories = [];
    private readonly Dictionary<string, InspectorTypeHints> _types = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>Описанные разделы — в порядке описания.</summary>
    public IReadOnlyList<InspectorCategoryHints> Categories => _categories;

    /// <summary>Описанные типы — в порядке первого описания.</summary>
    public IReadOnlyList<InspectorTypeHints> Types => _order.Select(name => _types[name]).ToArray();

    /// <summary>Раздел.</summary>
    /// <param name="id">Идентификатор, который называют свойства: <c>layout</c>.</param>
    public InspectorCategoryBuilder Category(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var hints = _categories.Find(category => string.Equals(category.Id, id, StringComparison.Ordinal));

        if (hints is null)
        {
            hints = new InspectorCategoryHints(id);
            _categories.Add(hints);
        }

        return new InspectorCategoryBuilder(hints);
    }

    /// <summary>Тип по полному имени — для типов, которых у описывающего нет: <c>MyLib.Card</c>.</summary>
    /// <param name="typeName">Полное имя типа.</param>
    public InspectorTypeBuilder For(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        return new InspectorTypeBuilder(HintsOf(typeName), type: null);
    }

    /// <summary>Тип.</summary>
    /// <typeparam name="T">Тип.</typeparam>
    public InspectorTypeBuilder For<T>() => new(HintsOf(InspectorTypeInfo.NameOf(typeof(T))), typeof(T));

    private InspectorTypeHints HintsOf(string typeName)
    {
        if (!_types.TryGetValue(typeName, out var hints))
        {
            hints = new InspectorTypeHints(typeName);
            _types[typeName] = hints;
            _order.Add(typeName);
        }

        return hints;
    }
}

/// <summary>Описание раздела.</summary>
/// <param name="Id">Идентификатор раздела.</param>
public sealed class InspectorCategoryHints(string Id)
{
    /// <summary>Идентификатор раздела.</summary>
    public string Id { get; } = Id;

    /// <summary>Заголовок; пусто — идентификатор.</summary>
    public string? Title { get; internal set; }

    /// <summary>Порядок среди разделов; пусто — после описанных, по заголовку.</summary>
    public int? Order { get; internal set; }

    /// <summary>Раздел свёрнут, пока его не раскроют.</summary>
    public bool? Collapsed { get; internal set; }
}

/// <summary>Описание раздела — по цепочке.</summary>
public sealed class InspectorCategoryBuilder
{
    private readonly InspectorCategoryHints _hints;

    internal InspectorCategoryBuilder(InspectorCategoryHints hints) => _hints = hints;

    /// <summary>Заголовок раздела.</summary>
    /// <param name="title">Текст — уже переведённый: свои строки описывающий берёт у своего контекста.</param>
    public InspectorCategoryBuilder Title(string title)
    {
        _hints.Title = title;
        return this;
    }

    /// <summary>Порядок среди разделов.</summary>
    /// <param name="order">Меньше — выше.</param>
    public InspectorCategoryBuilder Order(int order)
    {
        _hints.Order = order;
        return this;
    }

    /// <summary>Раздел свёрнут, пока его не раскроют.</summary>
    /// <param name="collapsed">Свёрнут ли.</param>
    public InspectorCategoryBuilder Collapsed(bool collapsed = true)
    {
        _hints.Collapsed = collapsed;
        return this;
    }
}

/// <summary>Описание типа: его свойства.</summary>
/// <param name="TypeName">Полное имя типа.</param>
public sealed class InspectorTypeHints(string TypeName)
{
    private readonly Dictionary<string, InspectorPropertyHints> _properties = new(StringComparer.Ordinal);

    /// <summary>Полное имя типа.</summary>
    public string TypeName { get; } = TypeName;

    /// <summary>Описанные свойства по идентификатору.</summary>
    public IReadOnlyDictionary<string, InspectorPropertyHints> Properties => _properties;

    internal InspectorPropertyHints Property(string id)
    {
        if (!_properties.TryGetValue(id, out var hints))
        {
            hints = new InspectorPropertyHints();
            _properties[id] = hints;
        }

        return hints;
    }
}

/// <summary>Описание типа — по цепочке.</summary>
public sealed class InspectorTypeBuilder
{
    private readonly InspectorTypeHints _hints;
    private readonly Type? _type;

    internal InspectorTypeBuilder(InspectorTypeHints hints, Type? type)
    {
        _hints = hints;
        _type = type;
    }

    /// <summary>Описывает свойство по идентификатору: <c>Width</c>, <c>Grid.Row</c>, <c>ToolTip.Tip</c>.</summary>
    /// <param name="id">Идентификатор свойства.</param>
    /// <param name="describe">Описание.</param>
    public InspectorTypeBuilder Property(string id, Action<InspectorPropertyBuilder> describe)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(describe);

        describe(new InspectorPropertyBuilder(_hints.Property(id)));
        return this;
    }

    /// <summary>Описывает свойство Avalonia; присоединённое — с владельцем: <c>Grid.Row</c>.</summary>
    /// <param name="property">Свойство.</param>
    /// <param name="describe">Описание.</param>
    /// <remarks>
    /// Присоединённое, у которого описываемый тип держит открытую обёртку, — <c>Visual.FlowDirection</c>,
    /// <c>FontSize</c> у <c>TemplatedControl</c> (оно присоединённое у <c>TextElement</c>), — называется простым именем:
    /// так его и пишет разметка, и так его называет источник. С владельцем такое описание не находило свойства
    /// (запись 373). Тип, описанный по имени (<see cref="InspectorMetadataBuilder.For(string)"/>), обёрток не
    /// показывает, и присоединённое у него — всегда с владельцем.
    /// </remarks>
    public InspectorTypeBuilder Property(AvaloniaProperty property, Action<InspectorPropertyBuilder> describe)
    {
        ArgumentNullException.ThrowIfNull(property);

        return Property(Wrapped(property) ? property.Name : IdOf(property), describe);
    }

    /// <summary>Прячет свойства: видны только среди всех свойств.</summary>
    /// <param name="ids">Идентификаторы.</param>
    public InspectorTypeBuilder Hide(params string[] ids) => Each(ids, property => property.Hidden());

    /// <summary>Делает свойства дополнительными: видны, когда просят все свойства.</summary>
    /// <param name="ids">Идентификаторы.</param>
    public InspectorTypeBuilder Advanced(params string[] ids) => Each(ids, property => property.Advanced());

    /// <summary>Делает свойства только читаемыми.</summary>
    /// <param name="ids">Идентификаторы.</param>
    public InspectorTypeBuilder ReadOnly(params string[] ids) => Each(ids, property => property.ReadOnly());

    /// <summary>Ставит свойства в раздел — по порядку перечисления.</summary>
    /// <param name="category">Раздел.</param>
    /// <param name="ids">Идентификаторы.</param>
    public InspectorTypeBuilder Category(string category, params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        for (var at = 0; at < ids.Length; at++)
        {
            var order = at;

            Property(ids[at], property => property.Category(category).Order(order));
        }

        return this;
    }

    /// <summary>Идентификатор свойства Avalonia: у присоединённого — с владельцем.</summary>
    /// <param name="property">Свойство.</param>
    public static string IdOf(AvaloniaProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.IsAttached ? property.OwnerType.Name + "." + property.Name : property.Name;
    }

    /// <summary>
    /// Держит ли описываемый тип присоединённое своим: реестр Avalonia числит его за типом, и у типа есть открытая
    /// обёртка с записью. Тем же правилом простое имя даёт член загрузчик разметки.
    /// </summary>
    /// <remarks>
    /// Одного имени обёртки мало: <c>Name</c> у <c>Control</c> — своё свойство, а не обёртка
    /// <c>AutomationProperties.Name</c>. Обёртка ищется перебором, а не по имени: свойство, скрытое наследником через
    /// <c>new</c>, дало бы неоднозначность.
    /// </remarks>
    private bool Wrapped(AvaloniaProperty property) =>
        property.IsAttached && _type is { } type &&
        AvaloniaPropertyRegistry.Instance.GetRegistered(type).Contains(property) &&
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(wrapper => wrapper.Name == property.Name && wrapper.SetMethod is { IsPublic: true });

    private InspectorTypeBuilder Each(string[] ids, Action<InspectorPropertyBuilder> describe)
    {
        ArgumentNullException.ThrowIfNull(ids);

        foreach (var id in ids)
            Property(id, describe);

        return this;
    }
}

/// <summary>Описание свойства: каждое поле — пусто, пока его не назвали; слои сливаются по полю.</summary>
public sealed class InspectorPropertyHints
{
    /// <summary>Раздел.</summary>
    public string? Category { get; internal set; }

    /// <summary>Порядок внутри раздела.</summary>
    public int? Order { get; internal set; }

    /// <summary>Подпись.</summary>
    public string? Title { get; internal set; }

    /// <summary>Пояснение.</summary>
    public string? Description { get; internal set; }

    /// <summary>Скрыто.</summary>
    public bool? Hidden { get; internal set; }

    /// <summary>Дополнительное.</summary>
    public bool? Advanced { get; internal set; }

    /// <summary>Только читается.</summary>
    public bool? ReadOnly { get; internal set; }

    /// <summary>Нижняя граница числа.</summary>
    public double? Minimum { get; internal set; }

    /// <summary>Верхняя граница числа.</summary>
    public double? Maximum { get; internal set; }

    /// <summary>Шаг.</summary>
    public double? Step { get; internal set; }

    /// <summary>Знаков после точки.</summary>
    public int? Decimals { get; internal set; }

    /// <summary>Может быть «авто».</summary>
    public bool? AllowsAuto { get; internal set; }

    /// <summary>Многострочный текст.</summary>
    public bool? Multiline { get; internal set; }

    /// <summary>Подсказка редактора.</summary>
    public string? Editor { get; internal set; }

    /// <summary>Варианты значения.</summary>
    public IReadOnlyList<InspectorChoice>? Choices { get; internal set; }

    /// <summary>Значение — только из вариантов.</summary>
    public bool? ChoicesOnly { get; internal set; }

    /// <summary>Свойство видно всегда — и тогда, когда цель его не показывает: <c>ToolTip.Tip</c>.</summary>
    public bool? Always { get; internal set; }

    /// <summary>Заголовки, воздух и пояснения.</summary>
    public IReadOnlyList<InspectorDecoration>? Decorations { get; internal set; }
}

/// <summary>Описание свойства — по цепочке.</summary>
public sealed class InspectorPropertyBuilder
{
    private readonly InspectorPropertyHints _hints;

    internal InspectorPropertyBuilder(InspectorPropertyHints hints) => _hints = hints;

    /// <summary>Раздел.</summary>
    /// <param name="category">Идентификатор раздела.</param>
    public InspectorPropertyBuilder Category(string category)
    {
        _hints.Category = category;
        return this;
    }

    /// <summary>Порядок внутри раздела.</summary>
    /// <param name="order">Меньше — выше.</param>
    public InspectorPropertyBuilder Order(int order)
    {
        _hints.Order = order;
        return this;
    }

    /// <summary>Подпись строки — уже переведённая.</summary>
    /// <param name="title">Текст.</param>
    public InspectorPropertyBuilder Title(string title)
    {
        _hints.Title = title;
        return this;
    }

    /// <summary>Пояснение — подсказкой у подписи.</summary>
    /// <param name="description">Текст.</param>
    public InspectorPropertyBuilder Description(string description)
    {
        _hints.Description = description;
        return this;
    }

    /// <summary>Скрыто: видно только среди всех свойств.</summary>
    /// <param name="hidden">Скрыто ли.</param>
    public InspectorPropertyBuilder Hidden(bool hidden = true)
    {
        _hints.Hidden = hidden;
        return this;
    }

    /// <summary>Дополнительное: видно, когда просят все свойства.</summary>
    /// <param name="advanced">Дополнительное ли.</param>
    public InspectorPropertyBuilder Advanced(bool advanced = true)
    {
        _hints.Advanced = advanced;
        return this;
    }

    /// <summary>Только читается.</summary>
    /// <param name="readOnly">Только ли читается.</param>
    public InspectorPropertyBuilder ReadOnly(bool readOnly = true)
    {
        _hints.ReadOnly = readOnly;
        return this;
    }

    /// <summary>Границы числа.</summary>
    /// <param name="minimum">Нижняя.</param>
    /// <param name="maximum">Верхняя.</param>
    public InspectorPropertyBuilder Range(double minimum, double maximum)
    {
        _hints.Minimum = minimum;
        _hints.Maximum = maximum;
        return this;
    }

    /// <summary>Шаг стрелки и мера протяжки.</summary>
    /// <param name="step">Шаг.</param>
    public InspectorPropertyBuilder Step(double step)
    {
        _hints.Step = step;
        return this;
    }

    /// <summary>Знаков после точки.</summary>
    /// <param name="decimals">Сколько.</param>
    public InspectorPropertyBuilder Decimals(int decimals)
    {
        _hints.Decimals = decimals;
        return this;
    }

    /// <summary>Число может быть «авто» — размер по содержимому.</summary>
    /// <param name="auto">Может ли.</param>
    public InspectorPropertyBuilder Auto(bool auto = true)
    {
        _hints.AllowsAuto = auto;
        return this;
    }

    /// <summary>Многострочный текст.</summary>
    /// <param name="multiline">Многострочный ли.</param>
    public InspectorPropertyBuilder Multiline(bool multiline = true)
    {
        _hints.Multiline = multiline;
        return this;
    }

    /// <summary>Подсказка редактора: идентификатор, который знает <see cref="PropertyEditor"/>.</summary>
    /// <param name="editor">Идентификатор.</param>
    public InspectorPropertyBuilder Editor(string editor)
    {
        _hints.Editor = editor;
        return this;
    }

    /// <summary>Варианты значения.</summary>
    /// <param name="choices">Варианты.</param>
    public InspectorPropertyBuilder Choices(params InspectorChoice[] choices)
    {
        _hints.Choices = choices;
        return this;
    }

    /// <summary>Значение — только из вариантов.</summary>
    /// <param name="only">Только ли.</param>
    public InspectorPropertyBuilder ChoicesOnly(bool only = true)
    {
        _hints.ChoicesOnly = only;
        return this;
    }

    /// <summary>Свойство видно всегда — и тогда, когда цель его сама не показывает.</summary>
    /// <param name="always">Всегда ли.</param>
    public InspectorPropertyBuilder Always(bool always = true)
    {
        _hints.Always = always;
        return this;
    }

    /// <summary>Заголовок над строкой.</summary>
    /// <param name="text">Текст.</param>
    public InspectorPropertyBuilder Header(string text) => Decorate(new InspectorDecoration(InspectorDecorationKind.Header, text));

    /// <summary>Воздух над строкой.</summary>
    public InspectorPropertyBuilder Space() => Decorate(new InspectorDecoration(InspectorDecorationKind.Space));

    /// <summary>Пояснение под полем.</summary>
    /// <param name="text">Текст.</param>
    public InspectorPropertyBuilder Help(string text) => Decorate(new InspectorDecoration(InspectorDecorationKind.Help, text));

    private InspectorPropertyBuilder Decorate(InspectorDecoration decoration)
    {
        _hints.Decorations = [.. _hints.Decorations ?? [], decoration];
        return this;
    }
}
