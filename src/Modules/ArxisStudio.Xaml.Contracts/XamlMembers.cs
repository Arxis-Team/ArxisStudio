using System.Collections.Immutable;

namespace ArxisStudio.Xaml;

/// <summary>Что построил элемент показа: тип объекта и тип его родителя — именами.</summary>
/// <param name="TypeName">Имя типа объекта: <c>Button</c>.</param>
/// <param name="TypeNamespace">Его пространство CLR: <c>Avalonia.Controls</c>; пусто — тип без пространства.</param>
/// <param name="ParentTypeName">Имя типа родителя в дереве показа; null — у корня.</param>
/// <param name="HoldsChildren">
/// Объект — панель: детей у него может быть сколько угодно. Снять с элемента такую обёртку можно и тогда,
/// когда у него несколько детей.
/// </param>
/// <remarks>
/// Имена, а не типы: тип — объект поколения, и держатель сведений держал бы его через замену. Появилось в
/// контракте 1.1.
/// </remarks>
public sealed record XamlElementInfo(string TypeName, string TypeNamespace, string? ParentTypeName, bool HoldsChildren);

/// <summary>Чем править значение члена: это следует из типа значения.</summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public enum XamlValueEditor
{
    /// <summary>Текст: у типа нет ответа лучше.</summary>
    Text,

    /// <summary>Число — всё ещё текст, но стрелками его можно шагать.</summary>
    Number,

    /// <summary>Флажок.</summary>
    Flag,

    /// <summary>Одно из значений перечисления (<see cref="XamlMemberRow.Choices"/>).</summary>
    Choice,

    /// <summary>Кисть или цвет: текстом, с образцом рядом.</summary>
    Brush,

    /// <summary>Значение не пишется: член только читается или это событие.</summary>
    None,
}

/// <summary>Откуда у объекта показа нынешнее значение члена.</summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public enum XamlValueOrigin
{
    /// <summary>Ниоткуда: значение по умолчанию.</summary>
    Default,

    /// <summary>Его написал документ.</summary>
    Document,

    /// <summary>Привязка.</summary>
    Binding,

    /// <summary>Стиль или тема.</summary>
    Style,

    /// <summary>Шаблон родителя.</summary>
    Template,

    /// <summary>Унаследовано от предка.</summary>
    Inherited,

    /// <summary>Анимация.</summary>
    Animation,
}

/// <summary>Член элемента показа — то, что инспектор показывает строкой.</summary>
/// <param name="Name">Имя, как его пишет документ: <c>Width</c>, <c>Canvas.Left</c>.</param>
/// <param name="OwnerTypeName">Тип, объявивший член: для присоединённого — его владелец.</param>
/// <param name="ValueTypeName">Имя типа значения: <c>Double</c>, <c>HorizontalAlignment</c>.</param>
/// <param name="IsAttached">Член присоединён: его даёт не тип объекта, а владелец — у ребёнка <c>Canvas</c> это <c>Canvas.Left</c>.</param>
/// <param name="Editor">Чем править значение.</param>
/// <param name="Choices">Значения перечисления — у <see cref="XamlValueEditor.Choice"/>; иначе пусто.</param>
/// <param name="Origin">Откуда нынешнее значение у объекта показа.</param>
/// <param name="ValueText">Нынешнее значение текстом — подсказка рядом с тем, что написано; null — значения нет.</param>
/// <remarks>
/// <para>
/// Строка — сведение о члене и о живом объекте, но не о тексте: что написано, читают у синтаксиса
/// документа (<see cref="IXamlDocumentHandle.Syntax"/>). Значение здесь — то, к чему объект пришёл, и
/// записывать его обратно в документ нельзя: унаследованный кегль, вписанный в атрибут, перестал бы
/// наследоваться.
/// </para>
/// <para>Появилось в контракте 1.1.</para>
/// </remarks>
public sealed record XamlMemberRow(
    string Name,
    string OwnerTypeName,
    string ValueTypeName,
    bool IsAttached,
    XamlValueEditor Editor,
    ImmutableArray<string> Choices,
    XamlValueOrigin Origin,
    string? ValueText);

/// <summary>Ответ на вопрос, прочтётся ли текст значением члена.</summary>
/// <param name="Error">Почему не прочтётся; null — прочтётся.</param>
/// <remarks>Появилось в контракте 1.1.</remarks>
public readonly record struct XamlValueCheck(string? Error)
{
    /// <summary>Текст прочтётся.</summary>
    public bool Succeeded => Error is null;
}
