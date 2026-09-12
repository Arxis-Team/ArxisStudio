using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка собранной студии: у корня она сама, платформа в Lib, модули в Modules.
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
    /// Сборки, которых в выходе нет намеренно.
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

    /// <summary>
    /// У корня студии — только она сама.
    /// </summary>
    /// <remarks>
    /// Платформа лежит в <c>Lib</c>, модули в <c>Modules</c>, нативные библиотеки в
    /// <c>runtimes/</c>, словари в <c>lang/</c>. Сборка, оказавшаяся у корня, — это либо забытая
    /// раскладкой, либо вторая копия той, что лежит в папке: корень основной контекст
    /// просматривает первым, и работать станет она.
    /// </remarks>
    [Fact]
    public void Beside_the_studio_there_is_only_the_studio()
    {
        var strangers = Root().Keys
            .Where(name => !string.Equals(name, "ArxisStudio", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(strangers.Count == 0, $"у корня лежат сборки, кроме самой студии: {string.Join(", ", strangers)}");
    }

    /// <summary>Каждый модуль лежит в папке модулей, а не у корня и не в платформе.</summary>
    [Fact]
    public void Every_module_lies_in_the_module_folder_and_nowhere_else()
    {
        var output = Output();
        var library = Library();
        var root = Root();

        foreach (var name in StudioModules.Assemblies.Select(assembly => assembly.GetName().Name!))
        {
            Assert.True(
                File.Exists(Path.Combine(output, StudioAssemblyFolder.Modules.Name, name + ".dll")),
                $"модуля {name} нет в папке {StudioAssemblyFolder.Modules.Name}");

            Assert.False(root.ContainsKey(name), $"модуль {name} лежит у корня студии");
            Assert.False(library.ContainsKey(name), $"модуль {name} лежит в платформе");
        }
    }

    /// <summary>
    /// Ни одна сборка не лежит в двух местах сразу.
    /// </summary>
    /// <remarks>
    /// Загрузится всё равно одна — корень первым, платформа раньше модулей, — а вторая копия только
    /// путала бы того, кто ищет, какой файл в работе.
    /// </remarks>
    [Fact]
    public void Nothing_lies_in_two_places_at_once()
    {
        var places = new (string Where, Dictionary<string, string> Assemblies)[]
        {
            ("корень", Root()),
            (StudioAssemblyFolder.Library.Name, Library()),
            (StudioAssemblyFolder.Modules.Name, Modules()),
        };

        var twice = places
            .SelectMany((first, index) => places.Skip(index + 1).SelectMany(second => first.Assemblies.Keys
                .Intersect(second.Assemblies.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(name => $"{name}: {first.Where} и {second.Where}")))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(twice.Count == 0, $"лежит в двух местах сразу: {string.Join(", ", twice)}");
    }

    /// <summary>
    /// Общие сборки лежат в платформе.
    /// </summary>
    /// <remarks>
    /// Плагины берут их из основного контекста, и место им там, где лежит всё, на чём стоит сама
    /// студия, а не в углу модуля, который оказался первым, кто их потянул. Ядро модели проектов —
    /// общее, хотя сама студия его типов и не зовёт.
    /// </remarks>
    [Fact]
    public void Shared_assemblies_live_in_the_library()
    {
        var library = Library();

        var elsewhere = Root().Keys
            .Concat(Modules().Keys)
            .Where(SharedAssemblies.IsShared)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(elsewhere.Count == 0, $"общие сборки лежат не в платформе: {string.Join(", ", elsewhere)}");
        Assert.True(library.ContainsKey("ArxisStudio.ProjectSystem"), "ядро модели проектов ушло из платформы");
    }

    /// <summary>
    /// В платформе нет ничего, что нужно только модулям.
    /// </summary>
    /// <remarks>
    /// Нужное самой студии — всё, до чего доходят ссылки от неё и от общих сборок, не заходя в
    /// модули. Нужное модулям — всё, до чего доходят ссылки от модулей. Сборка в платформе, нужная
    /// модулям и не нужная студии, — забытая раскладкой. Сборки, которые студия грузит без ссылки,
    /// этим правилом не задеты: модулям они не нужны, и под подозрение не попадают.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_library_is_needed_only_by_modules()
    {
        var library = Library();
        var studio = Together(Root(), library);
        var modules = Modules();

        var platform = Reach(studio, studio.Keys.Where(name => name == "ArxisStudio" || SharedAssemblies.IsShared(name)));
        var needed = Reach(Together(studio, modules), modules.Keys);

        var misplaced = library.Keys
            .Where(name => needed.Contains(name) && !platform.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            misplaced.Count == 0,
            $"в платформе лежит нужное только модулям: {string.Join(", ", misplaced)} — ему место в {StudioAssemblyFolder.Modules.Name}");
    }

    /// <summary>
    /// Каждая ссылка находится: у корня, в платформе, в папке модулей или в самой среде.
    /// </summary>
    /// <remarks>
    /// Раскладка, забывшая сборку, не падает при сборке — студия упала бы у человека, на первом
    /// обращении к её типу. Проверяются все три места сразу: студия ссылается и на платформу, и на
    /// модули, а модуль — на платформу и на своё.
    /// </remarks>
    [Fact]
    public void Every_reference_can_be_found()
    {
        var everything = Together(Together(Root(), Library()), Modules());
        var runtime = Assemblies(RuntimeEnvironment.GetRuntimeDirectory());

        var missing = everything
            .SelectMany(assembly => References(assembly.Value).Select(reference => (Assembly: assembly.Key, Reference: reference)))
            .Where(pair => !everything.ContainsKey(pair.Reference)
                && !runtime.ContainsKey(pair.Reference)
                && !FromTheSdk.Contains(pair.Reference))
            .Select(pair => $"{pair.Assembly} → {pair.Reference}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"не найти: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Нативные библиотеки остаются у корня, в runtimes/.
    /// </summary>
    /// <remarks>
    /// Их находит сама среда по файлу зависимостей приложения, откуда бы ни пришла управляемая
    /// сборка, которая их зовёт. Терминал без своей библиотеки на Linux и macOS не открылся бы, а
    /// на Windows этого не видно вовсе — там она ему не нужна.
    /// </remarks>
    [Fact]
    public void Native_libraries_stay_beside_the_studio()
    {
        var output = Output();

        Assert.True(
            File.Exists(Path.Combine(output, "runtimes", "linux-x64", "native", "libporta_pty.so")),
            "нативная библиотека терминала пропала из runtimes/ у корня студии");

        foreach (var folder in new[] { StudioAssemblyFolder.Library.Name, StudioAssemblyFolder.Modules.Name })
        {
            Assert.False(
                Directory.Exists(Path.Combine(output, folder, "runtimes")),
                $"в папку {folder} уехало runtimes/ — там его не ищут ни среда, ни резолвер");
        }
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

    /// <summary>
    /// Точка входа не делает ничего, кроме дороги к папкам и начала работы.
    /// </summary>
    /// <remarks>
    /// Платформа лежит в <c>Lib</c>, а JIT компилирует метод целиком до первой его строки: назови
    /// <c>Main</c> хоть один тип оттуда — и студия упадёт раньше, чем резолверы встанут, с
    /// сообщением о ненайденной сборке. Проверяется исходник, а не готовая сборка: правило о том,
    /// что в методе написано, и читается там же, где его нарушат.
    /// </remarks>
    [Fact]
    public void The_entry_point_only_shows_the_way_and_starts()
    {
        var source = File.ReadAllText(Path.Combine(SharedAssemblies.Repository(), "src", "ArxisStudio", "Program.cs"));
        var start = source.IndexOf("public static void Main(", StringComparison.Ordinal);

        Assert.True(start >= 0, "точки входа в Program.cs не нашлось");

        var body = Body(source, start)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
            ["StudioAssemblyFolder.Library.Attach();", "StudioAssemblyFolder.Modules.Attach();", "Start(args);"],
            body);
    }

    /// <summary>Тело метода: от первой фигурной скобки после него до парной ей.</summary>
    /// <param name="source">Исходник.</param>
    /// <param name="method">Где метод объявлен.</param>
    private static string Body(string source, int method)
    {
        var open = source.IndexOf('{', method);
        var depth = 0;

        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[(open + 1)..index];
            }
        }

        throw new InvalidOperationException("тело метода не закрылось");
    }

    /// <summary>Выход студии той же конфигурации, что у тестов.</summary>
    private static string Output()
    {
        var tests = new DirectoryInfo(AppContext.BaseDirectory);
        var output = Path.Combine(SharedAssemblies.Repository(), "src", "ArxisStudio", "bin", tests.Parent!.Name, tests.Name);

        Assert.True(File.Exists(Path.Combine(output, "ArxisStudio.dll")), $"студия не собрана в {output}");

        return output;
    }

    /// <summary>Сборки у корня студии.</summary>
    private static Dictionary<string, string> Root() => Assemblies(Output());

    /// <summary>Сборки платформы.</summary>
    private static Dictionary<string, string> Library() =>
        Assemblies(Path.Combine(Output(), StudioAssemblyFolder.Library.Name));

    /// <summary>Сборки модулей.</summary>
    private static Dictionary<string, string> Modules() =>
        Assemblies(Path.Combine(Output(), StudioAssemblyFolder.Modules.Name));

    /// <summary>Сборки папки: простое имя — путь.</summary>
    private static Dictionary<string, string> Assemblies(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.dll").ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Две папки одним набором; повтор остаётся за первой.</summary>
    /// <param name="first">Первая.</param>
    /// <param name="second">Вторая.</param>
    private static Dictionary<string, string> Together(
        Dictionary<string, string> first,
        Dictionary<string, string> second)
    {
        var together = new Dictionary<string, string>(first, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in second)
            together.TryAdd(name, path);

        return together;
    }

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
