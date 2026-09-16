using System.Reflection;
using ArxisStudio.Extensibility;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Чтение манифеста встроенного модуля: откуда он берётся и что говорит, когда не взялся.
/// </summary>
/// <remarks>
/// Манифест стал файлом в папке модуля, и вопросов у него прибавилось: где именно искать, что
/// ответить на пустое тело и на неразобранное. Проверяется каждый — на своём дереве, а не на
/// выходе студии: выход собран правильно, и неправильных случаев в нём нет.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ModuleManifestTests : IDisposable
{
    private const string Source = "namespace Probe; public static class Marker { }";

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-module-manifest-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Сборка уехала в основной контекст и держит свой файл до конца процесса.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Манифест читается из папки над <c>bin</c>.
    /// </summary>
    /// <remarks>
    /// Форма папки модуля — форма установленного плагина: сборки в <c>bin</c>, манифест в корне.
    /// Значит и папкой модуля считается та, что над <c>bin</c>, а не та, где лежит сборка.
    /// </remarks>
    [Fact]
    public void The_manifest_is_read_from_the_folder_above_the_bin()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "arxis.probe")).FullName;
        var assembly = Emit(folder, "bin");

        File.WriteAllText(Path.Combine(folder, "module.json"), """
            { "id": "arxis.probe", "name": "probe", "version": "1.0.0" }
            """);

        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(error);
        Assert.Equal("arxis.probe", manifest?.Id);
        Assert.Equal(folder, ModuleManifest.FolderOf(assembly), ignoreCase: true);
    }

    /// <summary>Сборке не в <c>bin</c> папкой модуля остаётся её собственная.</summary>
    [Fact]
    public void An_assembly_outside_a_bin_takes_its_own_folder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "beside")).FullName;
        var assembly = Emit(folder);

        Assert.Equal(folder, ModuleManifest.FolderOf(assembly), ignoreCase: true);
    }

    /// <summary>
    /// Манифест из одного <c>null</c> объясняется, а не отдаётся пустотой.
    /// </summary>
    /// <remarks>
    /// Такое тело разбирается без исключения и даёт <c>null</c>. Без отдельной проверки зовущий
    /// получил бы манифест <c>null</c> и ошибку <c>null</c> — то есть ничего, и модуль выпал бы из
    /// студии молча.
    /// </remarks>
    [Fact]
    public void A_manifest_that_is_only_null_says_so()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var assembly = Emit(folder);

        File.WriteAllText(Path.Combine(folder, "module.json"), "null");

        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains(folder, error);
    }

    /// <summary>Неразобранный манифест называет свой файл, а не только беду.</summary>
    [Fact]
    public void A_manifest_that_does_not_parse_names_its_file()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "broken")).FullName;
        var assembly = Emit(folder);

        File.WriteAllText(Path.Combine(folder, "module.json"), "{ \"id\": ");

        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains(Path.Combine(folder, "module.json"), error);
    }

    /// <summary>Собирает сборку в названную подпапку и загружает её оттуда.</summary>
    private static Assembly Emit(string folder, string? inside = null)
    {
        var where = inside is null ? folder : Directory.CreateDirectory(Path.Combine(folder, inside)).FullName;
        var name = $"Probe.Manifest{Guid.NewGuid():N}";
        var path = Path.Combine(where, name + ".dll");

        TestAssembly.EmitFile(path, name, Source);

        return Assembly.LoadFrom(path);
    }
}
