using System.Reflection;
using ArxisStudio.Extensibility;

namespace ArxisStudio.Services;

/// <summary>
/// Встроенные модули студии: единый список для всех, кто о них спрашивает.
/// </summary>
/// <remarks>
/// Список жил в окне студии, но нужен он и менеджеру плагинов: модуль —
/// годная цель зависимости, и карточка обязана считать его присутствующим —
/// иначе менеджер показывал бы «не установлен» там, где студия говорит
/// «есть». Порядок в списке виден человеку — им же поднимаются панели.
/// </remarks>
public static class StudioModules
{
    /// <summary>Сборки модулей в порядке подъёма.</summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(Modules.Sample.SampleModule).Assembly,
    ];

    /// <summary>
    /// Записи о модулях — как о плагинах, только встроенных.
    /// </summary>
    /// <remarks>
    /// Читаются манифесты из сборок, сами модули не поднимаются: менеджеру
    /// нужны цели зависимостей, а не работающий код.
    /// </remarks>
    public static IReadOnlyList<InstalledPlugin> Describe() =>
        Assemblies
            .Select(assembly =>
            {
                var (manifest, error) = ModuleManifest.Load(assembly);

                return new InstalledPlugin(
                    AppContext.BaseDirectory,
                    manifest,
                    error,
                    IsEnabled: true,
                    IsBuiltIn: true);
            })
            .Where(module => module.IsValid)
            .ToList();
}
