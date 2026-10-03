using System.Text.RegularExpressions;
using ArxisStudio.Extensibility;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Руководство автора плагина — <c>docs/xaml.md</c>.
/// </summary>
/// <remarks>
/// Документ, который никто не проверяет, устаревает первым. Здесь примеры собираются настоящим
/// компилятором против настоящих сборок, а имена, версии и дороги сверяются с тем, что в коде.
/// </remarks>
public class XamlGuideTests
{
    private static string Guide { get; } = File.ReadAllText(Repository.Path("docs", "xaml.md"));

    /// <summary>Каждый пример из руководства компилируется — все разом, одной сборкой.</summary>
    [Fact]
    public void Every_example_in_the_guide_compiles()
    {
        var examples = Examples("csharp");

        Assert.True(examples.Count >= 5, $"примеров в руководстве {examples.Count}");

        // Ссылки компилятору — сборки, загруженные в процесс, а грузятся они лениво: прогон одного этого
        // теста застал бы контракт и синтаксис ещё не загруженными.
        foreach (var assembly in new[]
                 {
                     typeof(StudioPlugin).Assembly,
                     typeof(IStudioXamlDocuments).Assembly,
                     typeof(XamlDocument).Assembly,
                     typeof(SourceText).Assembly,
                     typeof(SolutionSnapshot).Assembly,
                     typeof(Border).Assembly,
                 })
        {
            Assert.NotEmpty(assembly.Location);
        }

        TestAssembly.Emit("Arxis.Xaml.Guide.Examples", examples);
    }

    /// <summary>
    /// Руководство называет то, что в коде есть: модуль, его версию, номер SDK, настройку и дороги к
    /// файлам репозитория.
    /// </summary>
    [Fact]
    public void The_guide_names_what_the_code_really_has()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(XamlModule).Assembly);

        Assert.Null(error);

        var asked = Assert.Single(
            Regex.Matches(Guide, @"""id"": ""arxis\.xaml"", ""min"": ""(?<version>[\d.]+)""")
                .Select(match => match.Groups["version"].Value));

        Assert.StartsWith(asked, manifest!.Version, StringComparison.Ordinal);

        // Номер SDK, который руководство велит просить, у студии есть.
        var sdk = Assert.Single(
            Regex.Matches(Guide, @"""sdk"": \{ ""min"": ""(?<version>[\d.]+)"" \}")
                .Select(match => match.Groups["version"].Value));

        Assert.True(StudioSdk.Satisfies(sdk), $"руководство просит SDK {sdk}, а у студии {StudioSdk.Version}");

        Assert.Contains($"`{XamlSettings.BuildDelayKey}`, {XamlSettings.DefaultBuildDelay} мс", Guide, StringComparison.Ordinal);

        foreach (var state in Enum.GetNames<XamlDesignState>())
            Assert.Contains($"| `{state}` |", Guide, StringComparison.Ordinal);

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
