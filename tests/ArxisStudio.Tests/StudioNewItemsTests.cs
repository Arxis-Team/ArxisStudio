using System.Text;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell.Localization;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба создания студии: пункты «Добавить ▸» из манифестов и сборка файлов по ним.
/// </summary>
/// <remarks>
/// Расширения здесь настоящие по форме — каталог с манифестом, словарём и шаблонами, прочитанный
/// каталогом плагинов, — а состав вкладывающихся тест называет сам: служба спрашивает его на каждый
/// вопрос, и это ровно то, что проверяется. Очередь общая: подписи идут через словари расширений, а
/// сообщения человеку — через словарь студии, один на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class StudioNewItemsTests : IDisposable
{
    private readonly string _root = TempFolder.Reserve("newitems");
    private readonly string _target;
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly PluginContributionRegistry _contributions;
    private readonly StudioNewItems _items;
    private readonly List<InstalledPlugin> _contributing = [];

    public StudioNewItemsTests()
    {
        _target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        _contributions = new PluginContributionRegistry(_guard);
        _items = new StudioNewItems(_log, _guard, _contributions) { Contributing = () => _contributing };
    }

    public void Dispose()
    {
        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Пункты идут модулями вперёд, затем по идентификатору расширения, а внутри — как в манифесте.
    /// </summary>
    /// <remarks>
    /// Порядок тот же, что у полосы: числа порядка в манифесте нет, и решает студия. Незнакомый вид,
    /// пункт без строки меню, повтор идентификатора и выключенное расширение в меню не попадают.
    /// </remarks>
    [AvaloniaFact]
    public void Items_come_modules_first_then_by_id_in_manifest_order()
    {
        Extension("b.second", """
            { "id": "b.second", "name": "B", "contributions": { "newItems": [
              { "id": "b.one", "title": "B1" },
              { "id": "b.odd", "kind": "folder", "title": "Странный" },
              { "id": "b.bare" },
              { "id": "b.one", "title": "Повтор" },
              { "id": "b.two", "kind": "code", "title": "B2" }
            ] } }
            """);

        Extension("a.first", """
            { "id": "a.first", "name": "A", "contributions": { "newItems": [ { "id": "a.one", "kind": "directory", "title": "A1" } ] } }
            """);

        Extension("z.module", """
            { "id": "z.module", "name": "Z", "contributions": { "newItems": [ { "id": "z.one", "title": "Z1" } ] } }
            """, builtIn: true);

        Extension("c.off", """
            { "id": "c.off", "name": "C", "contributions": { "newItems": [ { "id": "c.one", "title": "C1" } ] } }
            """, enabled: false);

        var items = _items.Items;

        Assert.Equal(["z.one", "a.one", "b.one", "b.two"], items.Select(item => item.Id));
        Assert.Equal(["z.module", "a.first", "b.second", "b.second"], items.Select(item => item.Owner));
        Assert.Equal([NewItemKind.File, NewItemKind.Directory, NewItemKind.File, NewItemKind.Code], items.Select(item => item.Kind));
        Assert.Equal("B1", items[2].Title);
    }

    /// <summary>
    /// Строка меню, ветка, имя и варианты приходят переведёнными, значки — разобранными.
    /// </summary>
    [AvaloniaFact]
    public void Titles_branches_and_names_come_translated()
    {
        Extension("probe.words", """
            { "id": "probe.words", "name": "Слова", "contributions": { "newItems": [
              { "id": "probe.type", "title": "%add.type%", "icon": "arxis:DocumentCode", "menu": "%add.samples% / Глубже",
                "name": "%add.name%", "nameRule": "IDENTIFIER", "nested": true,
                "when": { "languages": [ " C# ", "" ], "packages": [ "Avalonia" ] },
                "variants": [
                  { "id": "class", "title": "%add.class%", "icon": "arxis:Nope" },
                  { "id": "", "title": "Без id" },
                  { "id": "record", "title": "Запись" }
                ] },
              { "id": "probe.dir", "kind": "directory", "title": "Каталог", "variants": [ { "id": "x", "title": "X" } ] }
            ] } }
            """, strings: new()
        {
            ["add.type"] = "Type",
            ["add.samples"] = "Samples",
            ["add.name"] = "Type$n$",
            ["add.class"] = "Class",
        });

        var item = _items.Items[0];

        Assert.Equal("Type", item.Title);
        Assert.Same(AxIcons.DocumentCode, item.Icon);
        Assert.Equal(["Samples", "Глубже"], item.Menu);
        Assert.Equal("Type$n$", item.Name);
        Assert.Equal(NewItemNameRule.Identifier, item.NameRule);
        Assert.True(item.Nested);
        Assert.Equal(["C#"], item.Languages);
        Assert.Equal(["Avalonia"], item.Packages);

        // Вариант без идентификатора выбрать нечем, а значок, который не разобрался, не отменяет
        // строки: она встаёт без него.
        Assert.Equal(["class", "record"], item.Variants.Select(variant => variant.Id));
        Assert.Equal("Class", item.Variants[0].Title);
        Assert.Null(item.Variants[0].Icon);

        // У каталога вариантов нет: имя у него одно, и выбирать не из чего.
        Assert.Empty(_items.Items[1].Variants);
    }

    /// <summary>
    /// Правило <c>when</c>: внутри списка — любое из, между списками — все сразу, без учёта регистра.
    /// </summary>
    [Fact]
    public void The_when_rule_is_any_within_a_list_and_all_across_lists()
    {
        var item = new StudioNewItem
        {
            Id = "probe.control",
            Owner = "probe",
            Kind = NewItemKind.File,
            Title = "Контрол",
            Languages = ["C#"],
            Packages = ["Avalonia", "Avalonia.Desktop"],
        };

        Assert.True(item.Fits("c#", ["Serilog", "avalonia.desktop"]));
        Assert.False(item.Fits("F#", ["Avalonia"]));
        Assert.False(item.Fits("C#", ["Serilog"]));
        Assert.False(item.Fits(null, ["Avalonia"]));

        var anywhere = new StudioNewItem { Id = "probe.file", Owner = "probe", Kind = NewItemKind.File, Title = "Файл" };

        Assert.True(anywhere.Fits(null, []));
    }

    /// <summary>Что положит пункт под именем — пути от целевого каталога, в порядке манифеста.</summary>
    [AvaloniaFact]
    public void Paths_name_what_an_item_would_lay_down()
    {
        Extension("probe.paths", """
            { "id": "probe.paths", "name": "Пути", "contributions": { "newItems": [
              { "id": "probe.dir", "kind": "directory", "title": "Каталог" },
              { "id": "probe.file", "title": "Файл" },
              { "id": "probe.control", "title": "Контрол",
                "files": [ { "path": "$name$.axaml" }, { "path": "Views\\$name$.axaml.cs" } ] },
              { "id": "probe.type", "title": "Тип", "files": [ { "path": "ignored.txt" } ],
                "variants": [
                  { "id": "class", "title": "Класс", "files": [ { "path": "$name$.cs" } ] },
                  { "id": "pair", "title": "Пара", "files": [ { "path": "$name$.cs" }, { "path": "I$name$.cs" } ] }
                ] },
              { "id": "probe.code", "kind": "code", "title": "Код" }
            ] } }
            """);

        var items = _items.Items;
        var separator = Path.DirectorySeparatorChar;

        Assert.Equal(["Проба"], _items.Paths(items[0], "Проба"));
        Assert.Equal(["notes.txt"], _items.Paths(items[1], "notes.txt"));
        Assert.Equal(["Card.axaml", $"Views{separator}Card.axaml.cs"], _items.Paths(items[2], "Card"));
        Assert.Equal(["Person.cs"], _items.Paths(items[3], "Person"));
        Assert.Equal(["Person.cs", "IPerson.cs"], _items.Paths(items[3], "Person", "pair"));
        Assert.Empty(_items.Paths(items[3], "Person", "missing"));
        Assert.Empty(_items.Paths(items[4], "Anything"));

        // Пустое поле — начало ввода, а не ошибка зовущего.
        Assert.Empty(_items.Paths(items[2], ""));
    }

    /// <summary>
    /// Предложенное имя берёт первое число, при котором свободно всё, что пункт положит.
    /// </summary>
    /// <remarks>
    /// Занят один файл из пары — занято и имя: <c>Card1.axaml</c> свободен, а <c>Card1.axaml.cs</c>
    /// лежит, и предложить <c>Card1</c> значило бы отказать человеку после нажатия кнопки.
    /// </remarks>
    [AvaloniaFact]
    public void The_suggested_name_takes_the_first_number_that_frees_every_file()
    {
        Extension("probe.names", """
            { "id": "probe.names", "name": "Имена", "contributions": { "newItems": [
              { "id": "probe.control", "title": "Контрол", "name": "Card$n$",
                "files": [ { "path": "$name$.axaml" }, { "path": "$name$.axaml.cs" } ] },
              { "id": "probe.dir", "kind": "directory", "title": "Каталог", "name": "NewDirectory$n$" },
              { "id": "probe.fixed", "title": "Файл", "name": "README.md" },
              { "id": "probe.code", "kind": "code", "title": "Код", "name": "Code$n$.txt" }
            ] } }
            """);

        File.WriteAllText(Path.Combine(_target, "Card1.axaml.cs"), "");
        File.WriteAllText(Path.Combine(_target, "Card2.axaml"), "");
        File.WriteAllText(Path.Combine(_target, "NewDirectory1"), "");
        Directory.CreateDirectory(Path.Combine(_target, "NewDirectory2"));
        File.WriteAllText(Path.Combine(_target, "Code1.txt"), "");
        File.WriteAllText(Path.Combine(_target, "README.md"), "");

        var items = _items.Items;

        Assert.Equal("Card3", _items.Suggest(items[0], _target));
        Assert.Equal("NewDirectory3", _items.Suggest(items[1], _target));
        Assert.Equal("README.md", _items.Suggest(items[2], _target));
        Assert.Equal("Code2.txt", _items.Suggest(items[3], _target));
    }

    /// <summary>
    /// Шаблон доезжает байт в байт: отметка порядка байт и переводы строк как были, переменные
    /// подставлены.
    /// </summary>
    /// <remarks>
    /// Переменная, которой зовущий не назвал, остаётся на месте, как и незнакомая: недописанное видно
    /// в файле. Строка вида <c>$"…"</c> переменной не бывает.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_template_arrives_byte_for_byte_with_its_variables_filled_in()
    {
        const string text = "namespace $namespace$;\r\n\r\n// $project$, $year$ — $rootnamespace$, $foo$\r\npublic class $name$ { string S => $\"{1}$\"; }\r\n";

        Extension("probe.template", """
            { "id": "probe.template", "name": "Шаблон", "contributions": { "newItems": [
              { "id": "probe.class", "title": "Класс",
                "files": [ { "path": "$name$.cs", "template": "templates/Class.cs", "open": true } ] }
            ] } }
            """, templates: new()
        {
            ["templates/Class.cs"] = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
        });

        var made = await _items.MakeAsync(_items.Items[0], new NewItemRequest("Person", _target)
        {
            Project = "Волна",
            Namespace = "Волна.Models",
        }, TestContext.Current.CancellationToken);

        Assert.Null(made.Error);

        var file = Assert.Single(made.Files);
        var year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expected = text
            .Replace("$namespace$", "Волна.Models", StringComparison.Ordinal)
            .Replace("$project$", "Волна", StringComparison.Ordinal)
            .Replace("$year$", year, StringComparison.Ordinal)
            .Replace("$name$", "Person", StringComparison.Ordinal);

        Assert.Equal("Person.cs", file.Path);
        Assert.True(file.Open);
        Assert.False(file.IsDirectory);
        Assert.Equal([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(expected)], file.Content.ToArray());
    }

    /// <summary>
    /// Шаблон в UTF-16 остаётся в UTF-16, а то, что текстом не читается, кладётся как есть.
    /// </summary>
    [AvaloniaFact]
    public async Task A_template_keeps_its_encoding_and_a_binary_one_is_copied_as_is()
    {
        byte[] binary = [0x89, 0x50, 0x4E, 0x47, 0xC3, 0x28, 0x24, 0x6E, 0x61, 0x6D, 0x65, 0x24, 0xFF];
        var wide = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        Extension("probe.bytes", """
            { "id": "probe.bytes", "name": "Байты", "contributions": { "newItems": [
              { "id": "probe.pair", "title": "Пара",
                "files": [ { "path": "$name$.txt", "template": "templates/wide.txt" }, { "path": "$name$.png", "template": "templates/logo.png" } ] }
            ] } }
            """, templates: new()
        {
            ["templates/wide.txt"] = [.. wide.GetPreamble(), .. wide.GetBytes("Привет, $name$!")],
            ["templates/logo.png"] = binary,
        });

        var made = await _items.MakeAsync(_items.Items[0], new NewItemRequest("Мир", _target), TestContext.Current.CancellationToken);

        Assert.Null(made.Error);
        Assert.Equal([.. wide.GetPreamble(), .. wide.GetBytes("Привет, Мир!")], made.Files[0].Content.ToArray());
        Assert.Equal(binary, made.Files[1].Content.ToArray());
        Assert.Equal("Мир.png", made.Files[1].Path);
    }

    /// <summary>
    /// Пункт без файлов — один пустой файл с набранным именем, каталог — один каталог.
    /// </summary>
    [AvaloniaFact]
    public async Task A_plain_file_is_one_empty_file_and_a_directory_is_one_directory()
    {
        Extension("probe.plain", """
            { "id": "probe.plain", "name": "Просто", "contributions": { "newItems": [
              { "id": "probe.file", "title": "Файл" },
              { "id": "probe.dir", "kind": "directory", "title": "Каталог" }
            ] } }
            """);

        var file = await _items.MakeAsync(_items.Items[0], new NewItemRequest("notes.txt", _target), TestContext.Current.CancellationToken);
        var directory = await _items.MakeAsync(_items.Items[1], new NewItemRequest("Models", _target), TestContext.Current.CancellationToken);

        var made = Assert.Single(file.Files);

        Assert.Equal("notes.txt", made.Path);
        Assert.True(made.Content.IsEmpty);
        Assert.False(made.IsDirectory);

        var folder = Assert.Single(directory.Files);

        Assert.Equal("Models", folder.Path);
        Assert.True(folder.IsDirectory);
    }

    /// <summary>
    /// Путь наружу, шаблон вне папки расширения, пропавший шаблон и файл без пути — отказ человеку и
    /// подробность автору в журнале.
    /// </summary>
    /// <remarks>
    /// Шаблон «наружу» при этом существует: отказывает проверка пути, а не случайное отсутствие файла.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("""{ "path": "../$name$.cs" }""", "уводит из целевого каталога")]
    [InlineData("""{ "path": "$name$.cs", "template": "../secret.txt" }""", "уводит из папки расширения")]
    [InlineData("""{ "path": "$name$.cs", "template": "templates/missing.cs" }""", "нет в папке расширения")]
    [InlineData("""{ "template": "templates/missing.cs" }""", "не назван путь")]
    public async Task A_broken_declaration_is_refused_and_told(string file, string told)
    {
        var plugin = Extension("probe.broken", $$"""
            { "id": "probe.broken", "name": "Сломанный", "contributions": { "newItems": [
              { "id": "probe.item", "title": "Пункт", "files": [ {{file}} ] }
            ] } }
            """);

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(plugin.Directory)!, "secret.txt"), "секрет");

        var made = await _items.MakeAsync(_items.Items[0], new NewItemRequest("Person", _target), TestContext.Current.CancellationToken);

        Assert.Equal(Localizer.Instance["newitem.broken"], made.Error);
        Assert.Empty(made.Files);
        Assert.Single(_log.Records, record =>
            record.Level == StudioLogLevel.Warning &&
            record.Message.Contains("Сломанный", StringComparison.Ordinal) &&
            record.Message.Contains("probe.item", StringComparison.Ordinal) &&
            record.Message.Contains(told, StringComparison.Ordinal));
    }

    /// <summary>
    /// Имя с каталогами и относительный целевой каталог — ошибка зовущего, а не отказ пункта.
    /// </summary>
    /// <remarks>
    /// Путь, набранный в диалоге, раскладывает зовущий: пространство имён считается от каталога, и у
    /// двух зовущих оно не должно расходиться.
    /// </remarks>
    [AvaloniaFact]
    public async Task Directories_in_the_name_are_the_callers_error()
    {
        Extension("probe.caller", """
            { "id": "probe.caller", "name": "Зовущий", "contributions": { "newItems": [ { "id": "probe.file", "title": "Файл" } ] } }
            """);

        var item = _items.Items[0];
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => _items.MakeAsync(item, new NewItemRequest("a/b.txt", _target), token));
        await Assert.ThrowsAsync<ArgumentException>(() => _items.MakeAsync(item, new NewItemRequest("a\\b.txt", _target), token));
        await Assert.ThrowsAsync<ArgumentException>(() => _items.MakeAsync(item, new NewItemRequest("b.txt", "relative"), token));
        await Assert.ThrowsAsync<ArgumentException>(() => _items.MakeAsync(item, new NewItemRequest(" ", _target), token));
    }

    /// <summary>Пункт расширения, выключенного между показом меню и выбором, больше не собирается.</summary>
    [AvaloniaFact]
    public async Task An_item_of_an_extension_turned_off_is_gone()
    {
        Extension("probe.gone", """
            { "id": "probe.gone", "name": "Ушедший", "contributions": { "newItems": [ { "id": "probe.file", "title": "Файл" } ] } }
            """);

        var item = _items.Items[0];

        _contributing.Clear();

        var made = await _items.MakeAsync(item, new NewItemRequest("a.txt", _target), TestContext.Current.CancellationToken);

        Assert.Equal(Localizer.Instance["newitem.gone"], made.Error);
        Assert.Empty(_items.Paths(item, "a.txt"));
    }

    /// <summary>
    /// Пункт с кодом будит своего хозяина событием <c>onNewItem:</c> и зовёт его класс; пути
    /// собранного приводятся к виду от целевого каталога.
    /// </summary>
    [AvaloniaFact]
    public async Task A_code_item_wakes_its_owner_and_runs_its_class()
    {
        var owner = Extension("probe.code", """
            { "id": "probe.code", "name": "Код", "contributions": { "newItems": [ { "id": "probe.make", "kind": "code", "title": "Собрать" } ] },
              "activation": [ "onNewItem:probe.make" ] }
            """);

        var asked = new List<string>();

        _items.Activate = matches =>
        {
            foreach (var waiting in _contributing.Where(matches))
            {
                asked.Add(waiting.Id);
                _contributions.Add(waiting.Id, waiting.DisplayName, [Maker("probe.make", """
                    var text = NewItemTemplate.Expand("$name$ in $project$", request);

                    return Task.FromResult(NewItemResult.Made([
                        new NewItemFile("Views/" + request.Name + ".txt") { Content = System.Text.Encoding.UTF8.GetBytes(text), Open = true },
                        new NewItemFile("Views") { IsDirectory = true },
                    ]));
                    """)]);
            }
        };

        var made = await _items.MakeAsync(_items.Items[0], new NewItemRequest("Card", _target) { Project = "Волна" }, TestContext.Current.CancellationToken);

        Assert.Equal([owner.Id], asked);
        Assert.Null(made.Error);
        Assert.Equal([Path.Combine("Views", "Card.txt"), "Views"], made.Files.Select(file => file.Path));
        Assert.Equal("Card in Волна", Encoding.UTF8.GetString(made.Files[0].Content.Span));
        Assert.True(made.Files[0].Open);
        Assert.True(made.Files[1].IsDirectory);
    }

    /// <summary>
    /// Упавший код пункта — отказ человеку и сбой, засчитанный его расширению; положивший файл наружу
    /// — отказ.
    /// </summary>
    [AvaloniaFact]
    public async Task A_failing_or_escaping_code_item_is_refused()
    {
        var failures = new List<PluginFailure>();

        _guard.Failed += (_, failure) => failures.Add(failure);

        Extension("probe.faulty", """
            { "id": "probe.faulty", "name": "Падающий", "contributions": { "newItems": [
              { "id": "probe.throw", "kind": "code", "title": "Падает" },
              { "id": "probe.escape", "kind": "code", "title": "Наружу" }
            ] } }
            """);

        _contributions.Add("probe.faulty", "Падающий", [
            Maker("probe.throw", """throw new InvalidOperationException("шаблон сломан");"""),
            Maker("probe.escape", """return Task.FromResult(NewItemResult.Made([new NewItemFile("../escape.txt")]));"""),
        ]);

        var token = TestContext.Current.CancellationToken;
        var thrown = await _items.MakeAsync(_items.Items[0], new NewItemRequest("a.txt", _target), token);
        var escaped = await _items.MakeAsync(_items.Items[1], new NewItemRequest("a.txt", _target), token);

        Assert.Equal(Localizer.Instance["newitem.failed"], thrown.Error);
        Assert.Single(failures, failure => failure.PluginId == "probe.faulty" && failure.Message.Contains("шаблон сломан", StringComparison.Ordinal));

        Assert.Equal(Localizer.Instance["newitem.broken"], escaped.Error);
        Assert.Contains(_log.Records, record => record.Message.Contains("../escape.txt", StringComparison.Ordinal));
    }

    /// <summary>
    /// Отмена — не сбой: код, бросивший на отмену, упавшим не считается, а зовущий получает отмену.
    /// </summary>
    /// <remarks>
    /// Отмена приходит, пока код пункта ещё ждёт: отменённый заранее запрос служба снимает сама, до
    /// кода, и шов тогда проверять было бы не на чем. «Передумал» — не отмена и не сбой: это ответ.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_cancelled_code_item_is_not_a_failure()
    {
        var failures = new List<PluginFailure>();

        _guard.Failed += (_, failure) => failures.Add(failure);

        Extension("probe.slow", """
            { "id": "probe.slow", "name": "Долгий", "contributions": { "newItems": [
              { "id": "probe.wait", "kind": "code", "title": "Ждёт" },
              { "id": "probe.decline", "kind": "code", "title": "Передумал" }
            ] } }
            """);

        _contributions.Add("probe.slow", "Долгий", [
            TestAssembly.Emit("Probe.Wait", """
                using System.Threading;
                using System.Threading.Tasks;
                using ArxisStudio.Sdk;

                [NewItem("probe.wait")]
                public sealed class Wait : NewItemMaker
                {
                    public override async Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken)
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);

                        return NewItemResult.Declined;
                    }
                }
                """),
            Maker("probe.decline", "return Task.FromResult(NewItemResult.Declined);"),
        ]);

        using var cancel = new CancellationTokenSource();

        var making = _items.MakeAsync(_items.Items[0], new NewItemRequest("a.txt", _target), cancel.Token);

        Assert.False(making.IsCompleted, "код пункта не дождался отмены");

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => making);

        Assert.Same(
            NewItemResult.Declined,
            await _items.MakeAsync(_items.Items[1], new NewItemRequest("a.txt", _target), TestContext.Current.CancellationToken));

        Assert.Empty(failures);
    }

    /// <summary>
    /// Кода нет — расширение не поднялось или класса не объявило — отказ, и журнал называет причины.
    /// </summary>
    [AvaloniaFact]
    public async Task A_code_item_without_its_code_is_refused()
    {
        Extension("probe.empty", """
            { "id": "probe.empty", "name": "Пустой", "contributions": { "newItems": [ { "id": "probe.none", "kind": "code", "title": "Ничего" } ] } }
            """);

        var made = await _items.MakeAsync(_items.Items[0], new NewItemRequest("a.txt", _target), TestContext.Current.CancellationToken);

        Assert.Equal(Localizer.Instance["newitem.nocode"], made.Error);
        Assert.Contains(_log.Records, record =>
            record.Message.Contains("onNewItem:probe.none", StringComparison.Ordinal) &&
            record.Message.Contains("[NewItem(\"probe.none\")]", StringComparison.Ordinal));
    }

    /// <summary>
    /// Реестр вкладов держит код пункта по хозяину и пункту: одноимённый пункт соседа — не его, второй
    /// класс на тот же пункт не заводится, упавший конструктор соседям не мешает, выгрузка снимает всё.
    /// </summary>
    [AvaloniaFact]
    public void The_registry_keeps_item_code_by_owner_and_item()
    {
        var failures = new List<PluginFailure>();

        _guard.Failed += (_, failure) => failures.Add(failure);

        var first = Maker("probe.one", "return Task.FromResult(NewItemResult.Declined);", name: "First");
        var second = Maker("probe.one", "return Task.FromResult(NewItemResult.Declined);", name: "Second");
        var broken = TestAssembly.Emit("Probe.Broken", """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using ArxisStudio.Sdk;

            [NewItem("probe.two")]
            public sealed class Broken : NewItemMaker
            {
                public Broken() => throw new InvalidOperationException("конструктор");

                public override Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken) =>
                    Task.FromResult(NewItemResult.Declined);
            }

            public sealed class Unmarked : NewItemMaker
            {
                public override Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken) =>
                    Task.FromResult(NewItemResult.Declined);
            }
            """);

        _contributions.Add("probe.owner", "Хозяин", [first, second, broken]);

        var kept = _contributions.MakerFor("probe.owner", "probe.one");

        Assert.NotNull(kept);
        Assert.Equal("First", kept.GetType().Name);
        Assert.Null(_contributions.MakerFor("probe.neighbour", "probe.one"));
        Assert.Null(_contributions.MakerFor("probe.owner", "probe.two"));
        Assert.Single(failures, failure => failure.Message.Contains("конструктор", StringComparison.Ordinal));

        _contributions.Remove("probe.owner");

        Assert.Null(_contributions.MakerFor("probe.owner", "probe.one"));
    }

    /// <summary>
    /// О пунктах, которые студия не покажет, и о значках, которые не разобрались, журнал слышит при
    /// чтении манифеста.
    /// </summary>
    [AvaloniaFact]
    public void Complaints_name_what_the_studio_will_not_read()
    {
        var plugin = Extension("probe.told", """
            { "id": "probe.told", "name": "Сказанный", "contributions": { "newItems": [
              { "title": "Без id" },
              { "id": "probe.odd", "kind": "folder", "title": "Странный" },
              { "id": "probe.untitled" },
              { "id": "probe.icons", "title": "Значки", "icon": "arxis:Nope", "variants": [ { "id": "v", "title": "V", "icon": "picture.png" } ] },
              { "id": "probe.fine", "title": "Хороший", "icon": "arxis:Text" }
            ] } }
            """);

        var complaints = StudioNewItems.Complaints(plugin).ToList();

        Assert.Equal(5, complaints.Count);
        Assert.Contains(complaints, complaint => complaint.StartsWith("пункт создания без id не показан", StringComparison.Ordinal));
        Assert.Contains(complaints, complaint => complaint.Contains("«folder»", StringComparison.Ordinal));
        Assert.Contains(complaints, complaint => complaint.Contains("probe.untitled не показан", StringComparison.Ordinal));
        Assert.Contains(complaints, complaint => complaint.StartsWith("пункт создания probe.icons —", StringComparison.Ordinal));
        Assert.Contains(complaints, complaint => complaint.StartsWith("пункт создания probe.icons, вариант v —", StringComparison.Ordinal));
    }

    /// <summary>
    /// Заметка модуля-образца собирается из шаблона, который сборка положила в каталог модуля.
    /// </summary>
    /// <remarks>
    /// Проверяет всю дорогу разом: шаблон лежит в <c>templates/</c> исходников, сборка его не
    /// компилирует и везёт в <c>modules/arxis.sample/templates</c>, манифест называет его путём от
    /// своего каталога, а служба читает и подставляет.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_sample_note_comes_from_the_template_the_build_carried()
    {
        _contributing.AddRange(StudioModules.Describe());

        var note = Assert.Single(_items.Items, item => item.Owner == "arxis.sample" && item.Id == "sample.note");

        Assert.Single(note.Menu);
        Assert.Equal("Note1", _items.Suggest(note, _target));

        var made = await _items.MakeAsync(note, new NewItemRequest("Idea", _target) { Project = "Волна" }, TestContext.Current.CancellationToken);
        var file = Assert.Single(made.Files);
        var year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("Idea.md", file.Path);
        Assert.True(file.Open);
        Assert.Equal($"# Idea\n\n> Волна · {year}\n", Encoding.UTF8.GetString(file.Content.Span));
    }

    /// <summary>
    /// Переменные подставляются только знакомые и только названные; всё прочее с долларами остаётся.
    /// </summary>
    /// <remarks>
    /// Код расширения зовёт ту же подстановку, что и студия для шаблонов манифеста, и разойтись они
    /// не должны: у «$$name$» переменная начинается со второго знака, незакрытая остаётся текстом, а
    /// имя в другом регистре — не переменная.
    /// </remarks>
    [Fact]
    public void The_template_expands_only_what_it_knows_and_was_told()
    {
        var request = new NewItemRequest("Card", "C:/probe") { Project = "Волна", RootNamespace = "Volna" };

        Assert.Equal(
            "$Card | Card$ | $Name$ | $namespace$ | Volna | Волна | $foo$ | $ | $$ | $\"{x}$\" | $name",
            NewItemTemplate.Expand("$$name$ | $name$$ | $Name$ | $namespace$ | $rootnamespace$ | $project$ | $foo$ | $ | $$ | $\"{x}$\" | $name", request));

        Assert.Equal(
            ["name", "namespace", "rootnamespace", "project", "year"],
            NewItemTemplate.Variables);
    }

    /// <summary>
    /// Исходов у сборки три, и спутать их нельзя: собранного без файлов не бывает, отказа без причины
    /// тоже — «ничего» называется своим именем.
    /// </summary>
    [Fact]
    public void A_result_is_made_failed_or_declined()
    {
        var made = NewItemResult.Made([new NewItemFile("a.txt")]);

        Assert.Null(made.Error);
        Assert.Single(made.Files);

        Assert.Equal("нет", NewItemResult.Failed("нет").Error);
        Assert.Empty(NewItemResult.Failed("нет").Files);

        Assert.Null(NewItemResult.Declined.Error);
        Assert.Empty(NewItemResult.Declined.Files);

        Assert.Throws<ArgumentException>(() => NewItemResult.Made([]));
        Assert.Throws<ArgumentException>(() => NewItemResult.Failed(" "));
    }

    /// <summary>
    /// Расширение на диске: манифест, словарь и шаблоны — прочитанное каталогом плагинов.
    /// </summary>
    private InstalledPlugin Extension(
        string id,
        string manifest,
        bool builtIn = false,
        bool enabled = true,
        Dictionary<string, string>? strings = null,
        Dictionary<string, byte[]>? templates = null)
    {
        var catalog = Path.Combine(_root, "catalog-" + id);
        var folder = Directory.CreateDirectory(Path.Combine(catalog, id)).FullName;

        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest);

        if (strings is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, "lang"));
            File.WriteAllText(Path.Combine(folder, "lang", "en.json"), System.Text.Json.JsonSerializer.Serialize(strings));
        }

        foreach (var (path, content) in templates ?? [])
        {
            var full = Path.Combine(folder, path);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        var plugin = Assert.Single(new PluginCatalog(catalog).Scan()) with { IsBuiltIn = builtIn, IsEnabled = enabled };

        Assert.True(plugin.IsValid, plugin.Error);

        _contributing.Add(plugin);

        return plugin;
    }

    /// <summary>Сборка с одним классом пункта создания — тело его <c>MakeAsync</c> подаётся текстом.</summary>
    private static System.Reflection.Assembly Maker(string id, string body, string name = "Maker") =>
        TestAssembly.Emit($"Probe.{name}", $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using ArxisStudio.Sdk;

            [NewItem("{{id}}")]
            public sealed class {{name}} : NewItemMaker
            {
                public override Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken)
                {
                    {{body}}
                }
            }
            """);
}
