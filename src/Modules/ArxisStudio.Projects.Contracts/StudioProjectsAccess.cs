using ArxisStudio.Sdk;

namespace ArxisStudio.Projects;

/// <summary>Дорога к службам проектов из контекста плагина.</summary>
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

    /// <summary>
    /// Служба сборки.
    /// </summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>
    /// Служба или null — по тем же причинам, что и у <see cref="Projects"/>. Плагину, которому
    /// нужна сборка, стоит объявить модулю нижнюю границу <c>1.1</c>: в версии 1.0 её не было.
    /// </returns>
    /// <remarks>
    /// Отдельная служба, а не часть модели: подписчику снимков события сборки не нужны, а тому, кто
    /// рисует окно сборки, не нужен снимок.
    /// </remarks>
    public static IStudioBuild? Build(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioBuild>();
    }

    /// <summary>
    /// Служба пакетов.
    /// </summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>
    /// Служба или null — по тем же причинам, что и у <see cref="Projects"/>. Плагину, которому
    /// нужны пакеты, стоит объявить модулю нижнюю границу <c>1.2</c>: раньше её не было.
    /// </returns>
    /// <remarks>
    /// Третья служба одного модуля, и разведены они по тому, чем занят берущий: снимки — читающему,
    /// сборка — собирающему, ссылки на пакеты — правящему проект.
    /// </remarks>
    public static IStudioPackages? Packages(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioPackages>();
    }
}
