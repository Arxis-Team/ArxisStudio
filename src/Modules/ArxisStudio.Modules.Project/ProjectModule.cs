using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Модель решения глазами панели проекта: снапшот и уведомление о смене.
/// </summary>
/// <remarks>
/// Открывает решение студия — оно нужно и панели, и дизайнеру, и запуску.
/// Модуль просит ровно то, без чего не построит дерево, и получает это службой
/// контекста.
/// </remarks>
public interface IProjectWorkspace
{
    /// <summary>Снапшот открытого решения; null, пока проект не открыт.</summary>
    SolutionSnapshot? Snapshot { get; }

    /// <summary>Открытое решение сменилось.</summary>
    event EventHandler? SnapshotChanged;
}

/// <summary>
/// Точка входа модуля панели проекта.
/// </summary>
/// <remarks>
/// Работы при активации нет: панель берёт модель решения службой контекста
/// тогда, когда её строит оболочка. Точка входа нужна, чтобы студия узнавала
/// сборку модуля тем же способом, что и сборку внешнего плагина.
/// </remarks>
public sealed class ProjectModule : StudioPlugin;
