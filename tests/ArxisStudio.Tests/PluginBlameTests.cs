using System.Reflection;
using ArxisStudio.Extensibility;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Кто виноват в исключении, пришедшем мимо шва.
/// </summary>
/// <remarks>
/// Необработанное исключение потока интерфейса и исключение забытой задачи
/// приходят от платформы: там никто не скажет, чей это код, — и узнать плагин
/// можно только по стеку. Без этого студия либо глушила бы всё подряд, включая
/// свои дефекты, либо не глушила ничего и падала от чужой ошибки.
/// </remarks>
public class PluginBlameTests
{
    /// <summary>Исключение, брошенное кодом плагина, приписано плагину.</summary>
    [Fact]
    public void An_exception_thrown_by_plugin_code_names_the_plugin()
    {
        var loaded = new[] { Plugin("arxis.demo", typeof(PluginBlameTests).Assembly) };
        var error = Caught(() => Throw());

        Assert.Equal("arxis.demo", PluginHost.Blame(error, loaded)?.Installed.Id);
    }

    /// <summary>В стеке только код студии — виноватых нет.</summary>
    /// <remarks>
    /// Свой дефект приписать плагину значит отключить невиновного и оставить
    /// дефект в студии.
    /// </remarks>
    [Fact]
    public void An_exception_from_the_studio_itself_names_nobody()
    {
        var loaded = new[] { Plugin("arxis.demo", typeof(PluginHost).Assembly) };
        var error = Caught(() => Throw());

        Assert.Null(PluginHost.Blame(error, loaded));
    }

    /// <summary>
    /// Виновник ищется и во вложенном исключении.
    /// </summary>
    /// <remarks>
    /// Задача заворачивает исключение в AggregateException, и в стеке обёртки
    /// чужого кода нет вовсе — он остался в том, что внутри.
    /// </remarks>
    [Fact]
    public void A_wrapped_exception_is_unwrapped_first()
    {
        var loaded = new[] { Plugin("arxis.demo", typeof(PluginBlameTests).Assembly) };
        var inner = Caught(() => Throw());
        var wrapped = new InvalidOperationException("обёртка", inner);

        Assert.Equal("arxis.demo", PluginHost.Blame(wrapped, loaded)?.Installed.Id);
    }

    /// <summary>Без исключения виноватых тоже нет.</summary>
    [Fact]
    public void Nothing_thrown_names_nobody()
    {
        Assert.Null(PluginHost.Blame(null, []));
    }

    private static LoadedPlugin Plugin(string id, Assembly assembly) =>
        new(
            new InstalledPlugin(id, new Sdk.Plugins.PluginManifest { Id = id, Name = id }, null, true),
            null,
            [assembly],
            null,
            [],
            [],
            null);

    private static Exception Caught(Action call)
    {
        try
        {
            call();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("вызов должен был упасть");
    }

    private static void Throw() => throw new InvalidOperationException("сломалось");
}
