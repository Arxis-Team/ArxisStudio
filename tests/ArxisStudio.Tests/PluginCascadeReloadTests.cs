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
    private readonly string _root = TempFolder.Create("cascade");

    // Ссылка, которую «забыли»: объект плагина, оставшийся у студии, держит его контекст загрузки.
    private object? _forgotten;

    public void Dispose()
    {
        _forgotten = null;

        TempFolder.Erase(_root);

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
    /// Невыгрузившийся называется своим именем, не пороча соседей.
    /// </summary>
    /// <remarks>
    /// Объект зависимого нарочно оставлен на руках — так выходит у всякого, кто
    /// забыл отписаться: студия продолжает держать его делегат, а через него и
    /// контекст. Ответ по каждому свой: безымянное «что-то не выгрузилось» не
    /// говорит, кого чинить.
    /// <para>
    /// Прежде забытым здесь был обработчик команды, и держался тест на том, что
    /// второй клон молча перезаписывал команду первого. Теперь занятую команду
    /// другому не отдают, и у клона-зависимого обработчиков нет вовсе — ссылку
    /// держит сам тест, тем же способом, каким её держала бы забытая подписка.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_dependent_that_kept_a_reference_is_reported_by_its_own_name()
    {
        Clone("cas.base");
        Clone("cas.user", depends: """[ { "id": "cas.base" } ]""");

        var commands = new StudioCommands();

        using var host = Host(commands);

        Start(host, expected: 2);

        // Команды снимаются у обоих — как их снимает оболочка перед выгрузкой; держать
        // контекст зависимого остаётся только забытая ссылка на его точку входа.
        commands.RemoveOwnedBy("cas.base");
        commands.RemoveOwnedBy("cas.user");

        _forgotten = Entry(host, "cas.user");

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
    /// Точка входа поднятого плагина — без записи о нём в кадре теста.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Entry(PluginHost host, string pluginId) =>
        host.Loaded.Single(plugin => plugin.Installed.Id == pluginId).Entries[0];

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

    private void Clone(string id, string? depends = null, bool waiting = false) =>
        HelloArchive.Clone(_root, id, depends, waiting ? $"""[ "onCommand:{id}.run" ]""" : null);

    private static PluginHost Host(StudioCommands? commands = null) =>
        new(new StudioContextFactory(new StudioLog(), commands ?? new StudioCommands(), null));
}
