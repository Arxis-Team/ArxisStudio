using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Sdk.Analyzers;
using Avalonia;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило ARX0014: расширение не правит стили и ресурсы всего приложения.
/// </summary>
/// <remarks>
/// <c>Application.Current.Styles</c> — общее на процесс: стиль оттуда оценивается на каждом
/// контроле студии и всех соседних панелей, а ресурс с чужим ключом молча подменяет значение
/// темы. Хуже того, с выгрузкой расширения ни то, ни другое не уходит — выключенный плагин
/// продолжает красить окно до перезапуска.
/// </remarks>
public class ApplicationStylesAnalyzerTests
{
    /// <summary>Стиль, добавленный в приложение, замечен.</summary>
    [Fact]
    public async Task Adding_a_style_to_the_application_is_noticed()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """
            using Avalonia;
            using Avalonia.Styling;

            public sealed class Panel
            {
                public void Paint() => Application.Current!.Styles.Add(new Style());
            }
            """));

        Assert.Equal(ApplicationStylesAnalyzer.DiagnosticId, found.Id);
        Assert.Contains("Styles", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Ресурс приложения — то же самое, и правило зовёт его по имени.</summary>
    [Fact]
    public async Task Writing_an_application_resource_is_noticed()
    {
        var found = Assert.Single(await AnalyzeAsync(
            """
            using Avalonia;

            public sealed class Panel
            {
                public void Paint() => Application.Current!.Resources["AxAccentBrush"] = null;
            }
            """));

        Assert.Contains("Resources", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Дорога к общему списку в обход правила не проходит.
    /// </summary>
    /// <remarks>
    /// Правило смотрит на само обращение, а не на добавление: сохранить список в переменную и
    /// дописать его потом — та же правка теми же последствиями.
    /// </remarks>
    [Fact]
    public async Task Saving_the_list_first_does_not_help()
    {
        Assert.Single(await AnalyzeAsync(
            """
            using Avalonia;
            using Avalonia.Styling;

            public sealed class Panel
            {
                public void Paint()
                {
                    var styles = Application.Current!.Styles;

                    styles.Add(new Style());
                }
            }
            """));
    }

    /// <summary>Свои стили панели правило не трогает: они живут столько же, сколько она.</summary>
    [Fact]
    public async Task The_styles_of_a_panel_are_its_own_business()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            using ArxisStudio.Controls;
            using Avalonia.Styling;

            public sealed class Panel : AxUserControl
            {
                public void Paint() => Styles.Add(new Style());
            }
            """));
    }

    /// <summary>Ресурсы своей панели — тоже её дело.</summary>
    [Fact]
    public async Task The_resources_of_a_panel_are_its_own_business()
    {
        Assert.Empty(await AnalyzeAsync(
            """
            using ArxisStudio.Controls;

            public sealed class Panel : AxUserControl
            {
                public void Paint() => Resources["Свой"] = 12d;
            }
            """));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string code)
    {
        Assembly[] anchors = [typeof(AxButton).Assembly, typeof(Application).Assembly];

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(anchors)
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(code)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ApplicationStylesAnalyzer()),
            new AnalyzerOptions([]));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
