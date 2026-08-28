using ArxisStudio.Sdk;

namespace Arxis.HelloPlugin;

/// <summary>
/// Точка входа примера.
/// </summary>
/// <remarks>
/// Команду заявлять не нужно: метод помечен атрибутом, и студия свяжет его с
/// идентификатором из манифеста сама.
/// </remarks>
public sealed class HelloPlugin : StudioPlugin
{
    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _context = context;

        context.Log.Write(StudioLogLevel.Info, "Hello", "Плагин поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "Hello", "Плагин выключен");
        _context = null;
    }

    [Command("hello.greet")]
    private void Greet() =>
        _context?.Log.Write(
            StudioLogLevel.Info,
            "Hello",
            _context.ProjectPath is { Length: > 0 } path
                ? $"Здравствуйте! Открыт проект {System.IO.Path.GetFileName(path)}"
                : "Здравствуйте! Проект не открыт");
}
