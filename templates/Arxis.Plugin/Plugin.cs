using ArxisStudio.Sdk;

namespace Arxis.MyPlugin;

/// <summary>
/// Точка входа плагина: студия создаёт её сама и зовёт при подъёме.
/// </summary>
/// <remarks>
/// Всё, что плагин знает о студии, приходит в <see cref="IStudioContext"/>: и
/// журнал, и команды, и словари, и фоновые задачи. Самой студии плагин не
/// видит — и не должен.
/// </remarks>
public sealed class Plugin : StudioPlugin
{
    private IStudioContext? _studio;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        _studio = context;

        context.Log.Write(StudioLogLevel.Info, "PLUGIN-NAME", "Плагин поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate() => _studio = null;

    /// <summary>
    /// Команда плагина.
    /// </summary>
    /// <remarks>
    /// Заявлять её не нужно: студия свяжет метод с идентификатором из манифеста
    /// сама. Идентификатор — тот же, что в <c>contributions.commands</c>: по
    /// манифесту студия строит меню и полосу, не загружая эту сборку, и команда,
    /// которой в нём нет, работать будет, но позвать её будет неоткуда.
    /// </remarks>
    [Command("arxis.my-plugin.hello")]
    private void Hello() =>
        _studio?.Log.Write(StudioLogLevel.Info, "PLUGIN-NAME", _studio.Strings["command.hello"]);
}
