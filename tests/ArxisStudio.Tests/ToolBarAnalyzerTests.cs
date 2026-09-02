using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правила про полосу: манифест и код обязаны говорить одно и то же.
/// </summary>
/// <remarks>
/// Разойтись они могут молча — класс переименовали, а в манифесте забыли, — и
/// человек увидит пустое место вместо кнопки. Студия скажет об этом в журнал,
/// но журнал прочтёт уже пользователь, а не автор.
/// </remarks>
public class ToolBarAnalyzerTests
{
    private const string Attribute = """
        namespace ArxisStudio.Sdk;

        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class ToolBarItemAttribute(string id) : System.Attribute
        {
            public string Id { get; } = id;
        }
        """;

    /// <summary>Кнопка зовёт команду, которую плагин не объявлял.</summary>
    [Fact]
    public async Task A_button_naming_an_undeclared_command_is_reported()
    {
        var found = await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run" } ],
                "toolBar": [
                  { "id": "probe.go", "command": "probe.run" },
                  { "id": "probe.stop", "command": "probe.halt" }
                ]
              }
            }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);

        // Место находки — сам манифест: править нужно там, а не в коде.
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Меню и свой контрол команды не зовут — и спрашивать её с них незачем.
    /// </summary>
    [Fact]
    public async Task Only_buttons_are_asked_about_their_command()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [
                  { "id": "probe.menu", "kind": "menu", "menu": "%menu.tools%", "command": "probe.halt" },
                  { "id": "probe.strip", "kind": "custom", "command": "probe.halt" }
                ]
              }
            }
            """, """
            using ArxisStudio.Sdk;

            [ToolBarItem("probe.strip")]
            public sealed class Strip { }
            """));
    }

    /// <summary>
    /// Кнопку вовсе без команды это правило не трогает.
    /// </summary>
    /// <remarks>
    /// Граница нарочная: правило сверяет ссылки — названо ли то, на что
    /// ссылаются. Полноты объявления оно не проверяет, и кнопку без команды
    /// студия отказывается ставить сама, вслух: <c>StudioToolBar</c> говорит об
    /// этом в журнал. Двум местам проверять одно незачем.
    /// </remarks>
    [Fact]
    public async Task A_button_without_any_command_is_left_to_the_studio()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [ { "id": "probe.mute", "icon": "arxis:Play" } ]
              }
            }
            """));
    }

    /// <summary>Свой контрол объявлен, а класса под него нет.</summary>
    [Fact]
    public async Task A_custom_item_without_its_class_is_reported()
    {
        var found = await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [ { "id": "probe.strip", "kind": "custom" } ]
              }
            }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.MissingId, diagnostic.Id);
        Assert.Contains("probe.strip", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Класс помечен атрибутом, а манифест о нём молчит.
    /// </summary>
    /// <remarks>
    /// Самая обычная ошибка первого раза: класс написан, а объявить его забыли —
    /// и полоса о нём не узнает, потому что собирается по манифесту.
    /// </remarks>
    [Fact]
    public async Task A_marked_class_that_the_manifest_does_not_declare_is_reported()
    {
        var found = await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [ { "id": "probe.other", "kind": "custom" } ]
              }
            }
            """, """
            using ArxisStudio.Sdk;

            [ToolBarItem("probe.other")]
            public sealed class Declared { }

            [ToolBarItem("probe.forgotten")]
            public sealed class Forgotten { }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.UndeclaredId, diagnostic.Id);
        Assert.Contains("probe.forgotten", diagnostic.GetMessage(), StringComparison.Ordinal);

        // А это место — в коде: манифест здесь ни при чём, забыли объявление.
        Assert.EndsWith(".cs", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Манифест и код совпали — правила молчат.</summary>
    [Fact]
    public async Task A_toolbar_that_matches_its_code_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run" } ],
                "toolBar": [
                  { "id": "probe.go", "command": "probe.run", "icon": "arxis:Play" },
                  { "id": "probe.strip", "kind": "custom" }
                ]
              }
            }
            """, """
            using ArxisStudio.Sdk;

            [ToolBarItem("probe.strip")]
            public sealed class Strip { }
            """));
    }

    /// <summary>
    /// Слово «toolBar» в описании — не секция.
    /// </summary>
    /// <remarks>
    /// Разбор ищет имя вместе с открывающей скобкой, а не просто слово: описание
    /// плагина пишет человек, и рассказ о его кнопках увёл бы разбор в середину
    /// строки — с находками на пустом месте и молчанием там, где надо сказать.
    /// </remarks>
    [Fact]
    public async Task A_word_in_the_description_is_not_a_section()
    {
        var found = await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "description": "Кнопки toolBar и команды commands этого плагина",
              "contributions": {
                "commands": [ { "id": "probe.run" } ],
                "toolBar": [ { "id": "probe.stop", "command": "probe.halt" } ]
              }
            }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Полоса, записанная не списком, читается как её отсутствие.
    /// </summary>
    /// <remarks>
    /// Обычная описка первого раза — фигурные скобки вместо квадратных. Такой
    /// манифест не разберёт и сама студия: она покажет плагин со сломанным
    /// манифестом, и это честнее находок, вычитанных из неправильно понятого
    /// места. Потому разбор и ищет имя вместе с открывающей скобкой списка.
    /// </remarks>
    [Fact]
    public async Task A_toolbar_that_is_not_a_list_is_read_as_no_toolbar()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": { "id": "probe.stop", "command": "probe.halt" }
              }
            }
            """));
    }

    /// <summary>Полосы в манифесте нет вовсе — спрашивать не о чем.</summary>
    [Fact]
    public async Task A_manifest_without_a_toolbar_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run" } ]
              }
            }
            """));
    }

    /// <summary>
    /// Проект без манифеста правила не касаются.
    /// </summary>
    /// <remarks>
    /// Так собирают частную зависимость плагина: объявлять ей нечего, и требовать
    /// от неё манифест значило бы запретить раскладывать плагин по сборкам.
    /// </remarks>
    [Fact]
    public async Task A_project_without_a_manifest_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(manifest: null, """
            using ArxisStudio.Sdk;

            [ToolBarItem("probe.strip")]
            public sealed class Strip { }
            """));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string? manifest, string? source = null)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        // Атрибут объявлен прямо здесь, а не взят из SDK: анализатор ищет его по
        // имени и пространству имён, и подделка проверяет ровно то, что он ищет.
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(Attribute, path: "C:/probe/Attribute.cs"),
            CSharpSyntaxTree.ParseText(source ?? "public sealed class Probe { }", path: "C:/probe/Probe.cs"),
        };

        var compilation = CSharpCompilation.Create(
            "Probe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var files = new List<AdditionalText>();

        if (manifest is not null)
            files.Add(new Given("C:/probe/plugin.json", manifest));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ToolBarAnalyzer()),
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
