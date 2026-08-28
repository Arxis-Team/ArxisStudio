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

    /// <summary>
    /// Здоровается тем словом, которое выбрал человек.
    /// </summary>
    /// <remarks>
    /// Настройка читается при каждом вызове, а не запоминается при активации:
    /// изменить её могут и мимо плагина — в настройках студии или прямо в
    /// файле.
    /// </remarks>
    [Command("hello.greet")]
    private void Greet()
    {
        if (_context is null)
            return;

        var greeting = _context.Settings.Get<string>("hello.greeting");

        _context.Log.Write(
            StudioLogLevel.Info,
            "Hello",
            _context.ProjectPath is { Length: > 0 } path
                ? $"{greeting} Открыт проект {System.IO.Path.GetFileName(path)}"
                : $"{greeting} Проект не открыт");
    }
}
