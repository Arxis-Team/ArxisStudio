using ArxisStudio.Extensibility;

namespace ArxisStudio.Welcome;

/// <summary>
/// Строка списка плагинов: запись каталога плюс состояние её зависимостей.
/// </summary>
/// <remarks>
/// Состояние считается при сборке списка, а не в привязке: ему нужны все
/// установленные разом — цели зависимостей ищутся среди соседей и модулей.
/// </remarks>
/// <param name="Plugin">Запись каталога.</param>
/// <param name="Dependencies">Зависимости с состоянием каждой цели.</param>
public sealed record PluginCard(
    InstalledPlugin Plugin,
    IReadOnlyList<PluginDependencyState> Dependencies)
{
    /// <summary>Строка зависимостей показывается только тем, у кого они есть.</summary>
    public bool HasDependencies => Dependencies.Count > 0;
}
