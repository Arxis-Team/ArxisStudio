using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Собирает сборку плагина прямо в память.
/// </summary>
/// <remarks>
/// Заводить отдельный проект ради каждого случая — забытого манифеста, команды
/// не там, где положено, — дороже самой проверки: модулю нужен манифест в папке
/// и несколько строк кода, и то и другое выдаётся здесь.
/// Настоящие примеры — <c>Arxis.HelloPlugin</c> и
/// <c>ArxisStudio.Modules.Sample</c> — отвечают за то, что работает вся дорога;
/// эти сборки отвечают за случаи, которых у примеров нет и быть не должно.
/// </remarks>
internal static class TestAssembly
{
    /// <summary>
    /// Компилирует сборку в память.
    /// </summary>
    /// <param name="name">Имя сборки.</param>
    /// <param name="source">Исходный код.</param>
    public static Assembly Emit(string name, string source) => Emit(name, [source]);

    /// <summary>
    /// Компилирует сборку из нескольких файлов.
    /// </summary>
    /// <param name="name">Имя сборки.</param>
    /// <param name="sources">Исходные файлы.</param>
    /// <remarks>
    /// Файлов бывает больше одного там, где проверяется не случай, а готовая
    /// раскладка: у шаблона плагина точка входа и панель лежат порознь, и
    /// склеить их в один файл значило бы проверять не то, что получит автор.
    /// </remarks>
    public static Assembly Emit(string name, IEnumerable<string> sources)
    {
        using var image = new MemoryStream();

        Compile(name, sources, image);

        return Assembly.Load(image.ToArray());
    }

    /// <summary>
    /// Собирает встроенный модуль в его папку и загружает оттуда.
    /// </summary>
    /// <param name="name">Имя сборки.</param>
    /// <param name="source">Исходный код.</param>
    /// <param name="manifest">Содержимое <c>module.json</c>.</param>
    /// <returns>Загруженная сборка модуля.</returns>
    /// <remarks>
    /// Манифест модуля — файл в его папке, поэтому сборка в памяти манифеста иметь не может: папки
    /// у неё нет, а <see cref="ModuleManifest.FolderOf"/> отдаёт ей папку приложения, где чужой
    /// манифест лежать не должен. Папка собирается той же формы, что у настоящего модуля, —
    /// <c>module.json</c> в корне, сборка в <c>bin</c>, — и проверяется на ней же.
    /// <para>
    /// Папки не убираются за собой: сборка едет в основной контекст загрузки и не выгружается, а
    /// файл под ней остаётся занятым до конца процесса. Вместо уборки — общий корень, который
    /// подметается один раз за прогон.
    /// </para>
    /// </remarks>
    public static Assembly EmitModule(string name, string source, string manifest) =>
        EmitModule(name, [source], manifest);

    /// <inheritdoc cref="EmitModule(string, string, string)"/>
    /// <param name="name">Имя сборки; к нему добавляется отличающая часть.</param>
    /// <param name="sources">Исходные файлы.</param>
    /// <param name="manifest">Содержимое <c>module.json</c>.</param>
    public static Assembly EmitModule(string name, IEnumerable<string> sources, string manifest)
    {
        // Имя делается единственным на процесс: сборка едет в основной контекст загрузки, а он
        // держит одно имя один раз — второй модуль, названный так же, вернул бы первую сборку
        // вместе с её папкой и её манифестом, и тест проверил бы чужой манифест. Сборка в память
        // этим не страдала: там каждый вызов давал новую сборку, чьё имя никого не связывало.
        var unique = $"{name}.{Guid.NewGuid():N}";
        var folder = Directory.CreateDirectory(Path.Combine(Root.Value, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName, unique + ".dll");

        File.WriteAllText(Path.Combine(folder, ModuleManifestFile), manifest);

        using (var file = File.Create(path))
            Compile(unique, sources, file);

        return Assembly.LoadFrom(path);
    }

    /// <summary>
    /// Компилирует сборку в файл, не загружая её в процесс.
    /// </summary>
    /// <param name="path">Куда положить сборку.</param>
    /// <param name="name">Имя сборки.</param>
    /// <param name="source">Исходный код.</param>
    /// <remarks>
    /// Нужна там, где проверяется сама загрузка: сборку, которую студия обязана
    /// взять с диска, заранее загруженная копия подменила бы, и проверка прошла
    /// бы на чужой сборке.
    /// </remarks>
    public static void EmitFile(string path, string name, string source)
    {
        using var file = File.Create(path);

        Compile(name, [source], file);
    }

    /// <summary>
    /// Кладёт внешний плагин папкой каталога: манифест в корне, сборка в <c>bin</c>.
    /// </summary>
    /// <param name="root">Папка плагинов.</param>
    /// <param name="id">Идентификатор — он же имя папки.</param>
    /// <param name="assembly">Имя сборки.</param>
    /// <param name="source">Код плагина.</param>
    /// <param name="activation">Событие активации.</param>
    /// <param name="name">Имя в манифесте; null — идентификатор.</param>
    /// <param name="dependencies">Зависимости записью манифеста; null — ни от кого.</param>
    /// <returns>Папку плагина.</returns>
    /// <remarks>
    /// Сборка не загружается: плагин поднимает хост из своего контекста, и копия, заранее
    /// загруженная в процесс тестов, подменила бы ту, что лежит в папке.
    /// </remarks>
    public static string EmitPlugin(
        string root,
        string id,
        string assembly,
        string source,
        string activation = "onStartup",
        string? name = null,
        string? dependencies = null)
    {
        var folder = Path.Combine(root, id);
        var bin = Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName;

        EmitFile(Path.Combine(bin, assembly + ".dll"), assembly, source);

        var fields = new List<string>
        {
            $"\"id\": \"{id}\"",
            $"\"name\": \"{name ?? id}\"",
            "\"version\": \"1.0.0\"",
            $"\"entry\": \"bin/{assembly}.dll\"",
        };

        if (dependencies is not null)
            fields.Add($"\"dependencies\": {dependencies}");

        fields.Add($"\"activation\": [ \"{activation}\" ]");

        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{\n  " + string.Join(",\n  ", fields) + "\n}\n");

        return folder;
    }

    /// <summary>Имя файла манифеста — то же, что у настоящего модуля.</summary>
    private const string ModuleManifestFile = "module.json";

    /// <summary>
    /// Общий корень папок собранных модулей, подметаемый один раз за прогон.
    /// </summary>
    /// <remarks>
    /// Тем же приёмом живут теневые копии плагинов: занятое удалить нельзя, а бросить навсегда
    /// незачем — чистится прошлое, а не своё.
    /// </remarks>
    private static readonly Lazy<string> Root = new(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "arxis-test-module");

        foreach (var stale in Directory.Exists(root) ? Directory.EnumerateDirectories(root) : [])
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Папка прошлого прогона занята другим процессом — не наше дело.
            }
        }

        return Directory.CreateDirectory(root).FullName;
    });

    private static void Compile(string name, IEnumerable<string> sources, Stream output)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var result = AnalyzerRun.Probe(sources.Select(text => AnalyzerRun.Tree(text)), name: name).Emit(output);

        // Сборка, не собравшаяся сама, проверила бы что угодно, кроме контракта.
        Assert.True(
            result.Success,
            string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.GetMessage())));
    }
}
