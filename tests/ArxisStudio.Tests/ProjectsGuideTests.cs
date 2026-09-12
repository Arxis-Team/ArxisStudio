using System.Reflection;
using System.Text.RegularExpressions;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Руководство автора плагина — <c>docs/projects.md</c>.
/// </summary>
/// <remarks>
/// Документ, который никто не проверяет, устаревает первым: контракт двигают, а примеры в нём
/// остаются вчерашними — и автор плагина узнаёт об этом не из репозитория, а из ошибки компилятора
/// у себя. Здесь примеры собираются настоящим компилятором против настоящих сборок, а числа, имена
/// и коды сверяются с тем, что в коде.
/// </remarks>
public class ProjectsGuideTests
{
    private static string Guide { get; } = File.ReadAllText(Path.Combine(Root(), "docs", "projects.md"));

    /// <summary>Каждый пример из руководства компилируется.</summary>
    /// <remarks>
    /// Все разом и одной сборкой — так же, как они собрались бы у автора, взявшего их в свой
    /// проект. Заодно ловится и повторное имя: два примера с одинаковым типом были бы двумя
    /// разными ответами на один вопрос.
    /// </remarks>
    [Fact]
    public void Every_example_in_the_guide_compiles()
    {
        var examples = Examples("csharp");

        // Руководство без примеров прошло бы эту проверку молча, и она перестала бы что-либо
        // значить в ту же минуту.
        Assert.True(examples.Count >= 5, $"примеров в руководстве {examples.Count}");

        // Ссылки компилятору берутся из сборок, загруженных в этот процесс, а грузятся они лениво:
        // прогон одного этого теста застал бы контракт и модель ещё не загруженными, и примеры не
        // собрались бы по причине, не имеющей к ним отношения.
        foreach (var assembly in new[]
                 {
                     typeof(StudioPlugin).Assembly,
                     typeof(IStudioProjects).Assembly,
                     typeof(SolutionSnapshot).Assembly,
                 })
        {
            Assert.NotEmpty(assembly.Location);
        }

        TestAssembly.Emit("Arxis.Guide.Examples", examples);
    }

    /// <summary>
    /// Руководство называет то, что в коде есть на самом деле.
    /// </summary>
    /// <remarks>
    /// Три вещи стареют молча: идентификатор модуля, нижняя граница его версии и коды диагностик.
    /// Первые две автор скопирует в свой манифест, третий сравнит в коде, и ошибётся он не на своей
    /// машине.
    /// </remarks>
    [Fact]
    public void The_guide_names_what_the_code_really_has()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ProjectsModule).Assembly);

        Assert.Null(error);
        Assert.Contains($"\"id\": \"{manifest!.Id}\"", Guide, StringComparison.Ordinal);

        // Граница, которую руководство советует просить за пакеты, — версия модуля, которая их
        // принесла. Больше объявленной она быть не может: такой плагин не поднимется.
        var asked = Assert.Single(
            Regex.Matches(Guide, @"""id"": ""arxis\.projects"", ""min"": ""(?<version>[\d.]+)""")
                .Select(match => match.Groups["version"].Value));

        Assert.StartsWith(asked, manifest.Version, StringComparison.Ordinal);

        var declared = typeof(ProjectsDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var code in Regex.Matches(Guide, @"\bPRJ\d{4}\b").Select(match => match.Value))
            Assert.Contains(code, declared);

        // Ссылки на файлы репозитория: переехавший файл оставляет в руководстве мёртвую дорогу.
        foreach (var link in Regex.Matches(Guide, @"\]\((?<path>[^)#:]+)\)")
                     .Select(match => match.Groups["path"].Value))
        {
            var full = Path.GetFullPath(Path.Combine(Root(), "docs", link));

            Assert.True(File.Exists(full) || Directory.Exists(full), $"{link} ведёт в никуда");
        }
    }

    /// <summary>Примеры на заданном языке, по порядку.</summary>
    /// <param name="language">Слово после трёх обратных кавычек.</param>
    private static List<string> Examples(string language) =>
        [.. Regex.Matches(
                Guide,
                $"^```{language}\r?\n(?<code>.*?)^```",
                RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Groups["code"].Value)];

    /// <summary>Корень репозитория: тесты бегут из своей выходной папки.</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArxisStudio.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
