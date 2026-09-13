using Arxis.Hello.Contracts;
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
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        // Публикация здесь, снятие — нигде: студия снимает публикации сама
        // при выгрузке плагина, как снимает команды. Без реестра — в тестах,
        // у встраивающих — служба честно отсутствует, и плагин это переживает.
        context.GetService<IStudioExports>()?.Publish<IGreeter>(new Greeter(context));

        context.Log.Write(StudioLogLevel.Info, "Hello", "Плагин поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "Hello", "Плагин выключен");
        _context = null;
    }

    /// <summary>
    /// Ставит каретку в панель примера.
    /// </summary>
    /// <remarks>
    /// Пункта меню у этой команды нет вовсе: её показывает палитра — по имени,
    /// объявленному в манифесте, — и зовёт клавиша. Так выглядит команда,
    /// которой место не в меню, а под рукой.
    /// <para>
    /// Служба фокуса необязательна: без реестра — в тестах, у встраивающих — её
    /// честно нет, и плагин это переживает. Показать панель мало: <c>Show</c>
    /// её только достанет, а человек просил перейти.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Ставит каретку в панель примера.
    /// </summary>
    /// <remarks>
    /// Пункта меню у этой команды нет вовсе: её показывает палитра — по имени,
    /// объявленному в манифесте, — и зовёт клавиша. Так выглядит команда,
    /// которой место не в меню, а под рукой.
    /// <para>
    /// Служба фокуса необязательна: без реестра — в тестах, у встраивающих — её
    /// честно нет, и плагин это переживает. Показать панель мало: <c>Show</c>
    /// её только достанет, а человек просил перейти.
    /// </para>
    /// </remarks>
    [Command("hello.focus")]
    private void Focus() => _context?.GetService<IStudioFocus>()?.Focus("hello.panel");

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
