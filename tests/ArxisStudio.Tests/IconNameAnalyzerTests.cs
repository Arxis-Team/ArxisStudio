using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Sdk.Analyzers;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило ARX0013: кнопка со значком называет себя средствам доступности.
/// </summary>
/// <remarks>
/// Кнопка, у которой всё содержимое — глиф, для экранного диктора немая: он прочтёт «кнопка» и
/// замолчит. Подсказка под курсором этого не заменяет — она приходит по наведению мыши, а тому,
/// кому имя нужно, мышь не помощник.
/// <para>
/// Входа два, код и разметка, и проверяются оба: правило, живущее в одном, обходилось бы сменой
/// места записи.
/// </para>
/// </remarks>
public class IconNameAnalyzerTests
{
    private const string Studio = "https://github.com/Arxis-Team/ArxisStudio";

    /// <summary>Кнопка со значком и без имени замечена в разметке.</summary>
    [Fact]
    public async Task An_icon_button_without_a_name_is_noticed_in_markup()
    {
        var found = Assert.Single(await MarkupAsync(
            $"""
             <AxUserControl xmlns="{Studio}">
               <AxButton>
                 <AxIcon/>
               </AxButton>
             </AxUserControl>
             """));

        Assert.Equal(IconNameAnalyzer.DiagnosticId, found.Id);
        Assert.Contains("AxButton", found.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(1, found.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>Имя, поставленное атрибутом, правило засчитывает.</summary>
    [Fact]
    public async Task A_named_icon_button_is_left_alone()
    {
        Assert.Empty(await MarkupAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:a="https://github.com/avaloniaui">
               <AxButton a:AutomationProperties.Name="Собрать решение">
                 <AxIcon/>
               </AxButton>
             </AxUserControl>
             """));
    }

    /// <summary>Кнопка с подписью называет себя сама.</summary>
    [Fact]
    public async Task A_button_with_a_label_names_itself()
    {
        Assert.Empty(await MarkupAsync(
            $"""
             <AxUserControl xmlns="{Studio}">
               <AxButton Content="Собрать"/>
             </AxUserControl>
             """));
    }

    /// <summary>
    /// Значок рядом с подписью — не «кнопка со значком».
    /// </summary>
    /// <remarks>
    /// Имя такой кнопке даёт её текст, и требовать второе имя значило бы заставлять писать одно и
    /// то же дважды.
    /// </remarks>
    [Fact]
    public async Task An_icon_beside_a_label_is_not_an_icon_button()
    {
        Assert.Empty(await MarkupAsync(
            $"""
             <AxUserControl xmlns="{Studio}" xmlns:a="https://github.com/avaloniaui">
               <AxButton>
                 <a:StackPanel>
                   <AxIcon/>
                   <a:TextBlock Text="Собрать"/>
                 </a:StackPanel>
               </AxButton>
             </AxUserControl>
             """));
    }

    /// <summary>Значок, положенный свойством-элементом, виден так же.</summary>
    [Fact]
    public async Task The_content_written_as_a_property_element_counts_too()
    {
        Assert.Single(await MarkupAsync(
            $"""
             <AxUserControl xmlns="{Studio}">
               <AxToggleButton>
                 <AxToggleButton.Content>
                   <AxIcon/>
                 </AxToggleButton.Content>
               </AxToggleButton>
             </AxUserControl>
             """));
    }

    /// <summary>Кнопка со значком и без имени замечена и в коде.</summary>
    [Fact]
    public async Task An_icon_button_without_a_name_is_noticed_in_code()
    {
        var found = Assert.Single(await CodeAsync(
            """
            using ArxisStudio.Controls;
            using ArxisStudio.Icons;

            public sealed class Panel
            {
                public AxButton Build() => new AxButton { Content = new AxIcon() };
            }
            """));

        Assert.Equal(IconNameAnalyzer.DiagnosticId, found.Id);
        Assert.Contains("AxButton", found.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Имя в том же инициализаторе правило засчитывает.</summary>
    [Fact]
    public async Task A_name_in_the_initializer_is_enough()
    {
        Assert.Empty(await CodeAsync(
            """
            using ArxisStudio.Controls;
            using ArxisStudio.Icons;
            using Avalonia.Automation;

            public sealed class Panel
            {
                public AxButton Build() => new AxButton
                {
                    Content = new AxIcon(),
                    [AutomationProperties.NameProperty] = "Собрать",
                };
            }
            """));
    }

    /// <summary>Имя, поставленное соседним вызовом той же кнопке, тоже засчитывается.</summary>
    [Fact]
    public async Task A_name_set_next_to_the_button_counts()
    {
        Assert.Empty(await CodeAsync(
            """
            using ArxisStudio.Controls;
            using ArxisStudio.Icons;
            using Avalonia.Automation;

            public sealed class Panel
            {
                public AxButton Build()
                {
                    var button = new AxButton { Content = new AxIcon() };

                    AutomationProperties.SetName(button, "Собрать");

                    return button;
                }
            }
            """));
    }

    /// <summary>Кнопка с текстом в коде правила не касается.</summary>
    [Fact]
    public async Task A_text_button_in_code_is_left_alone()
    {
        Assert.Empty(await CodeAsync(
            """
            using ArxisStudio.Controls;

            public sealed class Panel
            {
                public AxButton Build() => new AxButton { Content = "Собрать" };
            }
            """));
    }

    private static async Task<ImmutableArray<Diagnostic>> MarkupAsync(string markup) =>
        await AnalyzeAsync("public sealed class Probe { }", [new Given("C:/probe/View.axaml", markup)]);

    private static async Task<ImmutableArray<Diagnostic>> CodeAsync(string code) =>
        await AnalyzeAsync(code, []);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string code, AdditionalText[] files)
    {
        // Сборки грузятся лениво, а ссылки собираются по загруженным: без касания типов ни
        // контролов студии, ни виджетов Avalonia в списке может не оказаться вовсе.
        Assembly[] anchors = [typeof(AxButton).Assembly, typeof(AxIcon).Assembly, typeof(Button).Assembly];

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
            ImmutableArray.Create<DiagnosticAnalyzer>(new IconNameAnalyzer()),
            new AnalyzerOptions([.. files]));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Файл, переданный анализатору входом сборки.</summary>
    private sealed class Given(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }
}
