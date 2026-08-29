using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Пример плагина с зависимостью — Arxis.HelloFriend.
/// </summary>
/// <remarks>
/// По нему автор будет писать свой плагин с зависимостью, и потому пример
/// проверяется как настоящий: ставится архивами, будится командой и
/// поднимается после соседа. Устаревший пример хуже отсутствующего.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class HelloFriendExampleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-friend-{Guid.NewGuid():N}");

    public HelloFriendExampleTests() => Directory.CreateDirectory(_root);

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

    /// <summary>
    /// Вызов команды будит пример, а прежде него поднимается сосед.
    /// </summary>
    /// <remarks>
    /// Пример нарочно отложенный: здесь видна вся дорога — Invoke зовёт
    /// будильник, тот поднимает сперва Hello, затем Friend, и команда
    /// выполняется в том же вызове.
    /// </remarks>
    [Fact]
    public void The_friend_example_rises_after_its_dependency()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(Archive("Arxis.HelloPlugin", "arxis.hello.axplugin")).Error);
        Assert.Null(catalog.InstallFromArchive(Archive("Arxis.HelloFriend", "arxis.hello-friend.axplugin")).Error);

        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        // Hello нетерпелив (onToolWindow), Friend ждёт своей команды.
        Assert.Single(host.LoadStartup(catalog.Scan()), loaded => loaded.IsLoaded);
        Assert.Single(host.Deferred);

        commands.Awaken = command => host.Activate(
            host.Deferred.FirstOrDefault(waiting =>
                PluginActivation.WaitsForCommand(waiting.Manifest, command))?.Id ?? string.Empty);

        Assert.True(commands.Invoke("friend.cheer"), "команда не разбудила пример");

        Assert.Equal(
            ["arxis.hello", "arxis.hello-friend"],
            host.Loaded.Select(loaded => loaded.Installed.Id));
    }

    /// <summary>Манифест примера объявляет зависимость, и граф её видит.</summary>
    [Fact]
    public void The_friend_example_declares_its_dependency()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(Archive("Arxis.HelloFriend", "arxis.hello-friend.axplugin")).Error);

        var friend = catalog.Scan().Single();
        var declared = Assert.Single(friend.Manifest!.Dependencies);

        Assert.Equal("arxis.hello", declared.Id);
        Assert.False(declared.Optional);

        // Без соседа пример отказан с его именем — та же дорога, что у всех.
        var resolution = PluginGraph.Resolve([friend], present: []);

        Assert.Contains("arxis.hello", resolution.Refused[friend.Id]);
    }

    private static string Archive(string project, string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", project, file);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"Не найден архив {file}");
    }
}
