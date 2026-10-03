using ArxisStudio.Sdk;

namespace ArxisStudio.Xaml;

/// <summary>Дорога к службам XAML из контекста плагина.</summary>
/// <remarks>
/// Служб две, и разведены они по тому, чем занят берущий, как у службы проектов: документы — тому,
/// кто открывает, правит и показывает разметку; поколение типов проекта — тому, кто держит построенное
/// из него и должен отпускать это на замену. Обе публикует модуль <c>arxis.xaml</c>; плагину, который
/// их берёт, стоит объявить модулю нижнюю границу <c>1.0</c>.
/// </remarks>
public static class StudioXamlAccess
{
    /// <summary>
    /// Документы разметки.
    /// </summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>
    /// Служба или null: модуля нет, он не поднялся, или его версия ниже границы, объявленной в
    /// вашем манифесте.
    /// </returns>
    /// <remarks>
    /// Короткая запись <c>GetService&lt;IStudioExports&gt;()?.Get&lt;IStudioXamlDocuments&gt;()</c>.
    /// Модуль встроенный и не перезагружается, поэтому держать службу от <c>Activate</c> до
    /// <c>Deactivate</c> можно; аренды документов отпускают раньше — в <c>Deactivate</c> самое позднее.
    /// </remarks>
    public static IStudioXamlDocuments? XamlDocuments(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioXamlDocuments>();
    }

    /// <summary>
    /// Поколение типов проекта: его состояние, сборка дизайна и замена.
    /// </summary>
    /// <param name="context">Контекст плагина.</param>
    /// <returns>Служба или null — по тем же причинам, что и у <see cref="XamlDocuments"/>.</returns>
    /// <remarks>
    /// Отдельная служба, а не часть документов: тому, кто показывает текст, замена типов не нужна, а
    /// тому, кто держит построенное из них, — нужна вся.
    /// </remarks>
    public static IStudioXamlDesign? XamlDesign(this IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IStudioExports>()?.Get<IStudioXamlDesign>();
    }
}
