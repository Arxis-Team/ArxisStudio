using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Console;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Манифест консоли и то, что студия по нему строит.
/// </summary>
/// <remarks>
/// Манифест и код расходятся молча: студия читает первый, не заглядывая во
/// второй, — и панель, объявленная под одним именем, а помеченная другим,
/// просто не появится. Здесь они сверяются.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ConsoleModuleTests
{
    /// <summary>Каждая объявленная панель есть в сборке.</summary>
    [Fact]
    public void The_console_module_carries_every_panel_it_declares()
    {
        var declared = Manifest().Contributions.ToolWindows.Select(panel => panel.Id).Order();

        var built = typeof(ConsoleModule).Assembly
            .GetTypes()
            .Select(type => type.GetCustomAttributes(typeof(ToolWindowAttribute), false).FirstOrDefault())
            .OfType<ToolWindowAttribute>()
            .Select(attribute => attribute.Id)
            .Order();

        Assert.Equal(declared, built);
    }

    /// <summary>Идентификаторы команд в коде — те же, что в манифесте.</summary>
    [Fact]
    public void The_commands_in_code_are_the_commands_in_the_manifest()
    {
        string[] code =
        [
            ConsoleModule.OpenCommand,
            ConsoleModule.ClearCommand,
        ];

        Assert.Equal(
            Manifest().Contributions.Commands.Select(command => command.Id).Order(),
            code.Order());
    }

    /// <summary>Кнопка полосы и пункты меню зовут объявленные команды.</summary>
    [Fact]
    public void Every_button_and_menu_item_names_a_declared_command()
    {
        var manifest = Manifest();
        var declared = manifest.Contributions.Commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var item in manifest.Contributions.ToolBar.Where(item => item.IsButton))
            Assert.True(item.Command is { } command && declared.Contains(command), $"{item.Id} зовёт неизвестную команду");

        foreach (var item in manifest.Contributions.Menus)
            Assert.True(declared.Contains(item.Command), $"пункт {item.Path} зовёт неизвестную команду");
    }

    /// <summary>У всего, что студия рисует за модуль, есть подпись.</summary>
    /// <remarks>
    /// Без неё в полосе остаётся значок, о котором нельзя узнать ничего — ни
    /// наведя курсор, ни программой чтения с экрана.
    /// </remarks>
    [Fact]
    public void Everything_the_studio_draws_for_the_console_has_a_title()
    {
        var manifest = Manifest();

        foreach (var item in manifest.Contributions.ToolBar)
            Assert.False(string.IsNullOrWhiteSpace(item.Title), $"у элемента {item.Id} нет подписи");

        foreach (var panel in manifest.Contributions.ToolWindows)
            Assert.False(string.IsNullOrWhiteSpace(panel.Title), $"у панели {panel.Id} нет заголовка");

        foreach (var setting in manifest.Contributions.Settings)
            Assert.False(string.IsNullOrWhiteSpace(setting.Title), $"у настройки {setting.Key} нет подписи");
    }

    /// <summary>Манифест объявляет ровно те настройки, которые модуль читает.</summary>
    [Fact]
    public void The_manifest_declares_exactly_the_settings_the_module_reads()
    {
        var declared = Manifest().Contributions.Settings;

        Assert.Equal(ConsoleSettings.Keys.Order(), declared.Select(setting => setting.Key).Order());

        foreach (var setting in declared)
        {
            Assert.True(setting.IsBool, $"{setting.Key} объявлена не переключателем");
            Assert.False(setting.IsProject, $"{setting.Key} объявлена проектной, а проекта у студии нет");
        }
    }

    /// <summary>Панель у модуля одна, и просится она вниз.</summary>
    [Fact]
    public void The_only_panel_asks_to_stand_at_the_bottom()
    {
        var log = Assert.Single(Manifest().Contributions.ToolWindows);

        Assert.Equal(ConsoleModule.LogPanelId, log.Id);
        Assert.Equal("bottom", log.Wanted.Side);
        Assert.InRange(log.Wanted.Size, 0.2, 0.5);
    }

    /// <summary>Модуль поднимается той же дорогой, что и плагин, и заявляет команды.</summary>
    [Fact]
    public void The_console_module_rises_and_registers_its_commands()
    {
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var loaded = host.LoadBuiltIn(typeof(ConsoleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.Equal("arxis.console", loaded.Installed.Id);

        foreach (var command in Manifest().Contributions.Commands)
            Assert.Contains(command.Id, commands.Registered);
    }

    /// <summary>Просьба, пришедшая раньше панели, дожидается её.</summary>
    /// <remarks>
    /// Команды заявляет точка входа при подъёме, а панель создаёт студия, когда
    /// ставит её в раскладку. Порядок этих двух событий модулю не принадлежит,
    /// и терять просьбу из-за него он не вправе.
    /// </remarks>
    [Fact]
    public void A_command_given_before_the_panel_waits_for_it()
    {
        ConsoleHub.Reset();

        try
        {
            var commands = new StudioCommands();

            using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

            host.LoadBuiltIn(typeof(ConsoleModule).Assembly);

            // Дока у этой студии нет, поэтому показать панель нечем — просьба
            // ложится в очередь.
            Assert.True(commands.Invoke(ConsoleModule.OpenCommand));

            var shown = 0;
            ConsoleHub.Attach(() => shown++);

            Assert.Equal(1, shown);

            // А следующая доходит сразу.
            Assert.True(commands.Invoke(ConsoleModule.OpenCommand));
            Assert.Equal(2, shown);
        }
        finally
        {
            ConsoleHub.Reset();
        }
    }

    /// <summary>
    /// Очистка идёт через службу журнала, а не через панель.
    /// </summary>
    /// <remarks>
    /// Поэтому она работает и до того, как панель построили, и в студии,
    /// собранной без дока: чистится журнал студии, а панель узнает об этом
    /// обычным событием — той же дорогой, что и о новой записи.
    /// </remarks>
    [Fact]
    public void Clearing_the_log_goes_through_the_feed_not_the_panel()
    {
        var log = new StudioLog();
        var commands = new StudioCommands();

        var services = new Dictionary<Type, object> { [typeof(IStudioLogFeed)] = log };

        using var host = new PluginHost(new StudioContextFactory(log, commands, null, services));

        host.LoadBuiltIn(typeof(ConsoleModule).Assembly);

        log.Write(StudioLogLevel.Error, "Проверка", "есть что чистить");

        Assert.NotEmpty(log.Records);
        Assert.True(commands.Invoke(ConsoleModule.ClearCommand));
        Assert.Empty(log.Records);
    }

    private static PluginManifest Manifest()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ConsoleModule).Assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        return manifest;
    }
}
