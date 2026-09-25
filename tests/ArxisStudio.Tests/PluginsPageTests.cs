using System.Globalization;
using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Менеджер плагинов страницей окна настроек.
/// </summary>
/// <remarks>
/// Переехал сюда из экрана Welcome, и вместе с переездом сменилась его
/// главная привычка: включение и выключение больше не уходят на диск от
/// щелчка, а копятся до «Сохранить» — как всё прочее в этом окне.
/// <para>
/// Живого хоста здесь нет намеренно: служба расширений собрана, но не
/// поднята, и применение к работающей студии честно ничего не делает. Тут
/// проверяется транзакция — что на диск попадает ровно то и ровно тогда,
/// когда человек нажал «Сохранить».
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginsPageTests : IDisposable
{
    private readonly string _root = TempFolder.Create("manager");
    private readonly string _modules = TempFolder.Reserve("manager-modules");
    private readonly StudioPluginsHarness _studio = new();
    private readonly DialogAnswers _answers = new();

    public void Dispose()
    {
        _studio.Dispose();

        TempFolder.Erase(_root, strict: true);

        TempFolder.Erase(_modules, strict: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Галочка копится: до «Сохранить» на диске ничего не меняется.</summary>
    [Fact]
    public async Task Toggling_stages_the_switch_and_leaves_the_disk_alone()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out var catalog);
        var card = Assert.Single(page.Cards);

        await page.ToggleAsync(card);

        Assert.False(card.IsOn);
        Assert.True(page.HasChanges);
        Assert.True(catalog.Scan().Single().IsEnabled, "до сохранения плагин остаётся включённым");
    }

    /// <summary>«Сохранить» уносит галочку на диск.</summary>
    [Fact]
    public async Task Saving_writes_the_switch()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out var catalog);
        var problems = new List<string>();

        await page.ToggleAsync(page.Cards[0]);
        await page.CommitAsync(problems);

        Assert.Empty(problems);
        Assert.False(catalog.Scan().Single().IsEnabled);
        Assert.False(page.HasChanges, "записанное несохранённым не считается");
    }

    /// <summary>«Отмена» возвращает галочку, не тронув диска.</summary>
    [Fact]
    public async Task Cancelling_forgets_the_switch()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out var catalog);

        await page.ToggleAsync(page.Cards[0]);
        page.Revert();

        Assert.True(page.Cards[0].IsOn);
        Assert.False(page.HasChanges);
        Assert.True(catalog.Scan().Single().IsEnabled);
    }

    /// <summary>
    /// Выключение зависимости спрашивает и гасит зависимого — тоже галочкой.
    /// </summary>
    /// <remarks>
    /// Зависимый без соседа не поднимется, и человек должен решить это глазами.
    /// Гаснет зависимый здесь же, а не на диске: до «Сохранить» решение ещё
    /// передумывается целиком.
    /// </remarks>
    [Fact]
    public async Task Disabling_a_dependency_asks_and_takes_the_dependent_with_it()
    {
        Plugin("arxis.base", "Основа");
        Plugin("arxis.top", "Надстройка", dependsOn: "arxis.base");

        var page = Page(out var catalog);
        var asked = 0;

        _answers.Answer = () => { asked++; return true; };

        await page.ToggleAsync(page.Cards.First(card => card.Plugin.Id == "arxis.base"));

        Assert.Equal(1, asked);
        Assert.All(page.Cards, card => Assert.False(card.IsOn));
        Assert.All(catalog.Scan(), plugin => Assert.True(plugin.IsEnabled, "до сохранения диск не трогают"));
    }

    /// <summary>Отказ на вопрос оставляет всё как было.</summary>
    [Fact]
    public async Task Refusing_the_question_changes_nothing()
    {
        Plugin("arxis.base", "Основа");
        Plugin("arxis.top", "Надстройка", dependsOn: "arxis.base");

        var page = Page(out _);

        _answers.Answer = () => false;

        await page.ToggleAsync(page.Cards.First(card => card.Plugin.Id == "arxis.base"));

        Assert.All(page.Cards, card => Assert.True(card.IsOn));
        Assert.False(page.HasChanges);
    }

    /// <summary>Поиск окна находит страницу по имени плагина.</summary>
    [Fact]
    public void The_search_finds_the_page_by_the_name_of_a_plugin()
    {
        Plugin("arxis.one", "Забавный");

        var page = Page(out _);

        Assert.Contains("Забавный", page.Terms);
        Assert.Contains("arxis.one", page.Terms);
    }

    /// <summary>
    /// И по метке — той, что написана в манифесте.
    /// </summary>
    /// <remarks>
    /// По самому тегу, а не по его подписи: подпись переводится, тег — нет, и
    /// найденное не должно меняться вместе с языком интерфейса.
    /// </remarks>
    [Fact]
    public void The_search_finds_the_page_by_a_tag()
    {
        Plugin("arxis.one", "Забавный", tags: ["Tools"]);

        Assert.Contains("tools", Page(out _).Terms);
    }

    /// <summary>
    /// Поиск окна оставляет на странице только найденные плагины — и тогда, когда список собрали заново.
    /// </summary>
    /// <remarks>
    /// Список пересобирается после установки и удаления, а поиск в окне остаётся тем же: новая
    /// карточка обязана встать под тот же отбор, иначе установленный плагин выскочил бы посреди
    /// найденных.
    /// </remarks>
    [Fact]
    public void A_search_leaves_only_the_plugins_it_found()
    {
        Plugin("arxis.one", "Первый", tags: ["tools"]);
        Plugin("arxis.two", "Второй");

        var page = Page(out _);

        page.Narrow("tools");

        Assert.Equal(["arxis.one"], page.Cards.Where(card => card.IsShown).Select(card => card.Plugin.Id));

        page.Refresh();

        Assert.Equal(["arxis.one"], page.Cards.Where(card => card.IsShown).Select(card => card.Plugin.Id));

        page.Narrow(null);

        Assert.All(page.Cards, card => Assert.True(card.IsShown, $"{card.Plugin.Id} не вернулся, когда поиск стёрли"));
    }

    /// <summary>
    /// Плагин, который не поднялся, говорит об этом на своей карточке — и причину тоже.
    /// </summary>
    /// <remarks>
    /// Манифест у него цел, и прежде карточка выглядела исправной, хотя плагин не работал.
    /// Строка о неполном запуске говорит только, что такой есть, а журнал, где названа причина, с
    /// Welcome не виден. Выключенный плагин отметки не носит: его и не поднимали.
    /// </remarks>
    [AvaloniaFact]
    public void A_plugin_that_did_not_start_says_so_on_its_card()
    {
        Broken("arxis.broken", "Сломанный");
        Plugin("arxis.one", "Первый");

        var catalog = new PluginCatalog(_root);
        var extensions = Extensions(catalog);

        extensions.LoadPlugins();

        var page = new PluginsPage(catalog, extensions, _answers);
        var broken = page.Cards.Single(card => card.Plugin.Id == "arxis.broken");
        var fine = page.Cards.Single(card => card.Plugin.Id == "arxis.one");

        Assert.True(broken.Plugin.IsValid, "манифест сломанного цел — отказ не в нём");
        Assert.True(broken.HasRiseError, "не поднявшийся плагин выглядит исправным");
        Assert.False(string.IsNullOrWhiteSpace(broken.RiseError), "отметка есть, а причины нет");
        Assert.False(fine.HasRiseError);
    }

    /// <summary>
    /// Плагины стоят группами по роду: внешние, языковые пакеты, встроенные — последними и свёрнутыми.
    /// </summary>
    /// <remarks>
    /// Как Installed у Rider: группы и счётчик у каждой. Встроенные справочные, поэтому стоят в конце
    /// и не мешают тем, что ставят и снимают.
    /// </remarks>
    [Fact]
    public void Plugins_are_grouped_by_kind_with_the_built_in_modules_last()
    {
        Plugin("arxis.one", "Первый");
        Pack("arxis.lang.de", "Deutsch");

        var page = Page(out _, Module());

        Assert.Equal(["external", "languages", "builtin"], page.Groups.Select(group => group.Key));
        Assert.Equal(
            [
                Localizer.Instance["plugins.group.external"], "Первый",
                Localizer.Instance["plugins.group.languages"], "Deutsch",
                Localizer.Instance["plugins.group.builtin"],
            ],
            page.Rows.Select(Title));
        Assert.True(Assert.Single(page.BuiltIn).IsBuiltIn);
    }

    /// <summary>
    /// Встроенные свёрнуты, пока есть что-то ещё; поиск их раскрывает, а стёртый — сворачивает обратно.
    /// </summary>
    /// <remarks>
    /// Свёрнутая группа спрятала бы найденное, как свёрнутая ветка дерева настроек, — поэтому пока идёт
    /// поиск, группы раскрыты и не сворачиваются. Одни встроенные раскрыты сразу: иначе страница
    /// показывала бы пустоту под заголовком.
    /// </remarks>
    [Fact]
    public void The_built_in_group_starts_folded_and_unfolds_for_a_search()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out _, Module());
        var builtIn = page.Groups.Single(group => group.Key == "builtin");

        Assert.DoesNotContain(page.BuiltIn[0], page.Rows);

        page.Narrow("Терм");

        Assert.Equal([Localizer.Instance["plugins.group.builtin"], "Терминал"], page.Rows.Select(Title));
        Assert.False(builtIn.CanFold, "группу можно свернуть посреди поиска");

        page.Narrow(null);

        Assert.DoesNotContain(page.BuiltIn[0], page.Rows);
        Assert.True(builtIn.CanFold, "стёртый поиск оставил группу несворачиваемой");

        Directory.Delete(Path.Combine(_root, "arxis.one"), recursive: true);

        var alone = Page(out _, Module());

        Assert.Contains(alone.BuiltIn[0], alone.Rows);
    }

    /// <summary>
    /// Счётчик группы — сколько включено из всех, и идёт он за галочками, ещё не сохранёнными.
    /// </summary>
    /// <remarks>
    /// Счётчик показывает список, который человек видит, а не диск: снятая галочка сразу стоит в
    /// нём, как у Rider.
    /// </remarks>
    [Fact]
    public async Task A_group_counts_what_is_switched_on_including_the_unsaved()
    {
        Plugin("arxis.one", "Первый");
        Plugin("arxis.two", "Второй");

        var page = Page(out _);
        var group = Assert.Single(page.Groups);

        Assert.Equal(Counter(2, 2), group.Counter);

        await page.ToggleAsync(page.Cards.First(card => card.Plugin.Id == "arxis.one"));

        Assert.Equal(Counter(1, 2), group.Counter);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.group.counter.hint"], 1, 2),
            group.CounterHint);

        static string Counter(int on, int all) =>
            string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.group.counter"], on, all);
    }

    /// <summary>Заголовок группы выбрать нельзя: выбранным остаётся плагин.</summary>
    [Fact]
    public void A_group_header_cannot_be_chosen()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out _);
        var chosen = page.Card;

        Assert.NotNull(chosen);

        page.Selected = page.Rows[0];

        Assert.Same(chosen, page.Card);
        Assert.Same(chosen, page.Selected);
    }

    /// <summary>Выбор переживает пересборку списка — после установки соседа и после «Сохранить».</summary>
    [Fact]
    public void The_choice_survives_a_refresh()
    {
        Plugin("arxis.one", "Первый");
        Plugin("arxis.two", "Второй");

        var page = Page(out _);

        // Выбран не первый: первый страница выбирает сама, и выбор, потерянный пересборкой, пришёл
        // бы к нему же — проверять было бы нечего.
        var chosen = page.Rows.OfType<PluginCard>().Last();

        Assert.NotSame(page.Card, chosen);

        page.Selected = chosen;
        page.Refresh();

        Assert.Equal(chosen.Plugin.Id, page.Card?.Plugin.Id);
    }

    /// <summary>
    /// Встроенный модуль не выключается и не удаляется — и без флажка, и Пробелом, и Delete.
    /// </summary>
    /// <remarks>
    /// Флажка и меню у него нет, но клавиши строки и чужой вызов доходят до страницы и без них. Модуль
    /// уходит только вместе со студией — страница о нём даже не спрашивает.
    /// </remarks>
    [Fact]
    public async Task A_built_in_module_can_be_neither_switched_off_nor_removed()
    {
        var asked = 0;

        _answers.Answer = () =>
        {
            asked++;
            return true;
        };

        // Папка модуля — своя, временная: уйди удаление мимо охраны, оно снесло бы её, а не
        // настоящую папку терминала в выходе тестов.
        var page = Page(out _, Standalone());
        var module = Assert.Single(page.BuiltIn);

        Assert.False(module.CanToggle);
        Assert.False(module.CanRemove);

        await page.ToggleAsync(module);
        await page.RemoveAsync(module);

        Assert.True(module.IsOn, "встроенный модуль выключился");
        Assert.False(page.HasChanges, "у встроенного модуля появилась правка");
        Assert.Equal(0, asked);
        Assert.True(Directory.Exists(module.Folder), "папку встроенного модуля снесли");
    }

    /// <summary>
    /// Поиск оставляет в списке найденные плагины, прячет группы без них и переводит выбор на
    /// найденное.
    /// </summary>
    [Fact]
    public void A_search_shows_only_matching_plugins_and_hides_empty_groups()
    {
        Plugin("arxis.one", "Первый");
        Pack("arxis.lang.de", "Deutsch");

        var page = Page(out _);

        Assert.Equal("arxis.one", page.Card?.Plugin.Id);

        page.Narrow("deutsch");

        Assert.Equal([Localizer.Instance["plugins.group.languages"], "Deutsch"], page.Rows.Select(Title));
        Assert.Equal("arxis.lang.de", page.Card?.Plugin.Id);
    }

    /// <summary>
    /// Подробности называют и тех, кого плагин использует, и тех, кому он нужен, — как Unity Package
    /// Manager.
    /// </summary>
    /// <remarks>
    /// «Нужен плагинам» — прямые: кто назвал его в своём манифесте. Кто стоит за ними, видно в их
    /// собственных подробностях.
    /// </remarks>
    [Fact]
    public void The_details_name_who_needs_the_plugin()
    {
        Plugin("arxis.one", "Первый");
        Plugin("arxis.two", "Второй", dependsOn: "arxis.one");
        Plugin("arxis.three", "Третий", dependsOn: "arxis.two");

        var page = Page(out _);
        var one = page.Cards.Single(card => card.Plugin.Id == "arxis.one");
        var two = page.Cards.Single(card => card.Plugin.Id == "arxis.two");

        Assert.Equal(["Второй"], one.Dependents);
        Assert.Equal(["Третий"], two.Dependents);
        Assert.True(two.HasDependencies && two.HasDependents && two.HasLinks);
    }

    /// <summary>Подробности говорят, что плагин добавляет в студию и что о нём известно, — по манифесту.</summary>
    [Fact]
    public void The_details_tell_what_the_plugin_brings()
    {
        Rich("arxis.rich");

        var card = Assert.Single(Page(out _).Cards);

        Assert.Equal(
            [
                (Localizer.Instance["plugins.details.panels"], "Панель примера"),
                (Localizer.Instance["plugins.details.commands"], "2"),
                (Localizer.Instance["plugins.details.settings"], "1"),
            ],
            card.Brings.Select(fact => (fact.Label, fact.Value)));

        Assert.Equal(
            [
                (Localizer.Instance["plugins.details.id"], "arxis.rich"),
                (Localizer.Instance["plugins.details.version"], "2.1.0"),
                (Localizer.Instance["plugins.details.publisher"], "Тест"),
                (Localizer.Instance["plugins.details.sdk"], string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.details.sdk.value"], "7.0")),
                (Localizer.Instance["plugins.details.activation"], string.Join("; ",
                    Localizer.Instance["plugins.activation.startup"],
                    string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.activation.files"], ".cs, .md"),
                    string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.activation.commands"], "rich.one"))),
            ],
            card.Facts.Select(fact => (fact.Label, fact.Value)));
        Assert.True(card.DeclaresSettings);
        Assert.Equal("extension:arxis.rich", card.SettingsPageId);

        // Путь показывается с местами для переноса по сегментам, но остаётся тем же путём.
        Assert.Equal(card.Folder, card.FolderShown.Replace("​", string.Empty, StringComparison.Ordinal));
        Assert.Contains("​", card.FolderShown, StringComparison.Ordinal);
    }

    /// <summary>Непринятое подробности называют словами, а «Вернуть» его забывает.</summary>
    [Fact]
    public async Task A_pending_change_is_told_and_can_be_taken_back()
    {
        Plugin("arxis.one", "Первый");

        var page = Page(out _);
        var card = Assert.Single(page.Cards);

        Assert.Null(card.Pending);

        await page.ToggleAsync(card);

        Assert.Equal(Localizer.Instance["plugins.pending.off"], card.Pending);

        page.Undo(card);

        Assert.Null(card.Pending);
        Assert.False(page.HasChanges, "«Вернуть» оставило правку");
    }

    /// <summary>Подпись строки списка: заголовок группы — его подпись, плагин — его имя.</summary>
    private static string Title(object row) => row is PluginGroup group ? group.Title : ((PluginCard)row).Plugin.DisplayName;

    /// <summary>Страница поверх временной папки плагинов.</summary>
    private PluginsPage Page(out PluginCatalog catalog, params InstalledPlugin[] builtIn)
    {
        catalog = new PluginCatalog(_root);

        return new PluginsPage(catalog, Extensions(catalog), _answers, builtIn);
    }

    /// <summary>Встроенный модуль — как его видит студия.</summary>
    /// <remarks>Папка — настоящая папка терминала: из неё берутся подписи.</remarks>
    private static InstalledPlugin Module() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
        new ArxisStudio.Sdk.Plugins.PluginManifest { Id = "arxis.terminal", Name = "Терминал", Version = "1.0.0" },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Встроенный модуль в своей временной папке, вне каталога плагинов.</summary>
    private InstalledPlugin Standalone()
    {
        var folder = Path.Combine(_modules, "arxis.standalone");

        Directory.CreateDirectory(folder);

        return new InstalledPlugin(
            folder,
            new ArxisStudio.Sdk.Plugins.PluginManifest { Id = "arxis.standalone", Name = "Отдельный", Version = "1.0.0" },
            Error: null,
            IsEnabled: true,
            IsBuiltIn: true);
    }

    /// <summary>Кладёт языковой пакет: языки есть, сборки нет.</summary>
    private void Pack(string id, string name)
    {
        var folder = Path.Combine(_root, id);

        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["version"] = "1.0.0",
                    ["publisher"] = "Тест",
                    ["contributions"] = new Dictionary<string, object>
                    {
                        ["languages"] = new[] { new Dictionary<string, object> { ["code"] = "de", ["name"] = "Deutsch", ["file"] = "lang/de.json" } },
                    },
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Кладёт плагин, объявивший панель, две команды и настройку.</summary>
    private void Rich(string id)
    {
        var folder = Path.Combine(_root, id);

        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = "Богатый",
                    ["version"] = "2.1.0",
                    ["publisher"] = "Тест",
                    ["sdk"] = new Dictionary<string, object> { ["min"] = "7.0" },
                    ["activation"] = new[] { "onStartup", "onFileType:.cs", "onFileType:.md", "onCommand:rich.one" },
                    ["contributions"] = new Dictionary<string, object>
                    {
                        ["commands"] = new[] { new Dictionary<string, object> { ["id"] = "rich.one" }, new Dictionary<string, object> { ["id"] = "rich.two" } },
                        ["toolWindows"] = new[] { new Dictionary<string, object> { ["id"] = "main", ["title"] = "Панель примера" } },
                        ["settings"] = new[] { new Dictionary<string, object> { ["key"] = "rich.on", ["type"] = "bool", ["scope"] = "user", ["default"] = true } },
                    },
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Служба расширений, собранная, но не поднятая.
    /// </summary>
    /// <remarks>
    /// Хоста у неё нет, и применение к живой студии молча ничего не делает —
    /// ровно то, что нужно проверке транзакции.
    /// </remarks>
    private StudioPlugins Extensions(PluginCatalog catalog) => _studio.Build(catalog: catalog.Scan);

    /// <summary>Кладёт плагин, манифест которого цел, а сборка сборкой не является.</summary>
    private void Broken(string id, string name)
    {
        var folder = Path.Combine(_root, id);

        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllBytes(Path.Combine(folder, "bin", "Broken.dll"), "не сборка"u8.ToArray());
        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["version"] = "1.0.0",
                    ["publisher"] = "Тест",
                    ["entry"] = "bin/Broken.dll",
                    ["activation"] = new[] { "onStartup" },
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Кладёт в папку плагин: манифест без сборки — как у языкового пакета.</summary>
    private void Plugin(string id, string name, string? dependsOn = null, string[]? tags = null)
    {
        var folder = Path.Combine(_root, id);

        Directory.CreateDirectory(folder);

        var manifest = new Dictionary<string, object>
        {
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["publisher"] = "Тест",
        };

        if (dependsOn is not null)
            manifest["dependencies"] = new[] { new Dictionary<string, object> { ["id"] = dependsOn } };

        if (tags is not null)
            manifest["tags"] = tags;

        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
