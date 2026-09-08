using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Экран настроек: настройки объявляют и модули, и плагины.
/// </summary>
/// <remarks>
/// Доставок у расширения две, а контракт один, и секция <c>settings</c>
/// манифеста ничем не отличается у модуля от плагина: то же хранилище, те же
/// ключи, та же область. Экран же долго знал только плагинов — четыре
/// настройки терминала были объявлены, переведены на три языка и не показаны
/// никому. Отсюда и правило, которое здесь закреплено: настройка попадает на
/// экран по объявлению, а не по способу доставки.
/// <para>
/// Очередь общая с остальными: <c>RefreshPlugins</c> перечитывает языковые
/// пакеты, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class WelcomeSettingsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"arxis-welcome-{Guid.NewGuid():N}");

    public WelcomeSettingsTests() => Directory.CreateDirectory(Plugins());

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Настройка встроенного модуля доходит до экрана настроек.</summary>
    /// <remarks>
    /// Своей папки у модуля нет, и подпись ему даёт словарь студии — но на
    /// экране это такая же строка, как у плагина: человеку всё равно, приехал
    /// терминал со студией или его поставили после.
    /// </remarks>
    [Fact]
    public void Settings_declared_by_a_built_in_module_reach_the_screen()
    {
        var welcome = Welcome(Module());

        welcome.RefreshPlugins();

        var row = Assert.Single(welcome.PluginSettings, found => found.Key == "terminal.fontSize");

        Assert.Equal("Кегль", row.Label);
        Assert.Equal("Терминал", row.PluginName);
        Assert.False(welcome.HasNoPluginSettings, "объявленная настройка на экране есть");
    }

    /// <summary>
    /// Сперва принесённое студией, потом принесённое со стороны.
    /// </summary>
    /// <remarks>
    /// То же правило, что у полосы и у графа зависимостей: порядок один на всю
    /// студию, иначе одно и то же расширение стояло бы в разных местах разных
    /// списков.
    /// </remarks>
    [Fact]
    public void Module_settings_stand_before_the_plugins_ones()
    {
        Install("arxis.figma", "Figma", "figma.token");

        var welcome = Welcome(Module());

        welcome.RefreshPlugins();

        Assert.Equal(
            ["terminal.fontSize", "figma.token"],
            welcome.PluginSettings.Select(row => row.Key));
    }

    /// <summary>
    /// Карточки у модуля нет — и это не забывчивость.
    /// </summary>
    /// <remarks>
    /// Карточка предлагает выключить, обновить и удалить; модуль не умеет
    /// ничего из этого — он приезжает со студией и уезжает с ней же. Настройки
    /// и жизнь расширения — разные вопросы, и ответы на них разные.
    /// </remarks>
    [Fact]
    public void A_module_shows_its_settings_but_takes_no_card_in_the_manager()
    {
        var welcome = Welcome(Module());

        welcome.RefreshPlugins();

        Assert.NotEmpty(welcome.PluginSettings);
        Assert.Empty(welcome.InstalledPlugins);
    }

    /// <summary>Модуль, объявивший настройки, — как его видит студия.</summary>
    private static InstalledPlugin Module() => new(
        AppContext.BaseDirectory,
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings = { new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13) },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);

    /// <summary>Кладёт в папку плагинов установленный плагин с одной настройкой.</summary>
    private void Install(string id, string name, string key)
    {
        var directory = Path.Combine(Plugins(), id);

        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{name}}",
              "contributions": {
                "settings": [
                  { "key": "{{key}}", "type": "string", "scope": "user", "title": "Токен", "default": "" }
                ]
              }
            }
            """);
    }

    private string Plugins() => Path.Combine(_home, "plugins");

    private WelcomeViewModel Welcome(params InstalledPlugin[] modules) =>
        new(
            new JsonSettingsStore(Path.Combine(_home, "settings.json")),
            new RecentProjects(Path.Combine(_home, "recent.json")),
            new PluginCatalog(Plugins()))
        {
            Modules = modules,
        };
}
