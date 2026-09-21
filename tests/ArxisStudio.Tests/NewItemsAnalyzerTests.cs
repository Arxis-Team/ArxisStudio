using System.Collections.Immutable;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правила про пункты «Добавить ▸»: запись, которую студия прочтёт не так, и расхождение с кодом.
/// </summary>
/// <remarks>
/// Пункт студия читает без сборки и неверную запись обходит молча — снимает пункт, не создаёт файл,
/// оставляет переменную в имени как написано, — говоря об этом только в журнал. Журнал прочтёт
/// пользователь, а не автор; здесь то же самое слышит автор. Главные случаи идут на обоих
/// манифестах: пункты модуля и плагина студия читает одной секцией и одними правилами.
/// </remarks>
public class NewItemsAnalyzerTests
{
    private const string Attribute = """
        namespace ArxisStudio.Sdk;

        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class NewItemAttribute(string id) : System.Attribute
        {
            public string Id { get; } = id;
        }
        """;

    /// <summary>Запись, которую студия читает как задумано, правила не трогают.</summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task Items_the_studio_reads_are_left_alone(string manifestName)
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "newItems": [
                  { "id": "probe.dir", "kind": "directory", "title": "Каталог", "name": "NewDirectory$n$", "nested": true },
                  { "id": "probe.file", "title": "Файл", "name": "NewFile$n$.txt" },
                  { "id": "probe.control", "kind": "file", "title": "Контрол", "name": "Control$n$", "nameRule": "identifier",
                    "when": { "languages": [ "C#" ], "packages": [ "Avalonia" ] },
                    "files": [
                      { "path": "$name$.axaml", "template": "templates/Control.axaml", "open": true },
                      { "path": "Views/$name$.axaml.cs", "template": "templates/Control.axaml.cs" }
                    ] },
                  { "id": "probe.type", "title": "Тип", "nameRule": "Identifier",
                    "variants": [
                      { "id": "class", "title": "Класс", "files": [ { "path": "$name$.cs", "template": "templates/Class.cs" } ] },
                      { "id": "interface", "title": "Интерфейс", "files": [ { "path": "$name$.cs" } ] }
                    ] },
                  { "id": "probe.code", "kind": "code", "title": "Код", "name": "Code$n$.txt" }
                ]
              },
              "activation": [ "onCommand:probe.run", "onNewItem:probe.code" ]
            }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """, manifestName));
    }

    /// <summary>Незнакомый вид снимает пункт — находка на самом слове.</summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task An_unknown_kind_is_reported_where_it_is_written(string manifestName)
    {
        const string manifest = """
            { "contributions": { "newItems": [ { "id": "probe.dir", "kind": "folder", "title": "Каталог" } ] } }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest, manifestName: manifestName));

        Assert.Equal(NewItemsAnalyzer.RecordId, diagnostic.Id);
        Assert.Contains("пункт создания probe.dir", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("directory, file или code", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.EndsWith(manifestName, diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Equal("folder", Recorded(manifest, diagnostic));
    }

    /// <summary>Незнакомое правило имени читается как file — и автор узнаёт об этом при сборке.</summary>
    [Fact]
    public async Task An_unknown_name_rule_is_reported()
    {
        const string manifest = """
            { "contributions": { "newItems": [ { "id": "probe.type", "title": "Тип", "nameRule": "type" } ] } }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest));

        Assert.Contains("file или identifier", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("type", Recorded(manifest, diagnostic));
    }

    /// <summary>
    /// Переменная, которой в этом месте нет, — находка: студия оставит её в имени как написано.
    /// </summary>
    /// <remarks>
    /// В имени по умолчанию подставляется только <c>$n$</c>, в пути файла — только <c>$name$</c>, и
    /// <c>$namespace$</c> в пути дал бы файл с долларами в имени. Регистр строгий: <c>$Name$</c> — не
    /// <c>$name$</c>.
    /// </remarks>
    [Fact]
    public async Task A_variable_the_place_does_not_know_is_reported()
    {
        const string manifest = """
            { "contributions": { "newItems": [
              { "id": "probe.file", "title": "Файл", "name": "$name$$n$",
                "files": [
                  { "path": "$namespace$.$name$.cs" },
                  { "path": "$Name$.md" },
                  { "path": "$name$.txt" }
                ] }
            ] } }
            """;

        var found = await AnalyzeAsync(manifest);

        Assert.Equal(3, found.Length);
        Assert.Contains(found, diagnostic =>
            diagnostic.GetMessage().Contains("только $n$", StringComparison.Ordinal) &&
            diagnostic.GetMessage().Contains("$name$", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic =>
            diagnostic.GetMessage().Contains("только $name$", StringComparison.Ordinal) &&
            Recorded(manifest, diagnostic) == "$namespace$.$name$.cs");
        Assert.Contains(found, diagnostic => Recorded(manifest, diagnostic) == "$Name$.md");
    }

    /// <summary>
    /// Путь, уводящий из своего каталога, — находка: студия такой пункт не создаст и шаблон не прочтёт.
    /// </summary>
    [Fact]
    public async Task A_path_leaving_its_directory_is_reported()
    {
        const string manifest = """
            { "contributions": { "newItems": [
              { "id": "probe.file", "title": "Файл",
                "files": [
                  { "path": "../$name$.cs" },
                  { "path": "C:/$name$.cs" },
                  { "path": "$name$.md", "template": "../../secret.txt" },
                  { "path": "a/../$name$.txt", "template": "templates/note.md" }
                ] }
            ] } }
            """;

        var found = await AnalyzeAsync(manifest);

        Assert.Equal(4, found.Length);
        Assert.Contains(found, diagnostic => Recorded(manifest, diagnostic) == "../$name$.cs");
        Assert.Contains(found, diagnostic => Recorded(manifest, diagnostic) == "C:/$name$.cs");
        Assert.Contains(found, diagnostic =>
            Recorded(manifest, diagnostic) == "../../secret.txt" &&
            diagnostic.GetMessage().Contains("вне папки расширения", StringComparison.Ordinal));

        // «..» внутри пути — тоже наружу по записи: студия его, может, и пустит, но правило судит
        // запись, а не то, куда она случайно вернулась.
        Assert.Contains(found, diagnostic => Recorded(manifest, diagnostic) == "a/../$name$.txt");
    }

    /// <summary>
    /// Файлы там, где студия их не читает, — находка: у каталога и кода их нет, у пункта с вариантами
    /// они у вариантов.
    /// </summary>
    [Fact]
    public async Task Files_the_studio_does_not_read_are_reported()
    {
        var found = await AnalyzeAsync("""
            { "contributions": { "newItems": [
              { "id": "probe.dir", "kind": "directory", "title": "Каталог", "files": [ { "path": "$name$/a.txt" } ],
                "variants": [ { "id": "one", "title": "Один" } ] },
              { "id": "probe.code", "kind": "code", "title": "Код", "files": [ { "path": "$name$.txt" } ] },
              { "id": "probe.type", "title": "Тип", "files": [ { "path": "$name$.cs" } ],
                "variants": [ { "id": "class", "title": "Класс" } ] }
            ] } }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """);

        Assert.Equal(4, found.Length);
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.dir: у пункта вида directory файлов нет", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.dir: у каталога вариантов нет", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.code: у пункта вида code файлов нет", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.type: у пункта с вариантами", StringComparison.Ordinal));
    }

    /// <summary>Пункт без строки меню, без id, повторённый или с безымянным вариантом не покажется.</summary>
    [Fact]
    public async Task Items_the_studio_would_not_show_are_reported()
    {
        var found = await AnalyzeAsync("""
            { "contributions": { "newItems": [
              { "id": "probe.untitled" },
              { "title": "Без имени" },
              { "id": "probe.file", "title": "Файл" },
              { "id": "probe.file", "title": "Снова файл" },
              { "id": "probe.type", "title": "Тип", "variants": [ { "title": "Без id" }, { "id": "bare" } ] }
            ] } }
            """);

        Assert.Equal(5, found.Length);
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.untitled: у пункта нет строки меню", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("пункт создания без id: у пункта нет id", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.file: пункт с таким id уже объявлен", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("у варианта нет id", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("у варианта bare нет строки", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пункт с кодом у расширения, которое спит до события, без своего события не разбудит никого.
    /// </summary>
    /// <remarks>
    /// Правило подъёма то же, что у студии: без событий, с <c>onStartup</c>, с <c>onToolWindow:</c> или
    /// со своим контролом в полосе расширение поднимается сразу, и событие пункту не нужно.
    /// </remarks>
    [Theory]
    [InlineData("""[ "onCommand:probe.run" ]""", "", true)]
    [InlineData("""[ "onCommand:probe.run", "onNewItem:probe.code" ]""", "", false)]
    [InlineData("""[ "onCommand:probe.run", "onNewItem:probe.other" ]""", "", true)]
    [InlineData("""[ "onStartup" ]""", "", false)]
    [InlineData("""[ "onToolWindow:probe.panel" ]""", "", false)]
    [InlineData("""[ ]""", "", false)]
    [InlineData("""[ "onCommand:probe.run" ]""", """, "toolBar": [ { "id": "probe.strip", "kind": "custom" } ]""", false)]
    public async Task A_code_item_of_a_sleeping_extension_needs_its_event(string activation, string toolBar, bool reported)
    {
        var found = await AnalyzeAsync($$"""
            {
              "contributions": { "newItems": [ { "id": "probe.code", "kind": "code", "title": "Код" } ]{{toolBar}} },
              "activation": {{activation}}
            }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """);

        if (!reported)
        {
            Assert.Empty(found);
            return;
        }

        var diagnostic = Assert.Single(found);

        Assert.Equal(NewItemsAnalyzer.RecordId, diagnostic.Id);
        Assert.Contains("onNewItem:probe.code", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Модулю событие пункта не нужно: студия поднимает встроенные модули при запуске, мимо событий.
    /// </summary>
    /// <remarks>
    /// Тот же манифест плагином — жалоба: плагин с одним <c>onCommand:</c> спит, и выбор пункта его не
    /// разбудил бы. Жалоба приходит тогда, когда становится правдой, — в тот день, когда модуль
    /// переложат во внешний плагин.
    /// </remarks>
    [Theory]
    [InlineData("module.json", false)]
    [InlineData("plugin.json", true)]
    public async Task A_module_never_sleeps_and_its_code_item_needs_no_event(string manifestName, bool reported)
    {
        var found = await AnalyzeAsync("""
            {
              "contributions": { "newItems": [ { "id": "probe.code", "kind": "code", "title": "Код" } ] },
              "activation": [ "onCommand:probe.run" ]
            }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """, manifestName);

        Assert.Equal(reported ? 1 : 0, found.Length);
    }

    /// <summary>Пункт с кодом объявлен, а класса под него нет.</summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_code_item_without_its_class_is_reported(string manifestName)
    {
        const string manifest = """
            { "contributions": { "newItems": [ { "id": "probe.code", "kind": "code", "title": "Код" } ] } }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest, manifestName: manifestName));

        Assert.Equal(NewItemsAnalyzer.MissingId, diagnostic.Id);
        Assert.Contains("[NewItem(\"probe.code\")]", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("probe.code", Recorded(manifest, diagnostic));
    }

    /// <summary>
    /// Класс помечен атрибутом, а пункта с кодом под ним нет — ни пункта вовсе, ни пункта того вида.
    /// </summary>
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("module.json")]
    public async Task A_marked_class_without_a_code_item_is_reported(string manifestName)
    {
        var found = await AnalyzeAsync("""
            { "contributions": { "newItems": [
              { "id": "probe.code", "kind": "code", "title": "Код" },
              { "id": "probe.file", "title": "Файл" }
            ] } }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }

            [NewItem("probe.file")]
            public sealed class NotCode { }

            [NewItem("probe.forgotten")]
            public sealed class Forgotten { }
            """, manifestName);

        Assert.Equal(2, found.Length);
        Assert.All(found, diagnostic =>
        {
            Assert.Equal(NewItemsAnalyzer.UndeclaredId, diagnostic.Id);
            Assert.EndsWith(".cs", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        });
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().StartsWith("probe.file:", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().StartsWith("probe.forgotten:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пункт в комментарии не объявлен: студия комментариев не читает, и правило тоже.
    /// </summary>
    [Fact]
    public async Task A_commented_out_item_declares_nothing()
    {
        var found = await AnalyzeAsync("""
            {
              "contributions": {
                // { "id": "probe.old", "kind": "folder", "title": "" },
                "newItems": [
                  /* { "id": "probe.code", "kind": "code", "title": "Код" }, */
                  { "id": "probe.file", "title": "Файл" },
                ]
              }
            }
            """, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """);

        var diagnostic = Assert.Single(found);

        Assert.Equal(NewItemsAnalyzer.UndeclaredId, diagnostic.Id);
    }

    /// <summary>Проекту без манифеста сверять нечего: так собирают частную зависимость расширения.</summary>
    [Fact]
    public async Task A_project_without_a_manifest_is_left_alone()
    {
        Assert.Empty(await AnalyzeAsync(null, """
            using ArxisStudio.Sdk;

            [NewItem("probe.code")]
            public sealed class Code { }
            """));
    }

    private static string Recorded(string manifest, Diagnostic diagnostic) =>
        manifest.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    /// <summary>Прогоняет правила над манифестом и кодом.</summary>
    /// <param name="manifest">Содержимое манифеста; null — манифеста нет.</param>
    /// <param name="source">Код проекта; null — пустой класс.</param>
    /// <param name="manifestName">Имя манифеста: <c>plugin.json</c> у плагина, <c>module.json</c> у модуля.</param>
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string? manifest, string? source = null, string manifestName = "plugin.json")
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();

        // Атрибут объявлен прямо здесь, а не взят из SDK: анализатор ищет его по имени и
        // пространству имён, и подделка проверяет ровно то, что он ищет.
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

        // Словарь подаётся раньше манифеста, как в настоящей сборке: манифестом правила обязаны
        // признать ровно манифест, а не первый попавшийся JSON.
        var files = new List<AdditionalText> { new Given("C:/probe/lang/en.json", """{ "add.note": "Note" }""") };

        if (manifest is not null)
            files.Add(new Given($"C:/probe/{manifestName}", manifest));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new NewItemsAnalyzer()),
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
