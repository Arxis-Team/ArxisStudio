using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Дорога основного контекста загрузки к папкам сборок студии.
/// </summary>
/// <remarks>
/// Папок две — платформа и модули, — и правило поиска у них одно, поэтому проверяется оно, а не
/// каждая папка по отдельности. Резолвер идёт на своём, выгружаемом контексте, а не на основном:
/// подключить его к основному в процессе тестов значило бы поменять поиск сборок всем тестам
/// сразу. Правило то же — событие приходит, когда обычный поиск не нашёл, — а корня у такого
/// контекста нет вовсе. В общей очереди: сборки здесь компилируются из загруженных в процесс и
/// грузятся в него.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioAssemblyFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"arxis-assembly-folder-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Выгружаемый контекст отпускает файл, когда его соберёт сборщик мусора.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Сборка в папке модулей находится по имени.</summary>
    [Fact]
    public void An_assembly_in_the_module_folder_is_found_by_its_name()
    {
        var path = Path.Combine(_root, "Probe.Found.dll");

        File.WriteAllBytes(path, []);

        Assert.Equal(path, StudioAssemblyFolder.Locate(_root, new AssemblyName("Probe.Found")));
    }

    /// <summary>Сборка ресурсов ищется в подпапке своей культуры, а не рядом с основной.</summary>
    [Fact]
    public void A_resource_assembly_is_looked_up_in_the_folder_of_its_culture()
    {
        var culture = Directory.CreateDirectory(Path.Combine(_root, "de")).FullName;
        var path = Path.Combine(culture, "Probe.Found.resources.dll");

        File.WriteAllBytes(path, []);

        Assert.Equal(path, StudioAssemblyFolder.Locate(_root, new AssemblyName("Probe.Found.resources, Culture=de")));
        Assert.Null(StudioAssemblyFolder.Locate(_root, new AssemblyName("Probe.Found.resources")));
    }

    /// <summary>Чего в папке нет, того резолвер не находит.</summary>
    [Fact]
    public void What_is_not_in_the_folder_is_not_found()
    {
        Assert.Null(StudioAssemblyFolder.Locate(_root, new AssemblyName("Probe.Missing")));
    }

    /// <summary>Имя, похожее на путь, из папки модулей не уводит.</summary>
    [Fact]
    public void A_name_that_looks_like_a_path_does_not_lead_out_of_the_folder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Modules")).FullName;

        File.WriteAllBytes(Path.Combine(_root, "Escape.dll"), []);

        Assert.Null(StudioAssemblyFolder.Locate(folder, new AssemblyName { Name = "../Escape" }));
    }

    /// <summary>Контекст, спросивший сборку модуля, получает файл из папки модулей.</summary>
    [Fact]
    public void A_load_context_that_asks_for_a_module_assembly_gets_the_file_from_the_folder()
    {
        var name = $"Probe.Folder{Guid.NewGuid():N}";
        var path = Path.Combine(_root, name + ".dll");

        TestAssembly.EmitFile(path, name, "namespace Probe; public static class Marker { }");

        var context = new AssemblyLoadContext("arxis-assembly-folder-probe", isCollectible: true);

        context.Resolving += (asking, wanted) =>
            StudioAssemblyFolder.Locate(_root, wanted) is { } found ? asking.LoadFromAssemblyPath(found) : null;

        try
        {
            Assert.Equal(path, context.LoadFromAssemblyName(new AssemblyName(name)).Location, ignoreCase: true);
        }
        finally
        {
            context.Unload();
        }
    }
}
