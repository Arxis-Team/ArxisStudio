using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Модель решения глазами дизайнера: снапшот и поиск проекта по файлу.
/// </summary>
/// <remarks>
/// Саму модель держит студия — она нужна и панели проекта, и запуску. Модуль
/// просит ровно то, без чего не откроет документ, и получает это службой
/// контекста: жёсткая ссылка на приложение была бы кольцом.
/// </remarks>
public interface IDesignerWorkspace
{
    /// <summary>Снапшот открытого решения; null, пока проект не открыт.</summary>
    SolutionSnapshot? Snapshot { get; }

    /// <summary>Находит проект, которому принадлежит файл.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    ProjectSnapshot? FindProjectForFile(string filePath);
}

/// <summary>
/// Точка входа модуля дизайнера.
/// </summary>
/// <remarks>
/// Модуль поднимается тем же путём, что и внешний плагин: студия находит эту
/// точку входа в сборке и вызывает её. Всё, что дизайнеру нужно от студии,
/// приходит контекстом — и оседает в общем состоянии, откуда его читают панели
/// и представления документов.
/// </remarks>
public sealed class DesignerModule : StudioPlugin
{
    /// <inheritdoc/>
    public override void Activate(IStudioContext context) => DesignerState.Instance.Attach(context);

    /// <inheritdoc/>
    public override void Deactivate() => DesignerState.Instance.Detach();
}
