using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Palette;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значки панелей и команд из манифеста: вкладка, пункт меню, строка палитры.
/// </summary>
/// <remarks>
/// Запись та же, что у кнопки полосы, — имя из набора студии или контур в сетке 16, — и читается
/// так же, без загрузки сборки. Значок команды объявлен у команды, а не у каждого места, где она
/// показана: одна команда с разными значками в разных местах читалась бы как разные команды.
/// <para>
/// Очередь общая: подписи панелей привязываются к словарям, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class DeclaredIconsTests : IDisposable
{
    private readonly StudioLog _log = new();
    private readonly PluginGuard _guard = new();
    private readonly PluginContributionRegistry _contributions = new();

    private StudioPlugins? _plugins;

    public void Dispose()
    {
        // Хост отпускается первым: пока жив контекст загрузки, его файлы держит процесс.
        _plugins?.Stop();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Вкладка панели показывает значок, объявленный манифестом.
    /// </summary>
    /// <remarks>
    /// Панелей в студии столько, сколько принесли расширения, и вкладка — единственное место, где
    /// панель узнают, не читая подписи. Значок, который ничего не рисует, панель не отменяет: она
    /// встаёт без значка.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_tab_shows_the_icon_its_manifest_declared()
    {
        var view = Raised();

        Assert.Same(AxIcons.Terminal, Tab(view, "Проба").Icon);
        Assert.Null(Tab(view, "Пустая").Icon);
    }

    /// <summary>
    /// Пункт меню и строка палитры берут значок у объявления своей команды.
    /// </summary>
    /// <remarks>
    /// Меню и палитра собираются по манифестам на каждом открытии, и значок команды обязан доехать
    /// до обоих той же дорогой, что название. Команда без значка и команда с неразобравшимся
    /// значком показываются без него, но показываются.
    /// </remarks>
    [AvaloniaFact]
    public void A_menu_item_and_a_palette_row_take_the_icon_of_their_command()
    {
        Raised();

        var branch = Assert.Single(StudioMenu.Build(_plugins!.Contributing), item => item.Title == "Значки");

        Assert.Null(branch.Icon);
        Assert.Same(AxIcons.Refresh, Assert.Single(branch.Children).Icon);

        var declared = CommandPalette.Declared(_plugins.Contributing);

        Assert.Same(AxIcons.Refresh, Assert.Single(declared, entry => entry.CommandId == "glyphs.run").Icon);
        Assert.Null(Assert.Single(declared, entry => entry.CommandId == "glyphs.odd").Icon);
        Assert.Null(Assert.Single(declared, entry => entry.CommandId == "glyphs.plain").Icon);
    }

    /// <summary>
    /// О значке, который не разобрался, журнал слышит — один раз, при чтении манифеста.
    /// </summary>
    /// <remarks>
    /// Значок рисуют на каждой перестройке: меню и палитру — на каждом открытии, вкладку — при
    /// каждом подъёме. Скажи студия о нём там, где рисует, одно и то же замечание повторялось бы
    /// столько же раз; не скажи вовсе — автор гадал бы, почему значка нет.
    /// </remarks>
    [AvaloniaFact]
    public void A_broken_icon_is_told_once_when_the_manifest_is_read()
    {
        Raised();

        StudioMenu.Build(_plugins!.Contributing);
        CommandPalette.Declared(_plugins.Contributing);

        Assert.Single(_log.Records, record =>
            record.Level == StudioLogLevel.Warning
            && record.Message.Contains("glyphs.odd", StringComparison.Ordinal)
            && record.Message.Contains("arxis:Nope", StringComparison.Ordinal));

        Assert.Single(_log.Records, record =>
            record.Level == StudioLogLevel.Warning
            && record.Message.Contains("blank", StringComparison.Ordinal)
            && record.Message.Contains("M8 8", StringComparison.Ordinal));

        Assert.DoesNotContain(_log.Records, record =>
            record.Level == StudioLogLevel.Warning
            && (record.Message.Contains("glyphs.run", StringComparison.Ordinal)
                || record.Message.Contains("glyphs.plain", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Пункт меню находит значок и у команды, объявленной соседом, — а своё объявление старше.
    /// </summary>
    /// <remarks>
    /// Пункт зовёт команду по имени, и реестр у студии один: объявить команду мог и сосед, чей
    /// пункт стоит в этой ветке. Значок принадлежит объявлению, где бы оно ни стояло.
    /// </remarks>
    [AvaloniaFact]
    public void A_menu_item_finds_the_icon_where_its_command_is_declared()
    {
        var host = Installed("figma", commands: [new PluginCommand("figma.import") { Icon = "arxis:Download" }]);
        var guest = Installed("guest", menus: [new PluginMenuItem("Инструменты/Импорт соседа…", "figma.import")]);

        Assert.Same(AxIcons.Download, Assert.Single(Assert.Single(StudioMenu.Build([guest, host])).Children).Icon);

        // Сосед подан первым нарочно: первенство своего объявления не должно зависеть от очереди.
        guest.Manifest!.Contributions.Commands.Add(new PluginCommand("figma.import") { Icon = "arxis:Plus" });

        Assert.Same(AxIcons.Plus, Assert.Single(Assert.Single(StudioMenu.Build([host, guest])).Children).Icon);
    }

    /// <summary>
    /// Значок строки палитры переживает сборку списка: пункт меню отдаёт его строке.
    /// </summary>
    /// <remarks>
    /// Строка собирается из пункта меню, а сочетание дописывается ей копией. Потеряй копия значок,
    /// команда с пунктом меню стояла бы в палитре без значка, а без пункта — с ним.
    /// </remarks>
    [AvaloniaFact]
    public void The_palette_keeps_the_icon_a_menu_item_hands_over()
    {
        var tools = new StudioMenuItem("Инструменты");

        tools.Children.Add(new StudioMenuItem("Импорт…", "figma", "figma.import") { Icon = AxIcons.Download });

        var gathered = CommandPalette.Gather([tools], [], [], id => id == "figma.import" ? "Ctrl+I" : null);
        var row = Assert.Single(gathered);

        Assert.Equal("Ctrl+I", row.Gesture);
        Assert.Same(AxIcons.Download, row.Icon);
    }

    /// <summary>
    /// Значки манифестов, которые едут с репозиторием, разбираются все.
    /// </summary>
    /// <remarks>
    /// Опечатка в имени значка не валит ни сборку, ни подъём: пункт встаёт без значка, а в журнале
    /// остаётся строка, которую никто не читает. Модули, примеры и шаблон — то, с чего списывают
    /// авторы расширений, и опечатка в них разошлась бы по чужим манифестам.
    /// </remarks>
    [AvaloniaFact]
    public void The_icons_of_the_manifests_in_the_repository_resolve()
    {
        var plugins = Folder("src", "Plugins");
        var manifests = StudioModules.Describe()
            .Select(module => (Name: module.Id, Manifest: module.Manifest!))
            .Concat(Directory.GetDirectories(plugins)
                .Append(Folder("templates", "Arxis.Plugin"))
                .Select(folder => Path.Combine(folder, "plugin.json"))
                .Where(File.Exists)
                .Select(path => (Name: path, Manifest: Read(path))))
            .ToList();

        var records = manifests
            .SelectMany(each => each.Manifest.Contributions.Commands.Select(command => (each.Name, What: command.Id, command.Icon))
                .Concat(each.Manifest.Contributions.ToolWindows.Select(panel => (each.Name, What: panel.Id, panel.Icon)))
                .Concat(each.Manifest.Contributions.ToolBar.Select(item => (each.Name, What: item.Id, item.Icon))))
            .Where(record => record.Icon is { Length: > 0 })
            .ToList();

        Assert.True(records.Count > 10, $"значков в манифестах репозитория нашлось {records.Count} — искали не там");

        foreach (var (name, what, icon) in records)
        {
            ManifestIcons.Resolve(icon, out var problem);

            Assert.True(problem is null, $"{name}, {what}: {problem}");
        }
    }

    /// <summary>Папка репозитория — от папки тестов вверх.</summary>
    private static string Folder(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "ArxisStudio.slnx")))
                return candidate;
        }

        throw new InvalidOperationException($"Не найдена папка {string.Join('/', parts)}");
    }

    /// <summary>Манифест с диска — теми же правилами чтения, что у каталога плагинов.</summary>
    private static PluginManifest Read(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })!;

    /// <summary>Вкладка по подписи — там, где она стоит в раскладке.</summary>
    private static AxTabItem Tab(DockView view, string title) =>
        Assert.Single(view.GetVisualDescendants().OfType<AxTabItem>(), tab => Equals(tab.Content, title));

    /// <summary>Запись о расширении, собранная в памяти: сборки у неё нет.</summary>
    private static InstalledPlugin Installed(
        string id, PluginCommand[]? commands = null, PluginMenuItem[]? menus = null)
    {
        var manifest = new PluginManifest { Id = id, Name = id };

        foreach (var command in commands ?? [])
            manifest.Contributions.Commands.Add(command);

        foreach (var menu in menus ?? [])
            manifest.Contributions.Menus.Add(menu);

        return new InstalledPlugin(Path.Combine(Path.GetTempPath(), $"arxis-icons-{id}"), manifest, null, IsEnabled: true);
    }

    /// <summary>Поднимает модуль, объявивший значки панелям и командам.</summary>
    private DockView Raised()
    {
        var view = new DockView();
        var dock = new StudioDock(view);

        new Window { Width = 900, Height = 600, Content = view }.Show();
        dock.Shown();

        _plugins = new StudioPlugins(_log, _guard, new StudioTaskRegistry(), _contributions)
        {
            Assemblies = [TestAssembly.EmitModule("Probe.Glyphs", Source, Manifest)],
            Commands = new StudioCommands(),
            Dock = dock,
            ToolBar = new StudioToolBar(new ToolBarStrip(), new ToolBarStrip(), new ToolBarStrip()),
            Documents = new StudioDocuments(dock, _contributions.EditorFor, new Quiet()),
            Services = new Dictionary<Type, object>
            {
                [typeof(PluginContributionRegistry)] = _contributions,
                [typeof(PluginGuard)] = _guard,
            },
            Catalog = () => [],
        };

        _plugins.LoadModules();
        Dispatcher.UIThread.RunJobs();

        return view;
    }

    /// <summary>Статус, которому некому докладывать.</summary>
    private sealed class Quiet : IStudioStatus
    {
        public void Show(string message)
        {
        }
    }

    private const string Source = """
        using ArxisStudio.Sdk;
        using Avalonia.Controls;

        namespace Probe;

        public sealed class GlyphsModule : StudioPlugin
        {
            public override void Activate(IStudioContext context)
            {
            }
        }

        [ToolWindow("panel")]
        public sealed class GlyphsPanel : ToolWindow
        {
            protected override Control Build() => new Border();
        }

        [ToolWindow("blank")]
        public sealed class BlankPanel : ToolWindow
        {
            protected override Control Build() => new Border();
        }
        """;

    private const string Manifest = """
        {
          "id": "arxis.glyphs",
          "name": "Значки",
          "version": "1.0.0",
          "contributions": {
            "commands": [
              { "id": "glyphs.run", "title": "Запустить", "icon": "arxis:Refresh" },
              { "id": "glyphs.odd", "title": "Странная", "icon": "arxis:Nope" },
              { "id": "glyphs.plain", "title": "Простая" }
            ],
            "menus": [ { "path": "Значки/Запустить", "command": "glyphs.run" } ],
            "toolWindows": [
              { "id": "panel", "title": "Проба", "icon": "arxis:Terminal", "placement": { "side": "left" } },
              { "id": "blank", "title": "Пустая", "icon": "M8 8", "placement": { "side": "right" } }
            ]
          },
          "activation": [ "onStartup" ]
        }
        """;
}
