using System.Text.Json;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.ViewModels;
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
public class PluginsPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-manager-{Guid.NewGuid():N}");
    private readonly ToolBarStrip _left = new();
    private readonly ToolBarStrip _center = new();
    private readonly ToolBarStrip _right = new();
    private readonly DockView _view = new();
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly StudioTaskRegistry _tasks = new();
    private readonly PluginContributionRegistry _contributions = new();
    private readonly Answers _answers = new();

    public PluginsPageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

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

    /// <summary>Страница поверх временной папки плагинов.</summary>
    private PluginsPage Page(out PluginCatalog catalog)
    {
        catalog = new PluginCatalog(_root);

        return new PluginsPage(catalog, Extensions(catalog), _answers);
    }

    /// <summary>
    /// Служба расширений, собранная, но не поднятая.
    /// </summary>
    /// <remarks>
    /// Хоста у неё нет, и применение к живой студии молча ничего не делает —
    /// ровно то, что нужно проверке транзакции.
    /// </remarks>
    private StudioPlugins Extensions(PluginCatalog catalog)
    {
        var commands = new StudioCommands(_guard);
        var dock = new StudioDock(_view);

        return new StudioPlugins(_log, _guard, _tasks, _contributions)
        {
            Commands = commands,
            Dock = dock,
            ToolBar = new StudioToolBar(_left, _center, _right),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Silence()),
            Services = new Dictionary<Type, object>(),
            Catalog = catalog.Scan,
            Assemblies = [],
        };
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

    /// <summary>Ответы вместо человека: страница спрашивает, тест отвечает.</summary>
    private sealed class Answers : IPluginDialogs
    {
        public Func<bool> Answer { get; set; } = () => true;

        public Task<string?> AskFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> AskArchiveAsync(string title) => Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger) =>
            Task.FromResult(Answer());

        public void Reveal(string path)
        {
        }
    }

    private sealed class Silence : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }
}
