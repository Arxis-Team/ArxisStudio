using System.Reflection;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Команды, объявленные атрибутом: методы плагина с <see cref="CommandAttribute"/>.
/// </summary>
/// <remarks>
/// Плагину остаётся написать метод и повесить на него <see cref="CommandAttribute"/>: заявка —
/// работа однообразная, и требовать её от каждого автора значит собирать по ней одни и те же
/// опечатки.
/// </remarks>
internal static class CommandMethods
{
    /// <summary>
    /// Заявляет команды, помеченные атрибутом.
    /// </summary>
    /// <param name="assemblies">Сборки плагина.</param>
    /// <param name="owners">Объекты плагина: точки входа и службы.</param>
    /// <param name="studio">Контекст, через который заявляются команды.</param>
    /// <remarks>
    /// Обычный метод берётся у объектов самого плагина — точки входа и служб: они уже созданы, им
    /// уже отдан контекст, и команда видит то же состояние, что и остальной плагин. Создать ради
    /// команды второй экземпляр значило бы вызвать её на объекте, которому студия ничего не давала.
    /// <para>
    /// В любом другом классе сборки атрибут действует только на статическом методе: у такого
    /// класса нет ни контекста, ни причины существовать в одном экземпляре. Класс при этом может
    /// быть и статическим — это самый естественный дом для таких методов. Для среды исполнения
    /// статический класс — <c>abstract sealed</c>, и отбор по одному <c>IsAbstract</c> молча оставлял
    /// его команды незаявленными.
    /// </para>
    /// </remarks>
    public static void Bind(IEnumerable<Assembly> assemblies, IEnumerable<object> owners, IStudioContext studio)
    {
        foreach (var owner in owners)
            Register(owner.GetType(), owner, studio);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            // Абстрактный класс отсеивается, статический — нет: наследника у статического не
            // бывает, и его методы зовутся как есть. Открытый обобщённый тип звать не у кого.
            if (type is { IsPublic: true, ContainsGenericParameters: false } && (!type.IsAbstract || type.IsSealed))
                Register(type, owner: null, studio);
        }
    }

    /// <summary>Заявляет команды одного класса.</summary>
    /// <param name="type">Класс, в котором ищем.</param>
    /// <param name="owner">Объект плагина; null — берём только статические методы.</param>
    /// <param name="studio">Контекст, через который заявляются команды.</param>
    private static void Register(Type type, object? owner, IStudioContext studio)
    {
        const BindingFlags Where = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var method in type.GetMethods(Where))
        {
            if (method.GetCustomAttribute<CommandAttribute>() is not { } declared)
                continue;

            // Команда — это «сделай», а не «сделай вот с этим»: параметрам
            // взяться неоткуда, и молча передать null было бы хуже отказа.
            if (method.GetParameters().Length > 0)
                continue;

            if (method.IsStatic != (owner is null))
                continue;

            studio.Commands.Register(declared.Id, () => method.Invoke(owner, null));
        }
    }
}
