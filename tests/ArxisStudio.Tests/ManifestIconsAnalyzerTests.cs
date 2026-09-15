using System.Collections.Immutable;
using System.Reflection;
using ArxisStudio.Icons;
using ArxisStudio.Sdk.Analyzers;
using ArxisStudio.Shell;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правило «запись значка в манифесте студия разберёт».
/// </summary>
/// <remarks>
/// Значок, который не разобрался, ничего не отменяет: элемент встаёт без значка, а студия пишет о
/// записи в журнал. Журнал читает уже пользователь, и опечатку в имени автор узнал бы от него. При
/// сборке она стоит дешевле — и правило называет заодно то имя, которое имелось в виду.
/// </remarks>
public class ManifestIconsAnalyzerTests
{
    /// <summary>Имени нет в наборе — находка, и в ней ближайшее имя.</summary>
    [Fact]
    public async Task A_name_missing_from_the_set_is_reported_with_the_closest_glyph()
    {
        const string manifest = """
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run", "icon": "arxis:Termnal" } ]
              }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest));

        Assert.Equal(ManifestIconsAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("команда probe.run", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("ближе всего — arxis:Terminal", diagnostic.GetMessage(), StringComparison.Ordinal);

        // Место находки — сама запись в манифесте: править нужно там.
        Assert.EndsWith("plugin.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Equal("arxis:Termnal", Recorded(manifest, diagnostic));
    }

    /// <summary>
    /// Имя в другом регистре — находка: студия сверяет имя строго.
    /// </summary>
    /// <remarks>
    /// Имя копируют из кода как есть, и снисхождение к регистру означало бы два написания одного
    /// значка в манифестах разных авторов. Раз строго, правило обязано назвать правильное.
    /// </remarks>
    [Fact]
    public async Task A_name_in_another_case_is_reported_with_the_one_from_the_set()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "toolBar": [ { "id": "probe.go", "icon": "arxis:play" } ] } }
            """));

        Assert.Contains("элемент полосы probe.go", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("строго", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("arxis:Play", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Переставленные соседние буквы — одна опечатка, и имя находится.
    /// </summary>
    /// <remarks>
    /// Имя короче восьми букв: у него допуск в одну правку, а перестановка, посчитанная заменой
    /// двух букв, в допуск бы не влезла — и самая частая опечатка пальцев осталась бы без подсказки.
    /// </remarks>
    [Fact]
    public async Task Swapped_letters_are_one_slip_and_the_name_is_found()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "commands": [ { "id": "probe.run", "icon": "arxis:Rerfesh" } ] } }
            """));

        Assert.Contains("ближе всего — arxis:Refresh", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Ничего похожего в наборе нет — находка без догадки.</summary>
    /// <remarks>
    /// Подсказка, называющая чужой значок, хуже молчания: «Wrench» в двух правках от «Branch», но
    /// ветка репозитория — не гаечный ключ.
    /// </remarks>
    [Fact]
    public async Task A_name_nothing_resembles_is_reported_without_a_guess()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "commands": [ { "id": "probe.run", "icon": "arxis:Wrench" } ] } }
            """));

        Assert.Contains("такого значка нет", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("ближе всего", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Имя без приставки — находка: студия приняла бы его за контур.
    /// </summary>
    /// <remarks>
    /// Самая частая ошибка записи, и молча она обходится дороже всех: слово не разбирается ни
    /// именем, ни контуром, и значка просто нет. Контуром слово быть не может — у контура есть
    /// координаты.
    /// </remarks>
    [Fact]
    public async Task A_name_without_the_prefix_is_reported()
    {
        var found = await AnalyzeAsync("""
            { "contributions": { "commands": [
              { "id": "probe.run", "icon": "Play" },
              { "id": "probe.open", "icon": "Terminl" }
            ] } }
            """);

        Assert.Equal(2, found.Length);
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("приставкой — arxis:Play", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("приставкой — arxis:Terminal", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пробелы по краям записи опечатку не прячут.
    /// </summary>
    /// <remarks>
    /// Студия снимает их при чтении, и запись с пробелом впереди для неё — та же запись. Не сними
    /// их правило, оно приняло бы такую запись за контур и промолчало.
    /// </remarks>
    [Fact]
    public async Task Spaces_around_the_record_do_not_hide_a_slip()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "commands": [ { "id": "probe.run", "icon": "  arxis:Termnal " } ] } }
            """));

        Assert.Contains("arxis:Terminal", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Приставка без имени — находка со своим словом, а не «такого значка нет».</summary>
    [Fact]
    public async Task A_prefix_without_a_name_is_reported()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "commands": [ { "id": "probe.run", "icon": "arxis:" } ] } }
            """, withSet: false));

        Assert.Contains("не названо имя", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Картинка файлом — находка: значок принимается только именем или контуром.</summary>
    [Fact]
    public async Task A_picture_file_is_reported()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "toolWindows": [ { "id": "probe.panel", "title": "Проба", "icon": "assets/panel.png" } ] } }
            """));

        Assert.Contains("панель probe.panel", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("картинка файлом не принимается", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Имя из набора, контур и пустая запись правило не трогают.</summary>
    [Fact]
    public async Task Names_from_the_set_contours_and_empty_records_are_left_alone()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "contributions": {
                "toolBar": [ { "id": "probe.go", "command": "probe.run", "icon": "arxis:Play", "title": "Пуск" } ],
                "toolWindows": [ { "id": "probe.panel", "title": "Проба", "icon": "M3.5 8H12.5" } ],
                "commands": [
                  { "id": "probe.run", "icon": "" },
                  { "id": "probe.open", "icon": "  arxis: Refresh " }
                ]
              }
            }
            """));
    }

    /// <summary>
    /// Значок карточки в голове манифеста — путь к картинке, а не запись значка.
    /// </summary>
    /// <remarks>
    /// Одно имя поля значит разное по месту: в голове — файл PNG для менеджера плагинов, у команды
    /// — значок набора. Порядок полей в JSON свободный, и голова может стоять и после вкладов.
    /// Поле с тем же именем в других секциях значком студии тоже не читается.
    /// </remarks>
    [Fact]
    public async Task The_icon_of_the_plugin_card_is_not_a_glyph_record()
    {
        Assert.Empty(await AnalyzeAsync("""
            {
              "id": "arxis.probe",
              "contributions": {
                "commands": [ { "id": "probe.run", "icon": "arxis:Play" } ],
                "settings": [ { "key": "probe.icon", "type": "string", "scope": "user", "icon": "Play" } ]
              },
              "icon": "assets/icon.png"
            }
            """));
    }

    /// <summary>
    /// Кому принадлежит значок, правило берёт у того же элемента.
    /// </summary>
    /// <remarks>
    /// У панели внутри лежит объект места, и поле с именем в нём — не её. Назови правило ближайший в
    /// тексте id, находка указала бы автору не на ту панель.
    /// </remarks>
    [Fact]
    public async Task The_owner_is_named_from_its_own_item()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("""
            { "contributions": { "toolWindows": [
              { "id": "probe.first", "icon": "arxis:Play" },
              { "placement": { "side": "left", "id": "inner" }, "icon": "arxis:Nope", "id": "probe.second" }
            ] } }
            """));

        Assert.Contains("панель probe.second", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("inner", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Комментарии, висячие запятые и экранированные кавычки записи не прячут.
    /// </summary>
    /// <remarks>
    /// Студия читает манифест с комментариями и висячими запятыми, и проверка не вправе быть строже
    /// чтения — и не вправе от них слепнуть. Закомментированные элементы здесь с ошибкой нарочно:
    /// прочти разбор комментарий как текст, находок стало бы больше одной. Кавычка в описании одна,
    /// и скобка за ней: не узнай разбор экранирования, строка кончилась бы раньше, скобка закрыла бы
    /// манифест, и настоящая запись потеряла бы дорогу.
    /// </remarks>
    [Fact]
    public async Task Comments_and_trailing_commas_do_not_hide_a_record()
    {
        const string manifest = """
            {
              // Строкой: { "id": "ghost", "icon": "Ghost" }
              "description": "Дюйм \" и скобка }",
              "contributions": {
                /* Блоком: "commands": [ { "id": "ghost", "icon": "arxis:Ghst" } ], */
                "commands": [
                  // { "id": "ghost", "icon": "arxis:Ghst" },
                  { "id": "probe.run", "icon": "arxis:Serch", },
                ],
              },
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(manifest));

        Assert.Contains("arxis:Search", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("arxis:Serch", Recorded(manifest, diagnostic));
    }

    /// <summary>Манифест встроенного модуля проверяется той же дорогой.</summary>
    /// <remarks>Код, переносимый между режимами, не должен менять смысл при переносе.</remarks>
    [Fact]
    public async Task The_manifest_of_a_built_in_module_is_checked_too()
    {
        var found = await AnalyzeAsync(
            """{ "contributions": { "commands": [ { "id": "probe.run", "icon": "arxis:Nope" } ] } }""",
            manifestName: "module.json");

        Assert.EndsWith("module.json", Assert.Single(found).Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Без набора в компиляции имя сверять не с чем — судится только сама запись.
    /// </summary>
    /// <remarks>
    /// Имена правило берёт у набора, против которого расширение собирается. Нет его — назвать
    /// опечаткой можно только то, что неверно при любом наборе: слово без приставки и картинку.
    /// </remarks>
    [Fact]
    public async Task Without_the_icon_set_only_the_record_itself_is_judged()
    {
        var found = await AnalyzeAsync("""
            { "contributions": { "commands": [
              { "id": "probe.name", "icon": "arxis:Nope" },
              { "id": "probe.word", "icon": "Play" },
              { "id": "probe.file", "icon": "run.svg" }
            ] } }
            """, withSet: false);

        Assert.Equal(2, found.Length);
        Assert.DoesNotContain(found, diagnostic => diagnostic.GetMessage().Contains("probe.name", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.word", StringComparison.Ordinal)
                                             && !diagnostic.GetMessage().Contains("arxis:Play", StringComparison.Ordinal));
        Assert.Contains(found, diagnostic => diagnostic.GetMessage().Contains("probe.file", StringComparison.Ordinal));
    }

    /// <summary>
    /// Правило и студия знают один и тот же набор: что студия рисует, то правило пропускает.
    /// </summary>
    /// <remarks>
    /// Имена у них берутся разными дорогами — у студии отражением по типу, у правила по символам
    /// компиляции, — и разойтись им есть где: служебные члены набора, которые значками не являются,
    /// правило обязано пропускать так же, как студия. Сверяются все имена набора и одно, которого в
    /// нём нет.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_rule_and_the_studio_know_the_same_glyphs()
    {
        var records = Glyphs(typeof(AxIcons)).Append("Nope").Select(name => "arxis:" + name).ToList();

        var manifest = "{ \"contributions\": { \"commands\": [ "
            + string.Join(", ", records.Select((record, index) => $"{{ \"id\": \"c{index}\", \"icon\": \"{record}\" }}"))
            + " ] } }";

        var reported = (await AnalyzeAsync(manifest))
            .Select(diagnostic => Recorded(manifest, diagnostic))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var record in records)
        {
            ManifestIcons.Resolve(record, out var problem);

            Assert.True(
                (problem is null) != reported.Contains(record),
                $"{record}: студия — {problem ?? "рисует"}, правило — {(reported.Contains(record) ? "находка" : "молчит")}");
        }
    }

    /// <summary>Имена значков типа — так, как их берёт студия.</summary>
    private static List<string> Glyphs(Type set) =>
    [
        .. set.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => typeof(Avalonia.Media.Geometry).IsAssignableFrom(property.PropertyType))
            .Select(property => property.Name),
    ];

    /// <summary>Текст манифеста под находкой.</summary>
    private static string Recorded(string manifest, Diagnostic diagnostic) =>
        manifest.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string manifest, string manifestName = "plugin.json", bool withSet = true)
    {
        var locations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => assembly.Location)
            .Append(typeof(Avalonia.Media.Geometry).Assembly.Location)
            .Append(typeof(AxIcons).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(location => withSet || !Path.GetFileNameWithoutExtension(location).Equals("ArxisStudio.Icons", StringComparison.OrdinalIgnoreCase));

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText("public sealed class Probe { }", cancellationToken: TestContext.Current.CancellationToken)],
            locations.Select(location => (MetadataReference)MetadataReference.CreateFromFile(location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ManifestIconsAnalyzer()),
            new AnalyzerOptions([new Given($"C:/probe/{manifestName}", manifest)]));

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
