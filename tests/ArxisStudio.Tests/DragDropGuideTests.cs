using System.Globalization;
using System.Text.RegularExpressions;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Руководство автора плагина по перетаскиванию — <c>docs/drag-drop.md</c>.
/// </summary>
/// <remarks>
/// Примеры собираются настоящим компилятором против настоящих сборок, а числа, версия и ссылки
/// сверяются с кодом: руководство, которое никто не проверяет, стареет первым.
/// </remarks>
public class DragDropGuideTests
{
    private static string Guide { get; } = File.ReadAllText(Repository.Path("docs", "drag-drop.md"));

    /// <summary>Каждый пример компилируется — все разом, как у автора, взявшего их в один проект.</summary>
    [Fact]
    public void Every_example_in_the_guide_compiles()
    {
        var examples = Regex.Matches(Guide, "^```csharp\r?\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Groups["code"].Value)
            .ToList();

        Assert.True(examples.Count >= 3, $"примеров в руководстве {examples.Count}");

        // Ссылки компилятору берутся из сборок, загруженных в процесс, а грузятся они лениво.
        foreach (var assembly in new[] { typeof(StudioPlugin).Assembly, typeof(AxListBox).Assembly })
            Assert.NotEmpty(assembly.Location);

        TestAssembly.Emit("Arxis.Guide.DragDrop", examples);
    }

    /// <summary>
    /// Руководство называет то, что в коде: версию SDK, с которой API есть, порог жеста и файлы
    /// репозитория.
    /// </summary>
    [Fact]
    public void The_guide_names_what_the_code_really_has()
    {
        var asked = Assert.Single(Regex.Matches(Guide, @"""sdk"": \{ ""min"": ""(?<version>[\d.]+)"" \}").Select(match => match.Groups["version"].Value));

        Assert.True(StudioSdk.Satisfies(asked), $"руководство просит SDK {asked}, а студия — {StudioSdk.Version}");
        Assert.Contains($"SDK {asked}", Guide, StringComparison.Ordinal);
        Assert.Contains($"({StudioDragDrop.Threshold.ToString(CultureInfo.InvariantCulture)} точек", Guide, StringComparison.Ordinal);

        foreach (var link in Regex.Matches(Guide, @"\]\((?<path>[^)#:]+)\)").Select(match => match.Groups["path"].Value))
        {
            var full = Path.GetFullPath(Repository.Path("docs", link));

            Assert.True(File.Exists(full) || Directory.Exists(full), $"{link} ведёт в никуда");
        }
    }
}
