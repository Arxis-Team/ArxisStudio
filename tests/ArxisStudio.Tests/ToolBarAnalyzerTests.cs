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
/// <para>
/// Главные случаи идут на обоих манифестах — <c>plugin.json</c> внешнего плагина
/// и <c>module.json</c> встроенного модуля: полосу обоих студия строит по одной
/// секции, и код, переносимый между режимами, не должен менять смысл при
/// переносе. Прежде правила узнавали только <c>plugin.json</c>, и полоса модулей
/// при сборке не сверялась вовсе.
/// </para>
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

    /// <summary>Кнопка зовёт команду, которую расширение не объявляло.</summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_button_naming_an_undeclared_command_is_reported(string manifestName)
    {
        const string manifest = """
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
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest, manifestName: manifestName));

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);

        // Место находки — сам манифест: править нужно там, а не в коде. И в нём — имя элемента:
        // по нему автор кнопку и узнаёт.
        Assert.EndsWith(manifestName, diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Equal(
            "probe.stop",
            manifest.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
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
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_custom_item_without_its_class_is_reported(string manifestName)
    {
        var found = await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [ { "id": "probe.strip", "kind": "custom" } ]
              }
            }
            """, manifestName: manifestName);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.MissingId, diagnostic.Id);
        Assert.Contains("probe.strip", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.EndsWith(manifestName, diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Класс помечен атрибутом, а манифест о нём молчит.
    /// </summary>
    /// <remarks>
    /// Самая обычная ошибка первого раза: класс написан, а объявить его забыли —
    /// и полоса о нём не узнает, потому что собирается по манифесту.
    /// </remarks>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_marked_class_that_the_manifest_does_not_declare_is_reported(string manifestName)
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
            """, manifestName);

        var diagnostic = Assert.Single(found);

        Assert.Equal(ToolBarAnalyzer.UndeclaredId, diagnostic.Id);
        Assert.Contains("probe.forgotten", diagnostic.GetMessage(), StringComparison.Ordinal);

        // А это место — в коде: манифест здесь ни при чём, забыли объявление.
        Assert.EndsWith(".cs", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>Манифест и код совпали — правила молчат.</summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_toolbar_that_matches_its_code_is_left_alone(string manifestName)
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
            """, manifestName));
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

    /// <summary>
    /// Закомментированная старая полоса не подменяет собой настоящую.
    /// </summary>
    /// <remarks>
    /// Студия читает манифест с комментариями, и старую полосу автор вполне может оставить
    /// комментарием над новой. Разбор, искавший секцию первым вхождением в тексте, сверял бы её — с
    /// находкой про давно убранную команду и молчанием о настоящей кнопке.
    /// </remarks>
    [Fact]
    public async Task A_commented_out_toolbar_does_not_stand_in_for_the_real_one()
    {
        // Старых кнопок две, а настоящая одна: прочитанная как текст, первая старая встала бы на
        // ту же дорогу, что настоящая, и спряталась бы за ней, а второй спрятаться не за кого.
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run" } ],
                // "toolBar": [ { "id": "probe.old", "command": "probe.gone" }, { "id": "probe.older", "command": "probe.lost" } ],
                "toolBar": [ { "id": "probe.stop", "command": "probe.halt" } ]
              }
            }
            """));

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("probe.gone", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Команда, объявленная только в комментарии, не объявлена.</summary>
    /// <remarks>Студия её не прочтёт — значит, и кнопка, зовущая её, зовёт в пустоту.</remarks>
    [Fact]
    public async Task A_command_declared_only_in_a_comment_is_not_declared()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                /* "commands": [ { "id": "probe.halt" } ], */
                "commands": [ { "id": "probe.run" } ],
                "toolBar": [ { "id": "probe.stop", "command": "probe.halt" } ]
              }
            }
            """));

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Свой контрол, закомментированный в полосе, не объявлен — и класс под него объявления не
    /// получает.
    /// </summary>
    /// <remarks>
    /// Разбор, читавший комментарий как текст, находил в нём элемент: класс под убранный контрол
    /// проходил бы объявленным, и полоса его так и не построила бы.
    /// </remarks>
    [Fact]
    public async Task A_custom_item_in_a_comment_declares_nothing()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "toolBar": [
                  // { "id": "probe.ghost", "kind": "custom" },
                  { "id": "probe.strip", "kind": "custom" }
                ]
              }
            }
            """, """
            using ArxisStudio.Sdk;

            [ToolBarItem("probe.strip")]
            public sealed class Strip { }

            [ToolBarItem("probe.ghost")]
            public sealed class Ghost { }
            """));

        Assert.Equal(ToolBarAnalyzer.UndeclaredId, diagnostic.Id);
        Assert.Contains("probe.ghost", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Полоса студии — та, что во вкладах: одноимённые секции в другом месте манифеста не в счёт.
    /// </summary>
    /// <remarks>
    /// Студия читает полосу и команды только из <c>contributions</c>. Секция с тем же именем выше по
    /// тексту — не её, и сверять по ней значило бы проверять то, чего студия не увидит.
    /// </remarks>
    [Fact]
    public async Task Only_the_sections_of_the_contributions_count()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "toolBar": [ { "id": "probe.top", "command": "probe.nowhere" } ],
              "commands": [ { "id": "probe.halt" } ],
              "contributions": {
                "commands": [ { "id": "probe.run" } ],
                "toolBar": [ { "id": "probe.stop", "command": "probe.halt" } ]
              }
            }
            """));

        Assert.Equal(ToolBarAnalyzer.CommandId, diagnostic.Id);
        Assert.Contains("probe.halt", diagnostic.GetMessage(), StringComparison.Ordinal);
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

    /// <summary>Прогоняет правила над кодом и манифестом.</summary>
    /// <param name="manifest">Текст манифеста; null — проект без манифеста.</param>
    /// <param name="source">Код проекта; null — пустой класс.</param>
    /// <param name="manifestName">Имя манифеста: <c>plugin.json</c> у плагина, <c>module.json</c> у модуля.</param>
    /// <remarks>
    /// Словарь подаётся раньше манифеста, как в настоящей сборке: плагин отдаёт анализаторам
    /// <c>lang/strings.json</c>, модуль — словарь студии. Манифестом правила обязаны признать ровно
    /// манифест, а не первый попавшийся JSON.
    /// </remarks>
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string? manifest, string? source = null, string manifestName = "plugin.json")
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

        var files = new List<AdditionalText>
        {
            new Given(
                manifestName == "module.json" ? "C:/studio/Localization/Strings/en.json" : "C:/probe/lang/strings.json",
                """{ "menu.tools": "Tools" }"""),
        };

        if (manifest is not null)
            files.Add(new Given($"C:/probe/{manifestName}", manifest));

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
