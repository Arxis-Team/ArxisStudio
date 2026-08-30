using ArxisStudio.Docking;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Граница движка докинга.
/// </summary>
/// <remarks>
/// Раскладка переживает выключение плагина, уезжает в файл и возвращается при
/// запуске. Узел дерева, удержавший объект из контекста плагина, не дал бы этому
/// контексту выгрузиться никогда — а такой ошибки в проекте чинили уже три
/// (экспорты, строки локализации, контрактные сборки). Здесь она отсекается тем,
/// что движку просто нечем сослаться на студию, и это проверяется, а не обещается
/// комментарием.
/// </remarks>
public class DockingBoundaryTests
{
    /// <summary>Движок докинга не знает студийных сборок.</summary>
    /// <remarks>
    /// Ссылки берутся из готовой сборки, а не из csproj: так виден не список
    /// разрешений, а то, чем движок действительно пользуется.
    /// </remarks>
    [Fact]
    public void The_docking_engine_knows_nothing_about_the_studio()
    {
        var references = typeof(DockTree).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToList();

        Assert.Empty(references.Intersect(
            ["ArxisStudio.Sdk", "ArxisStudio.Shell", "ArxisStudio.Extensibility"],
            StringComparer.Ordinal));

        // Проверка обязана уметь провалиться: что-то в списке всё же есть.
        Assert.Contains("System.Text.Json", references, StringComparer.Ordinal);
    }
}
