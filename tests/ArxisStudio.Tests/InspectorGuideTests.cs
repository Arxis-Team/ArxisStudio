using System.ComponentModel;
using System.Text.RegularExpressions;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Руководство автора плагина — <c>docs/inspector.md</c> (запись 365).
/// </summary>
/// <remarks>
/// Документ, который никто не проверяет, устаревает первым. Здесь примеры собираются настоящим компилятором против
/// настоящих сборок, а имена, версии и дороги сверяются с тем, что в коде.
/// </remarks>
public class InspectorGuideTests
{
    private static string Guide { get; } = File.ReadAllText(Repository.Path("docs", "inspector.md"));

    /// <summary>Каждый пример из руководства компилируется — все разом, одной сборкой.</summary>
    [Fact]
    public void Every_example_in_the_guide_compiles()
    {
        var examples = Examples("csharp");

        Assert.True(examples.Count >= 6, $"примеров в руководстве {examples.Count}");

        // Ссылки компилятору — сборки, загруженные в процесс, а грузятся они лениво: прогон одного этого теста
        // застал бы контракт, контролы и атрибуты ещё не загруженными.
        foreach (var assembly in new[]
                 {
                     typeof(StudioPlugin).Assembly,
                     typeof(IStudioInspector).Assembly,
                     typeof(AxNumberField).Assembly,
                     typeof(Border).Assembly,
                     typeof(INotifyPropertyChanged).Assembly,
                     typeof(CategoryAttribute).Assembly,
                     typeof(System.ComponentModel.DataAnnotations.RangeAttribute).Assembly,
                 })
        {
            Assert.NotEmpty(assembly.Location);
        }

        TestAssembly.Emit("Arxis.Inspector.Guide.Examples", examples);
    }

    /// <summary>
    /// Руководство называет то, что в коде есть: модуль и его версию, номер SDK, событие пробуждения, источники
    /// значения и дороги к файлам репозитория.
    /// </summary>
    [Fact]
    public void The_guide_names_what_the_code_really_has()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(InspectorModule).Assembly);

        Assert.Null(error);

        var asked = Assert.Single(
            Regex.Matches(Guide, @"""id"": ""arxis\.inspector"", ""min"": ""(?<version>[\d.]+)""")
                .Select(match => match.Groups["version"].Value));

        Assert.StartsWith(asked, manifest!.Version, StringComparison.Ordinal);

        var sdk = Assert.Single(
            Regex.Matches(Guide, @"""sdk"": \{ ""min"": ""(?<version>[\d.]+)"" \}")
                .Select(match => match.Groups["version"].Value));

        Assert.True(StudioSdk.Satisfies(sdk), $"руководство просит SDK {sdk}, а у студии {StudioSdk.Version}");
        Assert.Contains($"\"onContribution:{manifest.Id}\"", Guide, StringComparison.Ordinal);

        foreach (var source in Enum.GetNames<InspectorValueSource>())
            Assert.Contains($"| `{source}` |", Guide, StringComparison.Ordinal);

        foreach (var link in Regex.Matches(Guide, @"\]\((?<path>[^)#:]+)\)")
                     .Select(match => match.Groups["path"].Value))
        {
            var full = Path.GetFullPath(Repository.Path("docs", link));

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
}
