using ArxisStudio.Sdk;

namespace ArxisStudio.Projects;

/// <summary>Дорога к службе проектов из контекста плагина.</summary>
public static class StudioProjectsAccess
{
    /// <summary>
    /// Служба проектов.
    /// </summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>
    /// Служба или null: модуля нет, он не поднялся, или его версия ниже границы, объявленной в
    /// вашем манифесте.
    /// </returns>
    /// <remarks>
    /// Короткая запись <c>GetService&lt;IStudioExports&gt;()?.Get&lt;IStudioProjects&gt;()</c>, и
    /// правила у неё те же, что у всякого экспорта. Одно послабление: модуль встроенный и не
    /// перезагружается, поэтому держать службу от <c>Activate</c> до <c>Deactivate</c> можно —
    /// подписке на <see cref="IStudioProjects.Changed"/> это и нужно.
    /// </remarks>
    public static IStudioProjects? Projects(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioProjects>();
    }
}
