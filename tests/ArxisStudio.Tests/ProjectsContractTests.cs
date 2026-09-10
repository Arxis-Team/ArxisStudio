using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Projects;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт службы проектов — отдельная сборка, и её поверхность закреплена.
/// </summary>
/// <remarks>
/// Плагин собирается против этих типов. Снятый член ломает его на первом обращении, уже у человека,
/// поэтому поверхность описана строками рядом с поверхностью ядра, а ссылки и идентичность
/// связывания проверяются на собранной сборке.
/// </remarks>
public class ProjectsContractTests
{
    /// <summary>Поверхность контракта — та, что записана.</summary>
    [Fact]
    public void The_public_surface_of_the_projects_contract_is_the_recorded_one()
    {
        PublicSurface.AssertRecorded(
            Path.Combine(Repository(), "tests", "ArxisStudio.Tests", "Surfaces", "ArxisStudio.Projects.Contracts.txt"),
            PublicSurface.Describe(typeof(IStudioProjects).Assembly));
    }

    /// <summary>
    /// Контракт ссылается только на SDK, модель проектов и рантайм.
    /// </summary>
    /// <remarks>
    /// Контракт, притянувший MSBuild или Avalonia, притянул бы их к каждому потребителю — и шов под
    /// движок в отдельном процессе закрылся бы уже на уровне типов.
    /// </remarks>
    [Fact]
    public void The_projects_contract_references_only_the_sdk_the_project_model_and_the_runtime()
    {
        var foreign = typeof(IStudioProjects).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name is not ("ArxisStudio.Sdk" or "ArxisStudio.ProjectSystem" or "System" or "netstandard" or "mscorlib")
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            foreign.Count == 0,
            $"контракт ссылается не только на SDK, модель и рантайм: {string.Join(", ", foreign)}");
    }

    /// <summary>Идентичность связывания контракта прибита, как у всех сборок студии.</summary>
    [Fact]
    public void The_binding_identity_of_the_projects_contract_is_pinned()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(IStudioProjects).Assembly.GetName().Version);
    }

    /// <summary>Модуль объявляет контрактом ровно ту сборку, в которой контракт лежит.</summary>
    [Fact]
    public void The_module_declares_the_assembly_the_contract_lives_in()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ProjectsModule).Assembly);

        Assert.Null(error);

        var provides = manifest?.Provides;

        Assert.NotNull(provides);
        Assert.Equal(Path.GetFileName(typeof(IStudioProjects).Assembly.Location), Assert.Single(provides.Contracts));
    }

    /// <summary>Корень репозитория: тесты бегут из bin, файлы лежат выше.</summary>
    private static string Repository()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ArxisStudio.slnx")))
            folder = folder.Parent;

        Assert.True(folder is not null, "не нашёл корень репозитория");

        return folder!.FullName;
    }
}
