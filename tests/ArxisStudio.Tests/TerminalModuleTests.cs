using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Terminal;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Модуль терминала: манифест и код говорят одно и то же, и команды доходят до панели.
/// </summary>
/// <remarks>
/// Манифест студия читает, не загружая сборку, а код находит потом по
/// идентификаторам из него. Разойтись они могут молча — панель, которой нет,
/// кнопка, за которой никого, настройка, которую студия не примет, — и
/// каждое расхождение здесь ловится по отдельности.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class TerminalModuleTests
{
    /// <summary>У каждой панели, объявленной в манифесте, есть класс в сборке.</summary>
    [Fact]
    public void The_terminal_module_carries_every_panel_it_declares()
    {
        var assembly = typeof(TerminalModule).Assembly;
        var manifest = Manifest();

        var declared = manifest.Contributions.ToolWindows.Select(panel => panel.Id).ToList();

        Assert.Equal([TerminalModule.PanelId], declared);

        var built = assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<ToolWindowAttribute>()?.Id)
            .OfType<string>()
            .ToList();

        Assert.All(declared, id => Assert.Contains(id, built));
    }

    /// <summary>Кнопка в полосе и каждый пункт меню зовут команду, которую модуль объявил.</summary>
    [Fact]
    public void Every_button_and_menu_item_names_a_declared_command()
    {
        var manifest = Manifest();
        var commands = manifest.Contributions.Commands.Select(command => command.Id).ToList();

        var buttons = manifest.Contributions.ToolBar.Where(item => item.IsButton).ToList();
        var menus = manifest.Contributions.Menus;

        Assert.NotEmpty(buttons);
        Assert.NotEmpty(menus);
        Assert.All(buttons, button => Assert.Contains(button.Command, commands));
        Assert.All(menus, item => Assert.Contains(item.Command, commands));
    }

    /// <summary>Идентификаторы команд в коде — те же, что в манифесте: иначе заявка уйдёт в никуда.</summary>
    [Fact]
    public void The_commands_in_code_are_the_commands_in_the_manifest()
    {
        var declared = Manifest().Contributions.Commands.Select(command => command.Id).Order().ToList();

        string[] known =
        [
            TerminalModule.OpenCommand,
            TerminalModule.NewCommand,
            TerminalModule.NewSshCommand,
            TerminalModule.SettingsCommand,
        ];

        Assert.Equal(known.Order(), declared);
    }

    /// <summary>У всего, что студия рисует за модуль, есть подпись.</summary>
    [Fact]
    public void Everything_the_studio_draws_for_the_terminal_has_a_title()
    {
        var manifest = Manifest();

        var drawn = manifest.Contributions.ToolBar.Where(item => !item.IsCustom).ToList();

        Assert.NotEmpty(drawn);
        Assert.All(drawn, item => Assert.False(string.IsNullOrEmpty(item.Title), item.Id));
        Assert.All(manifest.Contributions.ToolWindows, panel => Assert.False(string.IsNullOrEmpty(panel.Title), panel.Id));
        Assert.All(manifest.Contributions.Settings, setting => Assert.False(string.IsNullOrEmpty(setting.Title), setting.Key));
    }

    /// <summary>
    /// Манифест объявляет ровно те настройки, которые модуль читает.
    /// </summary>
    /// <remarks>
    /// Ключ, которого нет в манифесте, студия не примет: значение легло бы в
    /// никуда, и человек не нашёл бы его ни в одном списке.
    /// </remarks>
    [Fact]
    public void The_manifest_declares_exactly_the_settings_the_module_reads()
    {
        var declared = Manifest().Contributions.Settings;

        Assert.Equal(TerminalSettings.Keys.Order(), declared.Select(setting => setting.Key).Order());
        Assert.All(declared, setting => Assert.False(setting.IsProject, $"{setting.Key} — настройка машины, не проекта"));

        var font = Assert.Single(declared, setting => setting.Key == TerminalSettings.FontSizeKey);
        var blink = Assert.Single(declared, setting => setting.Key == TerminalSettings.CursorBlinkKey);

        Assert.True(font.IsNumber);
        Assert.True(blink.IsBool);
    }

    /// <summary>Панель просит место внизу — там, где терминал у всех.</summary>
    [Fact]
    public void The_panel_asks_for_the_bottom()
    {
        var panel = Assert.Single(Manifest().Contributions.ToolWindows);

        Assert.Equal("bottom", panel.Wanted.Side);
        Assert.InRange(panel.Wanted.Size, 0.2, 0.5);
    }

    /// <summary>
    /// Модуль поднимается тем же хостом, что и плагины, и заявляет все команды.
    /// </summary>
    [Fact]
    public void The_terminal_module_rises_and_registers_its_commands()
    {
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var loaded = host.LoadBuiltIn(typeof(TerminalModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.Equal("arxis.terminal", loaded.Installed.Id);

        foreach (var command in Manifest().Contributions.Commands)
            Assert.Contains(command.Id, commands.Registered);
    }

    /// <summary>
    /// Команда, поданная раньше панели, ждёт её, а не пропадает.
    /// </summary>
    /// <remarks>
    /// Панель строит студия, когда ставит её в раскладку; команды заявляет
    /// модуль при подъёме. Нажать кнопку в полосе до того, как панель встала,
    /// — обычное дело, и просьба обязана дождаться.
    /// </remarks>
    [Fact]
    public void A_command_given_before_the_panel_waits_for_it()
    {
        TerminalHub.Reset();

        try
        {
            var commands = new StudioCommands();

            using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

            Assert.True(host.LoadBuiltIn(typeof(TerminalModule).Assembly).IsLoaded);

            // Службы дока у этой студии нет — команда обязана это пережить.
            Assert.True(commands.Invoke(TerminalModule.NewCommand));
            Assert.True(commands.Invoke(TerminalModule.NewSshCommand));

            var received = new List<TerminalRequest>();

            TerminalHub.Attach(received.Add);

            Assert.Equal([TerminalRequestKind.NewSession, TerminalRequestKind.NewSsh], received.Select(request => request.Kind));
            Assert.NotNull(received[0].Profile);
            Assert.Contains(received[0].Profile!.Id, ShellCatalog.Available().Select(shell => shell.Id));

            // После постановки просьбы приходят сразу.
            Assert.True(commands.Invoke(TerminalModule.OpenCommand));
            Assert.Equal(3, received.Count);
            Assert.Equal(TerminalRequestKind.Open, received[2].Kind);
        }
        finally
        {
            TerminalHub.Reset();
        }
    }

    /// <summary>
    /// Настройки доезжают до студии и возвращаются оттуда — через настоящее хранилище.
    /// </summary>
    /// <remarks>
    /// Проверяется вся дорога: объявленные в манифесте умолчания, запись в файл
    /// и чтение обратно. Подделка вместо хранилища проверяла бы подделку —
    /// значения едут через JSON, и типы по дороге меняются: записанное целым
    /// читается дробным, а невынутое число приходит нулём.
    /// </remarks>
    [Fact]
    public void Settings_travel_through_the_real_studio_store()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-terminal-{Guid.NewGuid():N}");

        Directory.CreateDirectory(folder);

        try
        {
            var store = new PluginSettingsStore(userFile: Path.Combine(folder, "settings.json"));
            var settings = new PluginSettings("arxis.terminal", Manifest().Contributions.Settings, store, new StudioLog());

            // Пока человек ничего не менял, читаются умолчания из манифеста.
            Assert.Equal(TerminalSettings.Default, TerminalSettings.Read(settings));

            var chosen = new TerminalSettings(ShellCatalog.CommandPromptId, 15, 200, false);

            chosen.Write(settings);

            Assert.Equal(chosen, TerminalSettings.Read(settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Sdk.Plugins.PluginManifest Manifest()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(TerminalModule).Assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        return manifest!;
    }
}
