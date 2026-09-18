using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Project;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Манифест окна проекта и то, что студия по нему строит.
/// </summary>
/// <remarks>
/// Студия читает манифест, не заглядывая в сборку, — и панель, объявленная под одним именем, а
/// помеченная другим, просто не появится. Здесь они сверяются, как у консоли.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectWindowModuleTests
{
    /// <summary>Объявленная панель есть в сборке, а команда в коде — та же, что в манифесте.</summary>
    [Fact]
    public void The_manifest_and_the_code_name_the_same_panel_and_command()
    {
        var manifest = Manifest();

        var built = typeof(ProjectModule).Assembly
            .GetTypes()
            .Select(type => type.GetCustomAttributes(typeof(ToolWindowAttribute), false).FirstOrDefault())
            .OfType<ToolWindowAttribute>()
            .Select(attribute => attribute.Id);

        Assert.Equal([ProjectModule.PanelId], built);
        Assert.Equal([ProjectModule.PanelId], manifest.Contributions.ToolWindows.Select(panel => panel.Id));
        Assert.Equal([ProjectModule.ShowCommand], manifest.Contributions.Commands.Select(command => command.Id));
    }

    /// <summary>
    /// Окно просится вниз, рядом с журналом, — как Project в Unity.
    /// </summary>
    /// <remarks>
    /// Сторона без соседа поставила бы окно под документы отдельной зоной; рядом с журналом оно
    /// встаёт вкладкой в ту же нижнюю зону, где его и ищут.
    /// </remarks>
    [Fact]
    public void The_window_asks_to_stand_at_the_bottom_next_to_the_log()
    {
        var panel = Assert.Single(Manifest().Contributions.ToolWindows);

        Assert.Equal("bottom", panel.Wanted.Side);
        Assert.InRange(panel.Wanted.Size, 0.2, 0.5);
        Assert.Equal("arxis.console:console.log", panel.Wanted.Near);
    }

    /// <summary>У всего, что студия рисует за модуль, есть подпись, а у команды — клавиши Rider.</summary>
    [Fact]
    public void Everything_the_studio_draws_for_the_window_has_a_title()
    {
        var manifest = Manifest();
        var panel = Assert.Single(manifest.Contributions.ToolWindows);
        var command = Assert.Single(manifest.Contributions.Commands);

        Assert.False(string.IsNullOrWhiteSpace(panel.Title), "у окна проекта нет заголовка");
        Assert.False(string.IsNullOrWhiteSpace(command.Title), "у команды показа нет подписи");
        Assert.Equal("Alt+1", command.Key);
    }

    /// <summary>
    /// Команда показа выводит окно вперёд и отдаёт ему клавиатуру.
    /// </summary>
    [Fact]
    public void The_show_command_brings_the_window_forward_with_the_keyboard()
    {
        var commands = new StudioCommands();
        var windows = new WindowsProbe();
        var services = new Dictionary<Type, object>
        {
            [typeof(IStudioToolWindows)] = windows,
            [typeof(IStudioFocus)] = windows,
        };

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null, services));

        var loaded = host.LoadBuiltIn(typeof(ProjectModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Equal("arxis.project", loaded.Installed.Id);
        Assert.True(commands.Invoke(ProjectModule.ShowCommand));
        Assert.Equal(["show project.window", "focus project.window"], windows.Asked);
    }

    /// <summary>
    /// Манифест объявляет ровно те настройки, которые окно читает: раскладку и ступень плиток.
    /// </summary>
    /// <remarks>
    /// Необъявленную настройку служба не запишет вовсе — запись молча пропадёт, и ⋮ окна переключал
    /// бы раскладку до первого перезапуска.
    /// </remarks>
    [Fact]
    public void The_manifest_declares_exactly_the_settings_the_window_reads()
    {
        var declared = Manifest().Contributions.Settings;

        Assert.Equal(ProjectSettings.Keys.Order(), declared.Select(setting => setting.Key).Order());
        Assert.True(declared.Single(setting => setting.Key == ProjectSettings.TwoColumnsKey).IsBool, "раскладка объявлена не переключателем");
        Assert.True(declared.Single(setting => setting.Key == ProjectSettings.IconSizeKey).IsNumber, "ступень объявлена не числом");
        Assert.All(declared, setting => Assert.False(setting.IsProject, $"{setting.Key} уехала бы с проектом, а это вкус человека"));
    }

    /// <summary>
    /// Немецкий пакет переводит окно целиком и только его ключами.
    /// </summary>
    /// <remarks>
    /// Лишний ключ пакет переводил бы впустую, пропущенный показался бы по-английски посреди
    /// немецкого окна. Перевод объявлен в манифесте пакета — иначе студия его не спросит.
    /// </remarks>
    [Fact]
    public void The_german_pack_translates_the_whole_window_and_nothing_else()
    {
        var root = Repository();
        var own = Keys(Path.Combine(root, "src", "Modules", "ArxisStudio.Modules.Project", "lang", "en.json"));
        var pack = Path.Combine(root, "src", "Plugins", "Arxis.Lang.De");
        var german = Keys(Path.Combine(pack, "lang", "arxis.project.de.json"));

        Assert.Equal(own.Order(StringComparer.Ordinal), german.Order(StringComparer.Ordinal));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "plugin.json")));

        var translations = manifest.RootElement
            .GetProperty("contributions").GetProperty("languages")[0].GetProperty("translations")
            .EnumerateArray()
            .Select(translation => translation.GetProperty("id").GetString());

        Assert.Contains("arxis.project", translations);
    }

    private static List<string> Keys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
    }

    private static string Repository()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ArxisStudio.slnx")))
            folder = folder.Parent;

        Assert.True(folder is not null, "не нашёл корень репозитория");

        return folder!.FullName;
    }

    private static PluginManifest Manifest()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ProjectModule).Assembly);

        Assert.Null(error);

        return manifest!;
    }

    /// <summary>Служба показа и фокуса, которая помнит, о чём её просили.</summary>
    private sealed class WindowsProbe : IStudioToolWindows, IStudioFocus
    {
        public List<string> Asked { get; } = [];

        public void Show(string toolWindowId) => Asked.Add($"show {toolWindowId}");

        public bool Focus(string toolWindowId)
        {
            Asked.Add($"focus {toolWindowId}");

            return true;
        }

        public bool IsFocused(string toolWindowId) => false;
    }
}
