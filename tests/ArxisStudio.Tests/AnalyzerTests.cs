using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «интерфейс плагина строится на контролах студии»: что анализатор
/// запрещает и, не менее важно, чего он не трогает.
/// </summary>
public class AnalyzerTests
{
    [Fact]
    public async Task An_avalonia_widget_is_reported()
    {
        var found = await AnalyzeAsync("var control = new Avalonia.Controls.Button();");

        var diagnostic = Assert.Single(found);

        Assert.Equal(AvaloniaWidgetAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("Button", diagnostic.GetMessage());
    }

    [Fact]
    public async Task A_studio_control_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("var control = new ArxisStudio.Controls.AxButton();"));
    }

    /// <summary>
    /// Панели раскладки оформления не несут, и запрещать их значило бы
    /// запретить плагину раскладывать свои же контролы.
    /// </summary>
    [Theory]
    [InlineData("Avalonia.Controls.StackPanel")]
    [InlineData("Avalonia.Controls.Grid")]
    [InlineData("Avalonia.Controls.DockPanel")]
    [InlineData("Avalonia.Controls.Canvas")]
    [InlineData("Avalonia.Controls.Border")]
    [InlineData("Avalonia.Controls.TextBlock")]
    [InlineData("Avalonia.Controls.Image")]
    public async Task Layout_and_primitives_are_allowed(string type)
    {
        Assert.Empty(await AnalyzeAsync($"var control = new {type}();"));
    }

    /// <summary>
    /// Своя кнопка поверх <c>AxButton</c> — по-прежнему контрол студии, хотя
    /// предком у неё в конце концов оказывается шаблонный контрол Avalonia.
    /// </summary>
    [Fact]
    public async Task An_heir_of_a_studio_control_is_allowed()
    {
        var found = await AnalyzeAsync(
            "var control = new Heir();",
            "public sealed class Heir : ArxisStudio.Controls.AxButton { }");

        Assert.Empty(found);
    }

    /// <summary>
    /// Пустой наследник виджета — тот же виджет: создавать его запрет не мешал, потому что живёт он
    /// в сборке плагина, а тему приносит чужую. Замечание стоит у базы, названной виджетом, — один
    /// раз, а не у каждого наследника и каждого <c>new</c>.
    /// </summary>
    [Fact]
    public async Task A_class_derived_from_an_avalonia_widget_is_reported_where_it_names_the_widget()
    {
        var found = await AnalyzeAsync(
            "var box = new Deeper();",
            """
            public class Box : Avalonia.Controls.TextBox { }
            public sealed class Deeper : Box { }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(AvaloniaWidgetAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("TextBox — виджет Avalonia; вместо него — AxTextBox", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Avalonia.Controls.TextBox", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    /// <summary>
    /// Свой шаблонный контрол, своя панель раскладки и наследник контрола студии — законны.
    /// </summary>
    /// <remarks>
    /// Контролу прямо от <c>TemplatedControl</c> шаблон пишет само расширение, и чужой темы у него
    /// нет: это канва плагина, только с шаблоном.
    /// </remarks>
    [Fact]
    public async Task The_plugins_own_templated_control_and_panels_are_allowed()
    {
        var found = await AnalyzeAsync(
            "var gauge = new Gauge();",
            """
            public class Gauge : Avalonia.Controls.Primitives.TemplatedControl { }
            public class Strip : Avalonia.Controls.StackPanel { }
            public class Field : ArxisStudio.Controls.AxTextBox { }
            public sealed class Plain : IDisposable { public void Dispose() { } }
            """);

        Assert.Empty(found);
    }

    /// <summary>
    /// Разделитель меню у расширения — <c>AxSeparator</c>, и правило называет его само.
    /// </summary>
    /// <remarks>
    /// Замену правило ищет в наборе по имени, и пока её не было, говорило «замены пока нет» — меню
    /// расширения делило группы разве что цветом пункта.
    /// </remarks>
    [Fact]
    public async Task A_separator_is_reported_with_its_studio_replacement()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("var line = new Avalonia.Controls.Separator();"));

        Assert.Contains("вместо него — AxSeparator", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(await AnalyzeAsync("var line = new ArxisStudio.Controls.AxSeparator();"));
    }

    [Fact]
    public async Task Every_widget_in_the_file_is_reported()
    {
        var found = await AnalyzeAsync(
            """
            var first = new Avalonia.Controls.TextBox();
            var second = new Avalonia.Controls.CheckBox();
            var panel = new Avalonia.Controls.StackPanel();
            """);

        Assert.Equal(2, found.Length);
    }

    /// <summary>
    /// Собирает код и возвращает то, что нашёл в нём анализатор.
    /// </summary>
    /// <remarks>
    /// Ссылки берутся из сборок процесса тестов, но нужные сначала приходится
    /// тронуть: сборка загружается при первом обращении к её типу, и без этого
    /// библиотеки контролов в списке просто не окажется.
    /// </remarks>
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body, string extra = "")
    {
        var source = $$"""
            using System;

            public static class Probe
            {
                public static void Build()
                {
                    {{body}}
                }
            }

            {{extra}}
            """;

        Type[] anchors =
        [
            typeof(object),
            typeof(Avalonia.Controls.Button),
            typeof(Avalonia.Visual),
            typeof(ArxisStudio.Controls.AxButton),
        ];

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(anchors.Select(anchor => anchor.Assembly))
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Ошибки самого кода означали бы, что тест проверяет не то, что думает.
        var broken = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(broken.Count == 0, string.Join("; ", broken.Select(diagnostic => diagnostic.GetMessage())));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new AvaloniaWidgetAnalyzer()));

        return await analyzed.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
