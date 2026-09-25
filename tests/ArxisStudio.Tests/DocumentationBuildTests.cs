using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// XML-комментарии студии проверяет сборка — у каждого её проекта.
/// </summary>
/// <remarks>
/// Без файла документации компилятор комментарии не разбирает вовсе: незакрытый тег, параметр,
/// которого уже нет, и ссылка на переименованный метод живут в таком проекте сколько угодно. У
/// приложения и у каркаса генерация была выключена, и когда её включили впервые, нашлись все три.
/// Правило поэтому закреплено здесь: новый проект, заведённый без генерации, снова стал бы местом,
/// где комментарий может соврать молча.
/// </remarks>
public class DocumentationBuildTests
{
    /// <summary>Каждый проект под <c>src</c> собирает файл документации.</summary>
    [Fact]
    public void Every_project_of_the_studio_builds_its_documentation()
    {
        var source = Repository.Path("src");

        var silent = Directory
            .EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Generates(path))
            .Select(path => Path.GetRelativePath(source, path))
            .ToList();

        Assert.True(
            silent.Count == 0,
            "Проекты без GenerateDocumentationFile — их XML-комментарии сборка не проверяет: " + string.Join(", ", silent));
    }

    /// <summary>
    /// Файл документации приложения в выход не едет.
    /// </summary>
    /// <remarks>
    /// У корня выхода лежит только сама студия, и генерация нужна приложению ради проверки, а не
    /// ради файла. Снятая пометка положила бы <c>ArxisStudio.xml</c> рядом с exe.
    /// </remarks>
    [Fact]
    public void The_documentation_of_the_application_stays_out_of_its_output()
    {
        var project = XDocument.Load(
            Repository.Path("src", "ArxisStudio", "ArxisStudio.csproj"));

        Assert.Contains(
            project.Descendants("CopyDocumentationFileToOutputDirectory"),
            element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Generates(string project) =>
        XDocument.Load(project)
            .Descendants("GenerateDocumentationFile")
            .Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
