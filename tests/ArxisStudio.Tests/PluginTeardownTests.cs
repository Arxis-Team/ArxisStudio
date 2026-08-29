using ArxisStudio.Extensibility;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Уход плагина убирает его записи — по всякой дороге.
/// </summary>
/// <remarks>
/// Запись, заведённая на владельца и пережившая его, — не мусор, а беда:
/// она держит сильную ссылку на объект выгружаемого контекста, и тот не
/// умрёт никогда. Команда такого плагина продолжает звать код, который
/// студия уже объявила снятым. Дорог выгрузки несколько — перезагрузка,
/// снятие упавшего, закрытие студии, — и раньше уборку переписывал каждый,
/// отчего списки разъехались.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginTeardownTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-teardown-{Guid.NewGuid():N}");

    public PluginTeardownTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Снятие упавшего убирает и команды, и публикации.</summary>
    /// <remarks>
    /// Дорога гварда: три сбоя подряд — и плагин отключают. Раньше она звала
    /// выгрузку мимо хоста и потому убирала только часть записей: команды
    /// оставались заявленными и звали код выгруженного контекста.
    /// </remarks>
    [Fact]
    public void Dropping_a_plugin_clears_its_commands_and_exports()
    {
        var studio = Installed();

        Assert.NotEmpty(studio.Commands.Registered);

        Assert.True(studio.Host.Drop("arxis.hello"));

        Assert.Empty(studio.Commands.Registered);
        Assert.Null(studio.Exports.Get(typeof(Arxis.Hello.Contracts.IGreeter)));
        Assert.DoesNotContain(studio.Host.Loaded, plugin => plugin.Installed.Id == "arxis.hello");
    }

    /// <summary>Закрытие студии убирает записи всех поднятых.</summary>
    /// <remarks>
    /// <see cref="PluginHost.Dispose"/> — единственная дорога, выгружающая
    /// всех разом, и до сих пор она не убирала ничего: реестры переживали
    /// хост и держали его контексты.
    /// </remarks>
    [Fact]
    public void Disposing_the_host_clears_what_the_plugins_left()
    {
        var studio = Installed();

        studio.Host.Dispose();

        Assert.Empty(studio.Commands.Registered);
        Assert.Null(studio.Exports.Get(typeof(Arxis.Hello.Contracts.IGreeter)));
    }

    /// <summary>Снять того, кого нет, — не беда и не исключение.</summary>
    [Fact]
    public void Dropping_an_unknown_plugin_says_no()
    {
        using var studio = new TestHost();

        Assert.False(studio.Host.Drop("нет.такого"));
    }

    /// <summary>Ставит пример и поднимает его.</summary>
    private TestHost Installed()
    {
        var catalog = new PluginCatalog(_root);

        Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);

        var studio = new TestHost();

        Assert.Single(studio.Host.LoadStartup(catalog.Scan()), plugin => plugin.IsLoaded);

        return studio;
    }
}
