using System.IO.Compression;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каскадная перезагрузка: зависимые опускаются вместе с зависимостью и
/// поднимаются обратно.
/// </summary>
/// <remarks>
/// Зависимый держит соседа живым так же, как забытая подписка: перезагрузи
/// хост одну зависимость, её прежний контекст не умер бы, пока стоит
/// зависимый, — и студия честно, но бесполезно жаловалась бы на копию в
/// памяти.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginCascadeReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-cascade-{Guid.NewGuid():N}");

    public PluginCascadeReloadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Зависимый опускается и поднимается вместе с зависимостью.</summary>
    [Fact]
    public void Reloading_a_dependency_takes_dependents_down_and_up_again()
    {
        Clone("cas.base");
        Clone("cas.user", depends: """[ { "id": "cas.base" } ]""");

        var commands = new StudioCommands();

        using var host = Host(commands);

        Start(host, expected: 2);

        // Порядок оболочки: перед выгрузкой обработчики снимаются — команда
        // держит объект плагина, а тот — контекст.
        commands.RemoveOwnedBy("cas.user");
        commands.RemoveOwnedBy("cas.base");

        var installed = new PluginCatalog(_root).Scan();
        var cascade = host.Reload(
            lower: ["cas.user", "cas.base"],
            raise:
            [
                installed.Single(plugin => plugin.Id == "cas.base"),
                installed.Single(plugin => plugin.Id == "cas.user"),
            ]);

        Assert.Empty(cascade.Skipped);
        Assert.True(cascade.Released["cas.base"], "зависимость не выгрузилась");
        Assert.True(cascade.Released["cas.user"], "зависимый не выгрузился");
        Assert.Equal(["cas.base", "cas.user"], cascade.Raised.Select(loaded => loaded.Installed.Id));
        Assert.All(cascade.Raised, loaded => Assert.True(loaded.IsLoaded, loaded.Error));
    }

    /// <summary>
    /// Не поднятый пропускается целиком: и словом, и в подъёме.
    /// </summary>
    /// <remarks>
    /// Поднять пропущенного значило бы завести копию того, что опускать было
    /// нечего, — и запись о причине рядом с живой копией стала бы ложью.
    /// </remarks>
    [Fact]
    public void A_dependent_that_is_not_up_is_skipped_with_a_reason()
    {
        Clone("cas.base");
        Clone("cas.ghost", depends: """[ { "id": "cas.base" } ]""", waiting: true);

        var commands = new StudioCommands();

        using var host = Host(commands);

        Start(host, expected: 1);

        commands.RemoveOwnedBy("cas.base");

        var installed = new PluginCatalog(_root).Scan();
        var cascade = host.Reload(
            lower: ["cas.ghost", "cas.base"],
            raise:
            [
                installed.Single(plugin => plugin.Id == "cas.base"),
                installed.Single(plugin => plugin.Id == "cas.ghost"),
            ]);

        Assert.Contains("cas.ghost", cascade.Skipped.Keys);
        Assert.True(cascade.Released["cas.base"]);
        Assert.Equal(["cas.base"], cascade.Raised.Select(loaded => loaded.Installed.Id));
    }

    /// <summary>
    /// Одиночная перезагрузка ведёт себя как прежде.
    /// </summary>
    /// <remarks>
    /// Старый <c>Reload(InstalledPlugin)</c> теперь реализован поверх
    /// каскада — эквивалентность закреплена, чтобы каскад не переопределил
    /// её молча. Остальное про одиночную дорогу держат PluginReloadTests.
    /// </remarks>
    [Fact]
    public void A_single_plugin_reload_still_behaves_as_before()
    {
        Clone("cas.lonely");

        var commands = new StudioCommands();

        using var host = Host(commands);

        Start(host, expected: 1);

        commands.RemoveOwnedBy("cas.lonely");

        var installed = new PluginCatalog(_root).Scan().Single(plugin => plugin.Id == "cas.lonely");
        var reload = host.Reload(installed);

        Assert.Null(reload.Error);
        Assert.True(reload.Released, "контекст одиночки не выгрузился");
        Assert.True(reload.Plugin!.IsLoaded);
        Assert.Single(host.Loaded);
    }

    /// <summary>
    /// Невыгрузившийся называется своим именем, не пороча соседей.
    /// </summary>
    /// <remarks>
    /// Обработчик команды зависимого нарочно оставлен — так поступает всякий,
    /// кто забыл отписаться. Ответ по каждому свой: безымянное «что-то не
    /// выгрузилось» не говорит, кого чинить.
    /// </remarks>
    [Fact]
    public void A_dependent_that_kept_a_reference_is_reported_by_its_own_name()
    {
        Clone("cas.base");
        Clone("cas.user", depends: """[ { "id": "cas.base" } ]""");

        var commands = new StudioCommands();

        using var host = Host(commands);

        Start(host, expected: 2);

        // Снимаются только команды зависимости: команда зависимого остаётся
        // держать его объект, а через объект — контекст.
        commands.RemoveOwnedBy("cas.base");

        var installed = new PluginCatalog(_root).Scan();
        var cascade = host.Reload(
            lower: ["cas.user", "cas.base"],
            raise:
            [
                installed.Single(plugin => plugin.Id == "cas.base"),
                installed.Single(plugin => plugin.Id == "cas.user"),
            ]);

        Assert.False(cascade.Released["cas.user"], "оставленный обработчик не удержал контекст");
        Assert.True(cascade.Released["cas.base"], "зависимость оболгали: её никто не держал");
    }

    /// <summary>
    /// Поднимает каталог, не оставляя записей в кадре теста.
    /// </summary>
    /// <remarks>
    /// Тот же приём, что у PluginReloadTests: список поднятых, оставшийся в
    /// переменной теста — хоть явной, хоть заведённой компилятором, — держал
    /// бы контексты живыми, и проверка выгрузки мерила бы саму себя.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Start(PluginHost host, int expected) =>
        Assert.Equal(expected, host.LoadStartup(new PluginCatalog(_root).Scan()).Count);

    private void Clone(string id, string? depends = null, bool waiting = false)
    {
        var target = Path.Combine(_root, id);

        ZipFile.ExtractToDirectory(Archive(), target);

        var activation = waiting ? $"""[ "onCommand:{id}.run" ]""" : """[ "onStartup" ]""";

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entry": "bin/Arxis.HelloPlugin.dll",
              "dependencies": {{depends ?? "[]"}},
              "activation": {{activation}}
            }
            """);
    }

    private static PluginHost Host(StudioCommands? commands = null) =>
        new(new StudioContextFactory(new StudioLog(), commands ?? new StudioCommands(), null));

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
