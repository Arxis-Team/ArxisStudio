using ArxisStudio.Sdk;

namespace Arxis.HelloPlugin;

/// <summary>
/// Точка входа примера: заявляет команду, объявленную в манифесте.
/// </summary>
public sealed class HelloPlugin : StudioPlugin
{
    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _context = context;

        context.Commands.Register("hello.greet", Greet);
        context.Log.Write(StudioLogLevel.Info, "Hello", "Плагин поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "Hello", "Плагин выключен");
        _context = null;
    }

    private void Greet() =>
        _context?.Log.Write(
            StudioLogLevel.Info,
            "Hello",
            _context.ProjectPath is { Length: > 0 } path
                ? $"Здравствуйте! Открыт проект {System.IO.Path.GetFileName(path)}"
                : "Здравствуйте! Проект не открыт");
}
