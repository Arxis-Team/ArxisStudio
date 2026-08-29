using Arxis.Hello.Contracts;
using ArxisStudio.Sdk;

namespace Arxis.HelloFriend;

/// <summary>
/// Точка входа примера с зависимостью.
/// </summary>
/// <remarks>
/// Манифест объявляет <c>dependencies: [ arxis.hello ]</c>, и потому к моменту
/// активации сосед уже стоит: его службы существуют, его команды заявлены.
/// Плагин нарочно отложенный (<c>onCommand:</c>) — так видна вся дорога:
/// вызов команды будит его, а прежде него студия поднимает Hello.
/// </remarks>
public sealed class FriendPlugin : StudioPlugin
{
    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _context = context;

        context.Log.Write(StudioLogLevel.Info, "Friend", "Плагин поднят — сосед уже стоит");
    }

    /// <inheritdoc/>
    public override void Deactivate() => _context = null;

    /// <summary>
    /// Спрашивает о соседе и зовёт его команду.
    /// </summary>
    /// <remarks>
    /// Обязательному соседу вопрос «есть ли ты» не нужен — раз этот плагин
    /// поднят, сосед стоит под ним. Служба здесь показывает версию: она
    /// отвечает по манифесту, не загружая ничего.
    /// </remarks>
    [Command("friend.cheer")]
    private void Cheer()
    {
        if (_context is null)
            return;

        var neighbours = _context.GetService<IStudioPlugins>();
        var version = neighbours?.Version("arxis.hello");

        _context.Log.Write(
            StudioLogLevel.Info,
            "Friend",
            neighbours?.IsActive("arxis.hello") == true
                ? $"Сосед arxis.hello активен, версия {version} — передаю привет"
                : "Соседа arxis.hello нет — а без него меня бы не подняли");

        // Типизированный разговор через контракт: IGreeter здесь — тот же
        // тип, что у Hello, потому что контрактная сборка одна на всех.
        // Взял, использовал, отпустил: придержанный объект после
        // перезагрузки соседа был бы его прежней, уже выгруженной копией.
        if (_context.GetService<IStudioExports>()?.Get<IGreeter>() is { } greeter)
            _context.Log.Write(StudioLogLevel.Info, "Friend", greeter.Greet("Friend"));

        _context.Commands.Invoke("hello.greet");
    }
}
