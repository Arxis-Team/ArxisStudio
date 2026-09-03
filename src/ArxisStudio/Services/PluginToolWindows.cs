using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Панели на экране глазами одного плагина.
/// </summary>
/// <remarks>
/// Док один на студию, а имя панели в нём — с именем плагина впереди:
/// манифест обещает уникальность только внутри плагина. Подставить хозяина
/// может лишь тот, кто выдал контекст, — как у полосы и команд, — и чужие
/// панели отсюда недостижимы по построению. Звать можно из любого потока:
/// док живёт на потоке интерфейса, и перенос — забота студии, а не плагина.
/// </remarks>
/// <param name="dock">Док студии.</param>
/// <param name="pluginId">Чьи панели достаёт эта обёртка.</param>
public sealed class PluginToolWindows(StudioDock dock, string pluginId) : IStudioToolWindows
{
    /// <inheritdoc/>
    public void Show(string toolWindowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolWindowId);

        var id = $"{pluginId}:{toolWindowId}";

        if (Dispatcher.UIThread.CheckAccess())
            dock.Show(id);
        else
            Dispatcher.UIThread.Post(() => dock.Show(id));
    }
}
