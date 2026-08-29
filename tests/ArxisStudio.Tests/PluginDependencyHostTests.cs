using System.IO.Compression;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Зависимости в живом хосте: настоящие контексты загрузки, настоящий подъём.
/// </summary>
/// <remarks>
/// Плагины здесь — клоны примера: архив <c>arxis.hello.axplugin</c>
/// распаковывается по разу на роль, и каждой копии переписывается манифест.
/// Свой проект на каждую роль стоил бы дороже и не проверил бы ничего сверх;
/// клоны делят одну сборку между контекстами — это законно, но команды
/// достаются последнему владельцу, поэтому тесты порядка на обработчики
/// клонов не опираются.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginDependencyHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-deps-{Guid.NewGuid():N}");

    public PluginDependencyHostTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Сборка клона может быть ещё не отпущена контекстом — папка
            // догорит с временными файлами, тест от этого не честнее.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Старт поднимает плагины в порядке зависимостей.</summary>
    [Fact]
    public void Startup_raises_plugins_in_dependency_order()
    {
        Clone("dep.base");
        Clone("dep.middle", depends: """[ { "id": "dep.base" } ]""");
        Clone("dep.top", depends: """[ { "id": "dep.middle" } ]""");

        using var host = Host();
        var raised = host.LoadStartup(new PluginCatalog(_root).Scan());

        Assert.Equal(
            ["dep.base", "dep.middle", "dep.top"],
            raised.Where(loaded => loaded.IsLoaded).Select(loaded => loaded.Installed.Id));
    }

    /// <summary>
    /// Нетерпеливый тянет за собой отложенную зависимость.
    /// </summary>
    /// <remarks>
    /// У зависимости свои события активации, но в момент активации зависимого
    /// её службы обязаны существовать — иначе ответ соседа зависел бы от того,
    /// открывал ли кто-то меню.
    /// </remarks>
    [Fact]
    public void An_eager_plugin_drags_its_deferred_dependency_up()
    {
        Clone("dep.sleeper", activation: """[ "onCommand:sleeper.run" ]""");
        Clone("dep.eager", depends: """[ { "id": "dep.sleeper" } ]""");

        using var host = Host();
        var raised = host.LoadStartup(new PluginCatalog(_root).Scan());

        Assert.Equal(
            ["dep.sleeper", "dep.eager"],
            raised.Where(loaded => loaded.IsLoaded).Select(loaded => loaded.Installed.Id));
        Assert.Empty(host.Deferred);
    }

    /// <summary>
    /// Отказанный не поднимается и не ждёт событий.
    /// </summary>
    /// <remarks>
    /// Причина отказа не в событии, а в соседях: будить его значило бы поднять
    /// то, чему студия только что отказала словами.
    /// </remarks>
    [Fact]
    public void A_refused_plugin_is_failed_and_does_not_wait_for_events()
    {
        Clone("dep.orphan", depends: """[ { "id": "dep.gone" } ]""", activation: """[ "onCommand:orphan.run" ]""");

        using var host = Host();
        var raised = host.LoadStartup(new PluginCatalog(_root).Scan());

        var failed = Assert.Single(raised);

        Assert.False(failed.IsLoaded);
        Assert.Contains("dep.gone", failed.Error);
        Assert.Empty(host.Deferred);
    }

    /// <summary>Встроенный модуль удовлетворяет зависимость плагина.</summary>
    [Fact]
    public void A_present_module_satisfies_a_dependency()
    {
        Clone("dep.on-module", depends: """[ { "id": "arxis.sample" } ]""");

        using var host = Host();

        Assert.True(host.LoadBuiltIn(typeof(Modules.Sample.SampleModule).Assembly).IsLoaded);

        var raised = host.LoadStartup(new PluginCatalog(_root).Scan());

        Assert.True(Assert.Single(raised).IsLoaded, raised[0].Error);
    }

    /// <summary>
    /// Активация поднимает сперва обязательную цепочку, потом просимого.
    /// </summary>
    [Fact]
    public void Activating_a_plugin_raises_its_mandatory_chain_first()
    {
        Clone("dep.base", activation: """[ "onCommand:base.run" ]""");
        Clone("dep.middle", depends: """[ { "id": "dep.base" } ]""", activation: """[ "onCommand:middle.run" ]""");
        Clone("dep.top", depends: """[ { "id": "dep.middle" } ]""", activation: """[ "onCommand:top.run" ]""");

        using var host = Host();

        Assert.Empty(host.LoadStartup(new PluginCatalog(_root).Scan()));

        var raised = host.Activate("dep.top");

        Assert.Equal(
            ["dep.base", "dep.middle", "dep.top"],
            raised.Select(loaded => loaded.Installed.Id));
        Assert.Empty(host.Deferred);
    }

    /// <summary>Необязательный присутствующий сосед поднимается тоже.</summary>
    [Fact]
    public void Activating_raises_an_optional_neighbour_that_is_present()
    {
        Clone("dep.git", activation: """[ "onCommand:git.run" ]""");
        Clone("dep.user", depends: """[ { "id": "dep.git", "optional": true } ]""", activation: """[ "onCommand:user.run" ]""");

        using var host = Host();

        Assert.Empty(host.LoadStartup(new PluginCatalog(_root).Scan()));

        var raised = host.Activate("dep.user");

        Assert.Equal(["dep.git", "dep.user"], raised.Select(loaded => loaded.Installed.Id));
    }

    /// <summary>Уже поднятая зависимость второй раз не поднимается.</summary>
    [Fact]
    public void A_dependency_already_up_is_not_raised_twice()
    {
        Clone("dep.base");
        Clone("dep.user", depends: """[ { "id": "dep.base" } ]""", activation: """[ "onCommand:user.run" ]""");

        using var host = Host();

        Assert.Single(host.LoadStartup(new PluginCatalog(_root).Scan()));

        var raised = host.Activate("dep.user");

        Assert.Equal(["dep.user"], raised.Select(loaded => loaded.Installed.Id));
        Assert.Equal(2, host.Loaded.Count);
    }

    /// <summary>
    /// Клонирует пример плагина под новой ролью.
    /// </summary>
    /// <param name="id">Идентификатор клона — он же имя папки.</param>
    /// <param name="depends">JSON-массив dependencies или null.</param>
    /// <param name="activation">JSON-массив activation; null — onStartup.</param>
    private void Clone(string id, string? depends = null, string? activation = null)
    {
        var target = Path.Combine(_root, id);

        ZipFile.ExtractToDirectory(Archive(), target);

        // Манифест переписывается целиком: панелей у клона нет — их типы
        // объявлены атрибутом на общей сборке, и каждый клон тащил бы одну и
        // ту же панель в окно.
        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "dependencies": {{depends ?? "[]"}},
              "activation": {{activation ?? """[ "onStartup" ]"""}}
            }
            """);
    }

    private static PluginHost Host() =>
        new(new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

    private static string Archive()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", "Arxis.HelloPlugin", "arxis.hello.axplugin");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найден архив примера arxis.hello.axplugin");
    }
}
