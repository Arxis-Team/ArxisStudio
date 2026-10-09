using System.Collections.Immutable;

namespace ArxisStudio.Xaml;

/// <summary>Что построил элемент показа — именами: тип объекта, его базы и тип родителя.</summary>
/// <param name="TypeFullName">Полное имя типа объекта: <c>Avalonia.Controls.Button</c>.</param>
/// <param name="AssemblyName">Простое имя сборки типа.</param>
/// <param name="BaseTypes">Полные имена базовых классов — от ближайшего, без <c>System.Object</c>.</param>
/// <param name="ParentTypeFullName">Полное имя типа родителя в документе; null — у корня.</param>
/// <param name="ParentBaseTypes">Базовые классы родителя — от ближайшего; пусто — у корня.</param>
/// <param name="HoldsChildren">
/// Объект — панель: детей у него может быть сколько угодно. Снять с элемента такую обёртку можно и тогда,
/// когда у него несколько детей.
/// </param>
/// <param name="ContentMember">
/// Член, в который ложится содержимое элемента (<c>[Content]</c> типа): <c>Content</c> у кнопки,
/// <c>Children</c> у панели; null — у типа его нет.
/// </param>
/// <remarks>
/// Имена, а не типы: тип — объект поколения, и держатель сведений держал бы его через замену. Родитель — тот,
/// что в документе, а не в дереве показа: у корня родитель в дереве — рамка дизайнера. Появилось в
/// контракте 1.1.
/// </remarks>
public sealed record XamlElementInfo(
    string TypeFullName,
    string AssemblyName,
    ImmutableArray<string> BaseTypes,
    string? ParentTypeFullName,
    ImmutableArray<string> ParentBaseTypes,
    bool HoldsChildren,
    string? ContentMember)
{
    /// <summary>Короткое имя типа — каким его пишет документ без префикса: <c>Button</c>.</summary>
    public string TypeName => Short(TypeFullName);

    /// <summary>Объект — этого типа или его наследник.</summary>
    /// <param name="typeFullName">Полное имя типа.</param>
    public bool Is(string typeFullName) =>
        string.Equals(TypeFullName, typeFullName, StringComparison.Ordinal) ||
        BaseTypes.Contains(typeFullName, StringComparer.Ordinal);

    internal static string Short(string fullName)
    {
        var generic = fullName.IndexOf('`', StringComparison.Ordinal);
        var name = generic < 0 ? fullName : fullName[..generic];
        var dot = name.LastIndexOf('.');

        return dot < 0 ? name : name[(dot + 1)..];
    }
}

/// <summary>Тип значения члена — именами: чем его править, решает тот, кто показывает.</summary>
/// <param name="FullName">Полное имя типа; у <c>Nullable</c> — имя того, что внутри.</param>
/// <param name="BaseTypes">Базовые классы — от ближайшего, без <c>System.Object</c> и <c>System.ValueType</c>.</param>
/// <param name="Interfaces">Интерфейсы типа: так редактор кисти узнаёт <c>IBrush</c>.</param>
/// <param name="EnumNames">Имена значений перечисления; пусто — тип не перечисление.</param>
/// <param name="IsFlags">Перечисление флагов: значения складываются.</param>
/// <param name="IsNullable">Значение может отсутствовать: <c>double?</c>, <c>bool?</c>.</param>
/// <remarks>Появилось в контракте 1.1.</remarks>
public sealed record XamlTypeInfo(
    string FullName,
    ImmutableArray<string> BaseTypes,
    ImmutableArray<string> Interfaces,
    ImmutableArray<string> EnumNames,
    bool IsFlags,
    bool IsNullable)
{
    /// <summary>Короткое имя типа: <c>Thickness</c>.</summary>
    public string Name => XamlElementInfo.Short(FullName);

    /// <summary>Тип — перечисление.</summary>
    public bool IsEnum => !EnumNames.IsDefaultOrEmpty;

    /// <summary>Тип — этот, его наследник или реализует его.</summary>
    /// <param name="typeFullName">Полное имя типа или интерфейса.</param>
    public bool Is(string typeFullName) =>
        string.Equals(FullName, typeFullName, StringComparison.Ordinal) ||
        BaseTypes.Contains(typeFullName, StringComparer.Ordinal) ||
        Interfaces.Contains(typeFullName, StringComparer.Ordinal);
}

/// <summary>Каким свойством член держит значение.</summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public enum XamlPropertyKind
{
    /// <summary>Свойство Avalonia со стилями, наследованием и приоритетами — <c>StyledProperty</c>.</summary>
    Styled,

    /// <summary>Прямое свойство Avalonia — поле объекта за <c>DirectProperty</c>: стили его не ставят.</summary>
    Direct,

    /// <summary>Присоединённое: его даёт не тип объекта, а владелец — у ребёнка <c>Canvas</c> это <c>Canvas.Left</c>.</summary>
    Attached,

    /// <summary>Обычное свойство CLR: ни стилей, ни приоритетов, источник значения — только документ.</summary>
    Clr,
}

/// <summary>Насколько член виден тому, кто пишет код, — как его пометил автор типа.</summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public enum XamlEditorBrowsable
{
    /// <summary>Виден всегда.</summary>
    Always,

    /// <summary>Виден тому, кто попросил больше: член для тонкой настройки.</summary>
    Advanced,

    /// <summary>Скрыт: член для инфраструктуры, а не для автора формы.</summary>
    Never,
}

/// <summary>
/// Откуда у объекта показа нынешнее значение члена — то, что Unity показывает отметкой переопределения.
/// </summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public enum XamlValueOrigin
{
    /// <summary>Ниоткуда: значение по умолчанию.</summary>
    Default,

    /// <summary>Его написал этот элемент документа — значением.</summary>
    Document,

    /// <summary>Его написал этот элемент для дизайна: <c>d:Width</c>, <c>d:DesignWidth</c>.</summary>
    Design,

    /// <summary>Этот элемент написал выражение: <c>{Binding …}</c>, <c>{DynamicResource …}</c>.</summary>
    Expression,

    /// <summary>Стиль или тема, включая стиль состояния: <c>Button:pointerover</c>.</summary>
    Style,

    /// <summary>Шаблон контрола, построившего этот объект.</summary>
    Template,

    /// <summary>Унаследовано от предка.</summary>
    Inherited,

    /// <summary>Анимация или переход.</summary>
    Animation,

    /// <summary>Код: значение поставлено объекту, а в документе его нет.</summary>
    Code,
}

/// <summary>Как автор типа описал член — атрибутами .NET.</summary>
/// <param name="DisplayName">Имя для человека (<c>[DisplayName]</c>); null — имя члена.</param>
/// <param name="Category">Раздел (<c>[Category]</c>); null — автор не сказал.</param>
/// <param name="Description">Описание (<c>[Description]</c>); null — его нет.</param>
/// <param name="DefaultValueText">Значение по умолчанию текстом (<c>[DefaultValue]</c>); null — не объявлено.</param>
/// <param name="Browsable">Показывать ли член в окне свойств (<c>[Browsable]</c>).</param>
/// <param name="EditorBrowsable">Насколько член виден (<c>[EditorBrowsable]</c>).</param>
/// <param name="IsReadOnly">Автор просит не править (<c>[ReadOnly(true)]</c>).</param>
/// <param name="RangeMinimum">Нижняя граница (<c>[Range]</c> из DataAnnotations); null — её нет.</param>
/// <param name="RangeMaximum">Верхняя граница; null — её нет.</param>
/// <param name="ObsoleteMessage">Член устарел (<c>[Obsolete]</c>): сообщение или пустая строка; null — не устарел.</param>
/// <param name="IsPrivateApi">Внутренний член Avalonia (<c>[PrivateApi]</c>): не для автора формы.</param>
/// <param name="IsUnstable">Член Avalonia, который ещё может измениться (<c>[Unstable]</c>).</param>
/// <remarks>
/// Проект пользователя описывает свои контролы этими атрибутами и от студии не зависит: их понимает и окно
/// свойств Visual Studio. Появилось в контракте 1.1.
/// </remarks>
public sealed record XamlMemberAttributes(
    string? DisplayName,
    string? Category,
    string? Description,
    string? DefaultValueText,
    bool Browsable,
    XamlEditorBrowsable EditorBrowsable,
    bool IsReadOnly,
    double? RangeMinimum,
    double? RangeMaximum,
    string? ObsoleteMessage,
    bool IsPrivateApi,
    bool IsUnstable)
{
    /// <summary>Автор не сказал о члене ничего.</summary>
    public static XamlMemberAttributes None { get; } =
        new(null, null, null, null, true, XamlEditorBrowsable.Always, false, null, null, null, false, false);
}

/// <summary>Нынешнее значение члена у объекта показа и откуда оно.</summary>
/// <param name="Text">
/// Значение текстом, как его написал бы документ, в инвариантной культуре; null — значения нет или текстом его
/// не сказать.
/// </param>
/// <param name="Origin">Откуда значение.</param>
/// <param name="OriginDetail">
/// Подробность источника для человека: селектор стиля (<c>Button:pointerover</c>), предок, от которого
/// унаследовано, выражение, написанное документом; null — сказать нечего.
/// </param>
/// <param name="Shared">
/// Само значение, если его тип — из общих сборок и не меняется: число, <c>Thickness</c>, <c>Color</c>,
/// перечисление Avalonia; у сплошной кисти — её цвет. Null — значения нет или оно объект поколения.
/// </param>
/// <remarks>
/// Значение — то, к чему объект пришёл, и записывать его обратно в документ нельзя: унаследованный кегль,
/// вписанный в атрибут, перестал бы наследоваться. Что написано, читают у синтаксиса документа. Появилось в
/// контракте 1.1.
/// </remarks>
public sealed record XamlMemberValue(string? Text, XamlValueOrigin Origin, string? OriginDetail, object? Shared);

/// <summary>Член элемента показа — то, что инспектор показывает строкой.</summary>
/// <param name="Name">Имя, как его пишет документ: <c>Width</c>, <c>Canvas.Left</c>.</param>
/// <param name="Kind">Каким свойством член держит значение.</param>
/// <param name="OwnerTypeFullName">Тип, объявивший член; у присоединённого — его владелец.</param>
/// <param name="OwnerXmlNamespace">
/// Пространство разметки владельца — чтобы написать присоединённый член с нужным префиксом; null — у своего
/// члена.
/// </param>
/// <param name="OwnerPrefix">Префикс, который сборка владельца предлагает; null — не предлагает.</param>
/// <param name="ValueType">Тип значения.</param>
/// <param name="CanWrite">Документ может написать член: он пишется и держит значение.</param>
/// <param name="AcceptsText">Значение пишется атрибутом — текстом; иначе только элементом свойства.</param>
/// <param name="IsContent">Член — содержимое элемента: его пишут детьми, а не атрибутом.</param>
/// <param name="ParentReads">
/// Присоединённый член, который читает родитель: владелец — тип родителя или его база. Ребёнку наследника
/// сетки достаётся <c>Grid.Row</c>.
/// </param>
/// <param name="Attributes">Как член описал автор типа.</param>
/// <param name="Value">Нынешнее значение у объекта показа.</param>
/// <remarks>Появилось в контракте 1.1.</remarks>
public sealed record XamlMemberInfo(
    string Name,
    XamlPropertyKind Kind,
    string OwnerTypeFullName,
    string? OwnerXmlNamespace,
    string? OwnerPrefix,
    XamlTypeInfo ValueType,
    bool CanWrite,
    bool AcceptsText,
    bool IsContent,
    bool ParentReads,
    XamlMemberAttributes Attributes,
    XamlMemberValue Value)
{
    /// <summary>Член присоединён: его даёт владелец, и пишется он с его именем.</summary>
    public bool IsAttached => Kind == XamlPropertyKind.Attached;
}

/// <summary>Ответ на вопрос, прочтётся ли текст значением члена.</summary>
/// <param name="Error">Почему не прочтётся, словами для человека; null — прочтётся.</param>
/// <remarks>Появилось в контракте 1.1.</remarks>
public readonly record struct XamlValueCheck(string? Error)
{
    /// <summary>Текст прочтётся.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Предпросмотр значения: объект показа выглядит так, будто документ написал другое, — а документ не тронут.
/// </summary>
/// <remarks>
/// <para>
/// Так протяжка числа и перетаскивание цвета показывают значение на каждом движении, а в документ ложится
/// одна правка — при отпускании, одним шагом истории. Значение ставится объекту поверх его собственного,
/// и <c>Dispose</c> возвращает и локальное значение, и стиль, и привязку в точности.
/// </para>
/// <para>
/// Пока предпросмотр жив, служба не заменяет поколение — он держит замену, как недописанное значение. Кончается
/// он и сам: на правке текста документа (записанное значение пришло), на замене поколения и с показом. После
/// конца <see cref="Show"/> ничего не делает. Всё — в потоке интерфейса. Появилось в контракте 1.1.
/// </para>
/// </remarks>
public interface IXamlValuePreview : IDisposable
{
    /// <summary>Показывает объекту значение, написанное текстом.</summary>
    /// <param name="text">Значение так, как его написал бы документ; пусто — показать написанное документом.</param>
    /// <returns>Прочлось ли значение; непрочитанное не показывается, и объект остаётся с прежним.</returns>
    XamlValueCheck Show(string text);
}
