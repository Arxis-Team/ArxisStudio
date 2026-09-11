using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка собранной студии: модули и то, что везут только они, — в папке Modules.
/// </summary>
/// <remarks>
/// Проверяется готовый выход студии, а не правило сборки: правило может выглядеть верным, а выход
/// — нет, и узнал бы об этом человек, запустивший студию. Выход берётся той же конфигурации, что у
/// тестов: тестовый проект ссылается на студию, и к прогону она собрана.
/// <para>
/// Ссылки сборок читаются из метаданных, без загрузки: процессу тестов незачем получать вторые
/// копии того, что в нём уже есть.
/// </para>
/// </remarks>
public class StudioLayoutTests
{
    /// <summary>
    /// Сборки, которых рядом со студией нет намеренно.
    /// </summary>
    /// <remarks>
    /// Движок MSBuild приходит из SDK, который найдёт локатор; копия рядом со студией была бы
    /// вторым MSBuild в процессе. Что её не везёт ни один проект, проверяет
    /// <c>ProjectsModuleTests</c> — на выходе тестов: пакеты модуля попадают туда из тех же ссылок.
    /// </remarks>
    private static readonly HashSet<string> FromTheSdk = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Build",
        "Microsoft.Build.Framework",
    };

    /// <summary>Каждый модуль лежит в папке модулей, а не в корне студии.</summary>
    [Fact]
    public void Every_module_lies_in_the_module_folder_and_not_beside_the_studio()
    {
        var output = Output();

        foreach (var name in StudioModules.Assemblies.Select(assembly => assembly.GetName().Name!))
        {
            Assert.True(
                File.Exists(Path.Combine(output, StudioModuleFolder.Name, name + ".dll")),
                $"модуля {name} нет в папке {StudioModuleFolder.Name}");

            Assert.False(File.Exists(Path.Combine(output, name + ".dll")), $"модуль {name} лежит в корне студии");
        }
    }

    /// <summary>
    /// Ни одна сборка не лежит дважды — и у корня, и в папке модулей.
    /// </summary>
    /// <remarks>
    /// Корень основной контекст просматривает первым, так что копия в папке модулей не
    /// загрузилась бы никогда — она только путала бы того, кто ищет, какой файл в работе.
    /// </remarks>
    [Fact]
    public void Nothing_lies_both_beside_the_studio_and_in_the_module_folder()
    {
        var output = Output();

        var twice = Assemblies(Path.Combine(output, StudioModuleFolder.Name)).Keys
            .Intersect(Assemblies(output).Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(twice.Count == 0, $"лежит и у корня, и в папке модулей: {string.Join(", ", twice)}");
    }

    /// <summary>
    /// Общие сборки остаются у корня.
    /// </summary>
    /// <remarks>
    /// Плагины берут их из основного контекста, и место им рядом с самой студией, а не в углу
    /// модуля, который оказался первым, кто их потянул. Ядро модели проектов — общее, хотя сама
    /// студия его типов и не зовёт.
    /// </remarks>
    [Fact]
    public void Shared_assemblies_stay_beside_the_studio()
    {
        var output = Output();

        var inModules = Assemblies(Path.Combine(output, StudioModuleFolder.Name)).Keys
            .Where(SharedAssemblies.IsShared)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(inModules.Count == 0, $"в папке модулей лежат общие сборки: {string.Join(", ", inModules)}");
        Assert.True(File.Exists(Path.Combine(output, "ArxisStudio.ProjectSystem.dll")), "ядро модели проектов ушло из корня студии");
    }

    /// <summary>
    /// У корня нет ничего, что нужно только модулям.
    /// </summary>
    /// <remarks>
    /// Нужное самой студии — всё, до чего доходят ссылки от неё и от общих сборок, не заходя в
    /// модули. Нужное модулям — всё, до чего доходят ссылки от модулей. Сборка у корня, нужная
    /// модулям и не нужная студии, — забытая раскладкой. Сборки, которые студия грузит без
    /// ссылки, этим правилом не задеты: модулям они не нужны, и под подозрение не попадают.
    /// </remarks>
    [Fact]
    public void Nothing_beside_the_studio_is_needed_only_by_modules()
    {
        var output = Output();
        var root = Assemblies(output);
        var modules = Assemblies(Path.Combine(output, StudioModuleFolder.Name));

        var platform = Reach(root, root.Keys.Where(name => name == "ArxisStudio" || SharedAssemblies.IsShared(name)));

        var everything = new Dictionary<string, string>(root, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in modules)
            everything.TryAdd(name, path);

        var needed = Reach(everything, modules.Keys);

        var misplaced = root.Keys
            .Where(name => needed.Contains(name) && !platform.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            misplaced.Count == 0,
            $"у корня лежит нужное только модулям: {string.Join(", ", misplaced)} — ему место в {StudioModuleFolder.Name}");
    }

    /// <summary>
    /// Каждая ссылка модуля находится: в папке модулей, у корня или в самой среде.
    /// </summary>
    /// <remarks>
    /// Раскладка, забывшая зависимость, не падает при сборке — модуль упал бы у человека, на
    /// первом обращении к её типу.
    /// </remarks>
    [Fact]
    public void Every_reference_of_a_module_can_be_found()
    {
        var output = Output();
        var root = Assemblies(output);
        var modules = Assemblies(Path.Combine(output, StudioModuleFolder.Name));
        var runtime = Assemblies(RuntimeEnvironment.GetRuntimeDirectory());

        var missing = modules
            .SelectMany(module => References(module.Value).Select(reference => (Module: module.Key, Reference: reference)))
            .Where(pair => !modules.ContainsKey(pair.Reference)
                && !root.ContainsKey(pair.Reference)
                && !runtime.ContainsKey(pair.Reference)
                && !FromTheSdk.Contains(pair.Reference))
            .Select(pair => $"{pair.Module} → {pair.Reference}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"модулю не найти: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Нативные библиотеки пакетов модулей остаются у корня, в runtimes/.
    /// </summary>
    /// <remarks>
    /// Их находит сама среда по файлу зависимостей приложения, откуда бы ни пришла управляемая
    /// сборка, которая их зовёт. Терминал без своей библиотеки на Linux и macOS не открылся бы, а
    /// на Windows этого не видно вовсе — там она ему не нужна.
    /// </remarks>
    [Fact]
    public void Native_libraries_of_module_packages_stay_beside_the_studio()
    {
        var output = Output();

        Assert.True(
            File.Exists(Path.Combine(output, "runtimes", "linux-x64", "native", "libporta_pty.so")),
            "нативная библиотека терминала пропала из runtimes/ у корня студии");

        Assert.False(
            Directory.Exists(Path.Combine(output, StudioModuleFolder.Name, "runtimes")),
            "в папку модулей уехало runtimes/ — там его не ищут ни среда, ни резолвер модулей");
    }

    /// <summary>Документации ссылок в выходе студии нет: её читает компилятор, а не студия.</summary>
    [Fact]
    public void No_reference_documentation_lies_in_the_studio_output()
    {
        var output = Output();

        var documentation = Directory
            .EnumerateFiles(output, "*.xml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(documentation.Count == 0, $"в выходе студии лежит документация: {string.Join(", ", documentation)}");
    }

    /// <summary>Выход студии той же конфигурации, что у тестов.</summary>
    private static string Output()
    {
        var tests = new DirectoryInfo(AppContext.BaseDirectory);
        var output = Path.Combine(SharedAssemblies.Repository(), "src", "ArxisStudio", "bin", tests.Parent!.Name, tests.Name);

        Assert.True(File.Exists(Path.Combine(output, "ArxisStudio.dll")), $"студия не собрана в {output}");

        return output;
    }

    /// <summary>Сборки папки: простое имя — путь.</summary>
    private static Dictionary<string, string> Assemblies(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.dll").ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Имена сборок, на которые ссылается файл; у нативной библиотеки — ни одного.</summary>
    private static IReadOnlyList<string> References(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);

        if (!image.HasMetadata)
            return [];

        var metadata = image.GetMetadataReader();

        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }

    /// <summary>Всё, до чего доходят ссылки от названных сборок, — среди перечисленных.</summary>
    private static HashSet<string> Reach(IReadOnlyDictionary<string, string> assemblies, IEnumerable<string> starts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(starts.Where(assemblies.ContainsKey));

        while (pending.TryPop(out var name))
        {
            if (!seen.Add(name))
                continue;

            foreach (var reference in References(assemblies[name]))
            {
                if (assemblies.ContainsKey(reference) && !seen.Contains(reference))
                    pending.Push(reference);
            }
        }

        return seen;
    }
}
