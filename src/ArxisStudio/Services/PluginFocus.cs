using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Каретка в панелях глазами одного плагина.
/// </summary>
/// <remarks>
/// Тот же именной фасад, что у панелей, полосы и команд: имя панели в доке
/// начинается с имени плагина, и подставить его может лишь тот, кто выдал
/// контекст. Чужую панель отсюда не сфокусировать по построению.
/// <para>
/// Перенос на поток интерфейса — забота студии: фокус живёт там же, где дерево
/// раскладки, а плагин может позвать откуда угодно. Из чужого потока ответ
/// честный — <c>false</c>: сказать «досталась», не дождавшись, значило бы
/// соврать, а ждать чужой поток на посте нельзя.
/// </para>
/// </remarks>
/// <param name="dock">Док студии.</param>
/// <param name="pluginId">Чьи панели достаёт эта обёртка.</param>
public sealed class PluginFocus(StudioDock dock, string pluginId) : IStudioFocus
{
    /// <inheritdoc/>
    public bool Focus(string toolWindowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolWindowId);

        var id = Name(toolWindowId);

        if (Dispatcher.UIThread.CheckAccess())
            return dock.Focus(id);

        Dispatcher.UIThread.Post(() => dock.Focus(id));

        return false;
    }

    /// <inheritdoc/>
    public bool IsFocused(string toolWindowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolWindowId);

        return Dispatcher.UIThread.CheckAccess() && dock.IsFocused(Name(toolWindowId));
    }

    private string Name(string toolWindowId) => $"{pluginId}:{toolWindowId}";
}
