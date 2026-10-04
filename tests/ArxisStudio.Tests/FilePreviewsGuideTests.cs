using System.Text.RegularExpressions;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Руководство автора плагина по превью файлов — <c>docs/file-previews.md</c>.
/// </summary>
/// <remarks>
/// Примеры собираются настоящим компилятором против настоящих сборок, а версия, размер снимка,
/// переменная среды и ссылки сверяются с кодом: руководство, которое никто не проверяет, стареет первым.
/// </remarks>
public class FilePreviewsGuideTests
{
    private static string Guide { get; } = File.ReadAllText(Repository.Path("docs", "file-previews.md"));

    /// <summary>Каждый пример компилируется — все разом, как у автора, взявшего их в один проект.</summary>
    [Fact]
    public void Every_example_in_the_guide_compiles()
    {
        var examples = Regex.Matches(Guide, "^```csharp\r?\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Groups["code"].Value)
            .ToList();

        Assert.True(examples.Count >= 2, $"примеров в руководстве {examples.Count}");

        // Ссылки компилятору берутся из сборок, загруженных в процесс, а грузятся они лениво.
        foreach (var assembly in new[] { typeof(StudioPlugin).Assembly, typeof(Bitmap).Assembly, typeof(Image).Assembly })
            Assert.NotEmpty(assembly.Location);

        TestAssembly.Emit("Arxis.Guide.FilePreviews", examples);
    }

    /// <summary>
    /// Руководство называет то, что в коде: версию SDK, с которой API есть, размер снимка формы,
    /// переменную среды его папки, настройку фоновой съёмки и файлы репозитория.
    /// </summary>
    [Fact]
    public void The_guide_names_what_the_code_really_has()
    {
        var asked = Assert.Single(Regex.Matches(Guide, @"""sdk"": \{ ""min"": ""(?<version>[\d.]+)"" \}").Select(match => match.Groups["version"].Value));

        Assert.True(StudioSdk.Satisfies(asked), $"руководство просит SDK {asked}, а студия — {StudioSdk.Version}");
        Assert.Contains($"SDK {asked}", Guide, StringComparison.Ordinal);
        Assert.Contains($"— {FormSnapshots.Pixels} точки", Guide, StringComparison.Ordinal);
        Assert.Contains($"`{FormSnapshots.EnvironmentVariable}`", Guide, StringComparison.Ordinal);
        Assert.Contains($"`{UiDesignerModule.PreviewsKey}`", Guide, StringComparison.Ordinal);

        foreach (var link in Regex.Matches(Guide, @"\]\((?<path>[^)#:]+)\)").Select(match => match.Groups["path"].Value))
        {
            var full = Path.GetFullPath(Repository.Path("docs", link));

            Assert.True(File.Exists(full) || Directory.Exists(full), $"{link} ведёт в никуда");
        }
    }
}
