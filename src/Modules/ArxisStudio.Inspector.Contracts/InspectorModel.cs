using System.Globalization;
using Avalonia.Media;

namespace ArxisStudio.Inspector;

/// <summary>Шапка цели: что показано и чем её править, кроме строк.</summary>
/// <param name="Title">Имя цели: имя элемента, «3 элемента», имя объекта.</param>
/// <remarks>
/// Шапка стоит над списком и прилипает к верху, как у Unity: флажок «включён», значок, имя и тип, а у
/// элемента разметки — имя в документе и классы стиля. Свойства шапки называют строки цели по
/// идентификатору: в списке такие строки не повторяются.
/// </remarks>
public sealed record InspectorHeader(string Title)
{
    /// <summary>Вторая строка: тип, пространство, сборка.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Значок — контур в клетке 16: <c>AxIcons.Window</c> или свой.</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Свойство-флажок «включён» рядом с именем: <c>IsVisible</c>, <c>IsEnabled</c>.</summary>
    public string? EnabledProperty { get; init; }

    /// <summary>Свойство имени в шапке: <c>x:Name</c>.</summary>
    public string? NameProperty { get; init; }

    /// <summary>Свойство жетонов в шапке: <c>Classes</c>.</summary>
    public string? TagsProperty { get; init; }

    /// <summary>Строка над списком: почему что-то не показано — «типы проекта не загружены».</summary>
    public string? Notice { get; init; }
}

/// <summary>
/// Тип именами, а не <see cref="Type"/>: цель может жить в поколении типов, которое заменят, и держать его
/// инспектору нельзя.
/// </summary>
/// <param name="FullName">Полное имя: <c>Avalonia.Controls.Button</c>; у обобщённого — с аргументами в квадратных скобках.</param>
public sealed record InspectorTypeInfo(string FullName)
{
    /// <summary>Базовые типы — от ближайшего к <see cref="object"/>, его не считая.</summary>
    public IReadOnlyList<string> BaseTypes { get; init; } = [];

    /// <summary>Интерфейсы.</summary>
    public IReadOnlyList<string> Interfaces { get; init; } = [];

    /// <summary>Обобщённое определение — <c>System.Collections.Generic.List`1</c>; пусто у необобщённого.</summary>
    public string? GenericDefinition { get; init; }

    /// <summary>Имена значений перечисления; пусто у не-перечисления.</summary>
    public IReadOnlyList<string> EnumNames { get; init; } = [];

    /// <summary>Перечисление — флаги: значения складываются.</summary>
    public bool IsFlags { get; init; }

    /// <summary>Значение может отсутствовать: <c>Nullable&lt;T&gt;</c>; имя — у самого <c>T</c>.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Тип — перечисление.</summary>
    public bool IsEnum => EnumNames.Count > 0;

    /// <summary>Тип — этот, его наследник или реализует такой интерфейс.</summary>
    /// <param name="fullName">Полное имя типа.</param>
    public bool Is(string fullName) =>
        string.Equals(FullName, fullName, StringComparison.Ordinal) ||
        string.Equals(GenericDefinition, fullName, StringComparison.Ordinal) ||
        BaseTypes.Contains(fullName, StringComparer.Ordinal) ||
        Interfaces.Contains(fullName, StringComparer.Ordinal);

    /// <summary>Сведения о типе CLR — именами.</summary>
    /// <param name="type">Тип; <c>Nullable&lt;T&gt;</c> описывается как <c>T</c> с <see cref="IsNullable"/>.</param>
    public static InspectorTypeInfo Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var underlying = Nullable.GetUnderlyingType(type);
        var actual = underlying ?? type;
        var bases = new List<string>();

        for (var at = actual.BaseType; at is not null && at != typeof(object); at = at.BaseType)
            bases.Add(NameOf(at));

        return new InspectorTypeInfo(NameOf(actual))
        {
            BaseTypes = bases,
            Interfaces = actual.GetInterfaces().Select(NameOf).Distinct(StringComparer.Ordinal).ToArray(),
            GenericDefinition = actual.IsGenericType ? actual.GetGenericTypeDefinition().FullName : null,
            EnumNames = actual.IsEnum ? Enum.GetNames(actual) : [],
            IsFlags = actual.IsEnum && actual.IsDefined(typeof(FlagsAttribute), inherit: false),
            IsNullable = underlying is not null || !actual.IsValueType,
        };
    }

    /// <summary>Имя типа: полное, у обобщённого — с аргументами, без сборок.</summary>
    /// <param name="type">Тип.</param>
    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsGenericType || type.IsGenericTypeDefinition)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{definition}[{string.Join(",", type.GetGenericArguments().Select(NameOf))}]");
    }
}

/// <summary>Как устроено свойство — чем его читают и пишут.</summary>
public enum InspectorPropertyKind
{
    /// <summary>Свойство со стилем Avalonia: у него есть приоритеты, и его можно показать на ходу.</summary>
    Styled,

    /// <summary>Прямое свойство Avalonia: значение одно, без приоритетов.</summary>
    Direct,

    /// <summary>Присоединённое свойство: <c>Grid.Row</c>.</summary>
    Attached,

    /// <summary>Свойство CLR.</summary>
    Clr,

    /// <summary>Директива разметки: <c>x:Name</c>, <c>d:DesignWidth</c>.</summary>
    Directive,

    /// <summary>Что-то своё источника.</summary>
    Other,
}

/// <summary>Чем ограничено значение: границы, шаг, варианты.</summary>
public sealed record InspectorConstraints
{
    /// <summary>Нет ограничений.</summary>
    public static InspectorConstraints None { get; } = new();

    /// <summary>Нижняя граница числа.</summary>
    public double? Minimum { get; init; }

    /// <summary>Верхняя граница числа.</summary>
    public double? Maximum { get; init; }

    /// <summary>Шаг стрелки и мера протяжки.</summary>
    public double? Step { get; init; }

    /// <summary>Сколько знаков после точки.</summary>
    public int? Decimals { get; init; }

    /// <summary>Число может быть «авто» — <c>NaN</c>: размер по содержимому.</summary>
    public bool AllowsAuto { get; init; }

    /// <summary>Текст многострочный.</summary>
    public bool Multiline { get; init; }

    /// <summary>Варианты значения: подсказки, а у <see cref="ChoicesOnly"/> — единственно возможные.</summary>
    public IReadOnlyList<InspectorChoice> Choices { get; init; } = [];

    /// <summary>Значение — только из вариантов.</summary>
    public bool ChoicesOnly { get; init; }
}

/// <summary>Вариант значения: строка списка, сегмент, подсказка набора.</summary>
/// <param name="Value">Текст значения — так его пишут.</param>
public sealed record InspectorChoice(string Value)
{
    /// <summary>Подпись; пусто — само значение.</summary>
    public string? Title { get; init; }

    /// <summary>Значок — контур в клетке 16: сегмент показывает его вместо подписи, а подпись говорит диктору.</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Пояснение — подсказкой.</summary>
    public string? Description { get; init; }
}

/// <summary>Что стоит над строкой свойства или под ней.</summary>
public enum InspectorDecorationKind
{
    /// <summary>Заголовок над строкой — как <c>[Header]</c> у Unity.</summary>
    Header,

    /// <summary>Воздух над строкой — как <c>[Space]</c>.</summary>
    Space,

    /// <summary>Пояснение под полем — как <c>HelpBox</c>.</summary>
    Help,
}

/// <summary>Украшение строки: заголовок, воздух или пояснение.</summary>
/// <param name="Kind">Что это.</param>
/// <param name="Text">Текст заголовка или пояснения.</param>
public sealed record InspectorDecoration(InspectorDecorationKind Kind, string? Text = null);

/// <summary>Свойство цели.</summary>
/// <param name="Id">Идентификатор у цели: так его спрашивают и пишут — <c>Width</c>, <c>Grid.Row</c>, <c>x:Name</c>.</param>
/// <param name="ValueType">Тип значения.</param>
/// <remarks>
/// Свойство рассказывает о себе то, что знает его источник: атрибуты .NET автора, вид члена, ограничения.
/// Остальное — раздел, порядок, подписи, скрытость — дописывают метаданные модулей и плагинов
/// (<see cref="InspectorMetadata"/>): у Avalonia своих разделов нет.
/// </remarks>
public sealed record InspectorProperty(string Id, InspectorTypeInfo ValueType)
{
    /// <summary>Подпись строки; пусто — идентификатор.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Раздел — идентификатор, который знают метаданные: <c>layout</c>, <c>brush</c>; или имя раздела.</summary>
    public string? Category { get; init; }

    /// <summary>Порядок внутри раздела; равные — по имени.</summary>
    public int Order { get; init; }

    /// <summary>Пояснение — подсказкой у подписи.</summary>
    public string? Description { get; init; }

    /// <summary>Как устроено свойство.</summary>
    public InspectorPropertyKind Kind { get; init; }

    /// <summary>Тип-владелец: у присоединённого — <c>Avalonia.Controls.Grid</c>.</summary>
    public string? OwnerTypeName { get; init; }

    /// <summary>Только читается: поле — выделяемый текст.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Дополнительное: видно, когда просят все свойства.</summary>
    public bool IsAdvanced { get; init; }

    /// <summary>Скрытое: видно только среди всех свойств.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Значение пишется текстом: без этого строку правит только свой редактор.</summary>
    public bool AcceptsText { get; init; } = true;

    /// <summary>Подсказка редактора: идентификатор, который знает <see cref="PropertyEditor"/>.</summary>
    public string? Editor { get; init; }

    /// <summary>Чем ограничено значение.</summary>
    public InspectorConstraints Constraints { get; init; } = InspectorConstraints.None;

    /// <summary>Значение по умолчанию текстом — им строка показывает, к чему вернёт сброс.</summary>
    public string? DefaultText { get; init; }

    /// <summary>Заголовки, воздух и пояснения у строки.</summary>
    public IReadOnlyList<InspectorDecoration> Decorations { get; init; } = [];

    /// <summary>Подпись строки: <see cref="DisplayName"/> или идентификатор.</summary>
    public string Label => DisplayName ?? Id;
}

/// <summary>Откуда значение пришло: то, что строка говорит тоном и подсказкой.</summary>
public enum InspectorValueSource
{
    /// <summary>По умолчанию: никто не задавал.</summary>
    Default,

    /// <summary>Задано здесь: написано у самой цели — полоса акцента и полужирная подпись.</summary>
    Local,

    /// <summary>Задано для дизайна: <c>d:</c> — видно только в дизайнере.</summary>
    Design,

    /// <summary>Выражение: привязка, ресурс — значение вычисляется.</summary>
    Expression,

    /// <summary>Стиль.</summary>
    Style,

    /// <summary>Шаблон хозяина.</summary>
    Template,

    /// <summary>Унаследовано от предка.</summary>
    Inherited,

    /// <summary>Анимация.</summary>
    Animation,

    /// <summary>Поставлено кодом.</summary>
    Code,
}

/// <summary>Значение свойства у цели.</summary>
/// <param name="Text">Значение текстом — так его показывают и правят; пусто — значения нет.</param>
public sealed record InspectorValue(string? Text)
{
    /// <summary>Значения нет: свойство не задано и не вычислено.</summary>
    public static InspectorValue Empty { get; } = new((string?)null);

    /// <summary>Что написано у самой цели — например, выражение привязки; пусто — не написано.</summary>
    public string? Written { get; init; }

    /// <summary>Само значение, если его тип из общих сборок и не меняется: число, <c>Thickness</c>, цвет.</summary>
    public object? Shared { get; init; }

    /// <summary>Откуда значение.</summary>
    public InspectorValueSource Source { get; init; }

    /// <summary>Уточнение источника: селектор стиля, предок, выражение.</summary>
    public string? SourceDetail { get; init; }

    /// <summary>Значения у выбранных расходятся: <see cref="Text"/> — значение первого.</summary>
    public bool IsMixed { get; init; }

    /// <summary>Что с написанным не так: строка показывает это заметкой под полем.</summary>
    public string? Error { get; init; }

    /// <summary>Значение задано у самой цели: здесь, для дизайна или выражением.</summary>
    public bool IsWritten =>
        Source is InspectorValueSource.Local or InspectorValueSource.Design or InspectorValueSource.Expression;
}

/// <summary>Запись значения.</summary>
/// <param name="PropertyId">Свойство.</param>
/// <param name="Text">Текст значения; пусто — сбросить к значению по умолчанию.</param>
public sealed record InspectorWrite(string PropertyId, string? Text);

/// <summary>Прочтётся ли текст значением.</summary>
/// <param name="IsValid">Прочтётся.</param>
/// <param name="Message">Почему нет — заметкой под полем.</param>
public readonly record struct InspectorCheck(bool IsValid, string? Message)
{
    /// <summary>Прочтётся.</summary>
    public static InspectorCheck Valid => new(true, null);

    /// <summary>Не прочтётся.</summary>
    /// <param name="message">Почему.</param>
    public static InspectorCheck Invalid(string message) => new(false, message);
}
