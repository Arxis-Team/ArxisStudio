using Avalonia.Controls;

namespace ArxisStudio.Sdk;

/// <summary>
/// Свойство, каким его видит рисовальщик: что записано в разметке, что
/// действует сейчас и как записать новое.
/// </summary>
/// <remarks>
/// Значение — строка, потому что строкой оно и лежит в разметке. Отдавать сюда
/// разобранное значение значило бы потерять то, чего в разобранном виде не
/// бывает: <c>{Binding Customer.Name}</c> — законное значение свойства, у
/// которого нет ни цвета, ни толщины.
/// </remarks>
public interface IPropertyContext
{
    /// <summary>Имя свойства, как оно пишется в разметке.</summary>
    string Name { get; }

    /// <summary>Тип значения свойства.</summary>
    Type ValueType { get; }

    /// <summary>Значение, заданное в разметке; null, если свойство не задано.</summary>
    string? Value { get; }

    /// <summary>Действующее значение — то, что видно на канве.</summary>
    string? Effective { get; }

    /// <summary>Свойство задано прямо в разметке этого элемента.</summary>
    bool IsSet { get; }

    /// <summary>Значение изменилось — не нами, а правкой с другой стороны.</summary>
    event EventHandler? Changed;

    /// <summary>Записывает значение; null или пусто убирает атрибут.</summary>
    /// <param name="value">Новое значение.</param>
    void Set(string? value);
}

/// <summary>
/// Рисовальщик свойства: чем правится значение вместо обычного поля ввода.
/// </summary>
/// <remarks>
/// Строку с толщиной удобнее править четырьмя полями, цвет — образцом с
/// палитрой, а перечисление ролей — списком, где написаны роли, а не числа.
/// Рисовальщик заявляется на тип значения и достаётся всем свойствам этого
/// типа, у какого бы контрола они ни были.
/// </remarks>
public abstract class PropertyDrawer
{
    /// <summary>Строит редактор значения.</summary>
    /// <param name="property">Свойство, которое правится.</param>
    /// <returns>Контрол, который встанет в строку инспектора.</returns>
    public abstract Control Build(IPropertyContext property);
}

/// <summary>
/// Помечает рисовальщика свойств: студия отдаст ему свойства объявленного типа.
/// </summary>
/// <param name="valueType">Тип значения, который рисовальщик берётся править.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PropertyDrawerAttribute(Type valueType) : Attribute
{
    /// <summary>Тип значения.</summary>
    public Type ValueType { get; } = valueType;
}

/// <summary>Выделенный элемент, каким его видит инспектор плагина.</summary>
public interface IInspectorContext
{
    /// <summary>Имя типа, как оно написано в разметке.</summary>
    string TypeName { get; }

    /// <summary>Значение <c>x:Name</c>, если оно есть.</summary>
    string? ElementName { get; }

    /// <summary>Свойства элемента, которые студия показала бы сама.</summary>
    IReadOnlyList<IPropertyContext> Properties { get; }

    /// <summary>Что студия даёт плагину.</summary>
    IStudioContext Studio { get; }
}

/// <summary>
/// Свой инспектор для типа контрола: плагин строит всю панель целиком.
/// </summary>
/// <remarks>
/// Рисовальщик меняет одну строку, инспектор — весь разговор о выделенном
/// элементе: порядок свойств, их группировку, свои кнопки и всё, чего в общем
/// перечне быть не может.
/// </remarks>
public abstract class InspectorEditor
{
    /// <summary>Строит панель инспектора для выделенного элемента.</summary>
    /// <param name="element">Выделенный элемент.</param>
    /// <returns>Содержимое панели.</returns>
    public abstract Control Build(IInspectorContext element);
}

/// <summary>
/// Помечает свой инспектор: студия отдаст ему элементы объявленного типа.
/// </summary>
/// <param name="targetType">Тип контрола, для которого инспектор предназначен.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomInspectorAttribute(Type targetType) : Attribute
{
    /// <summary>Тип контрола.</summary>
    public Type TargetType { get; } = targetType;
}
