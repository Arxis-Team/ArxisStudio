using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Xaml;
using ArxisStudio.Xaml;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт службы XAML — обещание плагинам: его поверхность записана, ссылки названы, идентичность
/// прибита, и модуль объявляет его своим.
/// </summary>
/// <remarks>
/// Контракт плагин собирает против своей копии, а студия грузит его в общий контекст один раз и не
/// выгружает: правка, снявшая член, сломала бы уже собранный плагин, а ссылка на загрузчик Markup
/// притянула бы к каждому потребителю объекты поколения.
/// </remarks>
public class XamlContractTests
{
    /// <summary>Поверхность контракта — та, что записана.</summary>
    [Fact]
    public void The_public_surface_of_the_xaml_contract_is_the_recorded_one()
    {
        PublicSurface.AssertRecorded(
            Repository.Path("tests", "ArxisStudio.Tests", "Surfaces", "ArxisStudio.Xaml.Contracts.txt"),
            PublicSurface.Describe(typeof(IStudioXamlDocuments).Assembly));
    }

    /// <summary>
    /// Контракт ссылается на SDK, модель проектов, синтаксис разметки и Avalonia — и ни на загрузчик, ни
    /// на адаптер.
    /// </summary>
    /// <remarks>
    /// Всё, что переживает замену поколения, в контракте называется путём или именем; объектов поколения
    /// в нём нет, кроме корня и приложения формы самого показа. Ссылка на загрузчик или адаптер вывела бы
    /// их типы к плагинам — и держатель такого типа держал бы поколение.
    /// </remarks>
    [Fact]
    public void The_xaml_contract_references_only_the_sdk_the_model_the_syntax_and_avalonia()
    {
        var foreign = typeof(IStudioXamlDocuments).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name is not ("ArxisStudio.Sdk" or "ArxisStudio.ProjectSystem" or "ArxisStudio.Markup"
                    or "ArxisStudio.Markup.Xaml" or "Avalonia" or "System" or "netstandard" or "mscorlib")
                && !name.StartsWith("Avalonia.", StringComparison.Ordinal)
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            foreign.Count == 0,
            $"контракт ссылается не только на SDK, модель, синтаксис и Avalonia: {string.Join(", ", foreign)}");
    }

    /// <summary>Идентичность связывания контракта прибита, как у всех сборок студии.</summary>
    [Fact]
    public void The_binding_identity_of_the_xaml_contract_is_pinned()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(IStudioXamlDocuments).Assembly.GetName().Version);
    }

    /// <summary>
    /// Модуль объявляет контрактом ровно ту сборку, в которой контракт лежит, и требует службу проектов,
    /// которая пишет файлы и следит за их содержимым.
    /// </summary>
    [Fact]
    public void The_module_declares_its_contract_and_the_projects_service_it_stands_on()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(XamlModule).Assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        var provides = manifest.Provides;

        Assert.NotNull(provides);
        Assert.Equal(
            "bin/" + Path.GetFileName(typeof(IStudioXamlDocuments).Assembly.Location),
            Assert.Single(provides.Contracts));

        var projects = Assert.Single(manifest.Dependencies, dependency => dependency.Id == "arxis.projects");

        Assert.Equal("1.7", projects.Min);
        Assert.False(projects.Optional, "служба XAML без службы проектов не работает и не должна притворяться");
    }
}
