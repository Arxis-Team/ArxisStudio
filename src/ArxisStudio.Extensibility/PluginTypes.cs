using System.Reflection;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Классы сборок расширения, которые студия создаёт сама.
/// </summary>
/// <remarks>
/// Точки входа, службы, редакторы документов, панели и элементы полосы студия находит одинаково:
/// открытый неабстрактный наследник нужного класса. Отбор стоял копией у каждого, кто ищет, — у
/// хоста дважды, у реестра вкладов и у поверхностей студии.
/// <para>
/// <c>GetTypes</c> на сборке, чьи зависимости не нашлись, бросает, и это нарочно: такой плагин не
/// поднимается, а причина остаётся в записи об ошибке. Пропустить сломанные типы значило бы
/// поднять плагин без части его вкладов — молча.
/// </para>
/// </remarks>
public static class PluginTypes
{
    /// <summary>Открытые неабстрактные типы сборок — в порядке сборок и объявления.</summary>
    /// <param name="assemblies">Сборки расширения.</param>
    public static IEnumerable<Type> Concrete(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsPublic: true });
    }

    /// <summary>Открытые неабстрактные наследники <typeparamref name="T"/> — в порядке сборок и объявления.</summary>
    /// <typeparam name="T">Класс вклада: точка входа, служба, панель.</typeparam>
    /// <param name="assemblies">Сборки расширения.</param>
    public static IEnumerable<Type> Concrete<T>(IEnumerable<Assembly> assemblies) =>
        Concrete(assemblies).Where(type => typeof(T).IsAssignableFrom(type));
}
