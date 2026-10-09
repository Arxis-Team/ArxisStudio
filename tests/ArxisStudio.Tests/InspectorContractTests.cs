using ArxisStudio.Extensibility;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт инспектора — отдельная сборка, и её поверхность закреплена (запись 362).
/// </summary>
/// <remarks>
/// Против этих типов собираются плагины, которые показывают свои объекты и дополняют инспектор редакторами,
/// разделами, метаданными и пунктами меню. Снятый член ломает их на первом обращении, уже у человека.
/// </remarks>
public class InspectorContractTests
{
    /// <summary>Поверхность контракта — та, что записана.</summary>
    [Fact]
    public void The_public_surface_of_the_inspector_contract_is_the_recorded_one()
    {
        PublicSurface.AssertRecorded(
            Repository.Path("tests", "ArxisStudio.Tests", "Surfaces", "ArxisStudio.Inspector.Contracts.txt"),
            PublicSurface.Describe(typeof(IStudioInspector).Assembly));
    }

    /// <summary>Контракт ссылается только на SDK, контролы, Avalonia и рантайм.</summary>
    /// <remarks>
    /// Поле строки — контрол, значок — контур Avalonia, а протяжка — числовое поле набора; всё остальное
    /// контракт притянул бы к каждому плагину, который его взял.
    /// </remarks>
    [Fact]
    public void The_inspector_contract_references_only_the_sdk_the_controls_avalonia_and_the_runtime()
    {
        var foreign = typeof(IStudioInspector).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name is not ("ArxisStudio.Sdk" or "ArxisStudio.Controls" or "System" or "netstandard" or "mscorlib")
                && !name.StartsWith("System.", StringComparison.Ordinal)
                && !name.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToList();

        Assert.True(foreign.Count == 0, $"контракт ссылается не только на SDK, контролы и Avalonia: {string.Join(", ", foreign)}");
    }

    /// <summary>Идентичность связывания контракта прибита, как у всех сборок студии.</summary>
    [Fact]
    public void The_binding_identity_of_the_inspector_contract_is_pinned()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(IStudioInspector).Assembly.GetName().Version);
    }

    /// <summary>Модуль объявляет контрактом ровно ту сборку, в которой контракт лежит.</summary>
    [Fact]
    public void The_module_declares_the_assembly_the_inspector_contract_lives_in()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(InspectorModule).Assembly);

        Assert.Null(error);

        var provides = manifest?.Provides;

        Assert.NotNull(provides);
        Assert.Equal(
            "bin/" + Path.GetFileName(typeof(IStudioInspector).Assembly.Location),
            Assert.Single(provides.Contracts));
    }
}
